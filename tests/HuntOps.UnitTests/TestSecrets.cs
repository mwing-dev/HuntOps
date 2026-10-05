using System.Security.Cryptography;

namespace HuntOps.UnitTests;

/// <summary>
/// Generates throwaway credential values at runtime so no password-like literals are committed
/// (secret scanners flag those even when they are test fixtures).
/// </summary>
internal static class TestSecrets
{
    public static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
