using HuntOps.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace HuntOps.Application.Abstractions;

/// <summary>
/// Application-facing view of the HuntOps database. Implemented by the EF Core DbContext in Infrastructure.
/// </summary>
public interface IHuntOpsDb
{
    DbSet<WorkerHeartbeat> WorkerHeartbeats { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
