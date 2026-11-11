using MediatR;

namespace BillMate.Application.Common.Interfaces;

/// <summary>
/// Marker interface for mutating commands executed inside a database transaction.
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>
{
}

/// <summary>
/// Marker interface for read-only queries (never wrapped in a write transaction).
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse>
{
}

public interface ITokenService
{
    string GenerateAccessToken(Guid userId, Guid businessId, string email, string roleName, IEnumerable<string> permissions, Guid? deviceId = null);
    string GenerateRefreshTokenRaw();
    string HashRefreshToken(string rawToken);
}

public interface ISyncBroadcaster
{
    Task BroadcastInvoiceCreatedAsync(Guid businessId, Guid invoiceId, string invoiceNumber, decimal grandTotal, Guid? deviceId, CancellationToken ct = default);
    Task BroadcastStockUpdatedAsync(Guid businessId, Guid productId, Guid? variantId, decimal newStock, CancellationToken ct = default);
    Task BroadcastPaymentReceivedAsync(Guid businessId, Guid? customerId, Guid? invoiceId, decimal amount, CancellationToken ct = default);
}
