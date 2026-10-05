using Bunit;
using HuntOps.Application.Display;
using HuntOps.Application.Events;
using HuntOps.Application.Reference;
using HuntOps.Domain.Actions;
using HuntOps.IntegrationTests.Infrastructure;
using HuntOps.Web.Components.Shared;
using HuntOps.Web.Services;
using NodaTime;

namespace HuntOps.IntegrationTests.Web;

/// <summary>Dashboard components driven like a user would, backed by the real services and PostgreSQL.</summary>
public sealed class DashboardComponentTests(PostgresFixture postgres)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Completing_and_reopening_an_action_appends_history_as_the_owner()
    {
        await using var host = await ComponentTestHost.CreateAsync(postgres);
        var programEvent = await CreateEventAsync(host, "Apply or buy preference point");
        var action = programEvent.Actions[0];

        var completed = new TaskCompletionSource<ActionDto>();
        var form = host.Render<ActionStatusForm>(p => p
            .Add(c => c.ActionId, action.Id)
            .Add(c => c.ActionTitle, action.Title)
            .Add(c => c.Mode, ActionResolution.Completed)
            .Add(c => c.OnSaved, (ActionDto a) => completed.TrySetResult(a)));

        await form.InvokeAsync(() => form.FindAll(".mud-chip").Single(c => c.TextContent.Contains("Preference point purchased", StringComparison.Ordinal)).Click());
        await form.InvokeAsync(() => ComponentTestHost.Button(form, "Complete").Click());
        var afterComplete = await completed.Task.WaitAsync(Wait, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal(EffectiveActionStatus.Completed, afterComplete.Status);
        Assert.Equal("Preference point purchased", afterComplete.Outcome);

        var reopened = new TaskCompletionSource<ActionDto>();
        var reopenForm = host.Render<ActionStatusForm>(p => p
            .Add(c => c.ActionId, action.Id)
            .Add(c => c.ActionTitle, action.Title)
            .Add(c => c.Mode, ActionResolution.Reopened)
            .Add(c => c.OnSaved, (ActionDto a) => reopened.TrySetResult(a)));
        await reopenForm.InvokeAsync(() => ComponentTestHost.Button(reopenForm, "Reopen").Click());
        var afterReopen = await reopened.Task.WaitAsync(Wait, Xunit.TestContext.Current.CancellationToken);

        Assert.NotEqual(EffectiveActionStatus.Completed, afterReopen.Status);
        var history = await host.RunAsync<ActionService, IReadOnlyList<ActionStatusChangeDto>>(s => s.HistoryAsync(action.Id, CancellationToken.None));
        Assert.Equal([ActionResolution.Reopened, ActionResolution.Completed], history.Select(h => h.Status).ToArray());
        Assert.All(history, h => Assert.Equal(ChangeChannel.Web, h.ChangedVia));
        Assert.All(history, h => Assert.Equal("user:owner@huntops.test", h.ChangedBy));
    }

    [Fact]
    public async Task Reopening_an_unresolved_action_shows_the_reason_and_records_nothing()
    {
        await using var host = await ComponentTestHost.CreateAsync(postgres);
        var action = (await CreateEventAsync(host, "Apply")).Actions[0];

        var form = host.Render<ActionStatusForm>(p => p
            .Add(c => c.ActionId, action.Id)
            .Add(c => c.ActionTitle, action.Title)
            .Add(c => c.Mode, ActionResolution.Reopened));
        await form.InvokeAsync(() => ComponentTestHost.Button(form, "Reopen").Click());

        form.WaitForAssertion(() => Assert.Contains("nothing to reopen", form.Markup, StringComparison.Ordinal), Wait);
        Assert.Empty(await host.RunAsync<ActionService, IReadOnlyList<ActionStatusChangeDto>>(s => s.HistoryAsync(action.Id, CancellationToken.None)));
    }

    [Fact]
    public async Task Program_history_renders_each_season_with_outcomes_newest_first()
    {
        await using var host = await ComponentTestHost.CreateAsync(postgres);
        var (programId, _) = await CreateProgramAsync(host);
        foreach (var (year, outcome) in new[] { (2023, "Not drawn"), (2025, "Preference point purchased") })
        {
            var e = await host.RunAsync<ProgramEventService, EventDto>(s => s.CreateAsync(
                new EventInput(programId, "application-period", year, $"{year}-05-12", EndDate: $"{year}-06-12", Actions: [new ActionInput("Apply")]), CancellationToken.None));
            await host.RunAsync<ActionService, ActionDto>(s => s.ChangeStatusAsync(e.Actions[0].Id, new StatusChangeInput("completed", outcome), CancellationToken.None));
        }

        await host.RunAsync<ProgramEventService, EventDto>(s => s.CreateAsync(new EventInput(programId, "draw-results", 2027, "2027-06-25"), CancellationToken.None));
        var history = await host.RunAsync<ProgramService, IReadOnlyList<ProgramSeasonDto>>(s => s.GetHistoryAsync(programId, host.OwnerId, CancellationToken.None));

        var view = host.Render<ProgramHistoryView>(p => p.Add(c => c.Seasons, history));

        var seasons = view.FindAll(".history-season").Select(s => s.GetAttribute("data-season")).ToArray();
        Assert.Equal(["2027", "2025", "2023"], seasons);
        Assert.Contains("Apply: Preference point purchased", view.Markup, StringComparison.Ordinal);
        Assert.Contains("Apply: Not drawn", view.Markup, StringComparison.Ordinal);
        Assert.Contains("Draw results", view.Markup, StringComparison.Ordinal);
        Assert.Contains("June 25, 2027", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Concurrent_edit_shows_reload_message_and_does_not_overwrite()
    {
        await using var host = await ComponentTestHost.CreateAsync(postgres);
        var opened = await host.RunAsync<JurisdictionService, JurisdictionDto>(s => s.CreateAsync(new JurisdictionInput("Montana", "MT", "US", "America/Denver"), CancellationToken.None));

        // Someone else saves a change after this form was opened.
        await host.RunAsync<JurisdictionService, JurisdictionDto>(s => s.UpdateAsync(opened.Id,
            new JurisdictionInput("Montana", "MT", "US", "America/Denver", Notes: "edited elsewhere", Version: opened.Version), CancellationToken.None));

        var form = host.Render<JurisdictionForm>(p => p.Add(c => c.Existing, opened with { Notes = "my stale edit" }));
        await form.InvokeAsync(() => ComponentTestHost.Button(form, "Save").Click());

        form.WaitForAssertion(() => Assert.Contains(UiErrors.ConcurrencyMessage, form.Markup, StringComparison.Ordinal), Wait);
        var current = await host.RunAsync<JurisdictionService, JurisdictionDto>(s => s.GetAsync(opened.Id, CancellationToken.None));
        Assert.Equal("edited elsewhere", current.Notes);

        await form.InvokeAsync(() => ComponentTestHost.Button(form, "Reload latest").Click());
        form.WaitForAssertion(() => Assert.DoesNotContain(UiErrors.ConcurrencyMessage, form.Markup, StringComparison.Ordinal), Wait);
        Assert.Contains("edited elsewhere", form.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_validation_comes_from_the_application_layer_and_nothing_is_saved()
    {
        await using var host = await ComponentTestHost.CreateAsync(postgres);

        var form = host.Render<JurisdictionForm>();
        await form.InvokeAsync(() => ComponentTestHost.Button(form, "Save").Click());

        form.WaitForAssertion(() => Assert.Contains("Please fix the highlighted fields.", form.Markup, StringComparison.Ordinal), Wait);
        Assert.Contains("This field is required.", form.Markup, StringComparison.Ordinal);
        Assert.Empty(await host.RunAsync<JurisdictionService, IReadOnlyList<JurisdictionDto>>(s => s.ListAsync(true, CancellationToken.None)));
    }

    [Fact]
    public void Schedule_view_shows_event_and_owner_times_when_zones_differ()
    {
        using var context = new BunitContext();
        context.Services.AddMudServicesForTests();
        var e = SampleEvent("America/Denver", "2027-05-01", "2027-06-12", "17:00");

        var view = context.Render<ScheduleView>(p => p.Add(c => c.Display, TimeDisplay.Describe(e, "America/Los_Angeles", DateTimeZoneProviders.Tzdb)));

        Assert.Contains("Jun 12, 2027, 5:00 PM MDT", view.Markup, StringComparison.Ordinal);
        Assert.Contains("Jun 12, 2027, 4:00 PM PDT", view.Markup, StringComparison.Ordinal);
        Assert.Contains("Your time", view.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Schedule_view_does_not_repeat_times_when_zones_match()
    {
        using var context = new BunitContext();
        context.Services.AddMudServicesForTests();
        var e = SampleEvent("America/Los_Angeles", "2027-05-01", "2027-06-12", null);

        var view = context.Render<ScheduleView>(p => p.Add(c => c.Display, TimeDisplay.Describe(e, "America/Los_Angeles", DateTimeZoneProviders.Tzdb)));

        Assert.DoesNotContain("Your time", view.Markup, StringComparison.Ordinal);
        Assert.Contains("same as yours", view.Markup, StringComparison.Ordinal);
    }

    private static async Task<(Guid ProgramId, Guid JurisdictionId)> CreateProgramAsync(ComponentTestHost host)
    {
        var jurisdiction = await host.RunAsync<JurisdictionService, JurisdictionDto>(s => s.CreateAsync(new JurisdictionInput("Kansas", "KS", "US", "America/Chicago"), CancellationToken.None));
        var agency = await host.RunAsync<AgencyService, AgencyDto>(s => s.CreateAsync(new AgencyInput(jurisdiction.Id, "Kansas Wildlife Agency"), CancellationToken.None));
        var program = await host.RunAsync<ProgramService, ProgramDto>(s => s.CreateAsync(new ProgramInput(agency.Id, "Resident Antelope", Species: "Antelope"), CancellationToken.None));
        return (program.Id, jurisdiction.Id);
    }

    private static async Task<EventDto> CreateEventAsync(ComponentTestHost host, string actionTitle)
    {
        var (programId, _) = await CreateProgramAsync(host);
        return await host.RunAsync<ProgramEventService, EventDto>(s => s.CreateAsync(
            new EventInput(programId, "application-period", 2099, "2099-05-12", EndDate: "2099-06-12", Actions: [new ActionInput(actionTitle)]), CancellationToken.None));
    }

    private static EventDto SampleEvent(string zone, string start, string end, string? endTime)
    {
        var schedule = new HuntOps.Domain.Events.EventSchedule(
            NodaTime.Text.LocalDatePattern.Iso.Parse(start).Value, null,
            NodaTime.Text.LocalDatePattern.Iso.Parse(end).Value,
            endTime is null ? null : NodaTime.Text.LocalTimePattern.CreateWithInvariantCulture("HH:mm").Parse(endTime).Value,
            zone).Resolve(DateTimeZoneProviders.Tzdb);
        return new EventDto(Guid.NewGuid(), Guid.NewGuid(), "P", Guid.NewGuid(), "A", Guid.NewGuid(), "XX", "application-period", "Application period",
            HuntOps.Domain.Events.EventCategory.Application, "", 2027, null, "n", start, null, end, endTime, zone,
            HuntOps.Application.Common.TimeFormats.Format(schedule.StartsAt), HuntOps.Application.Common.TimeFormats.Format(schedule.EndsAt),
            true, HuntOps.Domain.Events.EventPhase.Upcoming, null, null, HuntOps.Domain.Events.OriginKind.Manual, null,
            HuntOps.Domain.Events.VerificationStatus.Unverified, null, null, null, false, null, "", "", 0, []);
    }
}

internal static class BunitServiceExtensions
{
    public static void AddMudServicesForTests(this Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
        MudBlazor.Services.ServiceCollectionExtensions.AddMudServices(services);
}
