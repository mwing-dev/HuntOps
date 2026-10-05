using HuntOps.Application.Common;
using HuntOps.Application.Users;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HuntOps.IntegrationTests.Web;

public sealed class OwnerSettingsTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Defaults_are_seeded_and_changes_persist()
    {
        await using var host = await ComponentTestHost.CreateAsync(postgres);

        var defaults = await host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s => s.GetAsync(Ct));
        Assert.Equal(("America/Los_Angeles", true, "21:00", "07:00", true), (defaults.TimeZoneId, defaults.QuietHoursEnabled, defaults.QuietHoursStart, defaults.QuietHoursEnd, defaults.UrgentBypassesQuietHours));

        var saved = await host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s =>
            s.UpdateAsync(new OwnerSettingsInput("America/Denver", true, "22:30", "06:15", false, defaults.Version), Ct));
        var reloaded = await host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s => s.GetAsync(Ct));

        Assert.Equal(("America/Denver", "22:30", "06:15", false), (reloaded.TimeZoneId, reloaded.QuietHoursStart, reloaded.QuietHoursEnd, reloaded.UrgentBypassesQuietHours));
        Assert.Equal(saved.Version, reloaded.Version);
    }

    [Fact]
    public async Task Invalid_settings_are_rejected_by_the_application_layer()
    {
        await using var host = await ComponentTestHost.CreateAsync(postgres);
        var current = await host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s => s.GetAsync(Ct));

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s =>
            s.UpdateAsync(new OwnerSettingsInput("Pacific Time", true, "9pm", "21:00", true, current.Version), Ct)));

        Assert.Contains("timeZoneId", ex.Errors.Keys);
        Assert.Contains("quietHoursStart", ex.Errors.Keys);
    }

    [Fact]
    public async Task Stale_settings_version_is_a_conflict_not_an_overwrite()
    {
        await using var host = await ComponentTestHost.CreateAsync(postgres);
        var opened = await host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s => s.GetAsync(Ct));
        await host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s => s.UpdateAsync(new OwnerSettingsInput("America/Chicago", true, "21:00", "07:00", true, opened.Version), Ct));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s =>
            s.UpdateAsync(new OwnerSettingsInput("America/New_York", true, "21:00", "07:00", true, opened.Version), Ct)));
        Assert.Equal("America/Chicago", (await host.RunAsync<OwnerSettingsService, OwnerSettingsDto>(s => s.GetAsync(Ct))).TimeZoneId);
    }
}
