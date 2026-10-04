extern alias worker;

using System.Net;
using HuntOps.Application.Access;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using worker::HuntOps.Worker.Commands;

namespace HuntOps.IntegrationTests.Api;

public sealed class OpenApiAndCliTests(PostgresFixture postgres)
{
    [Fact]
    public async Task OpenApi_document_describes_the_api_and_its_bearer_auth()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateClient();

        var document = await anonymous.GetApiAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, document.Status);
        var paths = document.Json.GetProperty("paths");
        foreach (var path in new[] { "/api/events", "/api/events/upcoming", "/api/events/{id}/complete", "/api/action-items", "/api/programs/{id}/history", "/api/event-types" })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"OpenAPI document is missing {path}");
        }

        Assert.False(paths.TryGetProperty("/health", out _));
        var scheme = document.Json.GetProperty("components").GetProperty("securitySchemes").GetProperty("ApiKey");
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Contains("\"enum\"", document.Text, StringComparison.Ordinal); // enums documented as strings
    }

    [Fact]
    public async Task Swagger_ui_is_served()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        using var anonymous = factory.CreateClient();

        using var response = await anonymous.GetAsync(new Uri("/swagger/index.html", UriKind.Relative), ApiCalls.Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("swagger-ui", await response.Content.ReadAsStringAsync(ApiCalls.Ct), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cli_creates_lists_and_revokes_keys_and_prints_the_secret_once()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var config = new Dictionary<string, string?> { ["ConnectionStrings:HuntOps"] = factory.ConnectionString, ["LOG_LEVEL"] = "Warning" };

        var createOut = new StringWriter();
        var createCode = await ApiKeyCommand.RunAsync(["apikey", "create", "--name", "claude-code", "--scope", "write"], createOut, new StringWriter(), config);
        var secret = createOut.ToString().Split('\n').Select(l => l.Trim()).Single(l => l.StartsWith("hops_", StringComparison.Ordinal) && l.Length == ApiKeyToken.TokenLength);

        var listOut = new StringWriter();
        await ApiKeyCommand.RunAsync(["apikey", "list"], listOut, new StringWriter(), config);

        Assert.Equal(0, createCode);
        Assert.Contains("claude-code", listOut.ToString(), StringComparison.Ordinal);
        Assert.Contains(secret[..13], listOut.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, listOut.ToString(), StringComparison.Ordinal);

        using var client = factory.CreateClient(secret);
        Assert.Equal(HttpStatusCode.Created, (await client.PostJsonAsync("/api/jurisdictions", new { name = "Idaho", code = "ID", country = "US", timeZoneId = "America/Boise" })).Status);

        Assert.Equal(0, await ApiKeyCommand.RunAsync(["apikey", "revoke", secret[..13]], new StringWriter(), new StringWriter(), config));
        (await client.GetApiAsync("/api/jurisdictions")).AssertSafeProblem(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Cli_rejects_invalid_input_with_a_message()
    {
        await using var factory = await ApiFactory.CreateAsync(postgres);
        var config = new Dictionary<string, string?> { ["ConnectionStrings:HuntOps"] = factory.ConnectionString, ["LOG_LEVEL"] = "Warning" };
        var error = new StringWriter();

        var code = await ApiKeyCommand.RunAsync(["apikey", "create", "--name", "x", "--scope", "admin"], new StringWriter(), error, config);

        Assert.Equal(2, code);
        Assert.Contains("scope", error.ToString(), StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApiKeyService>().ListAsync(ApiCalls.Ct));
    }
}
