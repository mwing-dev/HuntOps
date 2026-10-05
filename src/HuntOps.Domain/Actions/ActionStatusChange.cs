using NodaTime;

namespace HuntOps.Domain.Actions;

/// <summary>A decision the user records about an action. Only these are stored; time-based states are derived.</summary>
public enum ActionResolution
{
    Completed,
    NotApplicable,
    Cancelled,

    /// <summary>Undoes a previous resolution; the status is derived from the clock again.</summary>
    Reopened,
}

/// <summary>Through which interface a change was made.</summary>
public enum ChangeChannel
{
    Web,
    Api,
    Mcp,
    Notification,
    Cli,
    System,
}

/// <summary>
/// Append-only history of a user's decisions about an action. Rows are never updated or deleted
/// (enforced by a database trigger), so prior years and prior decisions are always preserved.
/// </summary>
public sealed class ActionStatusChange
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Monotonic insertion order; breaks ties between changes recorded at the same instant.</summary>
    public long Sequence { get; init; }

    public Guid RequiredActionId { get; init; }

    public RequiredAction RequiredAction { get; init; } = null!;

    /// <summary>Whose status this is. V1 has a single owner (<see cref="Users.Owner.UserId"/>).</summary>
    public required string UserId { get; init; }

    public ActionResolution Status { get; init; }

    /// <summary>What happened, e.g. "Preference point purchased", "Applied", "Not drawn".</summary>
    public string? Outcome { get; init; }

    public string? Note { get; init; }

    public Instant ChangedAt { get; init; }

    public ChangeChannel ChangedVia { get; init; }

    /// <summary>Who made the change, e.g. "apikey:hops_ab12cd34".</summary>
    public required string ChangedBy { get; init; }
}
