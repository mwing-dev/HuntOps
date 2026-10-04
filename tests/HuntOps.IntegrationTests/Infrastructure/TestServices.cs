using HuntOps.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HuntOps.IntegrationTests.Infrastructure;

internal static class TestServices
{
    /// <summary>Builds the same Infrastructure registrations the hosts use, pointed at a test database.</summary>
    public static ServiceProvider Build(string connectionString, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:HuntOps"] = connectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        configure?.Invoke(services);
        services.AddHuntOpsInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
