using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BillMate.Application.Features.Auth;
using BillMate.Application.Features.Invoices;
using BillMate.Application.Features.Parties;
using BillMate.Application.Features.Products;
using BillMate.Application.Features.Reports;
using BillMate.Domain.Enums;
using BillMate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BillMate.Api.Integration.Tests;

public sealed class ApiEndToEndTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("billmate_api_test")
        .WithUsername("billmate")
        .WithPassword("billmate_test_pw")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
                builder.UseSetting("Hangfire:Enabled", "false");
                builder.UseSetting("Database:AutoMigrateOnStartup", "true");
                builder.UseSetting("Database:SeedDefaultsOnStartup", "true");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<string> AuthenticateAsAdminAsync()
    {
        var resp = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand("admin@billmate.local", "ChangeMe@123"));
        resp.EnsureSuccessStatusCode();
        var auth = await resp.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        return auth!.AccessToken;
    }

    [Fact]
    public async Task HealthAndFullBillingSlice_WorkOverHttpWithPostgreSql()
    {
        // 1. Health endpoints
        var ping = await _client.GetAsync("/health/ping");
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);

        // 2. Unauthenticated call blocked
        var unauth = await _client.GetAsync("/api/v1/products/search");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        // 3. Login & authenticate
        var token = await AuthenticateAsAdminAsync();
        using var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 4. Find default 18% slab
        Guid slab18Id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            slab18Id = (await db.TaxSlabs.FirstAsync(s => s.TotalRate == 18m)).Id;
        }

        // 5. Create product & customer
        var prodResp = await authedClient.PostAsJsonAsync("/api/v1/products",
            new CreateProductCommand(slab18Id, "SKU-HTTP-1", "Thermal Paper Roll", 100m, OpeningStock: 10m, HsnSacCode: "4823", IsTaxInclusive: false));
        Assert.Equal(HttpStatusCode.Created, prodResp.StatusCode);
        var product = await prodResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions);

        var custResp = await authedClient.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerCommand("Apex Traders", "9811122233", Gstin: "27AAACP1234A1Z5", StateCode: "27"));
        Assert.Equal(HttpStatusCode.Created, custResp.StatusCode);
        var customer = await custResp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions);

        // 6. Create invoice (and verify HTTP idempotency)
        var idemKey = Guid.NewGuid();
        var invCmd = new CreateInvoiceCommand(
            IdempotencyKey: idemKey,
            Items: [new CreateInvoiceLineInput(product!.Id, 2m)],
            Payments: [new InvoicePaymentInput(PaymentMode.Upi, 236m, "UTR-HTTP-01")],
            CustomerId: customer!.Id);

        var inv1Resp = await authedClient.PostAsJsonAsync("/api/v1/invoices", invCmd);
        var inv2Resp = await authedClient.PostAsJsonAsync("/api/v1/invoices", invCmd);
        Assert.Equal(HttpStatusCode.Created, inv1Resp.StatusCode);
        Assert.Equal(HttpStatusCode.Created, inv2Resp.StatusCode);

        var inv1 = await inv1Resp.Content.ReadFromJsonAsync<InvoiceDto>(JsonOptions);
        var inv2 = await inv2Resp.Content.ReadFromJsonAsync<InvoiceDto>(JsonOptions);
        Assert.Equal(inv1!.Id, inv2!.Id);
        Assert.Equal(236m, inv1.GrandTotal);

        // 6b. PDF & ESC/POS Thermal Receipt + Cash Drawer Kick endpoints
        var pdfBytes = await authedClient.GetByteArrayAsync($"/api/v1/invoices/{inv1.Id}/pdf?format=A4");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdfBytes, 0, 5));

        var escPosBytes = await authedClient.GetByteArrayAsync($"/api/v1/invoices/{inv1.Id}/escpos?width=80&openDrawer=true");
        Assert.Equal(new byte[] { 0x1B, 0x70, 0x00, 0x19, 0xFA }, escPosBytes.TakeLast(5).ToArray());

        // 7. Reports & Tally XML
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var gstr1 = await authedClient.GetFromJsonAsync<Gstr1ReportDto>($"/api/v1/reports/gstr1?from={from}&to={to}", JsonOptions);
        Assert.NotNull(gstr1);
        Assert.Contains(gstr1!.B2BInvoices, x => x.InvoiceNumber == inv1.InvoiceNumber);

        var tallyXml = await authedClient.GetStringAsync($"/api/v1/reports/tally-export?from={from}&to={to}");
        Assert.Contains(inv1.InvoiceNumber, tallyXml);

        // 8. Cancel invoice & verify stock restored to 10
        var cancelResp = await authedClient.PostAsJsonAsync($"/api/v1/invoices/{inv1.Id}/cancel", new { reason = "Test cancel" });
        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode);

        var search = await authedClient.GetFromJsonAsync<List<ProductDto>>("/api/v1/products/search?q=SKU-HTTP-1", JsonOptions);
        Assert.Equal(10m, search!.Single().CurrentStock);
    }

    [Fact]
    public async Task FiveConcurrentPosTerminals_NeverOversellStock()
    {
        var token = await AuthenticateAsAdminAsync();
        using var authedClient = _factory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Guid slab5Id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            slab5Id = (await db.TaxSlabs.FirstAsync(s => s.TotalRate == 5m)).Id;
        }

        // Only 3 units in stock, 5 concurrent POS terminals each try to sell 1 unit
        var prodResp = await authedClient.PostAsJsonAsync("/api/v1/products",
            new CreateProductCommand(slab5Id, "SKU-RACE-1", "Limited Stock Item", 100m, OpeningStock: 3m));
        var product = await prodResp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions);

        var tasks = Enumerable.Range(0, 5).Select(_ =>
            authedClient.PostAsJsonAsync("/api/v1/invoices",
                new CreateInvoiceCommand(Guid.NewGuid(), [new CreateInvoiceLineInput(product!.Id, 1m)])));

        var responses = await Task.WhenAll(tasks);

        var createdCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var rejectedCount = responses.Count(r => r.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);

        Assert.Equal(3, createdCount);
        Assert.Equal(2, rejectedCount);

        var search = await authedClient.GetFromJsonAsync<List<ProductDto>>("/api/v1/products/search?q=SKU-RACE-1", JsonOptions);
        Assert.Equal(0m, search!.Single().CurrentStock);
    }
}
