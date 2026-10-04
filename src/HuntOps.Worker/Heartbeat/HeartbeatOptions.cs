using System.ComponentModel.DataAnnotations;

namespace HuntOps.Worker.Heartbeat;

internal sealed class HeartbeatOptions
{
    public const string SectionName = "Worker:Heartbeat";

    public const string Component = "worker";

    /// <summary>Stable worker identity (HUNTOPS_WORKER_ID). Container hostnames change on recreate, so they are not used.</summary>
    [Required]
    [MaxLength(100)]
    public string WorkerId { get; set; } = "worker";

    [Range(5, 600)]
    public int IntervalSeconds { get; set; } = 30;

    /// <summary>The heartbeat is considered stale after this many missed intervals.</summary>
    [Range(2, 20)]
    public int StaleAfterIntervals { get; set; } = 4;
}
