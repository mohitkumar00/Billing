using BillMate.Domain.Common;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;
using BillMate.Domain.ValueObjects;

namespace BillMate.Domain.Entities;

public sealed class Business : BaseEntity, IAggregateRoot
{
    public string Name { get; private set; } = string.Empty;
    public string? LegalName { get; private set; }
    public string? Gstin { get; private set; }
    public string? Pan { get; private set; }
    public BusinessType BusinessType { get; private set; } = BusinessType.Retail;
    public bool IsComposition { get; private set; }
    public string AddressLine1 { get; private set; } = string.Empty;
    public string? AddressLine2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string State { get; private set; } = string.Empty;
    public string StateCode { get; private set; } = "27";
    public string Pincode { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? LogoPath { get; private set; }
    public string? SignaturePath { get; private set; }
    public short FinancialYearStartMonth { get; private set; } = 4;
    public string InvoicePrefix { get; private set; } = "INV";
    public string CurrencyCode { get; private set; } = "INR";

    private Business() { }

    public static Business Create(
        string name,
        string addressLine1,
        string city,
        string state,
        string stateCode,
        string pincode,
        string phone,
        string? gstin = null,
        bool isComposition = false,
        BusinessType businessType = BusinessType.Retail,
        string invoicePrefix = "INV")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VAL_020", "Business name is required.");
        if (string.IsNullOrWhiteSpace(stateCode) || stateCode.Trim().Length != 2)
            throw new DomainException("VAL_021", "Valid 2-digit Indian GST state code is required.");

        var validatedGstin = ValueObjects.Gstin.CreateOptional(gstin);
        if (validatedGstin is not null && validatedGstin.StateCode != stateCode.Trim())
            throw new DomainException("VAL_022", "Business GSTIN state code does not match selected state code.");

        var id = Guid.NewGuid();
        return new Business
        {
            Id = id,
            BusinessId = id,
            Name = name.Trim(),
            Gstin = validatedGstin?.Value,
            Pan = validatedGstin?.Pan,
            BusinessType = businessType,
            IsComposition = isComposition,
            AddressLine1 = addressLine1.Trim(),
            City = city.Trim(),
            State = state.Trim(),
            StateCode = stateCode.Trim(),
            Pincode = pincode.Trim(),
            Phone = phone.Trim(),
            InvoicePrefix = string.IsNullOrWhiteSpace(invoicePrefix) ? "INV" : invoicePrefix.Trim().ToUpperInvariant()
        };
    }

    public void UpdateProfile(string name, string? gstin, bool isComposition, string state, string stateCode)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VAL_020", "Business name is required.");

        var validatedGstin = ValueObjects.Gstin.CreateOptional(gstin);
        if (validatedGstin is not null && validatedGstin.StateCode != stateCode.Trim())
            throw new DomainException("VAL_022", "Business GSTIN state code does not match selected state code.");

        Name = name.Trim();
        Gstin = validatedGstin?.Value;
        Pan = validatedGstin?.Pan;
        IsComposition = isComposition;
        State = state.Trim();
        StateCode = stateCode.Trim();
        MarkUpdated();
    }
}
