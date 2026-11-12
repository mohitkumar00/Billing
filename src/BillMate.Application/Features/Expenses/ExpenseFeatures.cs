using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Common.Security;
using BillMate.Domain.Entities;
using BillMate.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Expenses;

public sealed record ExpenseCategoryDto(Guid Id, string Name, string? Description);

public sealed record ExpenseDto(
    Guid Id,
    Guid ExpenseCategoryId,
    string CategoryName,
    decimal Amount,
    PaymentMode PaymentMode,
    DateOnly ExpenseDate,
    string Description);

[RequirePermission("settings.manage")]
public sealed record CreateExpenseCommand(
    Guid ExpenseCategoryId,
    decimal Amount,
    PaymentMode PaymentMode = PaymentMode.Cash,
    string? Description = null,
    DateOnly? ExpenseDate = null) : ICommand<ExpenseDto>;

[RequirePermission("reports.gst")]
public sealed record GetExpensesQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? CategoryId = null) : IQuery<IReadOnlyList<ExpenseDto>>;

public sealed record GetExpenseCategoriesQuery : IQuery<IReadOnlyList<ExpenseCategoryDto>>;

public sealed class CreateExpenseValidator : AbstractValidator<CreateExpenseCommand>
{
    public CreateExpenseValidator()
    {
        RuleFor(x => x.ExpenseCategoryId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Expense amount must be greater than zero.");
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

internal sealed class ExpenseHandlers :
    IRequestHandler<CreateExpenseCommand, ExpenseDto>,
    IRequestHandler<GetExpensesQuery, IReadOnlyList<ExpenseDto>>,
    IRequestHandler<GetExpenseCategoriesQuery, IReadOnlyList<ExpenseCategoryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ExpenseHandlers(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<ExpenseDto> Handle(CreateExpenseCommand request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var category = await _db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId && c.BusinessId == businessId, ct)
                       ?? throw new NotFoundException("EXP_001", "Expense category not found.");

        var userId = _currentUser.UserId ?? Guid.Empty;

        var expense = Expense.Create(
            businessId: businessId,
            expenseCategoryId: request.ExpenseCategoryId,
            createdByUserId: userId,
            amount: request.Amount,
            paymentMode: request.PaymentMode,
            description: string.IsNullOrWhiteSpace(request.Description) ? category.Name : request.Description.Trim(),
            expenseDate: request.ExpenseDate);

        _db.Expenses.Add(expense);
        await _db.SaveChangesAsync(ct);

        return new ExpenseDto(
            Id: expense.Id,
            ExpenseCategoryId: expense.ExpenseCategoryId,
            CategoryName: category.Name,
            Amount: expense.Amount,
            PaymentMode: expense.PaymentMode,
            ExpenseDate: expense.ExpenseDate,
            Description: expense.Description);
    }

    public async Task<IReadOnlyList<ExpenseDto>> Handle(GetExpensesQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var query = _db.Expenses.AsNoTracking().Where(e => e.BusinessId == businessId);

        if (request.CategoryId.HasValue && request.CategoryId.Value != Guid.Empty)
            query = query.Where(e => e.ExpenseCategoryId == request.CategoryId.Value);

        if (request.From.HasValue)
        {
            var fromDate = DateOnly.FromDateTime(request.From.Value.DateTime);
            query = query.Where(e => e.ExpenseDate >= fromDate);
        }

        if (request.To.HasValue)
        {
            var toDate = DateOnly.FromDateTime(request.To.Value.DateTime);
            query = query.Where(e => e.ExpenseDate <= toDate);
        }

        var expenses = await query
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        var categoryIds = expenses.Select(e => e.ExpenseCategoryId).Distinct().ToArray();
        var categories = await _db.ExpenseCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return expenses.Select(e => new ExpenseDto(
            Id: e.Id,
            ExpenseCategoryId: e.ExpenseCategoryId,
            CategoryName: categories.TryGetValue(e.ExpenseCategoryId, out var name) ? name : "General",
            Amount: e.Amount,
            PaymentMode: e.PaymentMode,
            ExpenseDate: e.ExpenseDate,
            Description: e.Description)).ToList();
    }

    public async Task<IReadOnlyList<ExpenseCategoryDto>> Handle(GetExpenseCategoriesQuery request, CancellationToken ct)
    {
        var businessId = _currentUser.BusinessId ?? throw new UnauthorizedAppException("AUTH_003", "Missing business context.");
        var list = await _db.ExpenseCategories.AsNoTracking()
            .Where(c => c.BusinessId == businessId)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return list.Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.Description)).ToList();
    }
}
