using BillMate.Domain.Common;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Entities;

public sealed class StockMovement : BaseEntity
{
    public Guid ProductId { get; private set; }
    public Guid? VariantId { get; private set; }
    public Guid? BatchId { get; private set; }
    public StockMovementType MovementType { get; private set; }
    public decimal QuantityDelta { get; private set; }
    public decimal BalanceAfter { get; private set; }
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? Reason { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    private StockMovement() { }

    public static StockMovement Create(
        Guid businessId,
        Guid productId,
        StockMovementType movementType,
        decimal quantityDelta,
        decimal balanceAfter,
        Guid createdByUserId,
        string? referenceType = null,
        Guid? referenceId = null,
        string? reason = null,
        Guid? variantId = null,
        Guid? batchId = null)
    {
        if (quantityDelta == 0m)
            throw new DomainException("VAL_060", "Stock movement quantity delta cannot be zero.");

        return new StockMovement
        {
            BusinessId = businessId,
            ProductId = productId,
            VariantId = variantId,
            BatchId = batchId,
            MovementType = movementType,
            QuantityDelta = decimal.Round(quantityDelta, 3, MidpointRounding.AwayFromZero),
            BalanceAfter = decimal.Round(balanceAfter, 3, MidpointRounding.AwayFromZero),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Reason = reason?.Trim(),
            CreatedByUserId = createdByUserId
        };
    }
}
