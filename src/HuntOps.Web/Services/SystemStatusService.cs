using System.Diagnostics;
using System.Reflection;
using HuntOps.Application.Operations;
using HuntOps.Infrastructure.Health;
using HuntOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NodaTime;

namespace HuntOps.Web.Services;

public sealed record HealthItem(string Name, bool Healthy, string Detail);

public sealed record WorkerStatus(string WorkerId, string Component, Instant LastBeatAt, Instant StartedAt, string? Version, bool Healthy);

public sealed record SystemStatus(
    bool DatabaseReachable,
    bool MigrationsApplied,
    string DatabaseDetail,
    string MigrationDetail,
    IReadOnlyList<WorkerStatus> Workers,
    string Version,
    string? Commit,
    string Environment,
    Duration Uptime,
    bool ApiEnabled,
    bool SwaggerEnabled,
    HealthItem Ntfy,
    IReadOnlyList<string> Warnings);

/// <summary>Operational status for the System page and dashboard warnings. Never exposes secrets.</summary>
public sealed class SystemStatusService(
    HealthCheckService healthChecks,
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IConfiguration configuration,
    IHostEnvironment environment,
    IClock clock)
{
    /// <summary>A worker is stale after missing this many 30-second heartbeats.</summary>
    public static readonly Duration WorkerStaleAfter = Duration.FromMinutes(2);

    private static readonly Instant ProcessStarted = Instant.FromDateTimeUtc(Process.GetCurrentProcess().StartTime.ToUniversalTime());

    public async Task<SystemStatus> GetAsync(bool includeNtfy, CancellationToken cancellationToken)
    {
        var report = await healthChecks.CheckHealthAsync(r => r.Tags.Contains(HealthEndpoints.ReadyTag), cancellationToken);
        var database = report.Entries.GetValueOrDefault("database");
        var migrations = report.Entries.GetValueOrDefault("migrations");
        var databaseOk = database.Status == HealthStatus.Healthy;
        var migrationsOk = migrations.Status == HealthStatus.Healthy;

        var workers = databaseOk ? await WorkersAsync(cancellationToken) : [];
        var ntfy = includeNtfy ? await NtfyAsync(cancellationToken) : new HealthItem("ntfy", true, "Not checked");

        var warnings = new List<string>();
        if (!databaseOk)
        {
            warnings.Add("The database is not reachable.");
        }
        else if (!migrationsOk)
        {
            warnings.Add($"Database migrations are pending: {migrations.Description}");
        }

        if (databaseOk && workers.Count == 0)
        {
            warnings.Add("No background worker has reported a heartbeat. Reminders and source checks will not run.");
        }

        warnings.AddRange(workers.Where(w => !w.Healthy).Select(w =>
            $"Worker '{w.WorkerId}' heartbeat is stale (last seen {Ago(clock.GetCurrentInstant() - w.LastBeatAt)} ago)."));

        var version = typeof(SystemStatusService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return new SystemStatus(
            databaseOk,
            migrationsOk,
            databaseOk ? "Reachable" : "Not reachable",
            migrations.Description ?? (migrationsOk ? "All migrations applied." : "Unknown"),
            workers,
            version.Split('+')[0],
            configuration["HUNTOPS_GIT_COMMIT"] is { Length: > 0 } commit ? commit : null,
            environment.EnvironmentName,
            clock.GetCurrentInstant() - ProcessStarted,
            ApiEnabled: true,
            SwaggerEnabled: configuration.GetValue("HUNTOPS_SWAGGER_ENABLED", true),
            ntfy,
            warnings);
    }

    public static string Ago(Duration duration) => duration.TotalMinutes switch
    {
        < 1 => $"{Math.Max(0, (int)duration.TotalSeconds)}s",
        < 60 => $"{(int)duration.TotalMinutes}m",
        < 60 * 48 => $"{(int)duration.TotalHours}h",
        _ => $"{(int)duration.TotalDays}d",
    };

    private async Task<IReadOnlyList<WorkerStatus>> WorkersAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>();
        var now = clock.GetCurrentInstant();
        var beats = await db.WorkerHeartbeats.AsNoTracking().OrderBy(h => h.WorkerId).ToListAsync(cancellationToken);
        return beats.ConvertAll(h => new WorkerStatus(
            h.WorkerId, h.Component, h.LastBeatAt, h.StartedAt, h.Version,
            HeartbeatFreshness.IsHealthy(HeartbeatFreshness.Evaluate(h.LastBeatAt, h.StartedAt, now, WorkerStaleAfter))));
    }

    /// <summary>Connectivity only (GET /v1/health on the internal ntfy URL). No notification logic.</summary>
    private async Task<HealthItem> NtfyAsync(CancellationToken cancellationToken)
    {
        var baseUrl = configuration["NTFY_BASE_URL"];
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return new HealthItem("ntfy", false, "NTFY_BASE_URL is not configured.");
        }

        try
        {
            using var client = httpClients.CreateClient("ntfy-health");
            using var response = await client.GetAsync(new Uri(baseUri, "/v1/health"), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var healthy = response.IsSuccessStatusCode && body.Contains("\"healthy\":true", StringComparison.Ordinal);
            return new HealthItem("ntfy", healthy, healthy ? $"Reachable at {baseUri.GetLeftPart(UriPartial.Authority)}" : $"Unhealthy response ({(int)response.StatusCode})");
        }
        catch (HttpRequestException)
        {
            return new HealthItem("ntfy", false, $"Not reachable at {baseUri.GetLeftPart(UriPartial.Authority)}");
        }
        catch (TaskCanceledException)
        {
            return new HealthItem("ntfy", false, "Timed out");
        }
    }
}
