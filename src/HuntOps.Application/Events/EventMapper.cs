using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Events;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Events;

public sealed record ActionDto(
    Guid Id,
    Guid EventId,
    string Title,
    string? Kind,
    bool IsOptional,
    string? Notes,
    EffectiveActionStatus Status,
    string? Outcome,
    string? StatusChangedAt,
    bool IsArchived,
    uint Version);

public sealed record EventDto(
    Guid Id,
    Guid ProgramId,
    string ProgramName,
    Guid AgencyId,
    string AgencyName,
    Guid JurisdictionId,
    string JurisdictionCode,
    string EventTypeKey,
    string EventTypeName,
    EventCategory EventCategory,
    string Qualifier,
    int SeasonYear,
    string? SeasonLabel,
    string Name,
    string StartDate,
    string? StartTime,
    string? EndDate,
    string? EndTime,
    string TimeZoneId,
    string StartsAtUtc,
    string? EndsAtUtc,
    bool IsWindow,
    EventPhase Phase,
    string? Description,
    string? SourceUrl,
    OriginKind OriginKind,
    string? ExternalKey,
    VerificationStatus VerificationStatus,
    string? LastVerifiedAt,
    string? VerifiedBy,
    string? LastSourceConfirmedAt,
    bool IsArchived,
    string? ArchivedAt,
    string CreatedAt,
    string UpdatedAt,
    uint Version,
    IReadOnlyList<ActionDto> Actions);

/// <summary>Maps events (with Program→Agency→Jurisdiction, EventType and Actions loaded) to API shapes.</summary>
internal static class EventMapper
{
    public static EventDto ToDto(
        ProgramEvent e,
        IReadOnlyDictionary<Guid, ActionStatusChange> latestChanges,
        Instant now,
        bool includeArchivedActions)
    {
        var actions = e.Actions
            .Where(a => includeArchivedActions || a.ArchivedAt is null)
            .OrderBy(a => a.CreatedAt)
            .Select(a => ToDto(a, e, latestChanges.GetValueOrDefault(a.Id), now))
            .ToList();

        var program = e.Program;
        var agency = program.Agency;
        return new EventDto(
            e.Id, program.Id, program.Name, agency.Id, agency.Name, agency.JurisdictionId, agency.Jurisdiction.Code,
            e.EventTypeKey, e.EventType.DisplayName, e.EventType.Category, e.Qualifier, e.SeasonYear, e.SeasonLabel, e.Name,
            TimeFormats.Format(e.StartDate), TimeFormats.Format(e.StartTime), TimeFormats.Format(e.EndDate), TimeFormats.Format(e.EndTime),
            e.TimeZoneId, TimeFormats.Format(e.StartsAtUtc), TimeFormats.Format(e.EndsAtUtc),
            e.IsWindow, e.PhaseAt(now), e.Description, e.SourceUrl, e.OriginKind, e.ExternalKey,
            e.VerificationStatus, TimeFormats.Format(e.LastVerifiedAt), e.VerifiedBy, TimeFormats.Format(e.LastSourceConfirmedAt),
            e.ArchivedAt is not null, TimeFormats.Format(e.ArchivedAt),
            TimeFormats.Format(e.CreatedAt), TimeFormats.Format(e.UpdatedAt), e.Version,
            actions);
    }

    public static ActionDto ToDto(RequiredAction a, ProgramEvent e, ActionStatusChange? latest, Instant now)
    {
        var status = ActionStatusResolver.Resolve(e, latest, now);
        var resolved = ActionStatusResolver.IsResolved(status);
        return new ActionDto(
            a.Id, a.ProgramEventId, a.Title, a.Kind, a.IsOptional, a.Notes, status,
            resolved ? latest?.Outcome : null,
            TimeFormats.Format(latest?.ChangedAt),
            a.ArchivedAt is not null, a.Version);
    }
}

internal static class ActionStatusLookup
{
    /// <summary>Latest status change per action for one user. Change volumes are tiny, so this resolves in memory.</summary>
    public static async Task<Dictionary<Guid, ActionStatusChange>> LatestAsync(
        IHuntOpsDb db,
        IEnumerable<Guid> actionIds,
        string userId,
        CancellationToken cancellationToken)
    {
        var ids = actionIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var changes = await db.ActionStatusChanges
            .AsNoTracking()
            .Where(c => ids.Contains(c.RequiredActionId) && c.UserId == userId)
            .ToListAsync(cancellationToken);

        return changes
            .GroupBy(c => c.RequiredActionId)
            .ToDictionary(g => g.Key, g => ActionStatusResolver.Latest(g)!);
    }
}
