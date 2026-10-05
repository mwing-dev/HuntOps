using System.Net;
using HuntOps.IntegrationTests.Infrastructure;
using Npgsql;

namespace HuntOps.IntegrationTests.Api;

/// <summary>Invalid requests produce useful, safe errors; important invariants hold in the database too.</summary>
public sealed class ValidationAndConstraintTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Invalid_event_fields_return_field_level_errors_without_internals(string environment)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, environment: environment);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);

        var response = await writer.PostJsonAsync("/api/events", new
        {
            programId,
            eventTypeKey = "application-period",
            seasonYear = 1850,
            startDate = "2027-13-01",
            endTime = "5pm",
            timeZoneId = "Kansas Time",
            sourceUrl = "javascript:alert(1)",
        });

        response.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Superset(
            new HashSet<string>(["seasonYear", "startDate", "endTime", "timeZoneId", "sourceUrl"]),
            new HashSet<string>(response.ErrorFields));
        Assert.Contains("yyyy-MM-dd", response.Json.GetProperty("errors").GetProperty("startDate")[0].GetString(), StringComparison.Ordinal);
        Assert.True(response.Json.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Missing_required_fields_are_all_reported()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();

        var response = await writer.PostJsonAsync("/api/events", new { });

        response.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Superset(new HashSet<string>(["programId", "eventTypeKey", "seasonYear", "startDate"]), new HashSet<string>(response.ErrorFields));
    }

    [Fact]
    public async Task Schedule_rules_are_reported_against_the_right_field()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);

        var response = await writer.PostJsonAsync("/api/events", new
        {
            programId, eventTypeKey = "application-period", seasonYear = 2027, startDate = "2027-06-12", endDate = "2027-06-01",
        });

        response.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("endDate", response.ErrorFields);
    }

    [Fact]
    public async Task Wrong_json_types_and_malformed_bodies_are_explained()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres, environment: "Production");
        using var writer = await factory.CreateWriterAsync();

        var wrongType = await writer.PostRawAsync("/api/events", """{ "seasonYear": "two thousand", "startDate": "2027-05-12" }""");
        wrongType.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("$.seasonYear", wrongType.ErrorFields);

        var malformed = await writer.PostRawAsync("/api/jurisdictions", "{ \"name\": ");
        malformed.AssertSafeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_enum_value_lists_allowed_values()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);
        var created = await writer.PostJsonAsync("/api/events", new
        {
            programId, eventTypeKey = "draw-results", seasonYear = 2027, startDate = "2027-07-01", actions = new[] { new { title = "Check results" } },
        });
        var actionId = created.Json.GetProperty("actions")[0].GetProperty("id").GetGuid();

        var response = await writer.PostJsonAsync($"/api/actions/{actionId}/status", new { status = "done" });

        response.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("notApplicable", response.Json.GetProperty("errors").GetProperty("status")[0].GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stale_version_is_a_conflict()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var created = await writer.PostJsonAsync("/api/jurisdictions", new { name = "Montana", code = "MT", country = "US", timeZoneId = "America/Denver" });

        var first = await writer.PutJsonAsync($"/api/jurisdictions/{created.Id}", new { name = "Montana", code = "MT", country = "US", timeZoneId = "America/Denver", notes = "edit 1", version = created.Version });
        var stale = await writer.PutJsonAsync($"/api/jurisdictions/{created.Id}", new { name = "Montana", code = "MT", country = "US", timeZoneId = "America/Denver", notes = "edit 2", version = created.Version });
        var missingVersion = await writer.PutJsonAsync($"/api/jurisdictions/{created.Id}", new { name = "Montana", code = "MT", country = "US", timeZoneId = "America/Denver" });

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.NotEqual(created.Version, first.Version);
        stale.AssertSafeProblem(HttpStatusCode.Conflict);
        missingVersion.AssertSafeProblem(HttpStatusCode.BadRequest);
        Assert.Contains("version", missingVersion.ErrorFields);
    }

    [Fact]
    public async Task Natural_key_is_unique_among_active_events_and_archiving_frees_it()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);
        var body = new { programId, eventTypeKey = "application-period", seasonYear = 2027, startDate = "2027-05-12", endDate = "2027-06-12" };

        var first = await writer.PostJsonAsync("/api/events", body);
        (await writer.PostJsonAsync("/api/events", body)).AssertSafeProblem(HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Created, (await writer.PostJsonAsync("/api/events", new { programId, eventTypeKey = "application-period", seasonYear = 2027, startDate = "2027-07-01", qualifier = "Leftover round" })).Status);

        Assert.Equal(HttpStatusCode.OK, (await writer.DeleteApiAsync($"/api/events/{first.Id}")).Status);
        var replacement = await writer.PostJsonAsync("/api/events", body);
        Assert.Equal(HttpStatusCode.Created, replacement.Status);

        (await writer.PostJsonAsync($"/api/events/{first.Id}/restore")).AssertSafeProblem(HttpStatusCode.Conflict);
        var archived = await writer.GetApiAsync($"/api/events/{first.Id}");
        Assert.True(archived.Json.GetProperty("isArchived").GetBoolean());
    }

    [Fact]
    public async Task Archiving_preserves_references_and_blocks_orphans()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (jurisdictionId, _, programId) = await Seed.ProgramAsync(writer);
        var programEvent = await writer.PostJsonAsync("/api/events", new { programId, eventTypeKey = "season", seasonYear = 2099, startDate = "2099-10-01", endDate = "2099-10-31" });

        (await writer.DeleteApiAsync($"/api/jurisdictions/{jurisdictionId}")).AssertSafeProblem(HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.OK, (await writer.DeleteApiAsync($"/api/programs/{programId}")).Status);

        var stillThere = await writer.GetApiAsync($"/api/events/{programEvent.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.Status);
        Assert.Empty((await writer.GetApiAsync("/api/events/upcoming?days=730")).Json.EnumerateArray());
        (await writer.PostJsonAsync("/api/events", new { programId, eventTypeKey = "season", seasonYear = 2100, startDate = "2100-10-01" }))
            .AssertSafeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Database_rejects_invalid_schedules_even_if_application_checks_are_bypassed()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(factory.ConnectionString, $"""
            INSERT INTO program_events (id, program_id, event_type_key, qualifier, season_year, name, start_date, end_date,
                time_zone_id, starts_at_utc, ends_at_utc, origin_kind, verification_status, created_at, updated_at)
            VALUES (gen_random_uuid(), '{programId}', 'season', '', 2027, 'bad', '2027-06-12', '2027-06-01',
                'America/Chicago', now(), now(), 'Manual', 'Unverified', now(), now())
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
        Assert.Equal("ck_program_events_end_after_start_date", ex.ConstraintName);
    }

    [Theory]
    [InlineData("UPDATE action_status_changes SET outcome = 'rewritten'")]
    [InlineData("DELETE FROM action_status_changes")]
    [InlineData("TRUNCATE action_status_changes")]
    public async Task Database_enforces_append_only_action_history(string sql)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();
        var (_, _, programId) = await Seed.ProgramAsync(writer);
        var created = await writer.PostJsonAsync("/api/events", new
        {
            programId, eventTypeKey = "draw-results", seasonYear = 2027, startDate = "2027-07-01", actions = new[] { new { title = "Check results" } },
        });
        await writer.PostJsonAsync($"/api/events/{created.Id}/complete", new { outcome = "Drawn" });

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(factory.ConnectionString, sql));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, ex.SqlState);
        Assert.Contains("append-only", ex.MessageText, StringComparison.Ordinal);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ApiCalls.Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ApiCalls.Ct);
    }
}
