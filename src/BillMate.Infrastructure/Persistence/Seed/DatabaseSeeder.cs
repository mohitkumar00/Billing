using BillMate.Application.Common.Interfaces;
using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Infrastructure.Persistence.Seed;

/// <summary>
/// Idempotently seeds default business, system roles/permissions, Indian GST slabs, starter HSN/SAC codes, and expense categories.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedDefaultsAsync(
        AppDbContext db,
        IPasswordHasher passwordHasher,
        string businessName = "BillMate Demo Shop",
        string state = "Maharashtra",
        string stateCode = "27",
        string adminEmail = "admin@billmate.local",
        string adminPassword = "ChangeMe@123",
        CancellationToken cancellationToken = default)
    {
        if (await db.Businesses.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        var business = Business.Create(
            name: businessName,
            addressLine1: "Main Market Road",
            city: "Mumbai",
            state: state,
            stateCode: stateCode,
            pincode: "400001",
            phone: "9800000000",
            businessType: BusinessType.Retail,
            invoicePrefix: "INV");

        db.Businesses.Add(business);

        // 1. Roles & Permissions (matching SRD Role Matrix)
        var adminRole = Role.Create(business.Id, "ADMIN", "Shop owner with full system access", isSystemRole: true);
        foreach (var perm in new[]
        {
            "invoices.create", "invoices.discount.high", "invoices.cancel", "products.cost.view",
            "products.manage", "purchases.manage", "reports.gst", "settings.manage", "backup.manage"
        })
        {
            adminRole.GrantPermission(perm);
        }

        var managerRole = Role.Create(business.Id, "MANAGER", "Store manager", isSystemRole: true);
        foreach (var perm in new[]
        {
            "invoices.create", "invoices.discount.high", "invoices.cancel",
            "products.manage", "purchases.manage"
        })
        {
            managerRole.GrantPermission(perm);
        }

        var cashierRole = Role.Create(business.Id, "CASHIER", "POS billing operator", isSystemRole: true);
        cashierRole.GrantPermission("invoices.create");

        var accountantRole = Role.Create(business.Id, "ACCOUNTANT", "Finance & GST compliance", isSystemRole: true);
        foreach (var perm in new[] { "products.cost.view", "reports.gst" })
        {
            accountantRole.GrantPermission(perm);
        }

        db.Roles.AddRange(adminRole, managerRole, cashierRole, accountantRole);

        // 2. Default Admin User (PIN 1234)
        var adminUser = User.Create(
            businessId: business.Id,
            roleId: adminRole.Id,
            fullName: "Shop Administrator",
            email: adminEmail,
            passwordHash: passwordHasher.Hash(adminPassword),
            phone: "9800000000",
            pinHash: passwordHasher.Hash("1234"));
        db.Users.Add(adminUser);

        // 3. Standard Indian GST Slabs
        var slab0 = TaxSlab.Create(business.Id, "GST 0% (Exempt)", 0m);
        var slab5 = TaxSlab.Create(business.Id, "GST 5%", 5m);
        var slab12 = TaxSlab.Create(business.Id, "GST 12%", 12m);
        var slab18 = TaxSlab.Create(business.Id, "GST 18%", 18m, isDefault: true);
        var slab28 = TaxSlab.Create(business.Id, "GST 28%", 28m);
        db.TaxSlabs.AddRange(slab0, slab5, slab12, slab18, slab28);

        // 4. Starter Indian HSN/SAC Master Codes
        db.HsnSacCodes.AddRange(
            HsnSacCode.Create(business.Id, "1006", "HSN", "Rice (Non-branded / Packaged)", slab0.Id),
            HsnSacCode.Create(business.Id, "1905", "HSN", "Bread, Pastry, Cakes, Biscuits", slab18.Id),
            HsnSacCode.Create(business.Id, "2202", "HSN", "Aerated & Bottled Beverages", slab28.Id),
            HsnSacCode.Create(business.Id, "3401", "HSN", "Soap & Washing Preparations", slab18.Id),
            HsnSacCode.Create(business.Id, "6109", "HSN", "T-shirts & Apparels", slab5.Id),
            HsnSacCode.Create(business.Id, "8517", "HSN", "Mobile Phones & Telecom Accessories", slab18.Id),
            HsnSacCode.Create(business.Id, "998314", "SAC", "IT & Repair Services", slab18.Id));

        // 5. Default Expense Categories
        db.ExpenseCategories.AddRange(
            ExpenseCategory.Create(business.Id, "Shop Rent", "Monthly premise rent"),
            ExpenseCategory.Create(business.Id, "Electricity & Utilities", "Power, water, internet"),
            ExpenseCategory.Create(business.Id, "Staff Tea & Misc", "Daily petty cash expenses"),
            ExpenseCategory.Create(business.Id, "Transport & Freight", "Inward/outward delivery charges"));

        await db.SaveChangesAsync(cancellationToken);
    }
}
