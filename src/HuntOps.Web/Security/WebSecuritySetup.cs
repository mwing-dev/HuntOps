using HuntOps.Application.Common;
using HuntOps.Infrastructure.Identity;
using HuntOps.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;

namespace HuntOps.Web.Security;

internal static class WebSecuritySetup
{
    public const string AuthCookieName = "HuntOps.Auth";
    public const string AntiforgeryCookieName = "HuntOps.Antiforgery";

    /// <summary>Owner sign-in (Identity cookies), dashboard services and MudBlazor.</summary>
    public static IServiceCollection AddHuntOpsDashboard(this IServiceCollection services, IConfiguration configuration)
    {
        var securePolicy = CookieSecurity(configuration);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();
        new IdentityBuilder(typeof(AppUser), services).AddSignInManager();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = AuthCookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = securePolicy;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.AccessDeniedPath = "/account/login";
        });
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = AntiforgeryCookieName;
            options.Cookie.SecurePolicy = securePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
        services.AddScoped<CircuitUser>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, UserCircuitHandler>());
        services.Replace(ServiceDescriptor.Scoped<ICurrentActor, WebCurrentActor>());

        services.AddScoped<AppServices>();
        services.AddScoped<UiErrors>();
        services.AddScoped<OwnerTime>();
        services.AddSingleton<SystemStatusService>();
        services.AddHttpClient("ntfy-health", client => client.Timeout = TimeSpan.FromSeconds(3));

        services.AddMudServices();
        return services;
    }

    /// <summary>
    /// HUNTOPS_COOKIE_SECURE: "auto" (default) marks cookies Secure whenever the request is HTTPS, directly or via a
    /// trusted proxy's X-Forwarded-Proto, so http://127.0.0.1 still works locally. "always" requires HTTPS for every
    /// request (recommended in production behind a TLS proxy, with HUNTOPS_TRUST_FORWARDED_HEADERS=true).
    /// HttpOnly and SameSite apply in both modes.
    /// </summary>
    internal static CookieSecurePolicy CookieSecurity(IConfiguration configuration)
    {
        var value = configuration["HUNTOPS_COOKIE_SECURE"]?.Trim();
        return value?.ToLowerInvariant() switch
        {
            null or "" or "auto" => CookieSecurePolicy.SameAsRequest,
            "always" => CookieSecurePolicy.Always,
            _ => throw new InvalidOperationException($"HUNTOPS_COOKIE_SECURE must be 'auto' or 'always' (was '{value}')."),
        };
    }

    /// <summary>
    /// Trust X-Forwarded-For/Proto/Host from the reverse proxy (HUNTOPS_TRUST_FORWARDED_HEADERS=true). Published
    /// ports bind to 127.0.0.1 by default, so only a proxy on the same host can reach the app.
    /// </summary>
    public static WebApplication UseHuntOpsForwardedHeaders(this WebApplication app)
    {
        if (app.Configuration.GetValue("HUNTOPS_TRUST_FORWARDED_HEADERS", false))
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
            };
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            app.UseForwardedHeaders(options);
        }

        return app;
    }

    /// <summary>POST /account/logout. Form-bound, so antiforgery validation applies.</summary>
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/logout", async (SignInManager<AppUser> signInManager, [FromForm] string? returnUrl) =>
            {
                await signInManager.SignOutAsync();
                return TypedResults.LocalRedirect("/account/login?loggedOut=true");
            })
            .ExcludeFromDescription();
        return endpoints;
    }
}
