using HuntOps.Application.Abstractions;
using HuntOps.Domain.Operations;
using NodaTime;

namespace HuntOps.Application.Operations;

public sealed class HeartbeatRecorder(IHuntOpsDb db, IClock clock)
{
    /// <summary>Upserts the heartbeat row for a worker component and returns the recorded instant.</summary>
    public async Task<Instant> RecordAsync(
        string workerId,
        string component,
        Instant startedAt,
        string? version,
        CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        var heartbeat = await db.WorkerHeartbeats.FindAsync([workerId, component], cancellationToken);

        if (heartbeat is null)
        {
            db.WorkerHeartbeats.Add(new WorkerHeartbeat
            {
                WorkerId = workerId,
                Component = component,
                StartedAt = startedAt,
                LastBeatAt = now,
                Version = version,
            });
        }
        else
        {
            heartbeat.StartedAt = startedAt;
            heartbeat.LastBeatAt = now;
            heartbeat.Version = version;
        }

        await db.SaveChangesAsync(cancellationToken);
        return now;
    }
}
