using System.Reflection;
using HuntOps.Application.Operations;
using Microsoft.Extensions.Options;

namespace HuntOps.Worker.Heartbeat;

/// <summary>Writes this worker's heartbeat row on a fixed interval.</summary>
internal sealed partial class HeartbeatService(
    IServiceScopeFactory scopeFactory,
    HeartbeatState state,
    IOptions<HeartbeatOptions> options,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    private static readonly string? Version =
        typeof(HeartbeatService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        LogStarting(logger, settings.WorkerId, settings.IntervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.IntervalSeconds));
        do
        {
            await BeatAsync(settings.WorkerId, stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task BeatAsync(string workerId, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var recorder = scope.ServiceProvider.GetRequiredService<HeartbeatRecorder>();
            var at = await recorder.RecordAsync(workerId, HeartbeatOptions.Component, state.StartedAt, Version, cancellationToken);
            state.MarkBeat(at);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // A failed beat must never stop the worker; the readiness check reports staleness.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogBeatFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Heartbeat started for worker {WorkerId} every {IntervalSeconds}s")]
    private static partial void LogStarting(ILogger logger, string workerId, int intervalSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to record worker heartbeat")]
    private static partial void LogBeatFailed(ILogger logger, Exception exception);
}
