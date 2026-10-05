using System.Security.Claims;
using HuntOps.Application.Common;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Users;
using Microsoft.AspNetCore.Http;

namespace HuntOps.Api.Security;

/// <summary>The caller of the current REST request, identified by API key prefix.</summary>
internal sealed class HttpCurrentActor(IHttpContextAccessor accessor) : ICurrentActor
{
    public string ActorId =>
        User?.FindFirstValue(ApiClaims.KeyPrefix) is { } prefix ? $"apikey:{prefix}" : "anonymous";

    public string UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Owner.UserId;

    public ChangeChannel Channel => ChangeChannel.Api;

    private ClaimsPrincipal? User => accessor.HttpContext?.User;
}
