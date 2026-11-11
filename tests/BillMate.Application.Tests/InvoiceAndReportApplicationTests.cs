using BillMate.Application;
using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Auth;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Products;
using BillMate.Application.Features.Reports;
using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;
using BillMate.Infrastructure.Persistence;
using BillMate.Infrastructure.Persistence.Interceptors;
using BillMate.Infrastructure.Persistence.Seed;
using BillMate.Infrastructure.Services;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BillMate.Application.Tests;

public sealed class InvoiceAndReportApplicationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Filename=:memory:");
    private ServiceProvider _provider = null!;
    private TestCurrentUserService _currentUser = null!;
    private ISyncBroadcaster _broadcaster = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _currentUser = new TestCurrentUserService();
        _broadcaster = Substitute.For<ISyncBroadcaster>();
        var tokenService = Substitute.For<ITokenService>();
        tokenService.GenerateAccessToken(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>>(), Arg.Any<Guid?>())
            .Returns("mock.jwt.token");
        tokenService.GenerateRefreshTokenRaw().Returns(_ => Guid.NewGuid().ToString("N"));
        tokenService.HashRefreshToken(Arg.Any<string>()).Returns(call => $"HASH_{call.Arg<string>()}");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUserService>(_currentUser);
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton(_broadcaster);
        services.AddSingleton(tokenService);
        services.AddScoped<AuditableAndAuditLogInterceptor>();
        services.AddDbContext<AppDbContext>((sp, opts) =>
            opts.UseSqlite(_connection).AddInterceptors(sp.GetRequiredService<AuditableAndAuditLogInterceptor>()));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddApplication();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await DatabaseSeeder.SeedDefaultsAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>());

        var business = await db.Businesses.FirstAsync();
        var admin = await db.Users.Include(u => u.Role!).ThenInclude(r => r.Permissions).FirstAsync();

        _currentUser.BusinessId = business.Id;
        _currentUser.UserId = admin.Id;
        _currentUser.Permissions = admin.Role!.Permissions.Select(p => p.PermissionKey).ToArray();
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task PermissionBehavior_BlocksCashierFromCancellingInvoice()
    {
        _currentUser.Permissions = ["invoices.create"]; // Cashier has no invoices.cancel
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var ex = await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            sender.Send(new CancelInvoiceCommand(Guid.NewGuid(), "Mistake")));
        Assert.Equal("AUTH_002", ex.ErrorCode);
    }

    [Fact]
    public async Task ValidationBehavior_RejectsEmptyCart()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
            sender.Send(new CreateInvoiceCommand(Guid.NewGuid(), [])));
        Assert.Equal("VAL_000", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateInvoice_IsIdempotent_AndRollsBackOnInsufficientStock()
    {
        Guid productId;
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var slab18 = await db.TaxSlabs.FirstAsync(s => s.TotalRate == 18m);

            var product = await sender.Send(new CreateProductCommand(
                slab18.Id, "SKU-APP-1", "USB Cable", 200m, OpeningStock: 5m, HsnSacCode: "8544", IsTaxInclusive: false));
            productId = product.Id;
        }

        // 1. Send same idempotency key twice -> only 2 units deducted, same invoice returned
        var key = Guid.NewGuid();
        InvoiceDto first, second;
        using (var scope = _provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var cmd = new CreateInvoiceCommand(
                IdempotencyKey: key,
                Items: [new CreateInvoiceLineInput(productId, 2m)],
                Payments: [new InvoicePaymentInput(PaymentMode.Upi, 472m, "UTR999")]);

            first = await sender.Send(cmd);
            second = await sender.Send(cmd);
        }

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.InvoiceNumber, second.InvoiceNumber);
        Assert.Equal(472m, first.GrandTotal);
        Assert.Equal(InvoiceStatus.Paid, first.Status);

        // 2. Ordering 10 units when only 3 remain fails and rolls back cleanly
        using (var scope = _provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await Assert.ThrowsAsync<InsufficientStockException>(() =>
                sender.Send(new CreateInvoiceCommand(Guid.NewGuid(), [new CreateInvoiceLineInput(productId, 10m)])));
        }

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Products.FirstAsync(x => x.Id == productId);
            Assert.Equal(3m, p.CurrentStock);
        }
    }

    [Fact]
    public async Task CancelInvoice_RestoresStock_AndReportsIncludeB2BAndTallyXml()
    {
        Guid productId, customerId, inv1Id, inv2Id;
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var slab5 = await db.TaxSlabs.FirstAsync(s => s.TotalRate == 5m);

            var product = await sender.Send(new CreateProductCommand(
                slab5.Id, "SKU-APP-2", "Basmati Rice 5kg", 500m, OpeningStock: 20m, HsnSacCode: "1006", IsTaxInclusive: false));
            productId = product.Id;

            var customer = await db.Customers.AddAsync(Customer.Create(_currentUser.BusinessId!.Value, "Metro Mart", "9988776655", gstin: "27AAACP1234A1Z5"));
            await db.SaveChangesAsync();
            customerId = customer.Entity.Id;

            var inv1 = await sender.Send(new CreateInvoiceCommand(Guid.NewGuid(), [new CreateInvoiceLineInput(productId, 2m)], CustomerId: customerId));
            var inv2 = await sender.Send(new CreateInvoiceCommand(Guid.NewGuid(), [new CreateInvoiceLineInput(productId, 1m)]));
            inv1Id = inv1.Id;
            inv2Id = inv2.Id;
        }

        // Cancel inv2 -> stock goes from 17 back to 18
        using (var scope = _provider.CreateScope())
        {
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            var cn = await sender.Send(new CancelInvoiceCommand(inv2Id, "Wrong item"));
            Assert.Equal(525m, cn.TotalAmount);

            var gstr1 = await sender.Send(new GetGstr1ReportQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
            Assert.Single(gstr1.B2BInvoices);
            Assert.Equal("27AAACP1234A1Z5", gstr1.B2BInvoices[0].CustomerGstin);
            Assert.Equal(1050m, gstr1.B2BInvoices[0].InvoiceValue);

            var gstr3b = await sender.Send(new GetGstr3bSummaryQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
            Assert.Single(gstr3b.RegularSlabs);
            Assert.Equal(5m, gstr3b.RegularSlabs[0].TaxRatePercent);

            var xml = await sender.Send(new ExportTallyXmlQuery(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)));
            Assert.Contains("<VOUCHERNUMBER>", xml);
        }

        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Products.FirstAsync(x => x.Id == productId);
            Assert.Equal(18m, p.CurrentStock);
        }
    }

    [Fact]
    public async Task AuthLoginAndPinAndRefresh_WorkEndToEnd()
    {
        using var scope = _provider.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        var login = await sender.Send(new LoginCommand("admin@billmate.local", "ChangeMe@123"));
        Assert.Equal("ADMIN", login.RoleName);
        Assert.Contains("invoices.create", login.Permissions);

        var pinLogin = await sender.Send(new PinLoginCommand(login.UserId, "1234"));
        Assert.Equal(login.UserId, pinLogin.UserId);

        var refreshed = await sender.Send(new RefreshTokenCommand(pinLogin.RefreshToken));
        Assert.NotEqual(pinLogin.RefreshToken, refreshed.RefreshToken);

        // Reusing old refresh token must fail
        await Assert.ThrowsAsync<UnauthorizedAppException>(() => sender.Send(new RefreshTokenCommand(pinLogin.RefreshToken)));
    }

    private sealed class TestCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; }
        public Guid? BusinessId { get; set; }
        public Guid? DeviceId => null;
        public string? IpAddress => "127.0.0.1";
        public IReadOnlyCollection<string> Permissions { get; set; } = [];
    }
}
