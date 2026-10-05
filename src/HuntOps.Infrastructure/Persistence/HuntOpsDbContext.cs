using HuntOps.Application.Abstractions;
using HuntOps.Domain.Access;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Events;
using HuntOps.Domain.Operations;
using HuntOps.Domain.Reference;
using HuntOps.Domain.Users;
using HuntOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HuntOps.Infrastructure.Persistence;

public sealed class HuntOpsDbContext(DbContextOptions<HuntOpsDbContext> options)
    : IdentityDbContext<AppUser>(options), IHuntOpsDb, IDataProtectionKeyContext
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

    public DbSet<OwnerSettings> OwnerSettings => Set<OwnerSettings>();

    /// <summary>ASP.NET Core Data Protection keys, persisted so cookies and encrypted secrets survive container recreation.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public void ExpectVersion<TEntity>(TEntity entity, uint version)
        where TEntity : class =>
        Entry(entity).Property(nameof(Domain.Common.Entity.Version)).OriginalValue = version;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Identity hard-codes "AspNet*" table names; use the same snake_case style as the rest of the schema.
        modelBuilder.Entity<AppUser>().ToTable("users");
        modelBuilder.Entity<IdentityRole>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<string>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<string>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<string>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityRoleClaim<string>>().ToTable("role_claims");
        modelBuilder.Entity<IdentityUserToken<string>>().ToTable("user_tokens");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HuntOpsDbContext).Assembly);
    }
}
