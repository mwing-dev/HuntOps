using System.Security.Claims;
using AngleSharp.Dom;
using Bunit;
using HuntOps.Application.Common;
using HuntOps.Infrastructure.Identity;
using HuntOps.Infrastructure.Persistence;
using HuntOps.IntegrationTests.Infrastructure;
using HuntOps.Web.Security;
using HuntOps.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor;
using MudBlazor.Services;

namespace HuntOps.IntegrationTests.Web;

/// <summary>
/// bUnit host for dashboard components: the real application/infrastructure services against PostgreSQL, the
/// dashboard's per-operation scoping, and a signed-in owner. Tests assert behavior (text, persisted data), not CSS.
/// </summary>
internal sealed class ComponentTestHost : BunitContext
{
    private ComponentTestHost()
    {
    }

    public string ConnectionString { get; private set; } = "";

    public string OwnerId { get; private set; } = "";

    public static async Task<ComponentTestHost> CreateAsync(PostgresFixture postgres)
    {
        var host = new ComponentTestHost { ConnectionString = await postgres.CreateMigratedDatabaseAsync() };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:HuntOps"] = host.ConnectionString,
                [OwnerBootstrapper.EmailVariable] = "owner@huntops.test",
                [OwnerBootstrapper.PasswordVariable] = TestSecrets.NewOwnerPassword(),
            })
            .Build();

        var services = host.Services;
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        HuntOps.Infrastructure.DependencyInjection.AddHuntOpsInfrastructure(services);
        services.AddHttpContextAccessor();
        services.AddScoped<CircuitUser>();
        services.AddScoped<AppServices>();
        services.AddScoped<UiErrors>();
        services.AddScoped<OwnerTime>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentActor, WebCurrentActor>());
        services.AddMudServices();
        host.JSInterop.Mode = JSRuntimeMode.Loose;

        await using (var scope = host.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OwnerBootstrapper>().RunAsync(CancellationToken.None);
            host.OwnerId = await scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>().Users.Select(u => u.Id).SingleAsync();
        }

        // The signed-in owner, as the circuit handler would provide it.
        host.Services.GetRequiredService<CircuitUser>().User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, host.OwnerId), new Claim(ClaimTypes.Name, "owner@huntops.test")], "Test"));

        // MudBlazor popovers (autocomplete, select) need their provider in the render tree.
        host.Render<MudPopoverProvider>();
        return host;
    }

    /// <summary>Runs an application service in its own scope as the owner (like the dashboard does).</summary>
    public Task<T> RunAsync<TService, T>(Func<TService, Task<T>> operation)
        where TService : notnull =>
        Services.GetRequiredService<AppServices>().RunAsync(operation);

    /// <summary>Finds a button by its visible text.</summary>
    public static IElement Button<TComponent>(IRenderedComponent<TComponent> component, string text)
        where TComponent : Microsoft.AspNetCore.Components.IComponent =>
        component.FindAll("button").Single(b => string.Equals(b.TextContent.Trim(), text, StringComparison.Ordinal));
}
