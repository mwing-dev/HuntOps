using HuntOps.Application.Operations;
using NodaTime;

namespace HuntOps.UnitTests.Application;

public sealed class HeartbeatFreshnessTests
{
    private static readonly Instant Started = Instant.FromUtc(2027, 5, 12, 12, 0);
    private static readonly Duration MaxAge = Duration.FromMinutes(2);

    [Fact]
    public void Worker_that_has_not_beaten_yet_is_starting_within_grace_window()
    {
        var status = HeartbeatFreshness.Evaluate(null, Started, Started + Duration.FromSeconds(90), MaxAge);

        Assert.Equal(HeartbeatStatus.Starting, status);
        Assert.True(HeartbeatFreshness.IsHealthy(status));
    }

    [Fact]
    public void Worker_that_never_beats_becomes_unhealthy_after_grace_window()
    {
        var status = HeartbeatFreshness.Evaluate(null, Started, Started + Duration.FromMinutes(3), MaxAge);

        Assert.Equal(HeartbeatStatus.NeverBeat, status);
        Assert.False(HeartbeatFreshness.IsHealthy(status));
    }

    [Fact]
    public void Recent_beat_is_fresh()
    {
        var now = Started + Duration.FromHours(5);

        var status = HeartbeatFreshness.Evaluate(now - Duration.FromSeconds(30), Started, now, MaxAge);

        Assert.Equal(HeartbeatStatus.Fresh, status);
    }

    [Fact]
    public void Beat_exactly_at_max_age_is_still_fresh()
    {
        var now = Started + Duration.FromHours(5);

        Assert.Equal(HeartbeatStatus.Fresh, HeartbeatFreshness.Evaluate(now - MaxAge, Started, now, MaxAge));
    }

    [Fact]
    public void Beat_older_than_max_age_is_stale_even_long_after_startup()
    {
        var now = Started + Duration.FromDays(3);

        var status = HeartbeatFreshness.Evaluate(now - MaxAge - Duration.FromSeconds(1), Started, now, MaxAge);

        Assert.Equal(HeartbeatStatus.Stale, status);
        Assert.False(HeartbeatFreshness.IsHealthy(status));
    }
}
