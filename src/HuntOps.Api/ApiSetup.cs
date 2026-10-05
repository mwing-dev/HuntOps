using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using HuntOps.Api.Endpoints;
using HuntOps.Api.Errors;
using HuntOps.Api.OpenApi;
using HuntOps.Api.Security;
using HuntOps.Application.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HuntOps.Api;

public static class ApiSetup
{
    /// <summary>Registers REST API services: API-key auth, policies, problem details, JSON options and OpenAPI.</summary>
    public static IServiceCollection AddHuntOpsApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<ICurrentActor, HttpCurrentActor>());

        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<ApiExceptionHandler>();

        // Malformed bodies/parameters raise BadHttpRequestException so ApiExceptionHandler can explain them.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        // No default scheme here: the web host makes the Identity cookie the default for the dashboard, and API
        // policies name the API-key scheme explicitly, so cookies never authorize /api calls.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);
        services.AddAuthorizationBuilder()
            .AddPolicy(ApiPolicies.Read, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(ApiClaims.Scope, ApiClaims.ReadScope))
            .AddPolicy(ApiPolicies.Write, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(ApiClaims.Scope, ApiClaims.WriteScope));

        services.AddOpenApi("v1", options =>
        {
            options.ShouldInclude = description => description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true;
            options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>();
        });

        return services;
    }

    public static bool IsApiRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps every /api endpoint. All of them require an authenticated API key with the 'read' scope;
    /// write endpoints additionally require 'write'. There are no anonymous API endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapHuntOpsApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api").RequireAuthorization(ApiPolicies.Read)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        api.MapGet("/me", (ClaimsPrincipal user) => new
            {
                keyPrefix = user.FindFirst(ApiClaims.KeyPrefix)?.Value,
                keyName = user.Identity?.Name,
                scopes = user.FindAll(ApiClaims.Scope).Select(c => c.Value).ToArray(),
            })
            .WithTags("Access").WithName("WhoAmI").WithSummary("Describe the API key used for this request");

        api.MapJurisdictions();
        api.MapAgencies();
        api.MapPrograms();
        api.MapEventTypes();
        api.MapEvents();
        api.MapActions();

        // Unknown /api routes return a problem-details 404 rather than the dashboard's HTML page.
        api.MapFallback(() => TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not found",
                detail: "No API endpoint matches this path. See /swagger for the API reference."))
            .ExcludeFromDescription();

        return endpoints;
    }

    /// <summary>Serves the OpenAPI document at /openapi/v1.json and Swagger UI at /swagger (configurable).</summary>
    public static WebApplication MapHuntOpsApiDocs(this WebApplication app)
    {
        if (!app.Configuration.GetValue("HUNTOPS_SWAGGER_ENABLED", true))
        {
            return app;
        }

        app.MapOpenApi("/openapi/{documentName}.json");
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "HuntOps API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "HuntOps API";
            options.EnablePersistAuthorization();
        });
        return app;
    }
}
