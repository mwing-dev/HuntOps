using HuntOps.Domain.Actions;
using HuntOps.Domain.Events;
using NodaTime;

namespace HuntOps.UnitTests.Domain;

/// <summary>Architecture R4: stored decisions win; otherwise status is derived from the clock.</summary>
public sealed class ActionStatusResolverTests
{
    private static readonly Instant BeforeOpen = Instant.FromUtc(2027, 5, 1, 12, 0);
    private static readonly Instant DuringWindow = Instant.FromUtc(2027, 5, 20, 12, 0);
    private static readonly Instant AfterClose = Instant.FromUtc(2027, 6, 20, 12, 0);

    private static ProgramEvent Window()
    {
        var e = new ProgramEvent { EventTypeKey = "application-period", Name = "w" };
        e.SetSchedule(new EventSchedule(new LocalDate(2027, 5, 12), null, new LocalDate(2027, 6, 12), null, "America/Chicago"), DateTimeZoneProviders.Tzdb);
        return e;
    }

    private static ProgramEvent Point()
    {
        var e = new ProgramEvent { EventTypeKey = "otc-sale", Name = "p" };
        e.SetSchedule(new EventSchedule(new LocalDate(2027, 7, 1), null, null, null, "America/Chicago"), DateTimeZoneProviders.Tzdb);
        return e;
    }

    private static ActionStatusChange Change(ActionResolution status, Instant at, long sequence = 1) => new()
    {
        RequiredActionId = Guid.NewGuid(),
        UserId = "owner",
        ChangedBy = "test",
        Status = status,
        ChangedAt = at,
        Sequence = sequence,
    };

    [Theory]
    [MemberData(nameof(ClockCases))]
    public void Without_decisions_status_follows_the_window(Instant now, EffectiveActionStatus expected)
    {
        Assert.Equal(expected, ActionStatusResolver.Resolve(Window(), null, now));
    }

    public static TheoryData<Instant, EffectiveActionStatus> ClockCases => new()
    {
        { BeforeOpen, EffectiveActionStatus.Upcoming },
        { DuringWindow, EffectiveActionStatus.Open },
        { AfterClose, EffectiveActionStatus.Missed },
    };

    [Fact]
    public void Completed_wins_even_after_the_window_closes()
    {
        var status = ActionStatusResolver.Resolve(Window(), Change(ActionResolution.Completed, DuringWindow), AfterClose);

        Assert.Equal(EffectiveActionStatus.Completed, status);
        Assert.True(ActionStatusResolver.IsResolved(status));
    }

    [Theory]
    [InlineData(ActionResolution.NotApplicable, EffectiveActionStatus.NotApplicable)]
    [InlineData(ActionResolution.Cancelled, EffectiveActionStatus.Cancelled)]
    public void Other_stored_decisions_are_reported_as_is(ActionResolution decision, EffectiveActionStatus expected)
    {
        Assert.Equal(expected, ActionStatusResolver.Resolve(Window(), Change(decision, BeforeOpen), DuringWindow));
    }

    [Fact]
    public void Reopened_falls_back_to_the_clock()
    {
        Assert.Equal(EffectiveActionStatus.Open, ActionStatusResolver.Resolve(Window(), Change(ActionResolution.Reopened, DuringWindow), DuringWindow));
        Assert.Equal(EffectiveActionStatus.Missed, ActionStatusResolver.Resolve(Window(), Change(ActionResolution.Reopened, DuringWindow), AfterClose));
    }

    [Fact]
    public void Point_event_action_stays_open_once_started_and_is_never_auto_missed()
    {
        Assert.Equal(EffectiveActionStatus.Upcoming, ActionStatusResolver.Resolve(Point(), null, Instant.FromUtc(2027, 6, 30, 0, 0)));
        Assert.Equal(EffectiveActionStatus.Open, ActionStatusResolver.Resolve(Point(), null, Instant.FromUtc(2028, 1, 1, 0, 0)));
    }

    [Fact]
    public void Latest_change_is_chosen_by_time_then_insertion_order()
    {
        var completed = Change(ActionResolution.Completed, DuringWindow, sequence: 1);
        var reopenedSameInstant = Change(ActionResolution.Reopened, DuringWindow, sequence: 2);
        var olderCancel = Change(ActionResolution.Cancelled, BeforeOpen, sequence: 3);

        var latest = ActionStatusResolver.Latest([olderCancel, completed, reopenedSameInstant]);

        Assert.Same(reopenedSameInstant, latest);
    }
}
