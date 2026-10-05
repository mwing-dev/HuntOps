using HuntOps.Domain.Common;
using NodaTime;

namespace HuntOps.Domain.Users;

/// <summary>
/// Owner preferences: the time zone used to display dates and (from Phase 4) to schedule reminders,
/// and quiet hours during which non-urgent reminders are deferred.
/// </summary>
public sealed class OwnerSettings : IAuditable
{
    public const string DefaultTimeZoneId = "America/Los_Angeles";
    public static readonly LocalTime DefaultQuietHoursStart = new(21, 0);
    public static readonly LocalTime DefaultQuietHoursEnd = new(7, 0);

    public required string UserId { get; init; }

    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public bool QuietHoursEnabled { get; set; } = true;

    public LocalTime QuietHoursStart { get; set; } = DefaultQuietHoursStart;

    public LocalTime QuietHoursEnd { get; set; } = DefaultQuietHoursEnd;

    /// <summary>Priority-5 (urgent) reminders are delivered even during quiet hours.</summary>
    public bool UrgentBypassesQuietHours { get; set; } = true;

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    public uint Version { get; set; }
}
