using System.Security.Claims;

namespace HuntOps.Web.Security;

/// <summary>
/// Runs an application-service call in its own DI scope (fresh DbContext per operation), carrying the dashboard
/// user into that scope. Blazor Server circuits are long-lived; sharing one DbContext across a whole circuit would
/// leak tracked state between operations (e.g. after a failed save).
/// </summary>
public sealed class AppServices(IServiceScopeFactory scopes, CircuitUser circuitUser, IHttpContextAccessor accessor)
{
    public ClaimsPrincipal CurrentUser =>
        circuitUser.User.Identity?.IsAuthenticated == true
            ? circuitUser.User
            : accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> operation)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CircuitUser>().User = CurrentUser;
        return await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task RunAsync<TService>(Func<TService, Task> operation)
        where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CircuitUser>().User = CurrentUser;
        await operation(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
