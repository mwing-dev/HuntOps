using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Access;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Access;

public sealed record ApiKeyCreateInput(string? Name, string? Scope, int? ExpiresInDays = null);

public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    string Prefix,
    IReadOnlyList<string> Scopes,
    string CreatedAt,
    string? CreatedBy,
    string? LastUsedAt,
    string? ExpiresAt,
    string? RevokedAt,
    bool IsActive);

/// <summary>Result of creating a key. <see cref="Secret"/> is the only time the plaintext key exists.</summary>
public sealed record ApiKeyCreatedDto(ApiKeyDto Key, string Secret);

/// <summary>An authenticated key (never contains the secret).</summary>
public sealed record ApiKeyIdentity(Guid Id, string Prefix, string Name, string UserId, ApiKeyScopes Scopes);

public enum ApiKeyScopeChoice
{
    Read,
    Write,
}

public sealed class ApiKeyService(IHuntOpsDb db, IClock clock, IDateTimeZoneProvider zones, ICurrentActor actor)
{
    /// <summary>LastUsedAt is refreshed at most this often, so authentication does not write on every request.</summary>
    public static readonly Duration LastUsedResolution = Duration.FromMinutes(5);

    public async Task<ApiKeyCreatedDto> CreateAsync(ApiKeyCreateInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var v = new InputValidator(zones);
        var name = v.RequiredText("name", input.Name, InputValidator.ShortTextMaxLength);
        var scope = v.Enum<ApiKeyScopeChoice>("scope", input.Scope, required: true);
        var expiresInDays = v.IntRange("expiresInDays", input.ExpiresInDays, 1, 3650, required: false);
        v.ThrowIfInvalid();

        var now = clock.GetCurrentInstant();
        var generated = ApiKeyToken.Generate();
        var key = new ApiKey
        {
            Name = name,
            Prefix = generated.Prefix,
            Hash = generated.Hash,
            Scopes = scope == ApiKeyScopeChoice.Write ? ApiKeyScopes.ReadWrite : ApiKeyScopes.Read,
            UserId = actor.UserId,
            CreatedAt = now,
            CreatedBy = actor.ActorId,
            ExpiresAt = expiresInDays is { } d ? now + Duration.FromDays(d) : null,
        };

        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
        return new ApiKeyCreatedDto(ToDto(key, now), generated.Plaintext);
    }

    public async Task<IReadOnlyList<ApiKeyDto>> ListAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        var keys = await db.ApiKeys.AsNoTracking().OrderBy(k => k.CreatedAt).ToListAsync(cancellationToken);
        return keys.ConvertAll(k => ToDto(k, now));
    }

    /// <summary>Revokes by prefix ("hops_ab12cd34") or id. Revocation is permanent.</summary>
    public async Task<ApiKeyDto> RevokeAsync(string prefixOrId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefixOrId);
        var value = prefixOrId.Trim();
        var key = Guid.TryParse(value, out var id)
            ? await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, cancellationToken)
            : await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == value, cancellationToken);
        if (key is null)
        {
            throw new NotFoundException("API key", value);
        }

        var now = clock.GetCurrentInstant();
        key.RevokedAt ??= now;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(key, now);
    }

    /// <summary>Validates a presented key. Returns null for malformed, unknown, revoked or expired keys.</summary>
    public async Task<ApiKeyIdentity?> AuthenticateAsync(string? presented, CancellationToken cancellationToken)
    {
        if (!ApiKeyToken.TryGetPrefix(presented, out var prefix))
        {
            return null;
        }

        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Prefix == prefix, cancellationToken);
        var now = clock.GetCurrentInstant();
        if (key is null || !ApiKeyToken.Matches(presented!, key.Hash) || !key.IsActiveAt(now))
        {
            return null;
        }

        if (key.LastUsedAt is null || now - key.LastUsedAt.Value >= LastUsedResolution)
        {
            key.LastUsedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ApiKeyIdentity(key.Id, key.Prefix, key.Name, key.UserId, key.Scopes);
    }

    private static ApiKeyDto ToDto(ApiKey k, Instant now)
    {
        var scopes = new List<string>();
        if (k.Allows(ApiKeyScopes.Read))
        {
            scopes.Add("read");
        }

        if (k.Allows(ApiKeyScopes.Write))
        {
            scopes.Add("write");
        }

        return new ApiKeyDto(
            k.Id, k.Name, k.Prefix, scopes, TimeFormats.Format(k.CreatedAt), k.CreatedBy,
            TimeFormats.Format(k.LastUsedAt), TimeFormats.Format(k.ExpiresAt), TimeFormats.Format(k.RevokedAt),
            k.IsActiveAt(now));
    }
}
