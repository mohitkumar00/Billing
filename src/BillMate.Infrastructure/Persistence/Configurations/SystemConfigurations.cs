using BillMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillMate.Infrastructure.Persistence.Configurations;

internal sealed class BackupLogConfiguration : IEntityTypeConfiguration<BackupLog>
{
    public void Configure(EntityTypeBuilder<BackupLog> builder)
    {
        builder.ToTable("backup_logs");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.BackupTarget).HasColumnName("backup_target").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.RemoteFileId).HasColumnName("remote_file_id").HasMaxLength(255);
        builder.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
        builder.Property(x => x.ChecksumSha256).HasColumnName("checksum_sha256").HasColumnType("char(64)");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
    }
}

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.SettingKey).HasColumnName("setting_key").HasMaxLength(120).IsRequired();
        builder.Property(x => x.SettingValue).HasColumnName("setting_value");
        builder.Property(x => x.EncryptedBlob).HasColumnName("encrypted_blob");
        builder.Property(x => x.IsSecret).HasColumnName("is_secret").HasDefaultValue(false).IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.SettingKey }).IsUnique();
    }
}

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.DeviceName).HasColumnName("device_name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Platform).HasColumnName("platform").HasMaxLength(20).IsRequired();
        builder.Property(x => x.DeviceIdentifier).HasColumnName("device_identifier").HasMaxLength(150).IsRequired();
        builder.Property(x => x.AppVersion).HasColumnName("app_version").HasMaxLength(30);
        builder.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(x => x.IsAuthorized).HasColumnName("is_authorized").HasDefaultValue(true).IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.DeviceIdentifier }).IsUnique();
    }
}

internal sealed class SyncQueueItemConfiguration : IEntityTypeConfiguration<SyncQueueItem>
{
    public void Configure(EntityTypeBuilder<SyncQueueItem> builder)
    {
        builder.ToTable("sync_queue");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.DeviceId).HasColumnName("device_id").IsRequired();
        builder.Property(x => x.OperationType).HasColumnName("operation_type").HasMaxLength(40).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.RetryCount).HasColumnName("retry_count").IsRequired();
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message");
        builder.Property(x => x.ClientCreatedAt).HasColumnName("client_created_at").IsRequired();
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");

        builder.HasIndex(x => new { x.BusinessId, x.Status, x.ClientCreatedAt }).HasFilter("status IN ('Pending', 'Failed')");
        builder.HasOne<Device>().WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}
