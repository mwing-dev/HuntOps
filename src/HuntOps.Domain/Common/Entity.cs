using NodaTime;

namespace HuntOps.Domain.Common;

/// <summary>Timestamps maintained automatically on save.</summary>
public interface IAuditable
{
    Instant CreatedAt { get; set; }

    Instant UpdatedAt { get; set; }
}

/// <summary>
/// Soft delete. Archived rows stay in the database so historical references (events, action history)
/// never dangle; they are hidden from active lists and cannot receive new children.
/// </summary>
public interface IArchivable
{
    Instant? ArchivedAt { get; set; }
}

/// <summary>Base for Guid-keyed, audited, optimistically concurrent entities.</summary>
public abstract class Entity : IAuditable
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    /// <summary>Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Clients echo it back on update.</summary>
    public uint Version { get; set; }
}
