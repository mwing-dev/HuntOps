using HuntOps.Infrastructure.Persistence;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HuntOps.IntegrationTests.Persistence;

public sealed class MigrationTests(PostgresFixture postgres)
{
    private const string Phase1Migration = "20261004210034_InitialCreate";

    [Fact]
    public async Task Migrator_creates_schema_on_empty_database()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var services = TestServices.Build(connectionString);

        await DatabaseMigrator.MigrateAsync(services, NullLogger.Instance, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var tables = await ListTablesAsync(connectionString);
        Assert.Contains("worker_heartbeats", tables);
        Assert.Contains("data_protection_keys", tables);
        Assert.Contains(HuntOpsDbContextOptions.MigrationsHistoryTable, tables);
        Assert.Superset(
            new HashSet<string>(["jurisdictions", "agencies", "programs", "event_types", "program_events", "required_actions", "action_status_changes", "api_keys"]),
            new HashSet<string>(tables));
    }

    [Fact]
    public async Task Upgrading_a_phase_1_database_preserves_its_data_and_seeds_event_types()
    {
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        await using var services = TestServices.Build(connectionString);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>();
            await db.GetService<IMigrator>().MigrateAsync(Phase1Migration, cancellationToken: TestContext.Current.CancellationToken);
        }

        await ExecuteAsync(connectionString,
            "INSERT INTO worker_heartbeats (worker_id, component, started_at, last_beat_at) VALUES ('worker', 'worker', now(), now());" +
            "INSERT INTO data_protection_keys (friendly_name, xml) VALUES ('key-1', '<key/>');");

        await DatabaseMigrator.MigrateAsync(services, NullLogger.Instance, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await using var verify = services.CreateAsyncScope();
        var migrated = verify.ServiceProvider.GetRequiredService<HuntOpsDbContext>();
        Assert.Empty(await migrated.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await migrated.WorkerHeartbeats.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await migrated.DataProtectionKeys.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(10, await migrated.EventTypes.CountAsync(t => t.IsSystem, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Migrator_is_idempotent_and_preserves_existing_data()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var services = TestServices.Build(connectionString);
        await ExecuteAsync(connectionString,
            "INSERT INTO worker_heartbeats (worker_id, component, started_at, last_beat_at) VALUES ('w', 'c', now(), now())");

        // Simulates the one-shot migrate container running again on every `docker compose up`.
        await DatabaseMigrator.MigrateAsync(services, NullLogger.Instance, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await DatabaseMigrator.MigrateAsync(services, NullLogger.Instance, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.WorkerHeartbeats.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Migrator_fails_clearly_when_database_is_unreachable()
    {
        await using var services = TestServices.Build(TestSecrets.UnreachableDatabase(out _));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseMigrator.MigrateAsync(services, NullLogger.Instance, TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        Assert.Contains("not reachable", ex.Message, StringComparison.Ordinal);
    }

    private static async Task<List<string>> ListTablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var tables = new List<string>();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
