using HuntOps.Infrastructure;
using HuntOps.Infrastructure.Health;
using HuntOps.Infrastructure.Hosting;
using HuntOps.Infrastructure.Logging;
using HuntOps.Worker.Commands;
using HuntOps.Worker.Heartbeat;
using HuntOps.Worker.Migrations;

if (ContainerHealthProbe.IsRequested(args))
{
    return await ContainerHealthProbe.RunAsync(defaultPort: 8081);
}

if (MigrateCommand.IsRequested(args))
{
    return await MigrateCommand.RunAsync(args);
}

if (ApiKeyCommand.IsRequested(args))
{
    return await ApiKeyCommand.RunAsync(args, Console.Out, Console.Error);
}

var builder = WebApplication.CreateSlimBuilder(args);

builder.AddHuntOpsLogging("huntops-worker");
builder.Services.AddHuntOpsInfrastructure();

builder.Services.AddOptions<HeartbeatOptions>()
    .Bind(builder.Configuration.GetSection(HeartbeatOptions.SectionName))
    .Configure(options => options.WorkerId = builder.Configuration["HUNTOPS_WORKER_ID"] ?? options.WorkerId)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<HeartbeatState>();
builder.Services.AddHostedService<HeartbeatService>();

builder.Services.AddHealthChecks()
    .AddHuntOpsReadinessChecks()
    .AddCheck<HeartbeatHealthCheck>("heartbeat", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

app.MapHuntOpsHealthEndpoints();

await app.RunAsync();
return 0;
