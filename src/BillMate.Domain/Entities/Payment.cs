using BillMate.Domain.Common;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Entities;

public sealed class Payment : BaseEntity
{
    public Guid? InvoiceId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid ReceivedByUserId { get; private set; }
    public PaymentMode PaymentMode { get; private set; }
    public decimal Amount { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public DateTimeOffset PaymentDate { get; private set; } = DateTimeOffset.UtcNow;
    public string? Notes { get; private set; }

    private Payment() { }

    public static Payment Create(
        Guid businessId,
        Guid? invoiceId,
        Guid? customerId,
        Guid receivedByUserId,
        PaymentMode mode,
        decimal amount,
        string? referenceNumber = null,
        string? notes = null)
    {
        if (amount <= 0m)
            throw new DomainException("VAL_043", "Payment amount must be greater than zero.");

        return new Payment
        {
            BusinessId = businessId,
            InvoiceId = invoiceId,
            CustomerId = customerId,
            ReceivedByUserId = receivedByUserId,
            PaymentMode = mode,
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            ReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber) ? null : referenceNumber.Trim(),
            Notes = notes?.Trim()
        };
    }
}
