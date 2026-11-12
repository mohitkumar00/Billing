using BillMate.Application.Common.Exceptions;
using BillMate.Application.Common.Interfaces;
using BillMate.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BillMate.Application.Features.Auth;

public sealed record AuthResponse(
    Guid UserId,
    Guid BusinessId,
    string FullName,
    string Email,
    string RoleName,
    IReadOnlyCollection<string> Permissions,
    string AccessToken,
    string RefreshToken);

public sealed record LoginCommand(string Email, string Password, Guid? DeviceId = null) : ICommand<AuthResponse>;

public sealed record PinLoginCommand(Guid? UserId, string Pin, Guid? DeviceId = null) : ICommand<AuthResponse>;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthResponse>;

public sealed record LogoutCommand(string RefreshToken) : ICommand<Unit>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class PinLoginCommandValidator : AbstractValidator<PinLoginCommand>
{
    public PinLoginCommandValidator()
    {
        RuleFor(x => x.Pin).NotEmpty().Length(4, 6).Matches(@"^\d+$");
    }
}

internal sealed class AuthHandlers :
    IRequestHandler<LoginCommand, AuthResponse>,
    IRequestHandler<PinLoginCommand, AuthResponse>,
    IRequestHandler<RefreshTokenCommand, AuthResponse>,
    IRequestHandler<LogoutCommand, Unit>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokenService;

    public AuthHandlers(IApplicationDbContext db, IPasswordHasher hasher, ITokenService tokenService)
    {
        _db = db;
        _hasher = hasher;
        _tokenService = tokenService;
    }

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Role!)
            .ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive, ct);

        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAppException("AUTH_001", "Invalid email or password.");

        return await IssueSessionAsync(user, request.DeviceId, ct);
    }

    public async Task<AuthResponse> Handle(PinLoginCommand request, CancellationToken ct)
    {
        User? user;
        if (request.UserId.HasValue && request.UserId.Value != Guid.Empty)
        {
            user = await _db.Users
                .Include(u => u.Role!)
                .ThenInclude(r => r.Permissions)
                .FirstOrDefaultAsync(u => u.Id == request.UserId.Value && u.IsActive, ct);

            if (user is null || string.IsNullOrEmpty(user.PinHash) || !_hasher.Verify(request.Pin, user.PinHash))
                throw new UnauthorizedAppException("AUTH_001", "Invalid cashier PIN.");
        }
        else
        {
            var activeUsers = await _db.Users
                .Include(u => u.Role!)
                .ThenInclude(r => r.Permissions)
                .Where(u => u.IsActive && u.PinHash != null)
                .ToListAsync(ct);

            user = activeUsers.FirstOrDefault(u => _hasher.Verify(request.Pin, u.PinHash!));

            if (user is null)
                throw new UnauthorizedAppException("AUTH_001", "Invalid cashier PIN.");
        }

        return await IssueSessionAsync(user, request.DeviceId, ct);
    }

    public async Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (existing is null || !existing.IsActive)
            throw new UnauthorizedAppException("AUTH_003", "Refresh token is invalid or expired.");

        var user = await _db.Users
            .Include(u => u.Role!)
            .ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.Id == existing.UserId && u.IsActive, ct)
            ?? throw new UnauthorizedAppException("AUTH_003", "User account is inactive.");

        var newRawRefresh = _tokenService.GenerateRefreshTokenRaw();
        var newHash = _tokenService.HashRefreshToken(newRawRefresh);
        existing.Revoke(newHash);

        _db.RefreshTokens.Add(RefreshToken.Create(
            user.BusinessId,
            user.Id,
            newHash,
            DateTimeOffset.UtcNow.AddDays(30),
            existing.DeviceId));

        await _db.SaveChangesAsync(ct);

        var perms = user.Role?.Permissions.Select(p => p.PermissionKey).ToArray() ?? [];
        var accessToken = _tokenService.GenerateAccessToken(
            user.Id, user.BusinessId, user.Email, user.Role?.Name ?? "CASHIER", perms, existing.DeviceId);

        return new AuthResponse(user.Id, user.BusinessId, user.FullName, user.Email, user.Role?.Name ?? "CASHIER", perms, accessToken, newRawRefresh);
    }

    public async Task<Unit> Handle(LogoutCommand request, CancellationToken ct)
    {
        var tokenHash = _tokenService.HashRefreshToken(request.RefreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (existing is not null && existing.IsActive)
        {
            existing.Revoke();
            await _db.SaveChangesAsync(ct);
        }
        return Unit.Value;
    }

    private async Task<AuthResponse> IssueSessionAsync(User user, Guid? deviceId, CancellationToken ct)
    {
        user.RecordLogin();
        var rawRefresh = _tokenService.GenerateRefreshTokenRaw();
        var refreshHash = _tokenService.HashRefreshToken(rawRefresh);

        _db.RefreshTokens.Add(RefreshToken.Create(
            user.BusinessId,
            user.Id,
            refreshHash,
            DateTimeOffset.UtcNow.AddDays(30),
            deviceId));

        await _db.SaveChangesAsync(ct);

        var perms = user.Role?.Permissions.Select(p => p.PermissionKey).ToArray() ?? [];
        var accessToken = _tokenService.GenerateAccessToken(
            user.Id, user.BusinessId, user.Email, user.Role?.Name ?? "CASHIER", perms, deviceId);

        return new AuthResponse(user.Id, user.BusinessId, user.FullName, user.Email, user.Role?.Name ?? "CASHIER", perms, accessToken, rawRefresh);
    }
}
