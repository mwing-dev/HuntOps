using HuntOps.Domain.Common;
using HuntOps.Domain.Events;
using NodaTime;

namespace HuntOps.UnitTests.Domain;

/// <summary>Time semantics from architecture §4.5: local dates/times in the event's IANA zone, resolved to UTC.</summary>
public sealed class EventScheduleTests
{
    private static readonly IDateTimeZoneProvider Zones = DateTimeZoneProviders.Tzdb;

    private static ResolvedSchedule Resolve(
        string zone,
        LocalDate start,
        LocalTime? startTime = null,
        LocalDate? end = null,
        LocalTime? endTime = null) =>
        new EventSchedule(start, startTime, end, endTime, zone).Resolve(Zones);

    [Fact]
    public void All_day_window_starts_at_local_midnight_and_ends_at_end_of_last_local_day()
    {
        // "Applications accepted May 12 through June 12" in a Pacific-time jurisdiction (PDT = UTC-7).
        var resolved = Resolve("America/Los_Angeles", new LocalDate(2027, 5, 12), end: new LocalDate(2027, 6, 12));

        Assert.Equal(Instant.FromUtc(2027, 5, 12, 7, 0), resolved.StartsAt);
        Assert.Equal(Instant.FromUtc(2027, 6, 13, 7, 0) - Duration.FromMilliseconds(1), resolved.EndsAt);
    }

    [Fact]
    public void Explicit_closing_time_is_interpreted_in_the_event_zone()
    {
        // "Closes 5:00 p.m. MT" on June 12 (MDT = UTC-6).
        var resolved = Resolve("America/Denver", new LocalDate(2027, 5, 1), end: new LocalDate(2027, 6, 12), endTime: new LocalTime(17, 0));

        Assert.Equal(Instant.FromUtc(2027, 6, 12, 23, 0), resolved.EndsAt);
    }

    [Fact]
    public void Same_wall_clock_deadline_differs_by_zone()
    {
        var central = Resolve("America/Chicago", new LocalDate(2027, 6, 1), end: new LocalDate(2027, 6, 12));
        var mountain = Resolve("America/Denver", new LocalDate(2027, 6, 1), end: new LocalDate(2027, 6, 12));

        Assert.Equal(Duration.FromHours(1), mountain.EndsAt!.Value - central.EndsAt!.Value);
    }

    [Fact]
    public void Point_event_has_no_end()
    {
        var schedule = new EventSchedule(new LocalDate(2027, 6, 20), new LocalTime(9, 0), null, null, "America/Chicago");

        var resolved = schedule.Resolve(Zones);

        Assert.False(schedule.IsWindow);
        Assert.Null(resolved.EndsAt);
        Assert.Equal(Instant.FromUtc(2027, 6, 20, 14, 0), resolved.StartsAt);
    }

    [Fact]
    public void Time_skipped_by_spring_forward_shifts_forward_by_the_gap()
    {
        // 2027-03-14 02:30 does not exist in Chicago (clocks jump 02:00 -> 03:00 CDT); lenient => 03:30 CDT.
        var resolved = Resolve("America/Chicago", new LocalDate(2027, 3, 14), new LocalTime(2, 30));

        Assert.Equal(Instant.FromUtc(2027, 3, 14, 8, 30), resolved.StartsAt);
    }

    [Fact]
    public void Ambiguous_fall_back_time_resolves_to_the_earlier_instant()
    {
        // 2027-11-07 01:30 occurs twice in Chicago; the earlier (CDT, UTC-5) is chosen.
        var resolved = Resolve("America/Chicago", new LocalDate(2027, 11, 7), new LocalTime(1, 30));

        Assert.Equal(Instant.FromUtc(2027, 11, 7, 6, 30), resolved.StartsAt);
    }

    [Fact]
    public void End_of_day_on_a_fall_back_day_accounts_for_the_25_hour_day()
    {
        // The day after the change starts at 00:00 CST (UTC-6).
        var resolved = Resolve("America/Chicago", new LocalDate(2027, 11, 1), end: new LocalDate(2027, 11, 7));

        Assert.Equal(Instant.FromUtc(2027, 11, 8, 6, 0) - Duration.FromMilliseconds(1), resolved.EndsAt);
    }

    [Fact]
    public void Start_of_day_when_midnight_does_not_exist_uses_first_valid_instant()
    {
        // Chile springs forward at local midnight (00:00 -> 01:00); the day starts at 01:00 -03.
        var resolved = Resolve("America/Santiago", new LocalDate(2027, 9, 5));

        Assert.Equal(Instant.FromUtc(2027, 9, 5, 4, 0), resolved.StartsAt);
    }

    [Fact]
    public void Same_day_window_with_times()
    {
        var resolved = Resolve("America/Chicago", new LocalDate(2027, 8, 1), new LocalTime(8, 0), new LocalDate(2027, 8, 1), new LocalTime(17, 0));

        Assert.Equal(Duration.FromHours(9), resolved.EndsAt!.Value - resolved.StartsAt);
    }

    [Fact]
    public void End_date_before_start_date_is_rejected()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Resolve("America/Chicago", new LocalDate(2027, 6, 12), end: new LocalDate(2027, 6, 11)));

        Assert.True(ex.Errors.ContainsKey("endDate"));
    }

    [Fact]
    public void End_time_before_start_time_on_the_same_day_is_rejected()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Resolve("America/Chicago", new LocalDate(2027, 6, 12), new LocalTime(17, 0), new LocalDate(2027, 6, 12), new LocalTime(9, 0)));

        Assert.True(ex.Errors.ContainsKey("endTime"));
    }

    [Fact]
    public void End_time_without_end_date_is_rejected()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Resolve("America/Chicago", new LocalDate(2027, 6, 12), endTime: new LocalTime(17, 0)));

        Assert.True(ex.Errors.ContainsKey("endTime"));
    }

    [Theory]
    [InlineData("Mountain Time")]
    [InlineData("MST7MDT-ish")]
    [InlineData("")]
    public void Unknown_time_zone_is_rejected(string zone)
    {
        var ex = Assert.Throws<DomainValidationException>(() => Resolve(zone, new LocalDate(2027, 6, 12)));

        Assert.True(ex.Errors.ContainsKey("timeZoneId"));
    }

    [Fact]
    public void Program_event_recomputes_instants_when_schedule_changes()
    {
        var programEvent = new ProgramEvent { EventTypeKey = "application-period", Name = "x" };
        programEvent.SetSchedule(new EventSchedule(new LocalDate(2027, 5, 12), null, new LocalDate(2027, 6, 10), null, "America/Chicago"), Zones);
        var firstEnd = programEvent.EndsAtUtc;

        programEvent.SetSchedule(new EventSchedule(new LocalDate(2027, 5, 12), null, new LocalDate(2027, 6, 12), null, "America/Chicago"), Zones);

        Assert.Equal(Duration.FromDays(2), programEvent.EndsAtUtc!.Value - firstEnd!.Value);
        Assert.True(programEvent.IsWindow);
    }

    [Fact]
    public void Phase_follows_the_window()
    {
        var programEvent = new ProgramEvent { EventTypeKey = "application-period", Name = "x" };
        programEvent.SetSchedule(new EventSchedule(new LocalDate(2027, 5, 12), null, new LocalDate(2027, 6, 12), null, "America/Chicago"), Zones);

        Assert.Equal(EventPhase.Upcoming, programEvent.PhaseAt(Instant.FromUtc(2027, 5, 12, 4, 59)));
        Assert.Equal(EventPhase.Open, programEvent.PhaseAt(Instant.FromUtc(2027, 5, 12, 5, 0)));
        Assert.Equal(EventPhase.Open, programEvent.PhaseAt(Instant.FromUtc(2027, 6, 13, 4, 59)));
        Assert.Equal(EventPhase.Closed, programEvent.PhaseAt(Instant.FromUtc(2027, 6, 13, 5, 0)));
    }
}
