using NodaTime;

namespace HuntOps.Domain.Access;

[Flags]
public enum ApiKeyScopes
{
    None = 0,
    Read = 1,
    Write = 2,
    ReadWrite = Read | Write,
}

/// <summary>
/// An API key for REST (and later MCP) access. Only a SHA-256 hash of the key is stored; the plaintext is
/// shown once at creation. <see cref="Prefix"/> is a non-secret lookup id that is safe to display and log.
/// </summary>
public sealed class ApiKey
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>Public identifier, e.g. "hops_ab12cd34". Unique.</summary>
    public required string Prefix { get; init; }

    /// <summary>SHA-256 of the full key (32 bytes).</summary>
    public required byte[] Hash { get; init; }

    public ApiKeyScopes Scopes { get; init; }

    public required string UserId { get; init; }

    public Instant CreatedAt { get; init; }

    public string? CreatedBy { get; init; }

    public Instant? LastUsedAt { get; set; }

    public Instant? ExpiresAt { get; init; }

    public Instant? RevokedAt { get; set; }

    public bool IsActiveAt(Instant now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt.Value > now);

    public bool Allows(ApiKeyScopes required) => (Scopes & required) == required;
}
