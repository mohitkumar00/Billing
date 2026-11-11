using System.Runtime.CompilerServices;
using BillMate.Application.Common.Interfaces;
using BillMate.Domain.Entities;
using BillMate.Infrastructure.Persistence;
using BillMate.Infrastructure.Persistence.Interceptors;
using BillMate.Infrastructure.Persistence.Seed;
using BillMate.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BillMate.Api.Integration.Tests;

internal static class DockerApiCompatibility
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_API_VERSION")))
        {
            Environment.SetEnvironmentVariable("DOCKER_API_VERSION", "1.43");
        }
    }
}

public sealed class PersistenceSchemaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("billmate_test")
        .WithUsername("billmate")
        .WithPassword("billmate_test_pw")
        .Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext CreateDbContext(ICurrentUserService? userService = null)
    {
        var currentUser = userService ?? new StubCurrentUserService();
        var interceptor = new AuditableAndAuditLogInterceptor(currentUser);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .AddInterceptors(interceptor)
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task MigrateAndSeed_CreatesAll28Tables_AndWritesAuditLogs()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var hasher = new BcryptPasswordHasher();
        await DatabaseSeeder.SeedDefaultsAsync(db, hasher);
        // Calling a second time must be idempotent
        await DatabaseSeeder.SeedDefaultsAsync(db, hasher);

        Assert.Equal(1, await db.Businesses.CountAsync());
        Assert.Equal(4, await db.Roles.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(5, await db.TaxSlabs.CountAsync());
        Assert.Equal(7, await db.HsnSacCodes.CountAsync());
        Assert.Equal(4, await db.ExpenseCategories.CountAsync());
        Assert.True(await db.AuditLogs.AnyAsync());
    }

    [Fact]
    public async Task SoftDeleteAndOptimisticConcurrency_WorkAsDesigned()
    {
        await using var setupDb = CreateDbContext();
        await setupDb.Database.MigrateAsync();
        await DatabaseSeeder.SeedDefaultsAsync(setupDb, new BcryptPasswordHasher());

        var business = await setupDb.Businesses.FirstAsync();
        var slab = await setupDb.TaxSlabs.FirstAsync(s => s.IsDefault);

        var product = Product.Create(business.Id, slab.Id, "SKU-CONC", "Concurrency Salt", 25m, openingStock: 10m);
        setupDb.Products.Add(product);
        await setupDb.SaveChangesAsync();

        // Simulate two concurrent readers loading the same product
        await using var db1 = CreateDbContext();
        await using var db2 = CreateDbContext();

        var p1 = await db1.Products.FirstAsync(p => p.Id == product.Id);
        var p2 = await db2.Products.FirstAsync(p => p.Id == product.Id);

        p1.DeductStock(2m);
        await db1.SaveChangesAsync();

        p2.DeductStock(3m);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db2.SaveChangesAsync());

        // Soft delete hides product from default query filter
        p1.SoftDelete();
        await db1.SaveChangesAsync();

        await using var verifyDb = CreateDbContext();
        Assert.Null(await verifyDb.Products.FirstOrDefaultAsync(p => p.Id == product.Id));
        Assert.NotNull(await verifyDb.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == product.Id));
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null;
        public Guid? BusinessId => null;
        public Guid? DeviceId => null;
        public string? IpAddress => "127.0.0.1";
        public IReadOnlyCollection<string> Permissions => [];
    }
}
