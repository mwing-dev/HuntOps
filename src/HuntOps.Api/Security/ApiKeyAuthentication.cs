using System.Security.Claims;
using System.Text.Encodings.Web;
using HuntOps.Application.Access;
using HuntOps.Domain.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HuntOps.Api.Security;

public static class ApiClaims
{
    public const string Scope = "huntops:scope";
    public const string KeyPrefix = "huntops:key";
    public const string ReadScope = "read";
    public const string WriteScope = "write";
}

public static class ApiPolicies
{
    public const string Read = "huntops.api.read";
    public const string Write = "huntops.api.write";
}

/// <summary>
/// Authenticates <c>Authorization: Bearer hops_...</c> (or <c>X-Api-Key</c>) against hashed keys.
/// The presented key is never logged or echoed; only its public prefix appears in logs.
/// </summary>
public sealed partial class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ApiKeyService apiKeys,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ReadPresentedKey(Request);
        if (presented is null)
        {
            return AuthenticateResult.NoResult();
        }

        var identity = await apiKeys.AuthenticateAsync(presented, Context.RequestAborted);
        if (identity is null)
        {
            LogRejected(Logger, ApiKeyToken.TryGetPrefix(presented, out var prefix) ? prefix : "(malformed)");
            return AuthenticateResult.Fail("Invalid, expired or revoked API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identity.UserId),
            new(ClaimTypes.Name, identity.Name),
            new(ApiClaims.KeyPrefix, identity.Prefix),
        };
        if (identity.Scopes.HasFlag(ApiKeyScopes.Read))
        {
            claims.Add(new Claim(ApiClaims.Scope, ApiClaims.ReadScope));
        }

        if (identity.Scopes.HasFlag(ApiKeyScopes.Write))
        {
            claims.Add(new Claim(ApiClaims.Scope, ApiClaims.WriteScope));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer realm=\"HuntOps\"";
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails =
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Authentication required",
                Detail = "Send a HuntOps API key as 'Authorization: Bearer hops_...'. Keys are created with the worker's 'apikey create' command.",
            },
        });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails =
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Insufficient scope",
                Detail = "This API key is read-only; this operation requires a key with the 'write' scope.",
            },
        });
    }

    internal static string? ReadPresentedKey(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization["Bearer ".Length..].Trim();
            return token.Length > 0 ? token : null;
        }

        var header = request.Headers[HeaderName].ToString().Trim();
        return header.Length > 0 ? header : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected API key {KeyPrefix}")]
    private static partial void LogRejected(ILogger logger, string keyPrefix);
}
