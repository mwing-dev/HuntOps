using System.Security.Claims;
using HuntOps.Api.Security;
using HuntOps.Application.Common;
using HuntOps.Domain.Actions;
using HuntOps.Domain.Users;

namespace HuntOps.Web.Security;

/// <summary>
/// The actor for anything running in the web host: an API key (REST), or the signed-in owner (dashboard,
/// either during the prerender HTTP request or inside the interactive circuit).
/// </summary>
internal sealed class WebCurrentActor(IHttpContextAccessor accessor, CircuitUser circuitUser) : ICurrentActor
{
    public string ActorId
    {
        get
        {
            var user = Principal;
            if (user.FindFirstValue(ApiClaims.KeyPrefix) is { } prefix)
            {
                return $"apikey:{prefix}";
            }

            return user.Identity?.IsAuthenticated == true ? $"user:{user.Identity.Name}" : "anonymous";
        }
    }

    public string UserId => Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? Owner.PlaceholderUserId;

    public ChangeChannel Channel => Principal.HasClaim(c => c.Type == ApiClaims.KeyPrefix) ? ChangeChannel.Api : ChangeChannel.Web;

    private ClaimsPrincipal Principal =>
        circuitUser.User.Identity?.IsAuthenticated == true
            ? circuitUser.User
            : accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
}
