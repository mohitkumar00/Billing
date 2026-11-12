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
    public string ServerBaseUrl { get; set; } = "http://localhost:5050";
    public Guid DeviceId { get; set; } = Guid.NewGuid();
    public AuthResponse? CurrentAuth { get; set; }
    public string SellerStateCode { get; set; } = "27";
    public bool IsCompositionScheme { get; set; }
}

public sealed class BearerAuthHandler : DelegatingHandler
{
    private readonly ClientSessionState _session;

    public BearerAuthHandler(ClientSessionState session) => _session = session;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var isAuthEndpoint = path.StartsWith("/api/v1/auth/", StringComparison.OrdinalIgnoreCase);

        if (!isAuthEndpoint && string.IsNullOrWhiteSpace(_session.CurrentAuth?.AccessToken))
        {
            await TryAutoLoginAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(_session.CurrentAuth?.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.CurrentAuth.AccessToken);
        }
        else if (!isAuthEndpoint)
        {
            throw new HttpRequestException("BillMate API is starting up or unreachable. Please tap Retry once server finishes initializing.", null, System.Net.HttpStatusCode.ServiceUnavailable);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !isAuthEndpoint)
        {
            if (await TryAutoLoginAsync(cancellationToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _session.CurrentAuth!.AccessToken);
                response = await base.SendAsync(request, cancellationToken);
            }
        }

        return response;
    }

    private async Task<bool> TryAutoLoginAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(_session.ServerBaseUrl), Timeout = TimeSpan.FromSeconds(2) };
                var content = new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { email = "admin@billmate.local", password = "ChangeMe@123" }),
                    System.Text.Encoding.UTF8,
                    "application/json");

                var res = await client.PostAsync("/api/v1/auth/login", content, ct);
                if (res.IsSuccessStatusCode)
                {
                    var json = await res.Content.ReadAsStringAsync(ct);
                    var auth = System.Text.Json.JsonSerializer.Deserialize<AuthResponse>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                    if (auth != null)
                    {
                        _session.CurrentAuth = auth;
                        return true;
                    }
                }
            }
            catch
            {
                if (attempt < 4)
                {
                    try { await Task.Delay(800, ct); } catch { break; }
                }
            }
        }
        return false;
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
        services.AddTransient<InventoryViewModel>();
        services.AddTransient<ReportsViewModel>();

        services.AddRefitClient<IBillMateApi>()
            .ConfigureHttpClient((sp, client) =>
            {
                var session = sp.GetRequiredService<ClientSessionState>();
                client.BaseAddress = new Uri(session.ServerBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .AddHttpMessageHandler<BearerAuthHandler>()
            .AddResilienceHandler("billmate-sfdc", builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 1,
                    Delay = TimeSpan.FromMilliseconds(500),
                    BackoffType = DelayBackoffType.Constant,
                    UseJitter = false
                });
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.8,
                    MinimumThroughput = 3,
                    SamplingDuration = TimeSpan.FromSeconds(10),
                    BreakDuration = TimeSpan.FromSeconds(10)
                });
            });

        return services;
    }
}
