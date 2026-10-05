using HuntOps.Application.Display;
using HuntOps.Application.Events;
using HuntOps.Application.Users;
using HuntOps.Web.Security;
using NodaTime;

namespace HuntOps.Web.Services;

/// <summary>The owner's time zone for display, cached per circuit and refreshed after preferences are saved.</summary>
public sealed class OwnerTime(AppServices services, IDateTimeZoneProvider zones, IClock clock)
{
    private string? _timeZoneId;

    public IClock Clock => clock;

    public IDateTimeZoneProvider Zones => zones;

    public async Task<string> TimeZoneIdAsync() =>
        _timeZoneId ??= await services.RunAsync<OwnerSettingsService, string>(s => s.GetTimeZoneIdAsync(CancellationToken.None));

    public void Invalidate() => _timeZoneId = null;

    public async Task<ScheduleDisplay> DescribeAsync(EventDto e) => TimeDisplay.Describe(e, await TimeZoneIdAsync(), zones);

    /// <summary>Today in the owner's zone.</summary>
    public async Task<LocalDate> TodayAsync() =>
        clock.GetCurrentInstant().InZone(zones.GetZoneOrNull(await TimeZoneIdAsync()) ?? DateTimeZone.Utc).Date;
}
