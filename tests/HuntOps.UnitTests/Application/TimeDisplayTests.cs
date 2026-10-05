using HuntOps.Application.Display;
using NodaTime;

namespace HuntOps.UnitTests.Application;

/// <summary>Timezone display: event-local as published, plus the owner's time when it differs.</summary>
public sealed class TimeDisplayTests
{
    private static readonly IDateTimeZoneProvider Zones = DateTimeZoneProviders.Tzdb;
    private static DateTimeZone Zone(string id) => Zones[id];

    [Fact]
    public void Closing_time_in_mountain_time_is_also_shown_in_pacific_time()
    {
        // "Closes 11:59 PM MT on June 12" for an owner in Los Angeles.
        var closes = new LocalDateTime(2027, 6, 12, 23, 59).InZoneLeniently(Zone("America/Denver")).ToInstant();

        var moment = TimeDisplay.Moment(closes, Zone("America/Denver"), Zone("America/Los_Angeles"));

        Assert.Equal("Jun 12, 2027, 11:59 PM MDT", moment.EventLocal);
        Assert.Equal("Jun 12, 2027, 10:59 PM PDT", moment.OwnerLocal);
    }

    [Fact]
    public void Eastern_deadline_falls_on_the_previous_evening_for_a_pacific_owner()
    {
        var closes = new LocalDateTime(2027, 6, 13, 1, 0).InZoneLeniently(Zone("America/New_York")).ToInstant();

        var moment = TimeDisplay.Moment(closes, Zone("America/New_York"), Zone("America/Los_Angeles"));

        Assert.Equal("Jun 13, 2027, 1:00 AM EDT", moment.EventLocal);
        Assert.Equal("Jun 12, 2027, 10:00 PM PDT", moment.OwnerLocal);
    }

    [Fact]
    public void Same_offset_shows_one_time()
    {
        var instant = Instant.FromUtc(2027, 6, 12, 12, 0);

        var moment = TimeDisplay.Moment(instant, Zone("America/Los_Angeles"), Zone("America/Los_Angeles"));

        Assert.Null(moment.OwnerLocal);
        Assert.Equal("Jun 12, 2027, 5:00 AM PDT", moment.ToString());
    }

    [Fact]
    public void Arizona_and_pacific_share_an_offset_in_summer_but_not_in_winter()
    {
        var summer = Instant.FromUtc(2027, 7, 1, 18, 0);
        var winter = Instant.FromUtc(2027, 1, 15, 18, 0);

        Assert.Null(TimeDisplay.Moment(summer, Zone("America/Phoenix"), Zone("America/Los_Angeles")).OwnerLocal);
        Assert.Equal("Jan 15, 2027, 10:00 AM PST", TimeDisplay.Moment(winter, Zone("America/Phoenix"), Zone("America/Los_Angeles")).OwnerLocal);
    }

    [Fact]
    public void Standard_time_abbreviations_are_used_in_winter()
    {
        Assert.Equal("Jan 15, 2027, 12:00 PM CST", TimeDisplay.Instant(Instant.FromUtc(2027, 1, 15, 18, 0), Zone("America/Chicago")));
    }

    [Theory]
    [InlineData(8, "8 days remaining")]
    [InlineData(1, "1 day remaining")]
    [InlineData(0, "Due today")]
    [InlineData(-1, "1 day overdue")]
    [InlineData(-6, "6 days overdue")]
    public void Days_remaining_reads_naturally(int days, string expected)
    {
        Assert.Equal(expected, TimeDisplay.DaysRemaining(days));
    }

    [Fact]
    public void Headlines_use_human_dates()
    {
        var opens = Instant.FromUtc(2027, 5, 12, 5, 0);

        Assert.Equal("Application period opens May 12, closes Jun 12",
            TimeDisplay.Headline("Application period", true, "2027-05-12", "2027-06-12", opens - Duration.FromDays(10), opens));
        Assert.Equal("Application period closes Jun 12",
            TimeDisplay.Headline("Application period", true, "2027-05-12", "2027-06-12", opens + Duration.FromDays(1), opens));
        Assert.Equal("Draw results Jun 25",
            TimeDisplay.Headline("Draw results", false, "2027-06-25", null, opens, opens));
    }

    [Fact]
    public void Long_dates_are_spelled_out()
    {
        Assert.Equal("June 12, 2027", TimeDisplay.Date(new LocalDate(2027, 6, 12)));
        Assert.Null(TimeDisplay.Date("not-a-date"));
    }
}
