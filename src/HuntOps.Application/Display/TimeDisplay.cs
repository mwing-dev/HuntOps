using System.Globalization;
using HuntOps.Application.Common;
using HuntOps.Application.Events;
using NodaTime;
using NodaTime.Text;

namespace HuntOps.Application.Display;

/// <summary>A moment shown as the agency publishes it, plus the owner's local time when that differs.</summary>
public sealed record DisplayMoment(string EventLocal, string? OwnerLocal)
{
    public override string ToString() => OwnerLocal is null ? EventLocal : $"{EventLocal} ({OwnerLocal} your time)";
}

/// <summary>Human-readable schedule for an event. Nothing about time zones is hidden.</summary>
public sealed record ScheduleDisplay(
    bool IsWindow,
    string Dates,
    DisplayMoment Starts,
    DisplayMoment? Ends,
    string TimeZoneId);

/// <summary>
/// Formats event times for people. Event-local times are shown as the agency publishes them (with the zone
/// abbreviation, e.g. "CDT"); when the owner's zone has a different UTC offset at that instant, the owner-local
/// time is shown as well, e.g. "Jun 12, 2027, 11:59 PM CDT" / "Jun 12, 2027, 9:59 PM PDT".
/// </summary>
public static class TimeDisplay
{
    private static readonly LocalDatePattern LongDate = LocalDatePattern.Create("MMMM d, yyyy", CultureInfo.InvariantCulture);
    private static readonly LocalDatePattern ShortDate = LocalDatePattern.Create("MMM d", CultureInfo.InvariantCulture);
    private static readonly LocalDateTimePattern DateTime = LocalDateTimePattern.Create("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture);

    public static string Date(LocalDate date) => LongDate.Format(date);

    /// <summary>"June 12" style, for compact labels ("Application closes June 12").</summary>
    public static string ShortDateOf(LocalDate date) => ShortDate.Format(date);

    public static string? Date(string? isoDate) =>
        TimeFormats.TryParseDate(isoDate, out var date) ? Date(date) : null;

    /// <summary>"Jun 12, 2027, 11:59 PM CDT".</summary>
    public static string Instant(Instant instant, DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = instant.InZone(zone).LocalDateTime;
        return $"{DateTime.Format(local)} {zone.GetZoneInterval(instant).Name}";
    }

    public static DisplayMoment Moment(Instant instant, DateTimeZone eventZone, DateTimeZone ownerZone)
    {
        ArgumentNullException.ThrowIfNull(eventZone);
        ArgumentNullException.ThrowIfNull(ownerZone);
        var eventLocal = Instant(instant, eventZone);
        var differs = eventZone.GetUtcOffset(instant) != ownerZone.GetUtcOffset(instant);
        return new DisplayMoment(eventLocal, differs ? Instant(instant, ownerZone) : null);
    }

    public static ScheduleDisplay Describe(EventDto e, string ownerTimeZoneId, IDateTimeZoneProvider zones)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(zones);
        var eventZone = zones.GetZoneOrNull(e.TimeZoneId) ?? DateTimeZone.Utc;
        var ownerZone = zones.GetZoneOrNull(ownerTimeZoneId) ?? eventZone;

        TimeFormats.TryParseInstant(e.StartsAtUtc, out var startsAt);
        Instant? endsAt = TimeFormats.TryParseInstant(e.EndsAtUtc, out var end) ? end : null;

        var startDate = Date(e.StartDate) ?? e.StartDate;
        var dates = e.IsWindow && Date(e.EndDate) is { } endDate
            ? $"{startDate} → {endDate}"
            : e.StartTime is null ? startDate : $"{startDate}, {e.StartTime}";

        return new ScheduleDisplay(
            e.IsWindow,
            dates,
            Moment(startsAt, eventZone, ownerZone),
            endsAt is { } closes ? Moment(closes, eventZone, ownerZone) : null,
            e.TimeZoneId);
    }

    /// <summary>"Application period closes June 12" / "Draw results June 25".</summary>
    public static string Headline(string eventTypeName, bool isWindow, string? startDate, string? endDate, Instant now, Instant startsAt)
    {
        if (isWindow && TimeFormats.TryParseDate(endDate, out var close))
        {
            var verb = now < startsAt && TimeFormats.TryParseDate(startDate, out var open)
                ? $"opens {ShortDateOf(open)}, closes"
                : "closes";
            return $"{eventTypeName} {verb} {ShortDateOf(close)}";
        }

        return TimeFormats.TryParseDate(startDate, out var date) ? $"{eventTypeName} {ShortDateOf(date)}" : eventTypeName;
    }

    /// <summary>"8 days remaining", "Due today", "3 days overdue".</summary>
    public static string DaysRemaining(int days) => days switch
    {
        > 1 => $"{days} days remaining",
        1 => "1 day remaining",
        0 => "Due today",
        -1 => "1 day overdue",
        _ => $"{-days} days overdue",
    };
}
