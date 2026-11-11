namespace BillMate.Domain.Enums;

public enum BusinessType
{
    Retail,
    Service,
    Restaurant,
    Wholesale
}

public enum InvoiceType
{
    TaxInvoice,
    BillOfSupply,
    Proforma,
    Quotation,
    Challan
}

public enum InvoiceStatus
{
    Draft,
    Hold,
    Confirmed,
    Partial,
    Paid,
    Cancelled
}

public enum PaymentMode
{
    Cash,
    Upi,
    Card,
    BankTransfer,
    Cheque,
    Loyalty
}

public enum StockMovementType
{
    Sale,
    SaleReturn,
    Purchase,
    AdjustmentAdd,
    AdjustmentRemove,
    Opening
}

public enum PricingTier
{
    Retail,
    Wholesale,
    Vip
}

public enum SystemRole
{
    Admin,
    Manager,
    Cashier,
    Accountant
}

public enum PurchaseOrderStatus
{
    Draft,
    Ordered,
    Received,
    Cancelled
}

public enum BackupTarget
{
    LocalDisk,
    LanNas,
    GoogleDrive
}

public enum BackupStatus
{
    Started,
    Verified,
    Uploaded,
    Failed
}

public enum SyncStatus
{
    Pending,
    Processed,
    Conflict,
    Failed
}
