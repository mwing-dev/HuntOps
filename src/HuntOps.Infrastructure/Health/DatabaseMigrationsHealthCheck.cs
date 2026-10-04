using HuntOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HuntOps.Infrastructure.Health;

/// <summary>Ready only when the database is reachable and every migration in this build has been applied.</summary>
internal sealed class DatabaseMigrationsHealthCheck(HuntOpsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy("All migrations applied.")
                : HealthCheckResult.Unhealthy($"{pending.Count} pending migration(s): {string.Join(", ", pending)}");
        }
#pragma warning disable CA1031 // Any failure here simply means "not ready".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Could not read migration state from the database.", ex);
        }
    }
}
