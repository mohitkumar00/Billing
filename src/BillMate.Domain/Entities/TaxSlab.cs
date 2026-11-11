using BillMate.Domain.Common;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Entities;

public sealed class TaxSlab : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public decimal TotalRate { get; private set; }
    public decimal CgstRate { get; private set; }
    public decimal SgstRate { get; private set; }
    public decimal IgstRate { get; private set; }
    public decimal CessRate { get; private set; }
    public bool IsDefault { get; private set; }

    private TaxSlab() { }

    public static TaxSlab Create(Guid businessId, string name, decimal totalGstRate, decimal cessRate = 0m, bool isDefault = false)
    {
        if (totalGstRate < 0m || cessRate < 0m)
            throw new DomainException("VAL_013", "Tax rates cannot be negative.");

        return new TaxSlab
        {
            BusinessId = businessId,
            Name = name.Trim(),
            TotalRate = totalGstRate,
            CgstRate = totalGstRate / 2m,
            SgstRate = totalGstRate / 2m,
            IgstRate = totalGstRate,
            CessRate = cessRate,
            IsDefault = isDefault
        };
    }
}
