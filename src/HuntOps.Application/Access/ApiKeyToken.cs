using System.Security.Cryptography;
using System.Text;

namespace HuntOps.Application.Access;

/// <summary>
/// API key format: <c>hops_{id}_{secret}</c>, where <c>id</c> is 8 lowercase alphanumerics (public lookup id)
/// and <c>secret</c> is 32 random bytes in base64url (43 chars). Only SHA-256(key) is stored. The key carries
/// 256 bits of entropy, so a fast hash is appropriate (no password-stretching needed).
/// </summary>
public static class ApiKeyToken
{
    public const string Scheme = "hops";
    private const int IdLength = 8;
    private const int SecretLength = 43;
    private const string IdAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static int TokenLength => Scheme.Length + 1 + IdLength + 1 + SecretLength;

    public static GeneratedApiKey Generate()
    {
        var id = RandomNumberGenerator.GetString(IdAlphabet, IdLength);
        var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var plaintext = $"{Scheme}_{id}_{secret}";
        return new GeneratedApiKey(plaintext, PrefixOf(id), Hash(plaintext));
    }

    /// <summary>Extracts the public prefix ("hops_ab12cd34") if the value is shaped like a HuntOps key.</summary>
    public static bool TryGetPrefix(string? presented, out string prefix)
    {
        prefix = "";
        if (presented is null || presented.Length != TokenLength || !presented.StartsWith(Scheme + "_", StringComparison.Ordinal))
        {
            return false;
        }

        var id = presented.AsSpan(Scheme.Length + 1, IdLength);
        var separator = presented[Scheme.Length + 1 + IdLength];
        var secret = presented.AsSpan(Scheme.Length + 2 + IdLength);

        foreach (var c in id)
        {
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        if (separator != '_')
        {
            return false;
        }

        foreach (var c in secret)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        prefix = PrefixOf(id.ToString());
        return true;
    }

    public static byte[] Hash(string plaintext) => SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));

    /// <summary>Constant-time comparison of a presented key against a stored hash.</summary>
    public static bool Matches(string presented, byte[] storedHash) =>
        CryptographicOperations.FixedTimeEquals(Hash(presented), storedHash);

    private static string PrefixOf(string id) => $"{Scheme}_{id}";

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>A freshly generated key. <see cref="Plaintext"/> must be shown once and never stored or logged.</summary>
public sealed record GeneratedApiKey(string Plaintext, string Prefix, byte[] Hash);
