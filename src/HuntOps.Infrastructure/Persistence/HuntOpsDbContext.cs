using HuntOps.Application.Abstractions;
using HuntOps.Domain.Operations;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HuntOps.Infrastructure.Persistence;

public sealed class HuntOpsDbContext(DbContextOptions<HuntOpsDbContext> options)
    : DbContext(options), IHuntOpsDb, IDataProtectionKeyContext
{
    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();

    /// <summary>ASP.NET Core Data Protection keys, persisted so cookies and encrypted secrets survive container recreation.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HuntOpsDbContext).Assembly);
    }
}
