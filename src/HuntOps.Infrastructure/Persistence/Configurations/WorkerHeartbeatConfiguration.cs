using HuntOps.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HuntOps.Infrastructure.Persistence.Configurations;

internal sealed class WorkerHeartbeatConfiguration : IEntityTypeConfiguration<WorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeat> builder)
    {
        builder.HasKey(h => new { h.WorkerId, h.Component });
        builder.Property(h => h.WorkerId).HasMaxLength(100);
        builder.Property(h => h.Component).HasMaxLength(100);
        builder.Property(h => h.Version).HasMaxLength(100);
    }
}
