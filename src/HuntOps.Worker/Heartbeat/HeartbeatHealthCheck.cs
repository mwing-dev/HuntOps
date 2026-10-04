using HuntOps.Application.Operations;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NodaTime;

namespace HuntOps.Worker.Heartbeat;

/// <summary>Unhealthy when this worker has stopped recording heartbeats (e.g. the database writes keep failing).</summary>
internal sealed class HeartbeatHealthCheck(HeartbeatState state, IClock clock, IOptions<HeartbeatOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var maxAge = Duration.FromSeconds((long)settings.IntervalSeconds * settings.StaleAfterIntervals);
        var status = HeartbeatFreshness.Evaluate(state.LastBeatAt, state.StartedAt, clock.GetCurrentInstant(), maxAge);

        var description = state.LastBeatAt is { } last ? $"{status}; last beat {last}" : status.ToString();
        return Task.FromResult(HeartbeatFreshness.IsHealthy(status)
            ? HealthCheckResult.Healthy(description)
            : HealthCheckResult.Unhealthy(description));
    }
}
