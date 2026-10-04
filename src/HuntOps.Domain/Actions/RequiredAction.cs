using HuntOps.Domain.Common;
using HuntOps.Domain.Events;
using NodaTime;

namespace HuntOps.Domain.Actions;

/// <summary>Something the user must do for an event ("Apply or buy preference point", "Submit harvest report").</summary>
public sealed class RequiredAction : Entity, IArchivable
{
    public Guid ProgramEventId { get; set; }

    public ProgramEvent ProgramEvent { get; set; } = null!;

    public required string Title { get; set; }

    /// <summary>Free-text classifier ("apply", "purchase", "report").</summary>
    public string? Kind { get; set; }

    public bool IsOptional { get; set; }

    public string? Notes { get; set; }

    public Instant? ArchivedAt { get; set; }
}
