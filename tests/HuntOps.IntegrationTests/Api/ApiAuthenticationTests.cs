using System.Net;
using HuntOps.Application.Access;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Testing;
using Npgsql;

namespace HuntOps.IntegrationTests.Api;

public sealed class ApiAuthenticationTests(PostgresFixture postgres)
{
    private static readonly object NewJurisdiction = new { name = "Wyoming", code = "WY", country = "US", timeZoneId = "America/Denver" };

    [Theory]
    [InlineData("GET", "/api/jurisdictions")]
    [InlineData("GET", "/api/events/upcoming")]
    [InlineData("GET", "/api/action-items")]
    [InlineData("POST", "/api/jurisdictions")]
    [InlineData("POST", "/api/events")]
    public async Task Every_api_endpoint_requires_a_key(string method, string path)
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateClient();

        var response = method == "GET" ? await anonymous.GetApiAsync(path) : await anonymous.PostJsonAsync(path, NewJurisdiction);

        response.AssertSafeProblem(HttpStatusCode.Unauthorized);
        Assert.StartsWith("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_or_malformed_keys_are_rejected()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var genuine = await factory.CreateKeyAsync(ApiKeyScopeChoice.Write);
        var forged = genuine[..^4] + "AAAA";

        foreach (var key in new[] { "not-a-key", forged, ApiKeyToken.Generate().Plaintext })
        {
            using var client = factory.CreateClient(key);
            (await client.GetApiAsync("/api/jurisdictions")).AssertSafeProblem(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Read_key_can_read_but_not_write()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var reader = factory.CreateClient(await factory.CreateKeyAsync(ApiKeyScopeChoice.Read));

        Assert.Equal(HttpStatusCode.OK, (await reader.GetApiAsync("/api/jurisdictions")).Status);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetApiAsync("/api/event-types")).Status);
        (await reader.PostJsonAsync("/api/jurisdictions", NewJurisdiction)).AssertSafeProblem(HttpStatusCode.Forbidden);
        (await reader.DeleteApiAsync($"/api/jurisdictions/{Guid.NewGuid()}")).AssertSafeProblem(HttpStatusCode.Forbidden);
        (await reader.PostJsonAsync($"/api/actions/{Guid.NewGuid()}/status", new { status = "completed" })).AssertSafeProblem(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Write_key_can_read_and_write()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var writer = await factory.CreateWriterAsync();

        Assert.Equal(HttpStatusCode.Created, (await writer.PostJsonAsync("/api/jurisdictions", NewJurisdiction)).Status);
        Assert.Equal(HttpStatusCode.OK, (await writer.GetApiAsync("/api/jurisdictions")).Status);
    }

    [Fact]
    public async Task X_Api_Key_header_is_accepted_and_me_describes_the_key_without_the_secret()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var key = await factory.CreateKeyAsync(ApiKeyScopeChoice.Read, name: "claude-desktop");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);

        var me = await client.GetApiAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, me.Status);
        Assert.Equal(key[..13], me.Str("keyPrefix"));
        Assert.Equal("claude-desktop", me.Str("keyName"));
        Assert.Equal(["read"], me.Json.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).ToArray());
        Assert.DoesNotContain(key, me.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoked_and_expired_keys_stop_working()
    {
        var clock = new FakeClock(Instant.FromUtc(2027, 1, 1, 0, 0));
        await using var factory = await ApiFactory.CreateAsync(postgres, clock);
        var revokedKey = await factory.CreateKeyAsync(ApiKeyScopeChoice.Write);
        var expiringKey = await factory.CreateKeyAsync(ApiKeyScopeChoice.Write, expiresInDays: 30);
        using var revoked = factory.CreateClient(revokedKey);
        using var expiring = factory.CreateClient(expiringKey);
        Assert.Equal(HttpStatusCode.OK, (await revoked.GetApiAsync("/api/jurisdictions")).Status);
        Assert.Equal(HttpStatusCode.OK, (await expiring.GetApiAsync("/api/jurisdictions")).Status);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ApiKeyService>().RevokeAsync(revokedKey[..13], CancellationToken.None);
        }

        clock.Advance(Duration.FromDays(31));

        (await revoked.GetApiAsync("/api/jurisdictions")).AssertSafeProblem(HttpStatusCode.Unauthorized);
        (await expiring.GetApiAsync("/api/jurisdictions")).AssertSafeProblem(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Database_never_contains_the_plaintext_key()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var key = await factory.CreateKeyAsync(ApiKeyScopeChoice.Write);
        var secretPart = key[14..];

        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(ApiCalls.Ct);
        await using var command = new NpgsqlCommand("SELECT row_to_json(k)::text FROM api_keys k", connection);
        var row = (string)(await command.ExecuteScalarAsync(ApiCalls.Ct))!;

        Assert.DoesNotContain(secretPart, row, StringComparison.Ordinal);
        Assert.DoesNotContain(key, row, StringComparison.Ordinal);
        Assert.Contains(key[..13], row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_api_route_returns_problem_json_not_the_dashboard()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var reader = factory.CreateClient(await factory.CreateKeyAsync(ApiKeyScopeChoice.Read));

        (await reader.GetApiAsync("/api/does-not-exist")).AssertSafeProblem(HttpStatusCode.NotFound);
    }
}
