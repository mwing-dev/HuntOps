using HuntOps.Infrastructure.Persistence;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace HuntOps.IntegrationTests.Persistence;

public sealed class MigrationTests(PostgresFixture postgres)
{
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
