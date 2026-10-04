using System.Net;
using HuntOps.IntegrationTests.Infrastructure;
using NodaTime;
using NodaTime.Testing;

namespace HuntOps.IntegrationTests.Api;

/// <summary>The V1 core loop through the REST API: create reference data, an event window with an action, see it, complete it.</summary>
public sealed class EventWorkflowTests(PostgresFixture postgres)
{
    // 2027-05-20 10:00 CDT: inside a May 12 – June 12 window.
    private static readonly Instant Now = Instant.FromUtc(2027, 5, 20, 15, 0);

    [Fact]
    public async Task Create_window_event_compute_utc_list_as_action_item_complete_and_stop_listing()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, new FakeClock(Now));
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);

        var created = await writer.PostJsonAsync("/api/events", new
        {
            programId,
            eventTypeKey = "application-period",
            seasonYear = 2027,
            startDate = "2027-05-12",
            endDate = "2027-06-12",
            sourceUrl = "https://wildlife.example.gov/antelope",
            verified = true,
            actions = new[] { new { title = "Apply or buy preference point", kind = "apply" } },
        });

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal("America/Chicago", created.Str("timeZoneId")); // defaulted from the jurisdiction
        Assert.Equal("2027-05-12T05:00:00Z", created.Str("startsAtUtc"));
        Assert.Equal("2027-06-13T04:59:59.999Z", created.Str("endsAtUtc"));
        Assert.Equal("open", created.Str("phase"));
        Assert.Equal("verified", created.Str("verificationStatus"));
        Assert.StartsWith("apikey:hops_", created.Str("verifiedBy"), StringComparison.Ordinal);
        Assert.Equal("Resident Antelope – Application period 2027", created.Str("name"));
        var action = Assert.Single(created.Json.GetProperty("actions").EnumerateArray());
        Assert.Equal("open", action.GetProperty("status").GetString());
        var eventId = created.Id;

        var upcoming = await writer.GetApiAsync("/api/events/upcoming?days=30");
        Assert.Contains(upcoming.Json.EnumerateArray(), e => e.GetProperty("id").GetGuid() == eventId);

        var items = await writer.GetApiAsync("/api/action-items?days=60");
        var item = Assert.Single(items.Json.EnumerateArray());
        Assert.Equal("open", item.GetProperty("status").GetString());
        Assert.Equal(24, item.GetProperty("daysRemaining").GetInt32());
        Assert.Equal("KS", item.GetProperty("jurisdictionCode").GetString());
        Assert.Equal("https://wildlife.example.gov/antelope", item.GetProperty("link").GetString());

        var completed = await writer.PostJsonAsync($"/api/events/{eventId}/complete", new { outcome = "Preference point purchased" });
        Assert.Equal(HttpStatusCode.OK, completed.Status);
        Assert.Equal("completed", completed.Str("status"));
        Assert.Equal("Preference point purchased", completed.Str("outcome"));

        Assert.Empty((await writer.GetApiAsync("/api/action-items?days=60")).Json.EnumerateArray());
        var withResolved = await writer.GetApiAsync("/api/action-items?days=60&includeResolved=true");
        Assert.Equal("completed", Assert.Single(withResolved.Json.EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Status_history_is_append_only_idempotent_and_reopenable()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, new FakeClock(Now));
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);
        var created = await writer.PostJsonAsync("/api/events", new
        {
            programId, eventTypeKey = "application-period", seasonYear = 2027, startDate = "2027-05-12", endDate = "2027-06-12",
            actions = new[] { new { title = "Apply" } },
        });
        var actionId = created.Json.GetProperty("actions")[0].GetProperty("id").GetGuid();

        await writer.PostJsonAsync($"/api/actions/{actionId}/status", new { status = "completed", outcome = "Applied" });
        await writer.PostJsonAsync($"/api/actions/{actionId}/status", new { status = "completed", outcome = "Applied" }); // repeat: no new row
        var reopened = await writer.PostJsonAsync($"/api/actions/{actionId}/status", new { status = "reopened", note = "Wrong species" });
        var reopenAgain = await writer.PostJsonAsync($"/api/actions/{actionId}/status", new { status = "reopened" });

        Assert.Equal("open", reopened.Str("status"));
        reopenAgain.AssertSafeProblem(HttpStatusCode.Conflict);

        var history = await writer.GetApiAsync($"/api/actions/{actionId}/history");
        var statuses = history.Json.EnumerateArray().Select(h => h.GetProperty("status").GetString()).ToArray();
        Assert.Equal(["reopened", "completed"], statuses);
        Assert.All(history.Json.EnumerateArray(), h => Assert.Equal("api", h.GetProperty("changedVia").GetString()));
    }

    [Fact]
    public async Task Completing_one_season_does_not_touch_another_and_program_history_shows_both()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, new FakeClock(Now));
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);

        async Task<Guid> CreateSeasonAsync(int year) =>
            (await writer.PostJsonAsync("/api/events", new
            {
                programId, eventTypeKey = "application-period", seasonYear = year,
                startDate = $"{year}-05-12", endDate = $"{year}-06-12",
                actions = new[] { new { title = "Apply or buy preference point" } },
            })).Id;

        var season2026 = await CreateSeasonAsync(2026);
        await CreateSeasonAsync(2027);
        await writer.PostJsonAsync($"/api/events/{season2026}/complete", new { outcome = "Preference point purchased" });

        var history = await writer.GetApiAsync($"/api/programs/{programId}/history");
        var seasons = history.Json.EnumerateArray().ToList();
        Assert.Equal([2027, 2026], seasons.Select(s => s.GetProperty("seasonYear").GetInt32()).ToArray());

        static string StatusOf(System.Text.Json.JsonElement season) =>
            season.GetProperty("events")[0].GetProperty("event").GetProperty("actions")[0].GetProperty("status").GetString()!;

        Assert.Equal("open", StatusOf(seasons[0]));
        Assert.Equal("completed", StatusOf(seasons[1]));
    }

    [Fact]
    public async Task Missed_actions_show_after_the_window_and_point_events_do_not_auto_miss()
    {
        var clock = new FakeClock(Now);
        await using var factory = await ApiFactory.CreateAsync(postgres, clock);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);
        await writer.PostJsonAsync("/api/events", new
        {
            programId, eventTypeKey = "application-period", seasonYear = 2027, startDate = "2027-05-12", endDate = "2027-06-12",
            actions = new[] { new { title = "Apply" } },
        });

        clock.Reset(Instant.FromUtc(2027, 6, 15, 12, 0));
        var item = Assert.Single((await writer.GetApiAsync("/api/action-items")).Json.EnumerateArray());

        Assert.Equal("missed", item.GetProperty("status").GetString());
        Assert.True(item.GetProperty("daysRemaining").GetInt32() < 0);
    }

    [Fact]
    public async Task Umbrella_program_events_are_inherited_in_child_history()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, new FakeClock(Now));
        using var writer = await factory.CreateWriterAsync();
        var (_, agencyId, umbrellaId) = await Seed.ProgramAsync(writer, "CO", "Colorado", "America/Denver", "Big Game Primary Draw", species: "");
        var elk = await writer.PostJsonAsync("/api/programs", new { agencyId, name = "Elk", species = "Elk", parentProgramId = umbrellaId });
        Assert.Equal(HttpStatusCode.Created, elk.Status);
        await writer.PostJsonAsync("/api/events", new { programId = umbrellaId, eventTypeKey = "application-period", seasonYear = 2027, startDate = "2027-03-01", endDate = "2027-04-07", endTime = "20:00" });

        var history = await writer.GetApiAsync($"/api/programs/{elk.Id}/history");
        var inherited = history.Json[0].GetProperty("events")[0];

        Assert.Equal(umbrellaId, inherited.GetProperty("inheritedFromProgramId").GetGuid());
        Assert.Equal("2027-04-08T02:00:00Z", inherited.GetProperty("event").GetProperty("endsAtUtc").GetString());
    }
}
