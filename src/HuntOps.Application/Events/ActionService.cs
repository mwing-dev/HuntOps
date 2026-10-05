using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Common;
using HuntOps.Domain.Events;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Events;

public sealed record ActionInput(
    string? Title,
    string? Kind = null,
    bool? IsOptional = null,
    string? Notes = null,
    uint? Version = null);

public sealed record StatusChangeInput(string? Status, string? Outcome = null, string? Note = null);

public sealed record CompleteEventInput(Guid? ActionId = null, string? Outcome = null, string? Note = null);

public sealed record ActionStatusChangeDto(
    Guid Id,
    Guid ActionId,
    ActionResolution Status,
    string? Outcome,
    string? Note,
    string ChangedAt,
    ChangeChannel ChangedVia,
    string ChangedBy);

/// <summary>Something the user needs to deal with, with enough context to act on it.</summary>
public sealed record ActionItemDto(
    Guid ActionId,
    string Title,
    string? Kind,
    bool IsOptional,
    EffectiveActionStatus Status,
    string? Outcome,
    string? StatusChangedAt,
    string Deadline,
    int DaysRemaining,
    Guid EventId,
    string EventName,
    string EventTypeKey,
    string EventTypeName,
    EventCategory EventCategory,
    bool IsWindow,
    int SeasonYear,
    string StartDate,
    string? EndDate,
    string StartsAtUtc,
    string? EndsAtUtc,
    string TimeZoneId,
    Guid ProgramId,
    string ProgramName,
    string? Species,
    string JurisdictionCode,
    string AgencyName,
    string? Link);

internal sealed record ValidatedAction(string Title, string? Kind, bool IsOptional, string? Notes);

/// <summary>
/// Required actions and their append-only status history. "Mark completed" from every interface
/// (REST now; dashboard and MCP later) goes through <see cref="ChangeStatusAsync"/>.
/// </summary>
public sealed class ActionService(IHuntOpsDb db, IClock clock, IDateTimeZoneProvider zones, ICurrentActor actor)
{
    public const int MaxActionItemDays = 730;

    /// <summary>Windows that closed within this period still show up as Missed in action items.</summary>
    public static readonly Duration MissedLookback = Duration.FromDays(14);

    private const string Resource = "Action";

    public async Task<IReadOnlyList<ActionDto>> ListForEventAsync(Guid eventId, bool includeArchived, CancellationToken cancellationToken)
    {
        var programEvent = await db.ProgramEvents.AsNoTracking().Include(e => e.Actions).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            ?? throw new NotFoundException("Event", eventId);
        var latest = await ActionStatusLookup.LatestAsync(db, programEvent.Actions.Select(a => a.Id), actor.UserId, cancellationToken);
        var now = clock.GetCurrentInstant();
        return programEvent.Actions
            .Where(a => includeArchived || a.ArchivedAt is null)
            .OrderBy(a => a.CreatedAt)
            .Select(a => EventMapper.ToDto(a, programEvent, latest.GetValueOrDefault(a.Id), now))
            .ToList();
    }

    public async Task<ActionDto> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var action = await LoadAsync(id, cancellationToken);
        return await ToDtoAsync(action, cancellationToken);
    }

    public async Task<ActionDto> CreateAsync(Guid eventId, ActionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var programEvent = await db.ProgramEvents.FindOrThrowAsync(eventId, "Event", cancellationToken);
        programEvent.EnsureActive("eventId", "The event");
        var v = new InputValidator(zones);
        var fields = ValidateAction(v, input, "");
        v.ThrowIfInvalid();

        var action = new RequiredAction
        {
            ProgramEventId = programEvent.Id,
            ProgramEvent = programEvent,
            Title = fields.Title,
            Kind = fields.Kind,
            IsOptional = fields.IsOptional,
            Notes = fields.Notes,
        };
        db.RequiredActions.Add(action);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(action, cancellationToken);
    }

    public async Task<ActionDto> UpdateAsync(Guid id, ActionInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var action = await LoadAsync(id, cancellationToken);
        var v = new InputValidator(zones);
        var fields = ValidateAction(v, input, "");
        var version = v.RequiredVersion(input.Version);
        v.ThrowIfInvalid();

        db.ExpectVersion(action, version);
        action.Title = fields.Title;
        action.Kind = fields.Kind;
        action.IsOptional = fields.IsOptional;
        action.Notes = fields.Notes;
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(action, cancellationToken);
    }

    /// <summary>Soft delete; the action's status history is preserved.</summary>
    public async Task<ActionDto> ArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var action = await LoadAsync(id, cancellationToken);
        if (action.ArchivedAt is null)
        {
            action.ArchivedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ToDtoAsync(action, cancellationToken);
    }

    public async Task<ActionDto> RestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        var action = await LoadAsync(id, cancellationToken);
        if (action.ArchivedAt is not null)
        {
            action.ArchivedAt = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ToDtoAsync(action, cancellationToken);
    }

    /// <summary>
    /// Records a decision (completed / not applicable / cancelled / reopened) as a new history row.
    /// Idempotent: repeating the current decision with the same outcome records nothing new.
    /// </summary>
    public async Task<ActionDto> ChangeStatusAsync(Guid id, StatusChangeInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var action = await LoadAsync(id, cancellationToken);
        var v = new InputValidator(zones);
        var status = v.Enum<ActionResolution>("status", input.Status, required: true);
        var outcome = v.OptionalText("outcome", input.Outcome, InputValidator.NameMaxLength);
        var note = v.OptionalText("note", input.Note, 2000);
        v.ThrowIfInvalid();

        action.EnsureActive("actionId", "The action");
        var latest = (await ActionStatusLookup.LatestAsync(db, [action.Id], actor.UserId, cancellationToken)).GetValueOrDefault(action.Id);
        var current = ActionStatusResolver.Resolve(action.ProgramEvent, latest, clock.GetCurrentInstant());

        if (status == ActionResolution.Reopened && !ActionStatusResolver.IsResolved(current))
        {
            throw new ConflictException($"The action is {InputValidator.ToCamelCase(current.ToString())}, not resolved; there is nothing to reopen.");
        }

        var unchanged = latest is not null && latest.Status == status && latest.Outcome == outcome && note is null;
        if (!unchanged)
        {
            db.ActionStatusChanges.Add(new ActionStatusChange
            {
                RequiredActionId = action.Id,
                UserId = actor.UserId,
                Status = status!.Value,
                Outcome = outcome,
                Note = note,
                ChangedAt = clock.GetCurrentInstant(),
                ChangedVia = actor.Channel,
                ChangedBy = actor.ActorId,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ToDtoAsync(action, cancellationToken);
    }

    /// <summary>
    /// Completes an event's action. With no <see cref="CompleteEventInput.ActionId"/>, the event must have exactly one
    /// active required (non-optional) action, or exactly one action in total.
    /// </summary>
    public async Task<ActionDto> CompleteEventAsync(Guid eventId, CompleteEventInput? input, CancellationToken cancellationToken)
    {
        input ??= new CompleteEventInput();
        var programEvent = await db.ProgramEvents.AsNoTracking().Include(e => e.Actions).FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken)
            ?? throw new NotFoundException("Event", eventId);
        var active = programEvent.Actions.Where(a => a.ArchivedAt is null).ToList();

        Guid actionId;
        if (input.ActionId is { } requested)
        {
            if (active.All(a => a.Id != requested))
            {
                throw new RequestValidationException("actionId", "That action does not belong to this event (or is archived).");
            }

            actionId = requested;
        }
        else
        {
            var required = active.Where(a => !a.IsOptional).ToList();
            var candidates = required.Count > 0 ? required : active;
            if (candidates.Count != 1)
            {
                var detail = candidates.Count == 0
                    ? "This event has no actions to complete."
                    : $"This event has {candidates.Count} actions; specify actionId ({string.Join(", ", candidates.Select(a => $"{a.Id} = '{a.Title}'"))}).";
                throw new RequestValidationException("actionId", detail);
            }

            actionId = candidates[0].Id;
        }

        return await ChangeStatusAsync(actionId, new StatusChangeInput(nameof(ActionResolution.Completed), input.Outcome, input.Note), cancellationToken);
    }

    public async Task<IReadOnlyList<ActionStatusChangeDto>> HistoryAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.RequiredActions.AnyAsync(a => a.Id == id, cancellationToken))
        {
            throw new NotFoundException(Resource, id);
        }

        var changes = await db.ActionStatusChanges
            .AsNoTracking()
            .Where(c => c.RequiredActionId == id && c.UserId == actor.UserId)
            .OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.Sequence)
            .ToListAsync(cancellationToken);

        return changes.ConvertAll(c => new ActionStatusChangeDto(
            c.Id, c.RequiredActionId, c.Status, c.Outcome, c.Note, TimeFormats.Format(c.ChangedAt), c.ChangedVia, c.ChangedBy));
    }

    /// <summary>
    /// "What do I need to deal with?": actions on active events that start within <paramref name="days"/>, are open now,
    /// or were missed within the last 14 days. Resolved actions are excluded unless requested. Sorted by deadline.
    /// </summary>
    public async Task<IReadOnlyList<ActionItemDto>> ActionItemsAsync(int? days, bool includeResolved, CancellationToken cancellationToken)
    {
        var v = new InputValidator(zones);
        var horizonDays = v.IntRange("days", days ?? 60, 1, MaxActionItemDays, required: true) ?? 60;
        v.ThrowIfInvalid();

        var now = clock.GetCurrentInstant();
        var horizon = now + Duration.FromDays(horizonDays);
        var lookback = now - MissedLookback;

        var actions = await db.RequiredActions
            .AsNoTracking()
            .Include(a => a.ProgramEvent).ThenInclude(e => e.Program).ThenInclude(p => p.Agency).ThenInclude(g => g.Jurisdiction)
            .Include(a => a.ProgramEvent).ThenInclude(e => e.EventType)
            .Where(a => a.ArchivedAt == null
                        && a.ProgramEvent.ArchivedAt == null
                        && a.ProgramEvent.Program.ArchivedAt == null
                        && a.ProgramEvent.StartsAtUtc <= horizon
                        && (a.ProgramEvent.EndsAtUtc ?? a.ProgramEvent.StartsAtUtc) >= lookback)
            .ToListAsync(cancellationToken);

        var latest = await ActionStatusLookup.LatestAsync(db, actions.Select(a => a.Id), actor.UserId, cancellationToken);

        return actions
            .OrderBy(a => a.ProgramEvent.EndsAtUtc ?? a.ProgramEvent.StartsAtUtc)
            .ThenBy(a => a.ProgramEvent.Program.Name, StringComparer.Ordinal)
            .Select(a => ToItem(a, latest.GetValueOrDefault(a.Id), now))
            .Where(item => includeResolved || !ActionStatusResolver.IsResolved(item.Status))
            .ToList();
    }

    /// <summary>Actions whose latest decision (completed / not applicable / cancelled) was recorded in the last <paramref name="days"/> days, newest first.</summary>
    public async Task<IReadOnlyList<ActionItemDto>> RecentlyResolvedAsync(int? days, CancellationToken cancellationToken)
    {
        var v = new InputValidator(zones);
        var lookbackDays = v.IntRange("days", days ?? 30, 1, 365, required: true) ?? 30;
        v.ThrowIfInvalid();

        var now = clock.GetCurrentInstant();
        var since = now - Duration.FromDays(lookbackDays);
        var actionIds = await db.ActionStatusChanges
            .Where(c => c.UserId == actor.UserId && c.ChangedAt >= since)
            .Select(c => c.RequiredActionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var actions = await db.RequiredActions
            .AsNoTracking()
            .Include(a => a.ProgramEvent).ThenInclude(e => e.Program).ThenInclude(p => p.Agency).ThenInclude(g => g.Jurisdiction)
            .Include(a => a.ProgramEvent).ThenInclude(e => e.EventType)
            .Where(a => actionIds.Contains(a.Id) && a.ArchivedAt == null && a.ProgramEvent.ArchivedAt == null)
            .ToListAsync(cancellationToken);

        var latest = await ActionStatusLookup.LatestAsync(db, actions.Select(a => a.Id), actor.UserId, cancellationToken);
        return actions
            .Where(a => latest.TryGetValue(a.Id, out var change) && change.ChangedAt >= since)
            .OrderByDescending(a => latest[a.Id].ChangedAt)
            .Select(a => ToItem(a, latest[a.Id], now))
            .Where(item => ActionStatusResolver.IsResolved(item.Status))
            .ToList();
    }

    internal static ValidatedAction ValidateAction(InputValidator v, ActionInput input, string fieldPrefix)
    {
        var title = v.RequiredText($"{fieldPrefix}title", input.Title);
        var kind = v.OptionalText($"{fieldPrefix}kind", input.Kind, 50);
        var notes = v.OptionalText($"{fieldPrefix}notes", input.Notes, 2000);
        return new ValidatedAction(title, kind, input.IsOptional ?? false, notes);
    }

    private static ActionItemDto ToItem(RequiredAction a, ActionStatusChange? latest, Instant now)
    {
        var e = a.ProgramEvent;
        var program = e.Program;
        var status = ActionStatusResolver.Resolve(e, latest, now);
        var deadline = e.EndsAtUtc ?? e.StartsAtUtc;
        var daysRemaining = (int)Math.Ceiling((deadline - now).TotalDays);
        return new ActionItemDto(
            a.Id, a.Title, a.Kind, a.IsOptional, status,
            ActionStatusResolver.IsResolved(status) ? latest?.Outcome : null,
            TimeFormats.Format(latest?.ChangedAt),
            TimeFormats.Format(deadline), daysRemaining,
            e.Id, e.Name, e.EventTypeKey, e.EventType.DisplayName, e.EventType.Category, e.IsWindow, e.SeasonYear,
            TimeFormats.Format(e.StartDate), TimeFormats.Format(e.EndDate),
            TimeFormats.Format(e.StartsAtUtc), TimeFormats.Format(e.EndsAtUtc), e.TimeZoneId,
            program.Id, program.Name, program.Species, program.Agency.Jurisdiction.Code, program.Agency.Name,
            e.SourceUrl ?? program.WebsiteUrl ?? program.Agency.WebsiteUrl);
    }

    private async Task<RequiredAction> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        await db.RequiredActions.Include(a => a.ProgramEvent).FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
        ?? throw new NotFoundException(Resource, id);

    private async Task<ActionDto> ToDtoAsync(RequiredAction action, CancellationToken cancellationToken)
    {
        var latest = (await ActionStatusLookup.LatestAsync(db, [action.Id], actor.UserId, cancellationToken)).GetValueOrDefault(action.Id);
        return EventMapper.ToDto(action, action.ProgramEvent, latest, clock.GetCurrentInstant());
    }
}
