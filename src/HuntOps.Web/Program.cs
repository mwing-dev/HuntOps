using HuntOps.Api;
using HuntOps.Infrastructure;
using HuntOps.Infrastructure.Health;
using HuntOps.Infrastructure.Hosting;
using HuntOps.Infrastructure.Logging;
using HuntOps.Web.Components;
using Serilog;

if (ContainerHealthProbe.IsRequested(args))
{
    return await ContainerHealthProbe.RunAsync(defaultPort: 8080);
}

var builder = WebApplication.CreateBuilder(args);

builder.AddHuntOpsLogging("huntops-web");
builder.Services.AddHuntOpsInfrastructure();
builder.Services.AddHuntOpsDataProtection();
builder.Services.AddHuntOpsApi();
builder.Services.AddHealthChecks().AddHuntOpsReadinessChecks();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Outermost, so it records the final status code after exception handling (a handled 409 is logged as 409).
// Logs method, path, status and timing only; headers (including Authorization) are never logged.
app.UseSerilogRequestLogging();

// /api gets JSON problem details for errors and status codes; the dashboard keeps its HTML pages.
app.UseWhen(ApiSetup.IsApiRequest, api =>
{
    api.UseExceptionHandler();
    api.UseStatusCodePages();
});
app.UseWhen(context => !ApiSetup.IsApiRequest(context), dashboard =>
{
    if (!app.Environment.IsDevelopment())
    {
        dashboard.UseExceptionHandler("/Error", createScopeForErrors: true);
    }

    dashboard.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
});

// TLS terminates at the reverse proxy; forwarded headers and HSTS arrive with dashboard authentication in Phase 3.
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHuntOpsHealthEndpoints();
app.MapHuntOpsApiDocs();
app.MapHuntOpsApi();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
return 0;

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
