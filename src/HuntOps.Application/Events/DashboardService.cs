using HuntOps.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace HuntOps.Application.Events;

public sealed record DashboardSummaryDto(
    int Jurisdictions,
    int Agencies,
    int Programs,
    int EventsNext60Days,
    int EventTypes);

/// <summary>Counts for the dashboard's summary card.</summary>
public sealed class DashboardService(IHuntOpsDb db, IClock clock)
{
    public async Task<DashboardSummaryDto> SummaryAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        var horizon = now + Duration.FromDays(60);
        return new DashboardSummaryDto(
            await db.Jurisdictions.CountAsync(j => j.ArchivedAt == null, cancellationToken),
            await db.Agencies.CountAsync(a => a.ArchivedAt == null, cancellationToken),
            await db.Programs.CountAsync(p => p.ArchivedAt == null, cancellationToken),
            await db.ProgramEvents.CountAsync(
                e => e.ArchivedAt == null && e.Program.ArchivedAt == null && e.StartsAtUtc <= horizon && (e.EndsAtUtc ?? e.StartsAtUtc) >= now,
                cancellationToken),
            await db.EventTypes.CountAsync(t => t.ArchivedAt == null, cancellationToken));
    }
}
