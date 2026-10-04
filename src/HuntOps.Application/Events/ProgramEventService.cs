using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Common;
using HuntOps.Domain.Events;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Events;

public sealed record EventInput(
    Guid? ProgramId,
    string? EventTypeKey,
    int? SeasonYear,
    string? StartDate,
    string? StartTime = null,
    string? EndDate = null,
    string? EndTime = null,
    string? TimeZoneId = null,
    string? Qualifier = null,
    string? SeasonLabel = null,
    string? Name = null,
    string? Description = null,
    string? SourceUrl = null,
    string? ExternalKey = null,
    bool? Verified = null,
    IReadOnlyList<ActionInput>? Actions = null,
    uint? Version = null);

public sealed record EventFilter(
    Guid? ProgramId = null,
    Guid? AgencyId = null,
    Guid? JurisdictionId = null,
    string? Species = null,
    int? SeasonYear = null,
    string? EventTypeKey = null,
    string? From = null,
    string? To = null,
    bool IncludeArchived = false,
    int? Skip = null,
    int? Take = null);

/// <summary>
/// The single write path for <see cref="ProgramEvent"/>. Manual entry, REST, and (later) MCP and
/// proposal approval all go through this service.
/// </summary>
public sealed class ProgramEventService(IHuntOpsDb db, IClock clock, IDateTimeZoneProvider zones, ICurrentActor actor)
{
    public const int MinSeasonYear = 1900;
    public const int MaxSeasonYear = 2200;
    public const int MaxUpcomingDays = 730;
    private const string Resource = "Event";

    public async Task<PagedResult<EventDto>> ListAsync(EventFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = ApplyFilter(WithDetails().AsNoTracking(), filter);
        var (skip, take) = Paging.Normalize(filter.Skip, filter.Take);
        var total = await query.CountAsync(cancellationToken);
        var events = await query.OrderBy(e => e.StartsAtUtc).ThenBy(e => e.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return new PagedResult<EventDto>(await ToDtosAsync(events, cancellationToken), total, skip, take);
    }

    /// <summary>
    /// Events that start within the next <paramref name="days"/> days, plus windows that are open right now.
    /// Archived events and events of archived programs are excluded.
    /// </summary>
    public async Task<IReadOnlyList<EventDto>> UpcomingAsync(int? days, EventFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var v = new InputValidator(zones);
        var horizonDays = v.IntRange("days", days ?? 60, 1, MaxUpcomingDays, required: true) ?? 60;
        v.ThrowIfInvalid();

        var now = clock.GetCurrentInstant();
        var horizon = now + Duration.FromDays(horizonDays);
        var query = ApplyFilter(WithDetails().AsNoTracking(), filter with { IncludeArchived = false })
            .Where(e => e.StartsAtUtc <= horizon && (e.EndsAtUtc ?? e.StartsAtUtc) >= now);

        var events = await query.OrderBy(e => e.StartsAtUtc).ThenBy(e => e.Id).Take(500).ToListAsync(cancellationToken);
        return await ToDtosAsync(events, cancellationToken);
    }

    public async Task<EventDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var programEvent = await LoadAsync(id, tracking: false, cancellationToken);
        return await ToDtoAsync(programEvent, includeArchivedActions: true, cancellationToken);
    }

    public async Task<EventDto> CreateAsync(EventInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var v = new InputValidator(zones);
        var fields = await ValidateAsync(v, input, existing: null, cancellationToken);
        var actions = ValidateActions(v, input.Actions);
        v.ThrowIfInvalid();

        var programEvent = new ProgramEvent
        {
            EventTypeKey = fields.EventType.Key,
            Name = "",
            OriginKind = actor.Channel == ChangeChannel.Mcp ? OriginKind.Mcp : OriginKind.Manual,
        };
        Apply(programEvent, fields);

        if (input.Verified == true)
        {
            programEvent.MarkVerified(clock.GetCurrentInstant(), actor.ActorId);
        }

        foreach (var action in actions)
        {
            programEvent.Actions.Add(new RequiredAction
            {
                Title = action.Title,
                Kind = action.Kind,
                IsOptional = action.IsOptional,
                Notes = action.Notes,
            });
        }

        db.ProgramEvents.Add(programEvent);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(programEvent.Id, cancellationToken);
    }

    /// <summary>
    /// Full update. Changing the dates of a verified event makes it unverified unless the request
    /// explicitly re-verifies (<c>verified: true</c>). Actions are managed through their own endpoints.
    /// </summary>
    public async Task<EventDto> UpdateAsync(Guid id, EventInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var programEvent = await LoadAsync(id, tracking: true, cancellationToken);
        var v = new InputValidator(zones);
        var fields = await ValidateAsync(v, input, programEvent, cancellationToken);
        var version = v.RequiredVersion(input.Version);
        if (input.Actions is not null)
        {
            v.Add("actions", "Actions cannot be replaced through an event update; use the event's actions endpoints.");
        }

        v.ThrowIfInvalid();

        db.ExpectVersion(programEvent, version);
        var scheduleChanged = programEvent.Schedule != fields.Schedule;
        Apply(programEvent, fields);

        switch (input.Verified)
        {
            case true:
                programEvent.MarkVerified(clock.GetCurrentInstant(), actor.ActorId);
                break;
            case false:
                programEvent.MarkUnverified();
                break;
            case null when scheduleChanged:
                programEvent.MarkUnverified();
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(programEvent.Id, cancellationToken);
    }

    /// <summary>A human confirms the event's dates are correct.</summary>
    public async Task<EventDto> VerifyAsync(Guid id, CancellationToken cancellationToken)
    {
        var programEvent = await LoadAsync(id, tracking: true, cancellationToken);
        programEvent.MarkVerified(clock.GetCurrentInstant(), actor.ActorId);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Soft delete: the event and its action history are kept but hidden from active lists.</summary>
    public async Task<EventDto> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var programEvent = await LoadAsync(id, tracking: true, cancellationToken);
        if (programEvent.ArchivedAt is null)
        {
            programEvent.ArchivedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<EventDto> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var programEvent = await LoadAsync(id, tracking: true, cancellationToken);
        if (programEvent.ArchivedAt is not null)
        {
            programEvent.Program.EnsureActive("programId", "The event's program");
            await EnsureNaturalKeyFreeAsync(programEvent.Id, programEvent.ProgramId, programEvent.SeasonYear, programEvent.EventTypeKey, programEvent.Qualifier, cancellationToken);
            programEvent.ArchivedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    internal static List<ValidatedAction> ValidateActions(InputValidator v, IReadOnlyList<ActionInput>? actions)
    {
        var result = new List<ValidatedAction>();
        if (actions is null)
        {
            return result;
        }

        if (actions.Count > 20)
        {
            v.Add("actions", "At most 20 actions can be created with an event.");
            return result;
        }

        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (action is null)
            {
                v.Add($"actions[{i}]", "An action is required.");
                continue;
            }

            result.Add(ActionService.ValidateAction(v, action, $"actions[{i}]."));
        }

        return result;
    }

    private IQueryable<ProgramEvent> WithDetails() =>
        db.ProgramEvents
            .Include(e => e.Program).ThenInclude(p => p.Agency).ThenInclude(a => a.Jurisdiction)
            .Include(e => e.EventType)
            .Include(e => e.Actions);

    private async Task<ProgramEvent> LoadAsync(Guid id, bool tracking, CancellationToken cancellationToken)
    {
        var query = tracking ? WithDetails() : WithDetails().AsNoTracking();
        return await query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException(Resource, id);
    }

    private IQueryable<ProgramEvent> ApplyFilter(IQueryable<ProgramEvent> query, EventFilter filter)
    {
        var v = new InputValidator(zones);
        var from = v.Date("from", filter.From, required: false);
        var to = v.Date("to", filter.To, required: false);
        if (from is { } f && to is { } t && t < f)
        {
            v.Add("to", "'to' must not be before 'from'.");
        }

        v.ThrowIfInvalid();

        if (!filter.IncludeArchived)
        {
            query = query.Where(e => e.ArchivedAt == null && e.Program.ArchivedAt == null);
        }

        if (filter.ProgramId is { } programId)
        {
            query = query.Where(e => e.ProgramId == programId);
        }

        if (filter.AgencyId is { } agencyId)
        {
            query = query.Where(e => e.Program.AgencyId == agencyId);
        }

        if (filter.JurisdictionId is { } jurisdictionId)
        {
            query = query.Where(e => e.Program.Agency.JurisdictionId == jurisdictionId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Species))
        {
            var species = filter.Species.Trim().ToLowerInvariant();
            query = query.Where(e => e.Program.Species != null && e.Program.Species.ToLower() == species);
        }

        if (filter.SeasonYear is { } year)
        {
            query = query.Where(e => e.SeasonYear == year);
        }

        if (!string.IsNullOrWhiteSpace(filter.EventTypeKey))
        {
            var key = filter.EventTypeKey.Trim();
            query = query.Where(e => e.EventTypeKey == key);
        }

        // Overlap on local dates: the event touches [from, to].
        if (from is { } fromDate)
        {
            query = query.Where(e => (e.EndDate ?? e.StartDate) >= fromDate);
        }

        if (to is { } toDate)
        {
            query = query.Where(e => e.StartDate <= toDate);
        }

        return query;
    }

    private async Task<ValidatedEvent> ValidateAsync(InputValidator v, EventInput input, ProgramEvent? existing, CancellationToken cancellationToken)
    {
        var programId = v.RequiredId("programId", input.ProgramId);
        var eventTypeKey = v.RequiredText("eventTypeKey", input.EventTypeKey, EventType.MaxKeyLength);
        var seasonYear = v.IntRange("seasonYear", input.SeasonYear, MinSeasonYear, MaxSeasonYear, required: true) ?? 0;
        var startDate = v.Date("startDate", input.StartDate, required: true);
        var startTime = v.Time("startTime", input.StartTime);
        var endDate = v.Date("endDate", input.EndDate, required: false);
        var endTime = v.Time("endTime", input.EndTime);
        var timeZoneId = v.TimeZone("timeZoneId", input.TimeZoneId, required: false);
        var qualifier = v.OptionalText("qualifier", input.Qualifier) ?? "";
        var seasonLabel = v.OptionalText("seasonLabel", input.SeasonLabel, 20);
        var name = v.OptionalText("name", input.Name, 300);
        var description = v.OptionalText("description", input.Description, InputValidator.NotesMaxLength);
        var sourceUrl = v.OptionalUrl("sourceUrl", input.SourceUrl);
        var externalKey = v.OptionalText("externalKey", input.ExternalKey, InputValidator.NameMaxLength);
        v.ThrowIfInvalid();

        var program = existing?.ProgramId == programId
            ? existing.Program
            : await db.Programs.Include(p => p.Agency).ThenInclude(a => a.Jurisdiction).FirstOrDefaultAsync(p => p.Id == programId, cancellationToken)
              ?? throw new RequestValidationException("programId", "Program not found.");
        if (existing?.ProgramId != programId)
        {
            program.EnsureActive("programId", "The program");
        }

        var eventType = existing?.EventTypeKey == eventTypeKey
            ? existing.EventType
            : await db.EventTypes.FindAsync([eventTypeKey], cancellationToken)
              ?? throw new RequestValidationException("eventTypeKey", $"Unknown event type '{eventTypeKey}'. List them at GET /api/event-types.");
        if (existing?.EventTypeKey != eventTypeKey)
        {
            eventType.EnsureActive("eventTypeKey", $"Event type '{eventTypeKey}'");
        }

        var schedule = new EventSchedule(startDate!.Value, startTime, endDate, endTime, timeZoneId ?? program.Agency.Jurisdiction.TimeZoneId);
        try
        {
            schedule.Resolve(zones);
        }
        catch (DomainValidationException ex)
        {
            throw new RequestValidationException(ex.Errors);
        }

        await EnsureNaturalKeyFreeAsync(existing?.Id ?? Guid.Empty, program.Id, seasonYear, eventType.Key, qualifier, cancellationToken);

        return new ValidatedEvent(program, eventType, seasonYear, schedule, qualifier, seasonLabel, name, description, sourceUrl, externalKey);
    }

    private void Apply(ProgramEvent target, ValidatedEvent fields)
    {
        target.ProgramId = fields.Program.Id;
        target.Program = fields.Program;
        target.EventTypeKey = fields.EventType.Key;
        target.EventType = fields.EventType;
        target.SeasonYear = fields.SeasonYear;
        target.Qualifier = fields.Qualifier;
        target.SeasonLabel = fields.SeasonLabel;
        target.Name = fields.Name ?? DefaultName(fields);
        target.Description = fields.Description;
        target.SourceUrl = fields.SourceUrl;
        target.ExternalKey = fields.ExternalKey;
        target.SetSchedule(fields.Schedule, zones);
    }

    private static string DefaultName(ValidatedEvent fields)
    {
        var qualifier = fields.Qualifier.Length > 0 ? $" ({fields.Qualifier})" : "";
        var name = $"{fields.Program.Name} – {fields.EventType.DisplayName}{qualifier} {fields.SeasonYear}";
        return name.Length <= 300 ? name : name[..300];
    }

    private async Task EnsureNaturalKeyFreeAsync(Guid id, Guid programId, int seasonYear, string eventTypeKey, string qualifier, CancellationToken cancellationToken)
    {
        if (await db.ProgramEvents.AnyAsync(
                e => e.ArchivedAt == null && e.Id != id && e.ProgramId == programId && e.SeasonYear == seasonYear
                     && e.EventTypeKey == eventTypeKey && e.Qualifier == qualifier,
                cancellationToken))
        {
            var suffix = qualifier.Length > 0 ? $" with qualifier '{qualifier}'" : "";
            throw new ConflictException(
                $"This program already has an active '{eventTypeKey}' event for {seasonYear}{suffix}. " +
                "Use a different qualifier (e.g. 'Phase 2') or update the existing event.");
        }
    }

    private async Task<EventDto> ToDtoAsync(ProgramEvent programEvent, bool includeArchivedActions, CancellationToken cancellationToken)
    {
        var latest = await ActionStatusLookup.LatestAsync(db, programEvent.Actions.Select(a => a.Id), actor.UserId, cancellationToken);
        return EventMapper.ToDto(programEvent, latest, clock.GetCurrentInstant(), includeArchivedActions);
    }

    private async Task<IReadOnlyList<EventDto>> ToDtosAsync(List<ProgramEvent> events, CancellationToken cancellationToken)
    {
        var latest = await ActionStatusLookup.LatestAsync(db, events.SelectMany(e => e.Actions).Select(a => a.Id), actor.UserId, cancellationToken);
        var now = clock.GetCurrentInstant();
        return events.ConvertAll(e => EventMapper.ToDto(e, latest, now, includeArchivedActions: false));
    }

    private sealed record ValidatedEvent(
        Domain.Reference.HuntProgram Program,
        EventType EventType,
        int SeasonYear,
        EventSchedule Schedule,
        string Qualifier,
        string? SeasonLabel,
        string? Name,
        string? Description,
        string? SourceUrl,
        string? ExternalKey);
}
