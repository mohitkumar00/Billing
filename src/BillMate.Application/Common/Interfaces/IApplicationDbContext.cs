using BillMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BillMate.Application.Common.Interfaces;

/// <summary>
/// Persistence abstraction exposing all 28 BillMate tables and explicit transaction control.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Business> Businesses { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<TaxSlab> TaxSlabs { get; }
    DbSet<HsnSacCode> HsnSacCodes { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<ProductBatch> ProductBatches { get; }

    DbSet<Customer> Customers { get; }
    DbSet<Supplier> Suppliers { get; }

    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceItem> InvoiceItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<CreditNote> CreditNotes { get; }

    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderItem> PurchaseOrderItems { get; }
    DbSet<StockMovement> StockMovements { get; }

    DbSet<ExpenseCategory> ExpenseCategories { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<LoyaltyTransaction> LoyaltyTransactions { get; }

    DbSet<BackupLog> BackupLogs { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<Device> Devices { get; }
    DbSet<SyncQueueItem> SyncQueue { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default);
    Task AcquireBusinessInvoiceLockAsync(Guid businessId, CancellationToken cancellationToken = default);
}
