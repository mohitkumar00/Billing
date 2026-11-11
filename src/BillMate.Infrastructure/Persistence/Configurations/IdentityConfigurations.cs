using BillMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BillMate.Infrastructure.Persistence.Configurations;

internal sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.ToTable("businesses", t =>
        {
            t.HasCheckConstraint("chk_businesses_fy_month", "financial_year_start_month BETWEEN 1 AND 12");
        });
        builder.ConfigureBaseEntity();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200);
        builder.Property(x => x.Gstin).HasColumnName("gstin").HasMaxLength(15);
        builder.Property(x => x.Pan).HasColumnName("pan").HasMaxLength(10);
        builder.Property(x => x.BusinessType).HasColumnName("business_type").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(x => x.IsComposition).HasColumnName("is_composition").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.AddressLine1).HasColumnName("address_line1").HasMaxLength(250).IsRequired();
        builder.Property(x => x.AddressLine2).HasColumnName("address_line2").HasMaxLength(250);
        builder.Property(x => x.City).HasColumnName("city").HasMaxLength(100).IsRequired();
        builder.Property(x => x.State).HasColumnName("state").HasMaxLength(100).IsRequired();
        builder.Property(x => x.StateCode).HasColumnName("state_code").HasColumnType("char(2)").IsRequired();
        builder.Property(x => x.Pincode).HasColumnName("pincode").HasMaxLength(10).IsRequired();
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(150);
        builder.Property(x => x.LogoPath).HasColumnName("logo_path").HasMaxLength(500);
        builder.Property(x => x.SignaturePath).HasColumnName("signature_path").HasMaxLength(500);
        builder.Property(x => x.FinancialYearStartMonth).HasColumnName("financial_year_start_month").HasDefaultValue((short)4).IsRequired();
        builder.Property(x => x.InvoicePrefix).HasColumnName("invoice_prefix").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasColumnType("char(3)").IsRequired();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(250);
        builder.Property(x => x.IsSystemRole).HasColumnName("is_system_role").HasDefaultValue(false).IsRequired();

        builder.HasIndex(x => new { x.BusinessId, x.Name }).IsUnique();
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(x => x.PermissionKey).HasColumnName("permission_key").HasMaxLength(100).IsRequired();

        builder.HasIndex(x => new { x.RoleId, x.PermissionKey }).IsUnique();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.RoleId).HasColumnName("role_id").IsRequired();
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(20);
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
        builder.Property(x => x.PinHash).HasColumnName("pin_hash").HasMaxLength(255);
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.LastLoginAt).HasColumnName("last_login_at");

        builder.HasIndex(x => new { x.BusinessId, x.Email }).IsUnique();
        builder.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Business>().WithMany().HasForeignKey(x => x.BusinessId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.DeviceId).HasColumnName("device_id");
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        builder.Property(x => x.ReplacedByToken).HasColumnName("replaced_by_token").HasMaxLength(255);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");
        builder.ConfigureBaseEntity();

        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.DeviceId).HasColumnName("device_id");
        builder.Property(x => x.EntityName).HasColumnName("entity_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(20).IsRequired();
        builder.Property(x => x.OldValuesJson).HasColumnName("old_values").HasColumnType("jsonb");
        builder.Property(x => x.NewValuesJson).HasColumnName("new_values").HasColumnType("jsonb");
        builder.Property(x => x.IpAddress).HasColumnName("ip_address").HasMaxLength(45);

        builder.HasIndex(x => new { x.BusinessId, x.EntityName, x.EntityId, x.CreatedAt });
    }
}
