using System.Text.Json;
using BillMate.Application.Common.Interfaces;
using BillMate.Domain.Common;
using BillMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BillMate.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Automatically maintains UTC audit timestamps and records JSONB snapshots to audit_logs.
/// </summary>
public sealed class AuditableAndAuditLogInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<string> MaskedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(User.PasswordHash),
        nameof(User.PinHash),
        nameof(RefreshToken.TokenHash),
        nameof(AppSetting.EncryptedBlob)
    };

    private readonly ICurrentUserService _currentUser;

    public AuditableAndAuditLogInterceptor(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAuditing(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditing(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditing(DbContext? context)
    {
        if (context is null) return;

        var now = DateTimeOffset.UtcNow;
        var auditLogs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.Entity is AuditLog) continue;

            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(BaseEntity.CreatedAt)).CurrentValue = now;
                entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = now;

                auditLogs.Add(CreateAuditRecord(entry, "CREATE", oldValues: null, newValues: SerializeValues(entry, useOriginal: false)));
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(BaseEntity.UpdatedAt)).CurrentValue = now;

                var isSoftDelete = entry.Property(nameof(BaseEntity.IsDeleted)).IsModified &&
                                   (bool)(entry.Property(nameof(BaseEntity.IsDeleted)).CurrentValue ?? false);

                var action = isSoftDelete ? "DELETE" : "UPDATE";
                auditLogs.Add(CreateAuditRecord(
                    entry,
                    action,
                    oldValues: SerializeValues(entry, useOriginal: true),
                    newValues: SerializeValues(entry, useOriginal: false)));
            }
            else if (entry.State == EntityState.Deleted)
            {
                auditLogs.Add(CreateAuditRecord(entry, "DELETE", oldValues: SerializeValues(entry, useOriginal: true), newValues: null));
            }
        }

        if (auditLogs.Count > 0)
        {
            context.Set<AuditLog>().AddRange(auditLogs);
        }
    }

    private AuditLog CreateAuditRecord(EntityEntry<BaseEntity> entry, string action, string? oldValues, string? newValues)
    {
        var businessId = entry.Entity.BusinessId != Guid.Empty
            ? entry.Entity.BusinessId
            : _currentUser.BusinessId ?? Guid.Empty;

        return AuditLog.Create(
            businessId: businessId,
            userId: _currentUser.UserId,
            entityName: entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
            entityId: entry.Entity.Id,
            action: action,
            oldValuesJson: oldValues,
            newValuesJson: newValues,
            deviceId: _currentUser.DeviceId,
            ipAddress: _currentUser.IpAddress);
    }

    private static string SerializeValues(EntityEntry<BaseEntity> entry, bool useOriginal)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            var name = prop.Metadata.Name;
            if (MaskedProperties.Contains(name))
            {
                dict[name] = "[REDACTED]";
                continue;
            }

            if (useOriginal && entry.State == EntityState.Modified && !prop.IsModified)
                continue;

            dict[name] = useOriginal ? prop.OriginalValue : prop.CurrentValue;
        }
        return JsonSerializer.Serialize(dict);
    }
}
