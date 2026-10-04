using System.Text.Json;
using HuntOps.Domain.Reference;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HuntOps.Infrastructure.Persistence.Configurations;

internal sealed class JurisdictionConfiguration : IEntityTypeConfiguration<Jurisdiction>
{
    public void Configure(EntityTypeBuilder<Jurisdiction> builder)
    {
        builder.ToTable("jurisdictions", t =>
        {
            t.HasCheckConstraint("ck_jurisdictions_code_upper", "code = upper(code) AND length(code) BETWEEN 1 AND 10");
            t.HasCheckConstraint("ck_jurisdictions_country", "country ~ '^[A-Z]{2}$'");
            t.HasCheckConstraint("ck_jurisdictions_name_not_blank", "length(btrim(name)) > 0");
        });
        builder.ConfigureEntity();
        builder.Property(j => j.Name).HasMaxLength(200);
        builder.Property(j => j.NameKey).HasMaxLength(200);
        builder.Property(j => j.Code).HasMaxLength(10);
        builder.Property(j => j.Country).HasMaxLength(2);
        builder.Property(j => j.TimeZoneId).HasMaxLength(64);
        builder.Property(j => j.WebsiteUrl).HasMaxLength(2000);
        builder.Property(j => j.Notes).HasMaxLength(4000);

        builder.HasIndex(j => j.Code).IsUnique().HasFilter(ConfigurationHelpers.ActiveFilter).HasDatabaseName("ux_jurisdictions_code_active");
        builder.HasIndex(j => j.NameKey).IsUnique().HasFilter(ConfigurationHelpers.ActiveFilter).HasDatabaseName("ux_jurisdictions_name_active");
    }
}

internal sealed class AgencyConfiguration : IEntityTypeConfiguration<Agency>
{
    public void Configure(EntityTypeBuilder<Agency> builder)
    {
        builder.ToTable("agencies", t => t.HasCheckConstraint("ck_agencies_name_not_blank", "length(btrim(name)) > 0"));
        builder.ConfigureEntity();
        builder.Property(a => a.Name).HasMaxLength(200);
        builder.Property(a => a.NameKey).HasMaxLength(200);
        builder.Property(a => a.Abbreviation).HasMaxLength(20);
        builder.Property(a => a.WebsiteUrl).HasMaxLength(2000);
        builder.Property(a => a.Notes).HasMaxLength(4000);

        builder.HasOne(a => a.Jurisdiction)
            .WithMany(j => j.Agencies)
            .HasForeignKey(a => a.JurisdictionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.JurisdictionId, a.NameKey })
            .IsUnique().HasFilter(ConfigurationHelpers.ActiveFilter).HasDatabaseName("ux_agencies_jurisdiction_name_active");
    }
}

internal sealed class HuntProgramConfiguration : IEntityTypeConfiguration<HuntProgram>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    public void Configure(EntityTypeBuilder<HuntProgram> builder)
    {
        builder.ToTable("programs", t =>
        {
            t.HasCheckConstraint("ck_programs_not_own_parent", "parent_program_id IS NULL OR parent_program_id <> id");
            t.HasCheckConstraint("ck_programs_slug_format", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
            t.HasCheckConstraint("ck_programs_name_not_blank", "length(btrim(name)) > 0");
        });
        builder.ConfigureEntity();
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.NameKey).HasMaxLength(200);
        builder.Property(p => p.Slug).HasMaxLength(100);
        builder.Property(p => p.Species).HasMaxLength(100);
        builder.Property(p => p.Method).HasMaxLength(100);
        builder.Property(p => p.Residency).HasMaxLength(100);
        builder.Property(p => p.PermitType).HasMaxLength(100);
        builder.Property(p => p.Unit).HasMaxLength(100);
        builder.Property(p => p.Category).HasMaxLength(100);
        builder.Property(p => p.WebsiteUrl).HasMaxLength(2000);
        builder.Property(p => p.Notes).HasMaxLength(4000);

        builder.Property(p => p.Attributes)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions) ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                    v => v.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                    v => new Dictionary<string, string>(v)));

        builder.HasOne(p => p.Agency)
            .WithMany(a => a.Programs)
            .HasForeignKey(p => p.AgencyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ParentProgram)
            .WithMany()
            .HasForeignKey(p => p.ParentProgramId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.AgencyId, p.NameKey })
            .IsUnique().HasFilter(ConfigurationHelpers.ActiveFilter).HasDatabaseName("ux_programs_agency_name_active");
        builder.HasIndex(p => p.Slug)
            .IsUnique().HasFilter(ConfigurationHelpers.ActiveFilter).HasDatabaseName("ux_programs_slug_active");
        builder.HasIndex(p => p.Species);
    }
}
