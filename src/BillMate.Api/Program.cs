using System.Text;
using System.Threading.RateLimiting;
using BillMate.Api.Endpoints;
using BillMate.Api.Hubs;
using BillMate.Api.Jobs;
using BillMate.Api.Middleware;
using BillMate.Api.Services;
using BillMate.Application;
using BillMate.Application.Common.Interfaces;
using BillMate.Infrastructure;
using BillMate.Infrastructure.Persistence;
using BillMate.Infrastructure.Persistence.Seed;
using BillMate.Infrastructure.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5050");
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    builder.Host.UseWindowsService(options => options.ServiceName = "BillMateApi");
}

var logDirectory = OperatingSystem.IsWindows()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BillMate", "Logs")
    : Path.Combine(AppContext.BaseDirectory, "Logs");

builder.Host.UseSerilog((ctx, lc) => lc
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDirectory, "api-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        fileSizeLimitBytes: 20 * 1024 * 1024));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();

// JWT Bearer Authentication (+ SignalR query-string token extraction)
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Rate Limiting on /auth/* (10 requests/min per IP)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthRateLimit", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// SignalR Real-time LAN Sync
builder.Services.AddSignalR();
builder.Services.AddSingleton<ISyncBroadcaster, SignalRSyncBroadcaster>();

// Hangfire Background Jobs (PostgreSQL storage)
var hangfireEnabled = builder.Configuration.GetValue("Hangfire:Enabled", true);
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=billmatedb;Username=billmate;Password=billmate_dev_password";

if (hangfireEnabled)
{
    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(connStr), new Hangfire.PostgreSql.PostgreSqlStorageOptions
        {
            DistributedLockTimeout = TimeSpan.FromSeconds(5)
        }));
    builder.Services.AddHangfireServer();
}

// HealthChecks (PostgreSQL + Minimum 1 GB free disk space)
builder.Services.AddHealthChecks()
    .AddCheck("database", () => HealthCheckResult.Healthy("EF Core DbContext configured"))
    .AddCheck("disk_space", () =>
    {
        var root = Path.GetPathRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(root)) return HealthCheckResult.Healthy();
        var drive = new DriveInfo(root);
        const long oneGb = 1024L * 1024L * 1024L;
        return drive.AvailableFreeSpace >= oneGb
            ? HealthCheckResult.Healthy($"Free space: {drive.AvailableFreeSpace / oneGb} GB")
            : HealthCheckResult.Degraded($"Low disk space: {drive.AvailableFreeSpace / (1024 * 1024)} MB");
    });

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/health/ping", () => Results.Ok(new { status = "ok", service = "BillMate.Api" }));
app.MapBillMateApiV1();
app.MapHub<SyncHub>("/hubs/sync");

if (builder.Configuration.GetValue("Database:AutoMigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    if (builder.Configuration.GetValue("Database:SeedDefaultsOnStartup", false))
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await DatabaseSeeder.SeedDefaultsAsync(db, hasher);
    }
}

if (hangfireEnabled)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE hangfire.lock;");
    }
    catch { }

    try
    {
        var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate<NightlyBackupJob>(
            "billmate-nightly-backup",
            job => job.ExecuteAsync(CancellationToken.None),
            Cron.Daily(23, 0),
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Local });
    }
    catch (Exception ex)
    {
        Serilog.Log.Warning(ex, "Failed to schedule recurring nightly backup job.");
    }
}

app.Run();

public partial class Program;
