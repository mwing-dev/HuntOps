using HuntOps.Application.Access;
using HuntOps.Application.Common;
using HuntOps.Application.Events;
using HuntOps.Application.Operations;
using HuntOps.Application.Reference;
using HuntOps.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;

namespace HuntOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddHuntOpsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.TryAddSingleton<IDateTimeZoneProvider>(DateTimeZoneProviders.Tzdb);

        // Hosts replace this with an interface-specific actor (API key, CLI, ...).
        services.TryAddScoped<ICurrentActor, SystemActor>();

        services.AddScoped<HeartbeatRecorder>();
        services.AddScoped<JurisdictionService>();
        services.AddScoped<AgencyService>();
        services.AddScoped<ProgramService>();
        services.AddScoped<EventTypeService>();
        services.AddScoped<ProgramEventService>();
        services.AddScoped<ActionService>();
        services.AddScoped<ApiKeyService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<OwnerSettingsService>();
        services.AddOptions<OwnerDefaultsOptions>();
        return services;
    }
}
