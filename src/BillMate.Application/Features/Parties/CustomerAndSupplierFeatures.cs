using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Common.Security;
using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Parties;

public sealed record CustomerDto(
    Guid Id,
    string Name,
    string Phone,
    string? Gstin,
    string? StateCode,
    PricingTier PricingTier,
    decimal CreditLimit,
    decimal OutstandingBalance,
    int LoyaltyPoints);

public sealed record CreateCustomerCommand(
    string Name,
    string Phone,
    string? Gstin = null,
    string? State = null,
    string? StateCode = null,
    decimal CreditLimit = 0m,
    PricingTier PricingTier = PricingTier.Retail) : ICommand<CustomerDto>;

public sealed record SearchCustomersQuery(string? Query = null, int Limit = 50) : IQuery<IReadOnlyList<CustomerDto>>;

[RequirePermission("invoices.create")]
public sealed record RecordCustomerPaymentCommand(
    Guid CustomerId,
    Guid? InvoiceId,
    PaymentMode PaymentMode,
    decimal Amount,
    string? ReferenceNumber = null,
    string? Notes = null) : ICommand<CustomerDto>;

public sealed record SupplierDto(Guid Id, string Name, string Phone, string? Gstin, int PaymentTermsDays, decimal OutstandingPayable);

[RequirePermission("purchases.manage")]
public sealed record CreateSupplierCommand(
    string Name,
    string Phone,
    string? ContactPerson = null,
    string? Gstin = null,
    string? State = null,
    string? StateCode = null,
    int PaymentTermsDays = 30) : ICommand<SupplierDto>;

public sealed record PoLineInput(Guid ProductId, decimal OrderedQty, decimal UnitCost, decimal TaxAmount);

[RequirePermission("purchases.manage")]
public sealed record CreatePurchaseOrderCommand(Guid SupplierId, IReadOnlyList<PoLineInput> Items, string? Notes = null) : ICommand<Guid>;

[RequirePermission("purchases.manage")]
public sealed record ReceivePurchaseOrderCommand(Guid PurchaseOrderId, string? SupplierInvoiceNo = null) : ICommand<Unit>;

public sealed class CreateCustomerValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(180);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0);
    }
}

internal sealed class PartyHandlers :
    IRequestHandler<CreateCustomerCommand, CustomerDto>,
    IRequestHandler<SearchCustomersQuery, IReadOnlyList<CustomerDto>>,
    IRequestHandler<RecordCustomerPaymentCommand, CustomerDto>,
    IRequestHandler<CreateSupplierCommand, SupplierDto>,
    IRequestHandler<CreatePurchaseOrderCommand, Guid>,
    IRequestHandler<ReceivePurchaseOrderCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISyncBroadcaster _broadcaster;

    public PartyHandlers(IApplicationDbContext db, ICurrentUserService currentUser, ISyncBroadcaster broadcaster)
    {
        _db = db;
        _currentUser = currentUser;
        _broadcaster = broadcaster;
    }

    public async Task<CustomerDto> Handle(CreateCustomerCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var customer = Customer.Create(businessId, request.Name, request.Phone, request.Gstin, request.State, request.StateCode, request.CreditLimit, request.PricingTier);
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);
        return Map(customer);
    }

    public async Task<IReadOnlyList<CustomerDto>> Handle(SearchCustomersQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var query = _db.Customers.AsNoTracking().Where(c => c.BusinessId == businessId);

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim().ToLowerInvariant();
            query = query.Where(c => c.Name.ToLower().Contains(term) || c.Phone.Contains(term) || (c.Gstin != null && c.Gstin.ToLower().Contains(term)));
        }

        var items = await query.OrderBy(c => c.Name).Take(Math.Clamp(request.Limit, 1, 200)).ToListAsync(ct);
        return items.Select(Map).ToArray();
    }

    public async Task<CustomerDto> Handle(RecordCustomerPaymentCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var userId = _currentUser.UserId ?? throw new UnauthorizedAppException("AUTH_003", "Missing user context.");

        var customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.BusinessId == businessId, ct)
            ?? throw new NotFoundException("CRM_004", "Customer not found.");

        if (request.InvoiceId.HasValue)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == request.InvoiceId.Value && i.BusinessId == businessId, ct)
                ?? throw new NotFoundException("BILL_001", "Invoice not found.");

            invoice.RecordPayment(userId, request.PaymentMode, request.Amount, request.ReferenceNumber, request.Notes);
        }
        else
        {
            _db.Payments.Add(Payment.Create(businessId, null, customer.Id, userId, request.PaymentMode, request.Amount, request.ReferenceNumber, request.Notes));
        }

        customer.RecordPayment(request.Amount);
        await _db.SaveChangesAsync(ct);

        await _broadcaster.BroadcastPaymentReceivedAsync(businessId, customer.Id, request.InvoiceId, request.Amount, ct);
        return Map(customer);
    }

    public async Task<SupplierDto> Handle(CreateSupplierCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var supplier = Supplier.Create(businessId, request.Name, request.Phone, request.ContactPerson, request.Gstin, request.State, request.StateCode, request.PaymentTermsDays);
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct);
        return new SupplierDto(supplier.Id, supplier.Name, supplier.Phone, supplier.Gstin, supplier.PaymentTermsDays, supplier.OutstandingPayable);
    }

    public async Task<Guid> Handle(CreatePurchaseOrderCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var count = await _db.PurchaseOrders.IgnoreQueryFilters().CountAsync(p => p.BusinessId == businessId, ct);
        var po = PurchaseOrder.Create(businessId, request.SupplierId, $"PO-{DateTime.UtcNow:yyyy}-{count + 1:0000}", request.Notes);

        foreach (var item in request.Items)
        {
            po.AddItem(item.ProductId, item.OrderedQty, item.UnitCost, item.TaxAmount);
        }

        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync(ct);
        return po.Id;
    }

    public async Task<Unit> Handle(ReceivePurchaseOrderCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var userId = _currentUser.UserId ?? throw new UnauthorizedAppException("AUTH_003", "Missing user context.");

        var po = await _db.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == request.PurchaseOrderId && p.BusinessId == businessId, ct)
            ?? throw new NotFoundException("PO_004", "Purchase order not found.");

        po.MarkReceived(request.SupplierInvoiceNo);

        var productIds = po.Items.Select(i => i.ProductId).Distinct().ToArray();
        var products = await _db.Products.Where(p => p.BusinessId == businessId && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        foreach (var item in po.Items)
        {
            item.SetReceivedQuantity(item.OrderedQty);
            if (products.TryGetValue(item.ProductId, out var product) && !product.IsService)
            {
                var balanceAfter = product.AddStock(item.OrderedQty);
                _db.StockMovements.Add(StockMovement.Create(
                    businessId, product.Id, StockMovementType.Purchase, item.OrderedQty, balanceAfter, userId, "PURCHASE_ORDER", po.Id, $"GRN {po.PoNumber}"));
            }
        }

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static CustomerDto Map(Customer c) => new(
        c.Id, c.Name, c.Phone, c.Gstin, c.StateCode, c.PricingTier, c.CreditLimit, c.OutstandingBalance, c.LoyaltyPoints);
}
