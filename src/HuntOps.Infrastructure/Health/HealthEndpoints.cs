using System.Text.Json;
using HuntOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HuntOps.Infrastructure.Health;

public static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Readiness checks shared by every HuntOps host: database reachable and migrations applied.</summary>
    public static IHealthChecksBuilder AddHuntOpsReadinessChecks(this IHealthChecksBuilder builder) =>
        builder
            .AddDbContextCheck<HuntOpsDbContext>("database", tags: [ReadyTag])
            .AddCheck<DatabaseMigrationsHealthCheck>("migrations", tags: [ReadyTag]);

    /// <summary>
    /// <c>/health</c>: liveness (the process is serving requests; runs no checks).
    /// <c>/health/ready</c>: readiness (every check tagged "ready").
    /// </summary>
    public static IEndpointRouteBuilder MapHuntOpsHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync,
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponseAsync,
        });

        return endpoints;
    }

    // Exception details are deliberately omitted: health output is unauthenticated.
    private static Task WriteResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
            }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
