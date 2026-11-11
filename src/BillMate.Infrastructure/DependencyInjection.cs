using BillMate.Application.Common.Interfaces;
using BillMate.Infrastructure.Backup;
using BillMate.Infrastructure.Persistence;
using BillMate.Infrastructure.Persistence.Interceptors;
using BillMate.Infrastructure.Printing;
using BillMate.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillMate.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditableAndAuditLogInterceptor>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IInvoicePdfGenerator, QuestPdfInvoiceGenerator>();
        services.AddSingleton<IEscPosReceiptBuilder, EscPosReceiptBuilder>();
        services.AddSingleton<IBackupStorageClient, GoogleDriveBackupClient>();
        services.AddSingleton<IPgToolsRunner, PgToolsRunner>();
        services.AddScoped<IBackupOrchestrator, BackupOrchestrator>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=billmatedb;Username=billmate;Password=billmate_dev_password";

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            });
            options.AddInterceptors(sp.GetRequiredService<AuditableAndAuditLogInterceptor>());
        });

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        return services;
    }
}
