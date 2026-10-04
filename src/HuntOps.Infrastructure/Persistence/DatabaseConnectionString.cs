using System.Globalization;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HuntOps.Infrastructure.Persistence;

/// <summary>
/// Resolves the PostgreSQL connection string. An explicit <c>ConnectionStrings:HuntOps</c> wins;
/// otherwise it is built from the same POSTGRES_* variables the postgres container uses.
/// </summary>
public static class DatabaseConnectionString
{
    public const string Name = "HuntOps";

    public static string Resolve(IConfiguration configuration)
    {
        var explicitConnectionString = configuration.GetConnectionString(Name);
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var password = configuration["POSTGRES_PASSWORD"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Database is not configured. Set ConnectionStrings__HuntOps, or POSTGRES_PASSWORD " +
                "(with optional POSTGRES_HOST, POSTGRES_PORT, POSTGRES_DB, POSTGRES_USER).");
        }

        var portText = configuration["POSTGRES_PORT"];
        var port = 5432;
        if (!string.IsNullOrWhiteSpace(portText) &&
            (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
        {
            throw new InvalidOperationException($"POSTGRES_PORT '{portText}' is not a valid TCP port.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = NonEmpty(configuration["POSTGRES_HOST"]) ?? "localhost",
            Port = port,
            Database = NonEmpty(configuration["POSTGRES_DB"]) ?? "huntops",
            Username = NonEmpty(configuration["POSTGRES_USER"]) ?? "huntops",
            Password = password,

            // The stack talks to PostgreSQL over the internal Docker network with password auth. Disabling GSS
            // stops Npgsql probing for Kerberos libraries that the slim .NET runtime image does not ship.
            GssEncryptionMode = GssEncryptionMode.Disable,
        };

        return builder.ConnectionString;
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
