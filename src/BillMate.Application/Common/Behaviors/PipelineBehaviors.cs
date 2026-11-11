using System.Diagnostics;
using System.Reflection;
using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Common.Security;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BillMate.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly ICurrentUserService _currentUser;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger, ICurrentUserService currentUser)
    {
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken);
            sw.Stop();
            if (sw.ElapsedMilliseconds > 500)
            {
                _logger.LogWarning("Slow request {RequestName} ({ElapsedMs}ms) for Business={BusinessId}, User={UserId}",
                    requestName, sw.ElapsedMilliseconds, _currentUser.BusinessId, _currentUser.UserId);
            }
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Request {RequestName} failed after {ElapsedMs}ms for Business={BusinessId}, User={UserId}",
                requestName, sw.ElapsedMilliseconds, _currentUser.BusinessId, _currentUser.UserId);
            throw;
        }
    }
}

public sealed class PermissionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ICurrentUserService _currentUser;

    public PermissionBehavior(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var attributes = typeof(TRequest).GetCustomAttributes<RequirePermissionAttribute>().ToArray();
        if (attributes.Length == 0)
            return next(cancellationToken);

        if (_currentUser.UserId is null || _currentUser.BusinessId is null)
            throw new UnauthorizedAppException("AUTH_003", "Authentication is required.");

        foreach (var attr in attributes)
        {
            if (!_currentUser.Permissions.Contains(attr.Permission, StringComparer.OrdinalIgnoreCase))
            {
                throw new ForbiddenAppException("AUTH_002", $"Missing required permission: '{attr.Permission}'.");
            }
        }

        return next(cancellationToken);
    }
}

public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next(cancellationToken);

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken)));
        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .GroupBy(f => f.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        if (failures.Count > 0)
            throw new ValidationAppException(failures);

        return await next(cancellationToken);
    }
}

public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    private readonly IApplicationDbContext _db;

    public TransactionBehavior(IApplicationDbContext db)
    {
        _db = db;
    }

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
        _db.ExecuteInTransactionAsync(ct => next(ct), cancellationToken);
}
