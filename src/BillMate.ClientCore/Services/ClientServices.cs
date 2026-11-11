using System.Net.Http.Headers;
using BillMate.Application.Features.Auth;
using BillMate.Application.Features.Sync;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Offline;
using BillMate.ClientCore.Theme;
using BillMate.ClientCore.ViewModels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Refit;

namespace BillMate.ClientCore.Services;

public sealed class ClientSessionState
{
    public string ServerBaseUrl { get; set; } = "https://localhost:5050";
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public AuthResponse? CurrentAuth { get; set; }
    public string SellerStateCode { get; set; } = "27";
    public bool IsCompositionScheme { get; set; }
}

public sealed class BearerAuthHandler : DelegatingHandler
{
    private readonly ClientSessionState _session;

    public BearerAuthHandler(ClientSessionState session) => _session = session;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_session.CurrentAuth?.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.CurrentAuth.AccessToken);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

public sealed class OfflineSyncCoordinator
{
    private readonly OfflineLocalStore _localStore;
    private readonly IBillMateApi _api;
    private readonly ClientSessionState _session;

    public OfflineSyncCoordinator(OfflineLocalStore localStore, IBillMateApi api, ClientSessionState session)
    {
        _localStore = localStore;
        _api = api;
        _session = session;
    }

    public async Task<SyncBatchResult> FlushPendingAsync(CancellationToken ct = default)
    {
        var pending = await _localStore.GetPendingOperationsAsync(ct);
        if (pending.Count == 0) return new SyncBatchResult(0, 0, 0);

        var ops = pending.Select(p => new OfflineSyncOperationInput(p.EntityId, p.OperationType, p.PayloadJson, p.ClientCreatedAtUtc)).ToArray();
        var result = await _api.SyncBatchAsync(new ProcessOfflineSyncBatchCommand(_session.DeviceId, ops), ct);

        if (result.FailedCount == 0 && result.ConflictCount == 0)
        {
            foreach (var item in pending)
                await _localStore.RemovePendingOperationAsync(item.Id, ct);
        }

        return result;
    }
}

public sealed class SignalRSyncClient : IAsyncDisposable
{
    private readonly ClientSessionState _session;
    private HubConnection? _connection;

    public event Action<string>? InvoiceReceived;
    public event Action<Guid, decimal>? StockUpdated;

    public SignalRSyncClient(ClientSessionState session) => _session = session;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (_connection is not null) return;
        if (string.IsNullOrWhiteSpace(_session.CurrentAuth?.AccessToken)) return;

        var hubUrl = $"{_session.ServerBaseUrl.TrimEnd('/')}/hubs/sync";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, opts =>
            {
                opts.AccessTokenProvider = () => Task.FromResult<string?>(_session.CurrentAuth?.AccessToken);
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<System.Text.Json.JsonElement>("InvoiceCreated", payload =>
        {
            if (payload.TryGetProperty("invoiceNumber", out var num))
                InvoiceReceived?.Invoke(num.GetString() ?? string.Empty);
        });

        _connection.On<System.Text.Json.JsonElement>("StockUpdated", payload =>
        {
            if (payload.TryGetProperty("productId", out var pid) && payload.TryGetProperty("newStock", out var stock))
                StockUpdated?.Invoke(pid.GetGuid(), stock.GetDecimal());
        });

        await _connection.StartAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}

public static class ClientCoreDependencyInjection
{
    public static IServiceCollection AddBillMateClientCore(
        this IServiceCollection services,
        string sqlitePath,
        WhiteLabelConfig? whiteLabel = null)
    {
        services.AddSingleton(whiteLabel ?? new WhiteLabelConfig());
        services.AddSingleton<ClientSessionState>();
        services.AddSingleton(_ => new OfflineLocalStore(sqlitePath));
        services.AddTransient<BearerAuthHandler>();
        services.AddSingleton<SignalRSyncClient>();
        services.AddTransient<OfflineSyncCoordinator>();
        services.AddTransient<PosViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<RestoreWizardViewModel>();
        services.AddTransient<PinLoginViewModel>();

        services.AddRefitClient<IBillMateApi>()
            .ConfigureHttpClient((sp, client) =>
            {
                var session = sp.GetRequiredService<ClientSessionState>();
                client.BaseAddress = new Uri(session.ServerBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            .AddHttpMessageHandler<BearerAuthHandler>()
            .AddResilienceHandler("billmate-sfdc", builder =>
            {
                // SRD Section 5.2: Exponential backoff (5 retries) + 3-failure 30s Circuit Breaker
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 5,
                    Delay = TimeSpan.FromSeconds(1),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                });
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 1.0,
                    MinimumThroughput = 3,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(30)
                });
            });

        return services;
    }
}
