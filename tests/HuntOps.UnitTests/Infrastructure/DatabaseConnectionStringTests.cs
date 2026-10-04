using HuntOps.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HuntOps.UnitTests.Infrastructure;

public sealed class DatabaseConnectionStringTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Explicit_connection_string_takes_precedence_over_postgres_variables()
    {
        var config = Config(
            ("ConnectionStrings:HuntOps", "Host=db.internal;Database=explicit;Username=u;Password=p"),
            ("POSTGRES_PASSWORD", "ignored"),
            ("POSTGRES_HOST", "ignored-host"));

        Assert.Equal("Host=db.internal;Database=explicit;Username=u;Password=p", DatabaseConnectionString.Resolve(config));
    }

    [Fact]
    public void Builds_from_postgres_variables_used_by_the_compose_stack()
    {
        var config = Config(
            ("POSTGRES_HOST", "huntops-postgres"),
            ("POSTGRES_PORT", "5433"),
            ("POSTGRES_DB", "huntops_prod"),
            ("POSTGRES_USER", "hunter"),
            ("POSTGRES_PASSWORD", "s3cret"));

        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConnectionString.Resolve(config));

        Assert.Equal("huntops-postgres", parsed.Host);
        Assert.Equal(5433, parsed.Port);
        Assert.Equal("huntops_prod", parsed.Database);
        Assert.Equal("hunter", parsed.Username);
        Assert.Equal("s3cret", parsed.Password);
        Assert.Equal(GssEncryptionMode.Disable, parsed.GssEncryptionMode);
    }

    [Fact]
    public void Uses_defaults_when_only_password_is_set()
    {
        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConnectionString.Resolve(Config(("POSTGRES_PASSWORD", "pw"))));

        Assert.Equal("localhost", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("huntops", parsed.Database);
        Assert.Equal("huntops", parsed.Username);
    }

    [Fact]
    public void Blank_optional_variables_fall_back_to_defaults()
    {
        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConnectionString.Resolve(
            Config(("POSTGRES_PASSWORD", "pw"), ("POSTGRES_HOST", "  "), ("POSTGRES_DB", ""), ("POSTGRES_PORT", ""))));

        Assert.Equal("localhost", parsed.Host);
        Assert.Equal("huntops", parsed.Database);
        Assert.Equal(5432, parsed.Port);
    }

    [Fact]
    public void Password_with_connection_string_metacharacters_round_trips()
    {
        const string password = "p@ss;word=1 'quoted\"";

        var parsed = new NpgsqlConnectionStringBuilder(DatabaseConnectionString.Resolve(Config(("POSTGRES_PASSWORD", password))));

        Assert.Equal(password, parsed.Password);
    }

    [Fact]
    public void Missing_configuration_fails_with_actionable_message_and_no_secret()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DatabaseConnectionString.Resolve(Config()));

        Assert.Contains("POSTGRES_PASSWORD", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__HuntOps", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    [InlineData("-1")]
    public void Invalid_port_is_rejected(string port)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConnectionString.Resolve(Config(("POSTGRES_PASSWORD", "pw"), ("POSTGRES_PORT", port))));

        Assert.Contains("POSTGRES_PORT", ex.Message, StringComparison.Ordinal);
    }
}
