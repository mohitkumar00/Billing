using BillMate.Domain.Common;
using BillMate.Domain.Enums;
using BillMate.Domain.Exceptions;

namespace BillMate.Domain.Entities;

public sealed class Role : BaseEntity
{
    private readonly List<RolePermission> _permissions = [];

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystemRole { get; private set; }

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    private Role() { }

    public static Role Create(Guid businessId, string name, string? description = null, bool isSystemRole = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("VAL_070", "Role name is required.");

        return new Role
        {
            BusinessId = businessId,
            Name = name.Trim().ToUpperInvariant(),
            Description = description?.Trim(),
            IsSystemRole = isSystemRole
        };
    }

    public RolePermission GrantPermission(string permissionKey)
    {
        var normalized = permissionKey.Trim().ToLowerInvariant();
        var existing = _permissions.FirstOrDefault(p => p.PermissionKey == normalized && !p.IsDeleted);
        if (existing is not null) return existing;

        var permission = RolePermission.Create(BusinessId, Id, normalized);
        _permissions.Add(permission);
        return permission;
    }
}

public sealed class RolePermission : BaseEntity
{
    public Guid RoleId { get; private set; }
    public string PermissionKey { get; private set; } = string.Empty;

    private RolePermission() { }

    internal static RolePermission Create(Guid businessId, Guid roleId, string permissionKey) => new()
    {
        BusinessId = businessId,
        RoleId = roleId,
        PermissionKey = permissionKey
    };
}

public sealed class User : BaseEntity, IAggregateRoot
{
    public Guid RoleId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public string? PinHash { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? LastLoginAt { get; private set; }

    public Role? Role { get; private set; }

    private User() { }

    public static User Create(
        Guid businessId,
        Guid roleId,
        string fullName,
        string email,
        string passwordHash,
        string? phone = null,
        string? pinHash = null)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new DomainException("VAL_071", "User full name is required.");
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("VAL_072", "User email is required.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("VAL_073", "Password hash is required.");

        return new User
        {
            BusinessId = businessId,
            RoleId = roleId,
            FullName = fullName.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            PasswordHash = passwordHash,
            PinHash = pinHash
        };
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void SetPinHash(string? pinHash)
    {
        PinHash = pinHash;
        MarkUpdated();
    }
}

public sealed class RefreshToken : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ReplacedByToken { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(Guid businessId, Guid userId, string tokenHash, DateTimeOffset expiresAt, Guid? deviceId = null) => new()
    {
        BusinessId = businessId,
        UserId = userId,
        DeviceId = deviceId,
        TokenHash = tokenHash,
        ExpiresAt = expiresAt
    };

    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt && !IsDeleted;

    public void Revoke(string? replacedByToken = null)
    {
        RevokedAt = DateTimeOffset.UtcNow;
        ReplacedByToken = replacedByToken;
        MarkUpdated();
    }
}

public sealed class AuditLog : BaseEntity
{
    public Guid? UserId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public string EntityName { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? OldValuesJson { get; private set; }
    public string? NewValuesJson { get; private set; }
    public string? IpAddress { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid businessId,
        Guid? userId,
        string entityName,
        Guid entityId,
        string action,
        string? oldValuesJson = null,
        string? newValuesJson = null,
        Guid? deviceId = null,
        string? ipAddress = null) => new()
    {
        BusinessId = businessId,
        UserId = userId,
        DeviceId = deviceId,
        EntityName = entityName,
        EntityId = entityId,
        Action = action,
        OldValuesJson = oldValuesJson,
        NewValuesJson = newValuesJson,
        IpAddress = ipAddress
    };
}
