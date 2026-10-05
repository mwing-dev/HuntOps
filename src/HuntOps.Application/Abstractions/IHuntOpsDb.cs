using HuntOps.Domain.Access;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Events;
using HuntOps.Domain.Operations;
using HuntOps.Domain.Reference;
using Microsoft.EntityFrameworkCore;

namespace HuntOps.Application.Abstractions;

/// <summary>
/// Application-facing view of the HuntOps database. Implemented by the EF Core DbContext in Infrastructure.
/// </summary>
public interface IHuntOpsDb
{
    DbSet<Jurisdiction> Jurisdictions { get; }

    DbSet<Agency> Agencies { get; }

    DbSet<HuntProgram> Programs { get; }

    DbSet<EventType> EventTypes { get; }

    DbSet<ProgramEvent> ProgramEvents { get; }

    DbSet<RequiredAction> RequiredActions { get; }

    DbSet<ActionStatusChange> ActionStatusChanges { get; }

    DbSet<ApiKey> ApiKeys { get; }

    DbSet<WorkerHeartbeat> WorkerHeartbeats { get; }

    /// <summary>
    /// Declares the version the client last read. Saving fails with a concurrency conflict if the row has changed since.
    /// </summary>
    void ExpectVersion<TEntity>(TEntity entity, uint version)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
