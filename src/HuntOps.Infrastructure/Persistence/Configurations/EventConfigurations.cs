using HuntOps.Domain.Actions;
using HuntOps.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NodaTime;

namespace HuntOps.Infrastructure.Persistence.Configurations;

internal sealed class EventTypeConfiguration : IEntityTypeConfiguration<EventType>
{
    /// <summary>Fixed timestamp for seeded rows so the model snapshot is deterministic.</summary>
    private static readonly Instant SeededAt = Instant.FromUtc(2026, 10, 4, 0, 0);

    public void Configure(EntityTypeBuilder<EventType> builder)
    {
        builder.ToTable("event_types", t =>
            t.HasCheckConstraint("ck_event_types_key_format", $"key ~ '{EventType.KeyRegex}'"));
        builder.HasKey(t => t.Key);
        builder.Property(t => t.Key).HasMaxLength(EventType.MaxKeyLength);
        builder.Property(t => t.DisplayName).HasMaxLength(100);
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.DefaultActionTitle).HasMaxLength(200);
        builder.Property(t => t.Version).IsRowVersion();
        builder.EnumAsText(t => t.Category, "event_types", "category");

        builder.HasData(SeedTypes());
    }

    /// <summary>
    /// The initial, generic vocabulary. Users can add their own; seeded types can be renamed or archived but
    /// their keys are permanent.
    /// </summary>
    internal static IEnumerable<EventType> SeedTypes() =>
    [
        Seed("application-period", "Application period", EventCategory.Application, window: true,
            "Window during which draw or permit applications are accepted.", "Submit application"),
        Seed("preference-point-period", "Preference/bonus point purchase", EventCategory.Purchase, window: true,
            "Window for buying a preference or bonus point without applying for a tag.", "Purchase preference point"),
        Seed("license-purchase", "License or tag purchase", EventCategory.Purchase, window: true,
            "Window for purchasing a license, tag or permit that was drawn or is otherwise available.", "Purchase license or tag"),
        Seed("otc-sale", "Over-the-counter sale", EventCategory.Purchase, window: false,
            "Date over-the-counter licenses or tags go on sale.", "Purchase OTC tag"),
        Seed("leftover-sale", "Leftover or reissue sale", EventCategory.Purchase, window: false,
            "Sale or draw of leftover, returned or reissued tags.", "Purchase leftover tag"),
        Seed("draw-results", "Draw results", EventCategory.Results, window: false,
            "Date draw results are expected to be published.", null),
        Seed("application-withdrawal", "Application withdrawal or refund deadline", EventCategory.Application, window: false,
            "Last day to withdraw an application, surrender a tag or request a refund.", null),
        Seed("harvest-reporting", "Harvest reporting", EventCategory.Reporting, window: true,
            "Period during which mandatory harvest or survey reports must be submitted.", "Submit harvest report"),
        Seed("season", "Season", EventCategory.Season, window: true,
            "Hunting season dates.", null),
        Seed("other", "Other", EventCategory.Other, window: false,
            "Any other dated item worth tracking.", null),
    ];

    private static EventType Seed(string key, string name, EventCategory category, bool window, string description, string? actionTitle) =>
        new()
        {
            Key = key,
            DisplayName = name,
            Category = category,
            IsWindowByDefault = window,
            Description = description,
            DefaultActionTitle = actionTitle,
            IsSystem = true,
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt,
        };
}

internal sealed class ProgramEventConfiguration : IEntityTypeConfiguration<ProgramEvent>
{
    public void Configure(EntityTypeBuilder<ProgramEvent> builder)
    {
        builder.ToTable("program_events", t =>
        {
            // The database enforces the time invariants as well as the application.
            t.HasCheckConstraint("ck_program_events_end_after_start_date", "end_date IS NULL OR end_date >= start_date");
            t.HasCheckConstraint("ck_program_events_end_time_needs_end_date", "end_time IS NULL OR end_date IS NOT NULL");
            t.HasCheckConstraint("ck_program_events_window_instants", "(end_date IS NULL) = (ends_at_utc IS NULL)");
            t.HasCheckConstraint("ck_program_events_end_after_start", "ends_at_utc IS NULL OR ends_at_utc >= starts_at_utc");
            t.HasCheckConstraint("ck_program_events_season_year", "season_year BETWEEN 1900 AND 2200");
            t.HasCheckConstraint("ck_program_events_name_not_blank", "length(btrim(name)) > 0");
        });
        builder.ConfigureEntity();
        builder.Property(e => e.EventTypeKey).HasMaxLength(EventType.MaxKeyLength);
        builder.Property(e => e.Qualifier).HasMaxLength(100).HasDefaultValue("");
        builder.Property(e => e.SeasonLabel).HasMaxLength(20);
        builder.Property(e => e.Name).HasMaxLength(300);
        builder.Property(e => e.TimeZoneId).HasMaxLength(64);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.SourceUrl).HasMaxLength(2000);
        builder.Property(e => e.ExternalKey).HasMaxLength(200);
        builder.Property(e => e.VerifiedBy).HasMaxLength(200);
        builder.EnumAsText(e => e.OriginKind, "program_events", "origin_kind");
        builder.EnumAsText(e => e.VerificationStatus, "program_events", "verification_status");

        builder.HasOne(e => e.Program)
            .WithMany()
            .HasForeignKey(e => e.ProgramId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EventType)
            .WithMany()
            .HasForeignKey(e => e.EventTypeKey)
            .OnDelete(DeleteBehavior.Restrict);

        // Natural key (architecture §4.2): one active event per program, year, type and qualifier.
        builder.HasIndex(e => new { e.ProgramId, e.SeasonYear, e.EventTypeKey, e.Qualifier })
            .IsUnique().HasFilter(ConfigurationHelpers.ActiveFilter).HasDatabaseName("ux_program_events_natural_key_active");
        builder.HasIndex(e => e.StartsAtUtc);
        builder.HasIndex(e => e.EndsAtUtc);
    }
}

internal sealed class RequiredActionConfiguration : IEntityTypeConfiguration<RequiredAction>
{
    public void Configure(EntityTypeBuilder<RequiredAction> builder)
    {
        builder.ToTable("required_actions", t =>
            t.HasCheckConstraint("ck_required_actions_title_not_blank", "length(btrim(title)) > 0"));
        builder.ConfigureEntity();
        builder.Property(a => a.Title).HasMaxLength(200);
        builder.Property(a => a.Kind).HasMaxLength(50);
        builder.Property(a => a.Notes).HasMaxLength(2000);

        builder.HasOne(a => a.ProgramEvent)
            .WithMany(e => e.Actions)
            .HasForeignKey(a => a.ProgramEventId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ActionStatusChangeConfiguration : IEntityTypeConfiguration<ActionStatusChange>
{
    public void Configure(EntityTypeBuilder<ActionStatusChange> builder)
    {
        // Append-only: a trigger created in the migration rejects UPDATE, DELETE and TRUNCATE.
        builder.ToTable("action_status_changes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Sequence).UseIdentityAlwaysColumn();
        builder.Property(c => c.UserId).HasMaxLength(100);
        builder.Property(c => c.Outcome).HasMaxLength(200);
        builder.Property(c => c.Note).HasMaxLength(2000);
        builder.Property(c => c.ChangedBy).HasMaxLength(200);
        builder.EnumAsText(c => c.Status, "action_status_changes", "status");
        builder.EnumAsText(c => c.ChangedVia, "action_status_changes", "changed_via");

        builder.HasOne(c => c.RequiredAction)
            .WithMany()
            .HasForeignKey(c => c.RequiredActionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.RequiredActionId, c.UserId, c.ChangedAt });
        builder.HasIndex(c => c.Sequence).IsUnique();
    }
}
