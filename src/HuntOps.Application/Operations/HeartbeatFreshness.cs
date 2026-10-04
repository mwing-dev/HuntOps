using NodaTime;

namespace HuntOps.Application.Operations;

public enum HeartbeatStatus
{
    Starting,
    Fresh,
    Stale,
    NeverBeat,
}

/// <summary>Decides whether a worker heartbeat is recent enough to consider the worker alive.</summary>
public static class HeartbeatFreshness
{
    public static HeartbeatStatus Evaluate(
        Instant? lastBeatAt,
        Instant startedAt,
        Instant now,
        Duration maxAge)
    {
        if (lastBeatAt is null)
        {
            // Give a freshly started worker one full window to produce its first beat.
            return now - startedAt <= maxAge ? HeartbeatStatus.Starting : HeartbeatStatus.NeverBeat;
        }

        return now - lastBeatAt.Value <= maxAge ? HeartbeatStatus.Fresh : HeartbeatStatus.Stale;
    }

    public static bool IsHealthy(HeartbeatStatus status) =>
        status is HeartbeatStatus.Fresh or HeartbeatStatus.Starting;
}
