using System.Net;
using System.Text.Json;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HuntOps.IntegrationTests.Web;

public sealed class HealthEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ready_when_database_is_migrated()
    {
        await using var factory = new WebFactory(await postgres.CreateMigratedDatabaseAsync());
        using var client = factory.CreateClient();

        var (status, body) = await GetAsync(client, "/health/ready");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", CheckStatus(body, "database"));
        Assert.Equal("Healthy", CheckStatus(body, "migrations"));
    }

    [Fact]
    public async Task Not_ready_while_migrations_are_pending_but_still_live()
    {
        await using var factory = new WebFactory(await postgres.CreateEmptyDatabaseAsync());
        using var client = factory.CreateClient();

        var (readyStatus, readyBody) = await GetAsync(client, "/health/ready");
        var (liveStatus, _) = await GetAsync(client, "/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyStatus);
        Assert.Equal("Healthy", CheckStatus(readyBody, "database"));
        Assert.Equal("Unhealthy", CheckStatus(readyBody, "migrations"));
        Assert.Contains("InitialCreate", CheckDescription(readyBody, "migrations"), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, liveStatus);
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_database_and_readiness_reports_outage_without_details()
    {
        await using var factory = new WebFactory(TestSecrets.UnreachableDatabase(out var password));
        using var client = factory.CreateClient();

        var (liveStatus, _) = await GetAsync(client, "/health");
        var readyResponse = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var readyText = await readyResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, liveStatus);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.DoesNotContain(password, readyText, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", readyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Home_page_renders()
    {
        await using var factory = new WebFactory(await postgres.CreateMigratedDatabaseAsync());
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Contains("HuntOps", html, StringComparison.Ordinal);
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, JsonDocument.Parse(json).RootElement.Clone());
    }

    private static string? CheckStatus(JsonElement body, string name) =>
        FindCheck(body, name).GetProperty("status").GetString();

    private static string CheckDescription(JsonElement body, string name) =>
        FindCheck(body, name).GetProperty("description").GetString() ?? "";

    private static JsonElement FindCheck(JsonElement body, string name) =>
        body.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    private sealed class WebFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:HuntOps", connectionString);
            builder.UseSetting("LOG_LEVEL", "Warning");
        }
    }
}
