using HuntOps.Domain.Actions;
using HuntOps.Domain.Common;
using HuntOps.Domain.Reference;
using NodaTime;

namespace HuntOps.Domain.Events;

public enum OriginKind
{
    Manual,
    Import,
    Source,
    Mcp,
}

public enum VerificationStatus
{
    Unverified,
    Verified,
}

/// <summary>Where an event sits relative to "now".</summary>
public enum EventPhase
{
    Upcoming,
    Open,
    Closed,
}

/// <summary>
/// A dated occurrence for a program in a season year: an application window, draw results, a season, ...
/// Either a point in time (start only) or a window (start + end). Generic: nothing here is specific to a
/// state, species or permit system.
/// </summary>
public sealed class ProgramEvent : Entity, IArchivable
{
    public Guid ProgramId { get; set; }

    public HuntProgram Program { get; set; } = null!;

    public required string EventTypeKey { get; set; }

    public EventType EventType { get; set; } = null!;

    /// <summary>Distinguishes several events of the same type in one year ("Phase 2", "Round 1"). Empty when unused.</summary>
    public string Qualifier { get; set; } = "";

    /// <summary>License/draw year the event belongs to.</summary>
    public int SeasonYear { get; set; }

    /// <summary>Optional display label for split seasons, e.g. "2026-27".</summary>
    public string? SeasonLabel { get; set; }

    public required string Name { get; set; }

    public LocalDate StartDate { get; private set; }

    public LocalTime? StartTime { get; private set; }

    public LocalDate? EndDate { get; private set; }

    public LocalTime? EndTime { get; private set; }

    public string TimeZoneId { get; private set; } = "";

    /// <summary>Computed from the schedule; never set directly.</summary>
    public Instant StartsAtUtc { get; private set; }

    /// <summary>Computed from the schedule; null for point-in-time events.</summary>
    public Instant? EndsAtUtc { get; private set; }

    public string? Description { get; set; }

    public string? SourceUrl { get; set; }

    public OriginKind OriginKind { get; set; } = OriginKind.Manual;

    /// <summary>Identifier assigned by an external source or import, used for matching.</summary>
    public string? ExternalKey { get; set; }

    public VerificationStatus VerificationStatus { get; private set; } = VerificationStatus.Unverified;

    public Instant? LastVerifiedAt { get; private set; }

    public string? VerifiedBy { get; private set; }

    /// <summary>Set when a monitored source re-states the same values (Phase 7). Never implies human verification.</summary>
    public Instant? LastSourceConfirmedAt { get; set; }

    public Instant? ArchivedAt { get; set; }

    public List<RequiredAction> Actions { get; } = [];

    public EventSchedule Schedule => new(StartDate, StartTime, EndDate, EndTime, TimeZoneId);

    public bool IsWindow => EndDate is not null;

    /// <summary>Validates and applies a schedule, recomputing the UTC instants.</summary>
    public void SetSchedule(EventSchedule schedule, IDateTimeZoneProvider zones)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var resolved = schedule.Resolve(zones);

        StartDate = schedule.StartDate;
        StartTime = schedule.StartTime;
        EndDate = schedule.EndDate;
        EndTime = schedule.EndTime;
        TimeZoneId = schedule.TimeZoneId;
        StartsAtUtc = resolved.StartsAt;
        EndsAtUtc = resolved.EndsAt;
    }

    /// <summary>A human confirmed the dates are correct.</summary>
    public void MarkVerified(Instant at, string verifiedBy)
    {
        VerificationStatus = VerificationStatus.Verified;
        LastVerifiedAt = at;
        VerifiedBy = verifiedBy;
    }

    /// <summary>Dates changed without human confirmation (e.g. an edit that does not re-verify).</summary>
    public void MarkUnverified()
    {
        VerificationStatus = VerificationStatus.Unverified;
    }

    public EventPhase PhaseAt(Instant now)
    {
        if (now < StartsAtUtc)
        {
            return EventPhase.Upcoming;
        }

        return EndsAtUtc is { } end && now > end ? EventPhase.Closed : EventPhase.Open;
    }
}
