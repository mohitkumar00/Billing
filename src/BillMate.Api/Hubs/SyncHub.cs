using BillMate.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BillMate.Api.Hubs;

[Authorize]
public sealed class SyncHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var businessId = Context.User?.FindFirst("business_id")?.Value;
        if (!string.IsNullOrWhiteSpace(businessId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(businessId));
        }
        await base.OnConnectedAsync();
    }

    public static string GroupName(Guid businessId) => GroupName(businessId.ToString());
    public static string GroupName(string businessId) => $"business_{businessId}";
}

public sealed class SignalRSyncBroadcaster : ISyncBroadcaster
{
    private readonly IHubContext<SyncHub> _hub;

    public SignalRSyncBroadcaster(IHubContext<SyncHub> hub)
    {
        _hub = hub;
    }

    public Task BroadcastInvoiceCreatedAsync(Guid businessId, Guid invoiceId, string invoiceNumber, decimal grandTotal, Guid? deviceId, CancellationToken ct = default) =>
        _hub.Clients.Group(SyncHub.GroupName(businessId))
            .SendAsync("InvoiceCreated", new { invoiceId, invoiceNumber, grandTotal, deviceId }, ct);

    public Task BroadcastStockUpdatedAsync(Guid businessId, Guid productId, Guid? variantId, decimal newStock, CancellationToken ct = default) =>
        _hub.Clients.Group(SyncHub.GroupName(businessId))
            .SendAsync("StockUpdated", new { productId, variantId, newStock }, ct);

    public Task BroadcastPaymentReceivedAsync(Guid businessId, Guid? customerId, Guid? invoiceId, decimal amount, CancellationToken ct = default) =>
        _hub.Clients.Group(SyncHub.GroupName(businessId))
            .SendAsync("PaymentReceived", new { customerId, invoiceId, amount }, ct);
}
