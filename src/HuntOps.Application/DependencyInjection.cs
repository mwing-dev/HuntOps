using HuntOps.Application.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NodaTime;

namespace HuntOps.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddHuntOpsApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock>(SystemClock.Instance);
        services.AddScoped<HeartbeatRecorder>();
        return services;
    }
}
