using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace HuntOps.Web.Security;

/// <summary>
/// The signed-in user of an interactive Blazor circuit. HttpContext is not available inside a circuit, so this is
/// how services resolved for dashboard operations learn who the actor is.
/// </summary>
public sealed class CircuitUser
{
    public ClaimsPrincipal User { get; set; } = new(new ClaimsIdentity());
}

/// <summary>Keeps <see cref="CircuitUser"/> in sync with the circuit's authentication state.</summary>
internal sealed class UserCircuitHandler(AuthenticationStateProvider authenticationStateProvider, CircuitUser circuitUser)
    : CircuitHandler, IDisposable
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        circuitUser.User = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        await base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task) =>
        _ = UpdateAsync(task);

    private async Task UpdateAsync(Task<AuthenticationState> task)
    {
        try
        {
            circuitUser.User = (await task).User;
        }
#pragma warning disable CA1031 // A failed state refresh leaves the previous user; the revalidating provider signs out stale users.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
