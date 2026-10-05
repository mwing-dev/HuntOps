using HuntOps.Domain.Common;
using NodaTime;
using NodaTime.TimeZones;

namespace HuntOps.Domain.Events;

/// <summary>
/// When an event happens, as published by the agency: local dates, optional local times, and an IANA zone.
/// <para>
/// Semantics (architecture §4.5):
/// <list type="bullet">
/// <item>No <see cref="EndDate"/>: a point-in-time event at <see cref="StartDate"/> (+ <see cref="StartTime"/>).</item>
/// <item>No <see cref="StartTime"/>: starts at the beginning of the start date in the event's zone.</item>
/// <item>No <see cref="EndTime"/>: ends at the end of the end date (23:59:59.999 local), e.g. "closes June 12".</item>
/// <item>Local times that DST skips or repeats resolve leniently: skipped times shift forward by the gap,
/// ambiguous times take the earlier (daylight) instant.</item>
/// </list>
/// </para>
/// </summary>
public sealed record EventSchedule(
    LocalDate StartDate,
    LocalTime? StartTime,
    LocalDate? EndDate,
    LocalTime? EndTime,
    string TimeZoneId)
{
    /// <summary>The last representable instant of an all-day end date (matches 23:59:59.999 local).</summary>
    public static readonly Duration EndOfDayPrecision = Duration.FromMilliseconds(1);

    public bool IsWindow => EndDate is not null;

    /// <summary>Validates the schedule and computes the UTC instants. Throws <see cref="DomainValidationException"/>.</summary>
    public ResolvedSchedule Resolve(IDateTimeZoneProvider zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        var errors = new Dictionary<string, string[]>();

        var zone = string.IsNullOrWhiteSpace(TimeZoneId) ? null : zones.GetZoneOrNull(TimeZoneId);
        if (zone is null)
        {
            errors["timeZoneId"] = [$"'{TimeZoneId}' is not a known IANA time zone (e.g. America/Chicago)."];
        }

        if (EndDate is null && EndTime is not null)
        {
            errors["endTime"] = ["An end time requires an end date."];
        }

        if (EndDate is { } endDate && endDate < StartDate)
        {
            errors["endDate"] = ["The end date cannot be before the start date."];
        }

        if (errors.Count > 0 || zone is null)
        {
            throw new DomainValidationException(errors);
        }

        var startsAt = StartTime is { } startTime
            ? StartDate.At(startTime).InZone(zone, Resolvers.LenientResolver).ToInstant()
            : zone.AtStartOfDay(StartDate).ToInstant();

        Instant? endsAt = null;
        if (EndDate is { } end)
        {
            endsAt = EndTime is { } endTime
                ? end.At(endTime).InZone(zone, Resolvers.LenientResolver).ToInstant()
                : zone.AtStartOfDay(end.PlusDays(1)).ToInstant() - EndOfDayPrecision;

            if (endsAt.Value < startsAt)
            {
                throw new DomainValidationException("endTime", "The event cannot end before it starts.");
            }
        }

        return new ResolvedSchedule(startsAt, endsAt);
    }
}

public readonly record struct ResolvedSchedule(Instant StartsAt, Instant? EndsAt);
