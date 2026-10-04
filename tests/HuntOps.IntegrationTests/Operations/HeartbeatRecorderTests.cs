using HuntOps.Application.Operations;
using HuntOps.Infrastructure.Persistence;
using HuntOps.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Testing;

namespace HuntOps.IntegrationTests.Operations;

public sealed class HeartbeatRecorderTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Repeated_beats_update_a_single_row_per_worker_component()
    {
        var clock = new FakeClock(Instant.FromUtc(2027, 5, 12, 6, 0));
        var started = clock.GetCurrentInstant();
        await using var services = TestServices.Build(
            await postgres.CreateMigratedDatabaseAsync(),
            s => s.AddSingleton<IClock>(clock));

        await RecordAsync(services, "worker", started);
        clock.Advance(Duration.FromSeconds(30));
        var second = await RecordAsync(services, "worker", started);
        await RecordAsync(services, "worker-b", started);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntOpsDbContext>();
        var rows = await db.WorkerHeartbeats.AsNoTracking().OrderBy(h => h.WorkerId).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        Assert.Equal("worker", rows[0].WorkerId);
        Assert.Equal(second, rows[0].LastBeatAt);
        Assert.Equal(started, rows[0].StartedAt);
        Assert.Equal("1.0.0", rows[0].Version);
    }

    private static async Task<Instant> RecordAsync(IServiceProvider services, string workerId, Instant started)
    {
        await using var scope = services.CreateAsyncScope();
        var recorder = scope.ServiceProvider.GetRequiredService<HeartbeatRecorder>();
        return await recorder.RecordAsync(workerId, "worker", started, "1.0.0", TestContext.Current.CancellationToken);
    }
}
