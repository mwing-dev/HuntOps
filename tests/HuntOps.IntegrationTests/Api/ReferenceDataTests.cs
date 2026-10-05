using System.Net;
using HuntOps.IntegrationTests.Infrastructure;

namespace HuntOps.IntegrationTests.Api;

public sealed class ReferenceDataTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Seeded_event_types_are_available_and_custom_types_can_be_added()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();

        var types = await writer.GetApiAsync("/api/event-types");
        var keys = types.Json.EnumerateArray().Select(t => t.GetProperty("key").GetString()).ToHashSet();
        Assert.Superset(new HashSet<string?>(["application-period", "preference-point-period", "draw-results", "otc-sale", "leftover-sale", "harvest-reporting", "season"]), keys);
        Assert.All(types.Json.EnumerateArray(), t => Assert.True(t.GetProperty("isSystem").GetBoolean()));

        var custom = await writer.PostJsonAsync("/api/event-types", new { key = "hunter-ed-deadline", displayName = "Hunter education deadline", category = "other" });
        Assert.Equal(HttpStatusCode.Created, custom.Status);
        Assert.False(custom.Json.GetProperty("isSystem").GetBoolean());

        (await writer.PostJsonAsync("/api/event-types", new { key = "hunter-ed-deadline", displayName = "Dup", category = "other" }))
            .AssertSafeProblem(HttpStatusCode.Conflict);
        var badKey = await writer.PostJsonAsync("/api/event-types", new { key = "Bad Key!", displayName = "x", category = "purchase" });
        badKey.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("key", badKey.ErrorFields);
    }

    [Fact]
    public async Task Archived_event_type_stays_on_existing_events_but_cannot_be_used_for_new_ones()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);
        var existing = await writer.PostJsonAsync("/api/events", new { programId, eventTypeKey = "leftover-sale", seasonYear = 2027, startDate = "2027-08-01" });

        Assert.Equal(HttpStatusCode.OK, (await writer.DeleteApiAsync("/api/event-types/leftover-sale")).Status);

        Assert.Equal("leftover-sale", (await writer.GetApiAsync($"/api/events/{existing.Id}")).Str("eventTypeKey"));
        var rejected = await writer.PostJsonAsync("/api/events", new { programId, eventTypeKey = "leftover-sale", seasonYear = 2028, startDate = "2028-08-01" });
        rejected.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("eventTypeKey", rejected.ErrorFields);
    }

    [Fact]
    public async Task Names_and_codes_are_unique_case_insensitively_among_active_records()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();

        var created = await writer.PostJsonAsync("/api/jurisdictions", new { name = "California", code = "ca", country = "us", timeZoneId = "America/Los_Angeles" });
        Assert.Equal("CA", created.Str("code"));
        Assert.Equal("US", created.Str("country"));

        (await writer.PostJsonAsync("/api/jurisdictions", new { name = "  CALIFORNIA ", code = "CX", country = "US", timeZoneId = "America/Los_Angeles" }))
            .AssertSafeProblem(HttpStatusCode.Conflict);
        (await writer.PostJsonAsync("/api/jurisdictions", new { name = "Calif.", code = "CA", country = "US", timeZoneId = "America/Los_Angeles" }))
            .AssertSafeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Programs_get_unique_slugs_free_text_categories_and_attributes()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, agencyId, _) = await Seed.ProgramAsync(writer, "WY", "Wyoming", "America/Denver", "Elk", "Elk");

        var program = await writer.PostJsonAsync("/api/programs", new
        {
            agencyId,
            name = "Elk – Area 7 Type 1",
            species = "Elk",
            method = "Any legal weapon",
            residency = "Nonresident",
            permitType = "Limited quota",
            unit = "Area 7",
            attributes = new Dictionary<string, string> { ["huntCode"] = "007-1", ["bag"] = "Any elk" },
        });

        Assert.Equal(HttpStatusCode.Created, program.Status);
        Assert.Equal("wy-elk-area-7-type-1", program.Str("slug"));
        Assert.Equal("007-1", program.Json.GetProperty("attributes").GetProperty("huntCode").GetString());

        var filtered = await writer.GetApiAsync("/api/programs?species=ELK");
        Assert.Equal(2, filtered.Json.GetArrayLength());
    }

    [Fact]
    public async Task Umbrella_programs_must_be_same_agency_and_acyclic()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, agencyA, programA) = await Seed.ProgramAsync(writer, "CO", "Colorado", "America/Denver", "Primary Draw", "");
        var (_, _, programB) = await Seed.ProgramAsync(writer, "NM", "New Mexico", "America/Denver", "Elk Draw", "Elk");
        var child = await writer.PostJsonAsync("/api/programs", new { agencyId = agencyA, name = "Deer", parentProgramId = programA });

        var crossAgency = await writer.PostJsonAsync("/api/programs", new { agencyId = agencyA, name = "Pronghorn", parentProgramId = programB });
        crossAgency.AssertSafeProblem(HttpStatusCode.BadRequest);

        var parent = await writer.GetApiAsync($"/api/programs/{programA}");
        var cycle = await writer.PutJsonAsync($"/api/programs/{programA}", new { agencyId = agencyA, name = "Primary Draw", parentProgramId = child.Id, version = parent.Version });
        cycle.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("parentProgramId", cycle.ErrorFields);

        (await writer.DeleteApiAsync($"/api/programs/{programA}")).AssertSafeProblem(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Editing_dates_clears_verification_unless_reverified()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);
        var created = await writer.PostJsonAsync("/api/events", new { programId, eventTypeKey = "application-period", seasonYear = 2027, startDate = "2027-05-12", endDate = "2027-06-10", verified = true });

        var moved = await writer.PutJsonAsync($"/api/events/{created.Id}", new { programId, eventTypeKey = "application-period", seasonYear = 2027, startDate = "2027-05-12", endDate = "2027-06-12", version = created.Version });
        Assert.Equal("unverified", moved.Str("verificationStatus"));
        Assert.Equal("2027-06-13T04:59:59.999Z", moved.Str("endsAtUtc"));

        var verified = await writer.PostJsonAsync($"/api/events/{created.Id}/verify");
        Assert.Equal("verified", verified.Str("verificationStatus"));
    }
}
