using NodaTime;

namespace HuntOps.Domain.Operations;

/// <summary>
/// Liveness record written periodically by each background worker component.
/// The dashboard and health checks use it to detect a silently stopped worker.
/// </summary>
public sealed class WorkerHeartbeat
{
    public required string WorkerId { get; init; }

    public required string Component { get; init; }

    public Instant StartedAt { get; set; }

    public Instant LastBeatAt { get; set; }

    public string? Version { get; set; }
}
