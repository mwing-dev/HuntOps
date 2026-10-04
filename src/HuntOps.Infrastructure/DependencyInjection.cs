using HuntOps.Application;
using HuntOps.Application.Abstractions;
using HuntOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HuntOps.Infrastructure;

public static class DependencyInjection
{
    public const string DataProtectionApplicationName = "HuntOps";

    public static IServiceCollection AddHuntOpsInfrastructure(this IServiceCollection services)
    {
        // The connection string is resolved lazily so hosts and tests can supply configuration after registration.
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<HuntOpsDbContext>((provider, options) =>
            HuntOpsDbContextOptions.Configure(
                    options,
                    DatabaseConnectionString.Resolve(provider.GetRequiredService<IConfiguration>()))
                .AddInterceptors(provider.GetRequiredService<AuditingInterceptor>()));
        services.AddScoped<IHuntOpsDb>(provider => provider.GetRequiredService<HuntOpsDbContext>());
        services.AddSingleton<IDatabaseErrorClassifier, PostgresErrorClassifier>();
        services.AddHuntOpsApplication();
        return services;
    }

    /// <summary>Data Protection keys stored in PostgreSQL so they survive container recreation (architecture risk 12).</summary>
    public static IDataProtectionBuilder AddHuntOpsDataProtection(this IServiceCollection services) =>
        services
            .AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<HuntOpsDbContext>();
}
