using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HuntOps.Application.Access;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Testing;

namespace HuntOps.IntegrationTests.Infrastructure;

/// <summary>The real web host (API, auth, error handling) against a test database, optionally with a fake clock.</summary>
internal sealed class ApiFactory(string connectionString, FakeClock? clock = null, string environment = "Development")
    : WebApplicationFactory<Program>
{
    public static async Task<ApiFactory> CreateAsync(PostgresFixture postgres, FakeClock? clock = null, string environment = "Development") =>
        new(await postgres.CreateMigratedDatabaseAsync(), clock, environment);

    public string ConnectionString => connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:HuntOps", connectionString);
        builder.UseSetting("LOG_LEVEL", "Warning");
        if (clock is not null)
        {
            builder.ConfigureTestServices(services => services.AddSingleton<IClock>(clock));
        }
    }

    /// <summary>Creates a key through the real service and returns its one-time plaintext.</summary>
    public async Task<string> CreateKeyAsync(ApiKeyScopeChoice scope, int? expiresInDays = null, string name = "test")
    {
        await using var scopeServices = Services.CreateAsyncScope();
        var service = scopeServices.ServiceProvider.GetRequiredService<ApiKeyService>();
        var created = await service.CreateAsync(new ApiKeyCreateInput(name, scope.ToString(), expiresInDays), CancellationToken.None);
        return created.Secret;
    }

    public HttpClient CreateClient(string apiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    public async Task<HttpClient> CreateWriterAsync() => CreateClient(await CreateKeyAsync(ApiKeyScopeChoice.Write));
}

internal static class ApiCalls
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<ApiResponse> GetApiAsync(this HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        return await ApiResponse.ReadAsync(response);
    }

    public static async Task<ApiResponse> PostJsonAsync(this HttpClient client, string path, object? body = null)
    {
        using var response = await client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);
        return await ApiResponse.ReadAsync(response);
    }

    public static async Task<ApiResponse> PutJsonAsync(this HttpClient client, string path, object body)
    {
        using var response = await client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body, Ct);
        return await ApiResponse.ReadAsync(response);
    }

    public static async Task<ApiResponse> PostRawAsync(this HttpClient client, string path, string rawJson)
    {
        using var content = new StringContent(rawJson, System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(new Uri(path, UriKind.Relative), content, Ct);
        return await ApiResponse.ReadAsync(response);
    }

    public static async Task<ApiResponse> DeleteApiAsync(this HttpClient client, string path)
    {
        using var response = await client.DeleteAsync(new Uri(path, UriKind.Relative), Ct);
        return await ApiResponse.ReadAsync(response);
    }
}

internal sealed record ApiResponse(HttpStatusCode Status, string Text, JsonElement Json, HttpResponseHeaders Headers, string? ContentType)
{
    public static async Task<ApiResponse> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(ApiCalls.Ct);
        var json = text.Length > 0 && (text[0] is '{' or '[') ? JsonDocument.Parse(text).RootElement.Clone() : default;
        return new ApiResponse(response.StatusCode, text, json, response.Headers, response.Content.Headers.ContentType?.MediaType);
    }

    public string Str(string property) => Json.GetProperty(property).GetString()!;

    public Guid Id => Json.GetProperty("id").GetGuid();

    public uint Version => Json.GetProperty("version").GetUInt32();

    public string[] ErrorFields => Json.TryGetProperty("errors", out var errors)
        ? errors.EnumerateObject().Select(p => p.Name).ToArray()
        : [];

    /// <summary>Error bodies are problem details and never contain stack traces or exception internals.</summary>
    public void AssertSafeProblem(HttpStatusCode expected)
    {
        Assert.Equal(expected, Status);
        Assert.Equal("application/problem+json", ContentType);
        Assert.DoesNotContain("   at ", Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", Text, StringComparison.Ordinal);
        Assert.DoesNotContain("HuntOps.", Text, StringComparison.Ordinal);
    }
}

/// <summary>Builds a small, generic reference-data graph through the API.</summary>
internal static class Seed
{
    public static async Task<(Guid JurisdictionId, Guid AgencyId, Guid ProgramId)> ProgramAsync(
        HttpClient writer,
        string code = "KS",
        string state = "Kansas",
        string timeZone = "America/Chicago",
        string programName = "Resident Antelope",
        string species = "Antelope")
    {
        var jurisdiction = await writer.PostJsonAsync("/api/jurisdictions", new { name = state, code, country = "US", timeZoneId = timeZone });
        Assert.Equal(HttpStatusCode.Created, jurisdiction.Status);
        var agency = await writer.PostJsonAsync("/api/agencies", new { jurisdictionId = jurisdiction.Id, name = $"{state} Wildlife Agency", websiteUrl = "https://wildlife.example.gov" });
        Assert.Equal(HttpStatusCode.Created, agency.Status);
        var program = await writer.PostJsonAsync("/api/programs", new { agencyId = agency.Id, name = programName, species, residency = "Resident" });
        Assert.Equal(HttpStatusCode.Created, program.Status);
        return (jurisdiction.Id, agency.Id, program.Id);
    }
}
