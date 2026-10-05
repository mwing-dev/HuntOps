using HuntOps.Application.Abstractions;
using HuntOps.Domain.Access;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Events;
using HuntOps.Domain.Operations;
using HuntOps.Domain.Reference;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HuntOps.Infrastructure.Persistence;

public sealed class HuntOpsDbContext(DbContextOptions<HuntOpsDbContext> options)
    : DbContext(options), IHuntOpsDb, IDataProtectionKeyContext
{
    public DbSet<Jurisdiction> Jurisdictions => Set<Jurisdiction>();

    public DbSet<Agency> Agencies => Set<Agency>();

    public DbSet<HuntProgram> Programs => Set<HuntProgram>();

    public DbSet<EventType> EventTypes => Set<EventType>();

    public DbSet<ProgramEvent> ProgramEvents => Set<ProgramEvent>();

    public DbSet<RequiredAction> RequiredActions => Set<RequiredAction>();

    public DbSet<ActionStatusChange> ActionStatusChanges => Set<ActionStatusChange>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<WorkerHeartbeat> WorkerHeartbeats => Set<WorkerHeartbeat>();

    /// <summary>ASP.NET Core Data Protection keys, persisted so cookies and encrypted secrets survive container recreation.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public void ExpectVersion<TEntity>(TEntity entity, uint version)
        where TEntity : class =>
        Entry(entity).Property(nameof(Domain.Common.Entity.Version)).OriginalValue = version;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HuntOpsDbContext).Assembly);
    }
}
