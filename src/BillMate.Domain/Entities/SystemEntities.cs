using BillMate.Domain.Common;
using BillMate.Domain.Enums;

namespace BillMate.Domain.Entities;

public sealed class BackupLog : BaseEntity
{
    public BackupTarget BackupTarget { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string? RemoteFileId { get; private set; }
    public long FileSizeBytes { get; private set; }
    public string? ChecksumSha256 { get; private set; }
    public BackupStatus Status { get; private set; } = BackupStatus.Started;
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }

    private BackupLog() { }

    public static BackupLog Start(Guid businessId, BackupTarget target, string fileName) => new()
    {
        BusinessId = businessId,
        BackupTarget = target,
        FileName = fileName,
        Status = BackupStatus.Started
    };

    public void MarkVerified(long fileSizeBytes, string checksumSha256)
    {
        FileSizeBytes = fileSizeBytes;
        ChecksumSha256 = checksumSha256;
        Status = BackupStatus.Verified;
        MarkUpdated();
    }

    public void MarkUploaded(string? remoteFileId)
    {
        RemoteFileId = remoteFileId;
        Status = BackupStatus.Uploaded;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void MarkFailed(string errorMessage)
    {
        Status = BackupStatus.Failed;
        ErrorMessage = errorMessage;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }
}

public sealed class AppSetting : BaseEntity
{
    public string SettingKey { get; private set; } = string.Empty;
    public string? SettingValue { get; private set; }
    public byte[]? EncryptedBlob { get; private set; }
    public bool IsSecret { get; private set; }

    private AppSetting() { }

    public static AppSetting Create(Guid businessId, string key, string? value, byte[]? encryptedBlob = null, bool isSecret = false) => new()
    {
        BusinessId = businessId,
        SettingKey = key.Trim(),
        SettingValue = isSecret ? null : value,
        EncryptedBlob = encryptedBlob,
        IsSecret = isSecret
    };

    public void UpdateValue(string? value, byte[]? encryptedBlob = null)
    {
        SettingValue = IsSecret ? null : value;
        EncryptedBlob = encryptedBlob;
        MarkUpdated();
    }
}

public sealed class Device : BaseEntity
{
    public string DeviceName { get; private set; } = string.Empty;
    public string Platform { get; private set; } = "WINDOWS";
    public string DeviceIdentifier { get; private set; } = string.Empty;
    public string? AppVersion { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public bool IsAuthorized { get; private set; } = true;

    private Device() { }

    public static Device Register(Guid businessId, string deviceName, string platform, string deviceIdentifier, string? appVersion = null) => new()
    {
        BusinessId = businessId,
        DeviceName = deviceName.Trim(),
        Platform = platform.Trim().ToUpperInvariant(),
        DeviceIdentifier = deviceIdentifier.Trim(),
        AppVersion = appVersion,
        LastSeenAt = DateTimeOffset.UtcNow
    };

    public void Touch(string? appVersion = null)
    {
        LastSeenAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(appVersion)) AppVersion = appVersion;
        MarkUpdated();
    }
}

public sealed class SyncQueueItem : BaseEntity
{
    public Guid DeviceId { get; private set; }
    public string OperationType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string PayloadJson { get; private set; } = "{}";
    public SyncStatus Status { get; private set; } = SyncStatus.Pending;
    public int RetryCount { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset ClientCreatedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    private SyncQueueItem() { }

    public static SyncQueueItem Enqueue(
        Guid businessId,
        Guid deviceId,
        string operationType,
        Guid entityId,
        string payloadJson,
        DateTimeOffset clientCreatedAt) => new()
    {
        BusinessId = businessId,
        DeviceId = deviceId,
        OperationType = operationType.Trim().ToUpperInvariant(),
        EntityId = entityId,
        PayloadJson = payloadJson,
        ClientCreatedAt = clientCreatedAt
    };

    public void MarkProcessed()
    {
        Status = SyncStatus.Processed;
        ProcessedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void MarkConflict(string reason)
    {
        Status = SyncStatus.Conflict;
        ErrorMessage = reason;
        MarkUpdated();
    }

    public void MarkFailed(string errorMessage)
    {
        Status = SyncStatus.Failed;
        RetryCount++;
        ErrorMessage = errorMessage;
        MarkUpdated();
    }
}
