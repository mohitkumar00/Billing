using BillMate.Domain.Common;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;
using BillMate.Domain.ValueObjects;

namespace BillMate.Domain.Entities;

public sealed class Customer : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Gstin { get; private set; }
    public string? BillingAddress { get; private set; }
    public string? ShippingAddress { get; private set; }
    public string? State { get; private set; }
    public string? StateCode { get; private set; }
    public PricingTier PricingTier { get; private set; } = PricingTier.Retail;
    public decimal CreditLimit { get; private set; }
    public decimal OutstandingBalance { get; private set; }
    public int LoyaltyPoints { get; private set; }

    private Customer() { }

    public static Customer Create(
        Guid businessId,
        string name,
        string phone,
        string? gstin = null,
        string? state = null,
        string? stateCode = null,
        decimal creditLimit = 0m,
        PricingTier pricingTier = PricingTier.Retail)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VAL_040", "Customer name is required.");
        if (string.IsNullOrWhiteSpace(phone))
            throw new DomainException("VAL_041", "Customer phone is required.");
        if (creditLimit < 0m)
            throw new DomainException("VAL_042", "Credit limit cannot be negative.");

        var validatedGstin = ValueObjects.Gstin.CreateOptional(gstin);
        return new Customer
        {
            BusinessId = businessId,
            Name = name.Trim(),
            Phone = phone.Trim(),
            Gstin = validatedGstin?.Value,
            State = state?.Trim(),
            StateCode = validatedGstin?.StateCode ?? stateCode?.Trim(),
            CreditLimit = creditLimit,
            PricingTier = pricingTier
        };
    }

    public void AddCreditSale(decimal unpaidAmount)
    {
        if (unpaidAmount <= 0m) return;

        var nextBalance = decimal.Round(OutstandingBalance + unpaidAmount, 2, MidpointRounding.AwayFromZero);
        if (CreditLimit > 0m && nextBalance > CreditLimit)
            throw new DomainException("CRM_001", $"Credit limit of ₹{CreditLimit:N2} exceeded for customer '{Name}'.");

        OutstandingBalance = nextBalance;
        MarkUpdated();
    }

    public void RecordPayment(decimal amount)
    {
        if (amount <= 0m)
            throw new DomainException("VAL_043", "Payment amount must be positive.");

        OutstandingBalance = decimal.Round(OutstandingBalance - amount, 2, MidpointRounding.AwayFromZero);
        MarkUpdated();
    }

    public void EarnLoyaltyPoints(int points)
    {
        if (points <= 0) return;
        LoyaltyPoints += points;
        MarkUpdated();
    }

    public void RedeemLoyaltyPoints(int points)
    {
        if (points <= 0)
            throw new DomainException("VAL_044", "Points to redeem must be positive.");
        if (LoyaltyPoints < points)
            throw new DomainException("CRM_002", $"Insufficient loyalty points. Available: {LoyaltyPoints}, Requested: {points}.");

        LoyaltyPoints -= points;
        MarkUpdated();
    }
}
