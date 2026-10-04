using HuntOps.Infrastructure;
using HuntOps.Infrastructure.Health;
using HuntOps.Infrastructure.Hosting;
using HuntOps.Infrastructure.Logging;
using HuntOps.Web.Components;

if (ContainerHealthProbe.IsRequested(args))
{
    return await ContainerHealthProbe.RunAsync(defaultPort: 8080);
}

var builder = WebApplication.CreateBuilder(args);

builder.AddHuntOpsLogging("huntops-web");
builder.Services.AddHuntOpsInfrastructure();
builder.Services.AddHuntOpsDataProtection();
builder.Services.AddHealthChecks().AddHuntOpsReadinessChecks();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// TLS terminates at the reverse proxy; forwarded headers and HSTS arrive with authentication in Phase 3.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapHuntOpsHealthEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
return 0;

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
