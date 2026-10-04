using NodaTime;

namespace HuntOps.Worker.Heartbeat;

/// <summary>In-process record of this worker's own heartbeat, used by its readiness check.</summary>
internal sealed class HeartbeatState(IClock clock)
{
    private long _lastBeatTicks = long.MinValue;

    public Instant StartedAt { get; } = clock.GetCurrentInstant();

    public Instant? LastBeatAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastBeatTicks);
            return ticks == long.MinValue ? null : Instant.FromUnixTimeTicks(ticks);
        }
    }

    public void MarkBeat(Instant at) => Interlocked.Exchange(ref _lastBeatTicks, at.ToUnixTimeTicks());
}
