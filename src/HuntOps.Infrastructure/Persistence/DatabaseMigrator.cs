using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HuntOps.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF Core migrations. Non-destructive and idempotent: it never drops or recreates the database.
/// </summary>
public static partial class DatabaseMigrator
{
    public static async Task MigrateAsync(
        IServiceProvider services,
        ILogger logger,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>();

        await WaitForDatabaseAsync(db, logger, connectTimeout, cancellationToken);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            LogUpToDate(logger);
            return;
        }

        LogApplying(logger, pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
        LogApplied(logger);
    }

    private static async Task WaitForDatabaseAsync(
        HuntOpsDbContext db,
        ILogger logger,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var delay = TimeSpan.FromSeconds(1);

        while (!await db.Database.CanConnectAsync(cancellationToken))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException($"Database was not reachable within {timeout.TotalSeconds:0} seconds.");
            }

            LogWaiting(logger, delay.TotalSeconds);
            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 5));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date; no migrations to apply")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s): {Migrations}")]
    private static partial void LogApplying(ILogger logger, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrations applied successfully")]
    private static partial void LogApplied(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database not reachable yet; retrying in {DelaySeconds}s")]
    private static partial void LogWaiting(ILogger logger, double delaySeconds);
}
