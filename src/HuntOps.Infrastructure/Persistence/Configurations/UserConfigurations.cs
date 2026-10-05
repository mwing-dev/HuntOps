using HuntOps.Domain.Users;
using HuntOps.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HuntOps.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        // At most one owner account.
        builder.HasIndex(u => u.IsOwner).IsUnique().HasFilter("is_owner").HasDatabaseName("ux_users_single_owner");
    }
}

internal sealed class OwnerSettingsConfiguration : IEntityTypeConfiguration<OwnerSettings>
{
    public void Configure(EntityTypeBuilder<OwnerSettings> builder)
    {
        builder.ToTable("owner_settings", t =>
            t.HasCheckConstraint("ck_owner_settings_quiet_hours_distinct", "quiet_hours_start <> quiet_hours_end"));
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.UserId).HasMaxLength(450);
        builder.Property(s => s.TimeZoneId).HasMaxLength(64);
        builder.Property(s => s.Version).IsRowVersion();
        builder.HasOne<AppUser>().WithOne().HasForeignKey<OwnerSettings>(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
