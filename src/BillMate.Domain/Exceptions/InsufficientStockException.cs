namespace BillMate.Domain.Exceptions;

/// <summary>
/// Raised when attempting to deduct more stock than available for a non-service product.
/// </summary>
public sealed class InsufficientStockException : DomainException
{
    public Guid ProductId { get; }
    public decimal Available { get; }
    public decimal Requested { get; }

    public InsufficientStockException(Guid productId, string productName, decimal available, decimal requested)
        : base("INV_002", $"Insufficient stock for '{productName}'. Available: {available:0.###}, Requested: {requested:0.###}.")
    {
        ProductId = productId;
        Available = available;
        Requested = requested;
    }
}
