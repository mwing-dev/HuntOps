using HuntOps.Infrastructure;
using HuntOps.Infrastructure.Logging;
using HuntOps.Infrastructure.Identity;
using HuntOps.Infrastructure.Persistence;
using Serilog.Events;

namespace HuntOps.Worker.Migrations;

/// <summary>
/// <c>HuntOps.Worker migrate</c>: applies pending EF Core migrations, bootstraps the owner account
/// (see <see cref="OwnerBootstrapper"/>) and exits. Exit code 1 means migration failed or the owner bootstrap
/// configuration is invalid.
/// Docker Compose runs this as the one-shot <c>huntops-migrate</c> service before web and worker start.
/// </summary>
internal static class MigrateCommand
{
    public const string Name = "migrate";

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args[1..]);
        // On a brand-new database EF probes the not-yet-created history table and logs that probe as an Error.
        // Real migration failures still surface as exceptions and are logged as Critical below.
        builder.AddHuntOpsLogging("huntops-migrate", logger =>
            logger.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Fatal));
        builder.Services.AddHuntOpsInfrastructure();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("HuntOps.Migrate");

        try
        {
            await DatabaseMigrator.MigrateAsync(host.Services, logger, TimeSpan.FromSeconds(60), CancellationToken.None);

            // Owner bootstrap runs here, before web/worker start, so a bad configuration is reported once and clearly.
            await using var scope = host.Services.CreateAsyncScope();
            var bootstrap = await scope.ServiceProvider.GetRequiredService<OwnerBootstrapper>().RunAsync(CancellationToken.None);
            return bootstrap.Outcome == OwnerBootstrapOutcome.InvalidConfiguration ? 1 : 0;
        }
#pragma warning disable CA1031 // Top-level command: report any failure as a non-zero exit code.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogCritical(ex, "Database migration failed");
            return 1;
        }
    }
}
