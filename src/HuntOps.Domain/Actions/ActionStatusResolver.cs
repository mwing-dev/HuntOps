using HuntOps.Domain.Events;
using NodaTime;

namespace HuntOps.Domain.Actions;

/// <summary>The status shown to the user. Combines stored decisions with the clock.</summary>
public enum EffectiveActionStatus
{
    Upcoming,
    Open,
    Missed,
    Completed,
    NotApplicable,
    Cancelled,
}

/// <summary>
/// Derives an action's effective status (architecture R4):
/// the latest stored decision wins if it is Completed, NotApplicable or Cancelled; otherwise
/// (no decision, or Reopened) the status comes from the clock: before the start it is Upcoming,
/// inside the window it is Open, after a window ends it is Missed. Point-in-time events have
/// no end, so their actions stay Open once started and are never auto-Missed.
/// </summary>
public static class ActionStatusResolver
{
    public static EffectiveActionStatus Resolve(
        ProgramEvent programEvent,
        ActionStatusChange? latestChange,
        Instant now)
    {
        ArgumentNullException.ThrowIfNull(programEvent);

        switch (latestChange?.Status)
        {
            case ActionResolution.Completed:
                return EffectiveActionStatus.Completed;
            case ActionResolution.NotApplicable:
                return EffectiveActionStatus.NotApplicable;
            case ActionResolution.Cancelled:
                return EffectiveActionStatus.Cancelled;
        }

        return programEvent.PhaseAt(now) switch
        {
            EventPhase.Upcoming => EffectiveActionStatus.Upcoming,
            EventPhase.Open => EffectiveActionStatus.Open,
            _ => EffectiveActionStatus.Missed,
        };
    }

    /// <summary>Picks the latest change by time, then by insertion order.</summary>
    public static ActionStatusChange? Latest(IEnumerable<ActionStatusChange> changes) =>
        changes
            .OrderByDescending(c => c.ChangedAt)
            .ThenByDescending(c => c.Sequence)
            .FirstOrDefault();

    public static bool IsResolved(EffectiveActionStatus status) =>
        status is EffectiveActionStatus.Completed or EffectiveActionStatus.NotApplicable or EffectiveActionStatus.Cancelled;
}
