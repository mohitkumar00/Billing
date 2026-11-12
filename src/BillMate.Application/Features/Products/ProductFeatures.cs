using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Common.Security;
using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Products;

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string? Barcode,
    string Name,
    string Unit,
    string? HsnSacCode,
    Guid TaxSlabId,
    decimal GstRate,
    decimal SellingPrice,
    decimal Mrp,
    decimal CurrentStock,
    decimal LowStockThreshold,
    bool IsService,
    bool IsTaxInclusive,
    bool IsFavourite,
    int RowVersion,
    string? ImagePath = null);

public sealed record SearchProductsQuery(string? Query = null, int Limit = 50) : IQuery<IReadOnlyList<ProductDto>>;

public sealed record GetLowStockProductsQuery : IQuery<IReadOnlyList<ProductDto>>;

[RequirePermission("products.manage")]
public sealed record CreateProductCommand(
    Guid TaxSlabId,
    string Sku,
    string Name,
    decimal SellingPrice,
    decimal PurchasePrice = 0m,
    decimal Mrp = 0m,
    decimal OpeningStock = 0m,
    decimal LowStockThreshold = 5m,
    string Unit = "PCS",
    string? Barcode = null,
    string? HsnSacCode = null,
    bool IsService = false,
    bool IsTaxInclusive = true,
    string? ImagePath = null) : ICommand<ProductDto>;

[RequirePermission("products.manage")]
public sealed record AdjustStockCommand(
    Guid ProductId,
    decimal QuantityDelta,
    string Reason) : ICommand<ProductDto>;

public sealed record StockAdjustBody(decimal QuantityDelta, string Reason);

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.TaxSlabId).NotEmpty();
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OpeningStock).GreaterThanOrEqualTo(0);
    }
}

public sealed class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.QuantityDelta).NotEqual(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(250);
    }
}

internal sealed class ProductHandlers :
    IRequestHandler<SearchProductsQuery, IReadOnlyList<ProductDto>>,
    IRequestHandler<GetLowStockProductsQuery, IReadOnlyList<ProductDto>>,
    IRequestHandler<CreateProductCommand, ProductDto>,
    IRequestHandler<AdjustStockCommand, ProductDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ISyncBroadcaster _broadcaster;

    public ProductHandlers(IApplicationDbContext db, ICurrentUserService currentUser, ISyncBroadcaster broadcaster)
    {
        _db = db;
        _currentUser = currentUser;
        _broadcaster = broadcaster;
    }

    public async Task<IReadOnlyList<ProductDto>> Handle(SearchProductsQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var query = _db.Products
            .AsNoTracking()
            .Include(p => p.TaxSlab)
            .Where(p => p.BusinessId == businessId && p.IsActive);

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var term = request.Query.Trim().ToLowerInvariant();
            query = query.Where(p =>
                p.Name.ToLower().Contains(term) ||
                p.Sku.ToLower().Contains(term) ||
                (p.Barcode != null && p.Barcode.ToLower().Contains(term)));
        }

        var items = await query
            .OrderByDescending(p => p.IsFavourite)
            .ThenBy(p => p.Name)
            .Take(Math.Clamp(request.Limit, 1, 200))
            .ToListAsync(ct);

        return items.Select(p => Map(p)).ToArray();
    }

    public async Task<IReadOnlyList<ProductDto>> Handle(GetLowStockProductsQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var items = await _db.Products
            .AsNoTracking()
            .Include(p => p.TaxSlab)
            .Where(p => p.BusinessId == businessId && !p.IsService && p.IsActive && p.CurrentStock <= p.LowStockThreshold)
            .OrderBy(p => p.CurrentStock)
            .ToListAsync(ct);

        return items.Select(p => Map(p)).ToArray();
    }

    public async Task<ProductDto> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var userId = _currentUser.UserId ?? throw new UnauthorizedAppException("AUTH_003", "Missing user context.");

        var slab = await _db.TaxSlabs.FirstOrDefaultAsync(t => t.Id == request.TaxSlabId && t.BusinessId == businessId, ct)
            ?? throw new NotFoundException("TAX_001", "Tax slab not found.");

        var product = Product.Create(
            businessId: businessId,
            taxSlabId: slab.Id,
            sku: request.Sku,
            name: request.Name,
            sellingPrice: request.SellingPrice,
            purchasePrice: request.PurchasePrice,
            mrp: request.Mrp,
            openingStock: request.OpeningStock,
            lowStockThreshold: request.LowStockThreshold,
            unit: request.Unit,
            barcode: request.Barcode,
            hsnSacCode: request.HsnSacCode,
            isService: request.IsService,
            isTaxInclusive: request.IsTaxInclusive,
            imagePath: request.ImagePath);

        _db.Products.Add(product);

        if (!product.IsService && request.OpeningStock > 0m)
        {
            _db.StockMovements.Add(StockMovement.Create(
                businessId,
                product.Id,
                StockMovementType.Opening,
                request.OpeningStock,
                product.CurrentStock,
                userId,
                "PRODUCT",
                product.Id,
                "Opening stock"));
        }

        await _db.SaveChangesAsync(ct);
        return Map(product, slab.TotalRate);
    }

    public async Task<ProductDto> Handle(AdjustStockCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var userId = _currentUser.UserId ?? throw new UnauthorizedAppException("AUTH_003", "Missing user context.");

        var product = await _db.Products
            .Include(p => p.TaxSlab)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.BusinessId == businessId, ct)
            ?? throw new NotFoundException("INV_001", "Product not found.");

        var newBalance = request.QuantityDelta > 0m
            ? product.AddStock(request.QuantityDelta)
            : product.DeductStock(Math.Abs(request.QuantityDelta));

        var movementType = request.QuantityDelta > 0m
            ? StockMovementType.AdjustmentAdd
            : StockMovementType.AdjustmentRemove;

        _db.StockMovements.Add(StockMovement.Create(
            businessId,
            product.Id,
            movementType,
            request.QuantityDelta,
            newBalance,
            userId,
            "ADJUSTMENT",
            product.Id,
            request.Reason));

        await _db.SaveChangesAsync(ct);
        await _broadcaster.BroadcastStockUpdatedAsync(businessId, product.Id, null, newBalance, ct);

        return Map(product);
    }

    private static ProductDto Map(Product p) => Map(p, p.TaxSlab?.TotalRate ?? 0m);

    private static ProductDto Map(Product p, decimal gstRate) => new(
        p.Id,
        p.Sku,
        p.Barcode,
        p.Name,
        p.Unit,
        p.HsnSacCode,
        p.TaxSlabId,
        gstRate,
        p.SellingPrice,
        p.Mrp,
        p.CurrentStock,
        p.LowStockThreshold,
        p.IsService,
        p.IsTaxInclusive,
        p.IsFavourite,
        p.RowVersion,
        p.ImagePath);
}
