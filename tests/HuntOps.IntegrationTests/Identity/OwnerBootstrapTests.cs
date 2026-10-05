using HuntOps.Application.Access;
using HuntOps.Application.Events;
using HuntOps.Application.Reference;
using HuntOps.Domain.Users;
using HuntOps.Infrastructure.Identity;
using HuntOps.Infrastructure.Persistence;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HuntOps.IntegrationTests.Identity;

/// <summary>Owner bootstrap and the Phase 2 → Phase 3 placeholder-owner migration.</summary>
public sealed class OwnerBootstrapTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creates_the_owner_once_and_never_duplicates_it()
    {
        var password = TestSecrets.NewOwnerPassword();
        await using var services = Build(await postgres.CreateMigratedDatabaseAsync(), "owner@huntops.test", password, out var logs);

        var first = await BootstrapAsync(services);
        var second = await BootstrapAsync(services);

        Assert.Equal(OwnerBootstrapOutcome.Created, first.Outcome);
        Assert.Equal(OwnerBootstrapOutcome.AlreadyExists, second.Outcome);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>();
        var owner = await db.Users.SingleAsync(Ct);
        Assert.True(owner.IsOwner);
        Assert.True(owner.EmailConfirmed);
        Assert.Equal(OwnerSettings.DefaultTimeZoneId, (await db.OwnerSettings.SingleAsync(Ct)).TimeZoneId);
        Assert.DoesNotContain(logs, line => line.Contains(password, StringComparison.Ordinal));
        Assert.Contains(logs, line => line.Contains("Created owner account", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_bootstrap_variables_no_account_is_created()
    {
        await using var services = Build(await postgres.CreateMigratedDatabaseAsync(), null, null, out var logs);

        var result = await BootstrapAsync(services);

        Assert.Equal(OwnerBootstrapOutcome.NotConfigured, result.Outcome);
        Assert.Contains(OwnerBootstrapper.EmailVariable, result.Message, StringComparison.Ordinal);
        await using var scope = services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>().Users.AnyAsync(Ct));
    }

    [Theory]
    [InlineData("owner@huntops.test", "too-short")]
    [InlineData("owner@huntops.test", "lowercase-only")]
    [InlineData("not-an-email", "policy-compliant")]
    public async Task Invalid_bootstrap_configuration_is_refused_without_echoing_the_password(string email, string passwordKind)
    {
        // Generated at runtime so no password-like literals are committed.
        var password = passwordKind switch
        {
            "too-short" => TestSecrets.NewOwnerPassword()[..6],
            "lowercase-only" => TestSecrets.NewPassword().ToLowerInvariant().Replace('0', 'a'),
            _ => TestSecrets.NewOwnerPassword(),
        };
        await using var services = Build(await postgres.CreateMigratedDatabaseAsync(), email, password, out var logs);

        var result = await BootstrapAsync(services);

        Assert.Equal(OwnerBootstrapOutcome.InvalidConfiguration, result.Outcome);
        Assert.DoesNotContain(password, result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(logs, line => line.Contains(password, StringComparison.Ordinal));
        await using var scope = services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>().Users.AnyAsync(Ct));
    }

    [Fact]
    public async Task Existing_owner_password_is_never_overwritten_by_bootstrap_variables()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using (var first = Build(connectionString, "owner@huntops.test", TestSecrets.NewOwnerPassword(), out _))
        {
            await BootstrapAsync(first);
        }

        string hashBefore;
        await using (var scope = Build(connectionString, null, null, out _).CreateAsyncScope())
        {
            hashBefore = (await scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>().Users.SingleAsync(Ct)).PasswordHash!;
        }

        await using var second = Build(connectionString, "someone-else@huntops.test", TestSecrets.NewOwnerPassword(), out _);
        var result = await BootstrapAsync(second);

        await using var verify = second.CreateAsyncScope();
        var users = await verify.ServiceProvider.GetRequiredService<HuntOpsDbContext>().Users.ToListAsync(Ct);
        Assert.Equal(OwnerBootstrapOutcome.AlreadyExists, result.Outcome);
        Assert.Equal("owner@huntops.test", Assert.Single(users).Email);
        Assert.Equal(hashBefore, users[0].PasswordHash);
    }

    [Fact]
    public async Task Placeholder_owned_api_keys_and_history_are_adopted_without_losing_anything()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        // Phase 2 state: everything belongs to the placeholder user "owner".
        string apiKeySecret;
        Guid actionId;
        await using (var phase2 = Build(connectionString, null, null, out _))
        await using (var scope = phase2.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            apiKeySecret = (await sp.GetRequiredService<ApiKeyService>().CreateAsync(new ApiKeyCreateInput("phase2-key", "write"), Ct)).Secret;
            var jurisdiction = await sp.GetRequiredService<JurisdictionService>().CreateAsync(new JurisdictionInput("Wyoming", "WY", "US", "America/Denver"), Ct);
            var agency = await sp.GetRequiredService<AgencyService>().CreateAsync(new AgencyInput(jurisdiction.Id, "Wyoming Game and Fish"), Ct);
            var program = await sp.GetRequiredService<ProgramService>().CreateAsync(new ProgramInput(agency.Id, "Nonresident Elk"), Ct);
            var programEvent = await sp.GetRequiredService<ProgramEventService>().CreateAsync(
                new EventInput(program.Id, "application-period", 2027, "2027-01-02", EndDate: "2027-01-31", Actions: [new ActionInput("Apply for elk")]), Ct);
            actionId = programEvent.Actions[0].Id;
            var actions = sp.GetRequiredService<ActionService>();
            await actions.ChangeStatusAsync(actionId, new StatusChangeInput("completed", "Applied"), Ct);
            await actions.ChangeStatusAsync(actionId, new StatusChangeInput("reopened", Note: "Wrong unit"), Ct);
        }

        var before = await HistorySnapshotAsync(connectionString);
        Assert.All(before, row => Assert.StartsWith("owner|", row, StringComparison.Ordinal));

        await using var phase3 = Build(connectionString, "owner@huntops.test", TestSecrets.NewOwnerPassword(), out _);
        var result = await BootstrapAsync(phase3);
        var rerun = await BootstrapAsync(phase3);

        Assert.Equal(OwnerBootstrapOutcome.Created, result.Outcome);
        Assert.Equal(2, result.ReassignedStatusChanges);
        Assert.Equal(1, result.ReassignedApiKeys);
        Assert.Equal(0, rerun.ReassignedStatusChanges + rerun.ReassignedApiKeys);

        await using var scope3 = phase3.CreateAsyncScope();
        var db = scope3.ServiceProvider.GetRequiredService<HuntOpsDbContext>();
        var ownerId = (await db.Users.SingleAsync(Ct)).Id;

        // History: same rows, same order, same content; only the owning user id changed.
        var after = await HistorySnapshotAsync(connectionString);
        Assert.Equal(before.Select(r => r[(r.IndexOf('|', StringComparison.Ordinal) + 1)..]), after.Select(r => r[(r.IndexOf('|', StringComparison.Ordinal) + 1)..]));
        Assert.All(after, row => Assert.StartsWith(ownerId + "|", row, StringComparison.Ordinal));

        // The Phase 2 API key still works and now acts for the real owner.
        var identity = await scope3.ServiceProvider.GetRequiredService<ApiKeyService>().AuthenticateAsync(apiKeySecret, Ct);
        Assert.NotNull(identity);
        Assert.Equal(ownerId, identity.UserId);

        // The append-only guard still rejects any other change, including re-assigning a real owner's row.
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, "UPDATE action_status_changes SET outcome = 'rewritten'"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, "UPDATE action_status_changes SET user_id = 'someone-else'"));
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, "DELETE FROM action_status_changes"));
    }

    private static ServiceProvider Build(string connectionString, string? email, string? password, out List<string> logs)
    {
        var captured = new List<string>();
        logs = captured;
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:HuntOps"] = connectionString };
        if (email is not null)
        {
            settings[OwnerBootstrapper.EmailVariable] = email;
        }

        if (password is not null)
        {
            settings[OwnerBootstrapper.PasswordVariable] = password;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(captured)).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<IConfiguration>(configuration);
        HuntOps.Infrastructure.DependencyInjection.AddHuntOpsInfrastructure(services);
        return services.BuildServiceProvider();
    }

    private static async Task<OwnerBootstrapResult> BootstrapAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OwnerBootstrapper>().RunAsync(Ct);
    }

    private static async Task<List<string>> HistorySnapshotAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(
            "SELECT user_id || '|' || id || '|' || sequence || '|' || status || '|' || coalesce(outcome,'') || '|' || coalesce(note,'') || '|' || changed_at || '|' || changed_via || '|' || changed_by FROM action_status_changes ORDER BY sequence",
            connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }
}

/// <summary>Collects formatted log messages (and exception text) so tests can assert secrets never appear.</summary>
internal sealed class CapturingLoggerProvider(List<string> lines) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(List<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (lines)
            {
                lines.Add(formatter(state, exception) + " " + exception);
            }
        }
    }
}
