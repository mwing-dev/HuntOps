using HuntOps.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(HuntOps.IntegrationTests.Infrastructure.PostgresFixture))]

namespace HuntOps.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL container per test run. Each test gets its own freshly created database,
/// so tests are isolated without paying for a container per test.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates an empty database (no migrations applied) and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>Creates a database with all migrations applied.</summary>
    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await CreateEmptyDatabaseAsync();
        await using var services = TestServices.Build(connectionString);
        await DatabaseMigrator.MigrateAsync(services, NullLogger.Instance, TimeSpan.FromSeconds(10), CancellationToken.None);
        return connectionString;
    }
}
