using System.Security.Cryptography;
using Npgsql;

namespace HuntOps.IntegrationTests.Infrastructure;

/// <summary>
/// Generates throwaway credential values at runtime so no password-like literals are committed
/// (secret scanners flag those even when they are test fixtures).
/// </summary>
internal static class TestSecrets
{
    public static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>A random password that satisfies the owner password policy (upper, lower, digit, symbol, 12+).</summary>
    public static string NewOwnerPassword() => "Hx7!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();

    /// <summary>A connection string to a port nothing listens on, with a random password the test can assert never leaks.</summary>
    public static string UnreachableDatabase(out string password)
    {
        password = NewPassword();
        return new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = 1,
            Database = "none",
            Username = "nobody",
            Password = password,
            Timeout = 1,
        }.ConnectionString;
    }
}
