using HuntOps.Application.Common;
using HuntOps.Application.Events;
using HuntOps.Application.Reference;
using HuntOps.Application.Users;
using HuntOps.Domain.Actions;
using HuntOps.Infrastructure;
using HuntOps.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Events;

namespace HuntOps.Worker.Commands;

/// <summary>
/// <c>HuntOps.Worker dev-seed</c>: creates development/manual-test data (Kansas · KDWP · Resident Antelope with a
/// multi-year history, an upcoming application, a draw-results date and a currently open purchase window).
/// Development data only: it is never run automatically and is not part of any migration. Idempotent: existing
/// records (matched by code/name/natural key) are reused, and already-resolved actions are left alone.
/// Everything goes through the application services, exactly like the dashboard.
/// </summary>
internal static class DevSeedCommand
{
    public const string Name = "dev-seed";

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(TextWriter output, TextWriter error, IReadOnlyDictionary<string, string?>? configurationOverrides = null)
    {
        var builder = Host.CreateApplicationBuilder([]);
        if (configurationOverrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(configurationOverrides);
        }

        builder.AddHuntOpsLogging("huntops-cli", logger => logger.MinimumLevel.Is(LogEventLevel.Warning));
        builder.Services.AddHuntOpsInfrastructure();
        var actor = new CliActor();
        builder.Services.Replace(ServiceDescriptor.Scoped<ICurrentActor>(_ => actor));

        using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var ownerId = await sp.GetRequiredService<IOwnerDirectory>().GetOwnerUserIdAsync(CancellationToken.None);
        if (ownerId is null)
        {
            await error.WriteLineAsync("No owner account exists yet. Start the stack with HUNTOPS_ADMIN_EMAIL/HUNTOPS_ADMIN_PASSWORD set first.");
            return 1;
        }

        actor.UserId = ownerId;
        var seeder = new Seeder(sp, output);
        await seeder.RunAsync();
        await output.WriteLineAsync("Development data is ready: Kansas · KDWP · Resident Antelope.");
        return 0;
    }

    private sealed class Seeder(IServiceProvider services, TextWriter output)
    {
        private readonly JurisdictionService jurisdictions = services.GetRequiredService<JurisdictionService>();
        private readonly AgencyService agencies = services.GetRequiredService<AgencyService>();
        private readonly ProgramService programs = services.GetRequiredService<ProgramService>();
        private readonly ProgramEventService events = services.GetRequiredService<ProgramEventService>();
        private readonly ActionService actions = services.GetRequiredService<ActionService>();
        private readonly CancellationToken ct = CancellationToken.None;

        public async Task RunAsync()
        {
            var kansas = (await jurisdictions.ListAsync(false, ct)).FirstOrDefault(j => j.Code == "KS")
                ?? await jurisdictions.CreateAsync(new JurisdictionInput("Kansas", "KS", "US", "America/Chicago", "https://ksoutdoors.com"), ct);

            var kdwp = (await agencies.ListAsync(kansas.Id, false, ct)).FirstOrDefault(a => a.Name.Contains("Wildlife", StringComparison.OrdinalIgnoreCase))
                ?? await agencies.CreateAsync(new AgencyInput(kansas.Id, "Kansas Department of Wildlife and Parks", "KDWP", "https://ksoutdoors.com"), ct);

            var antelope = (await programs.ListAsync(new ProgramFilter(AgencyId: kdwp.Id), ct)).FirstOrDefault(p => p.Name == "Resident Antelope")
                ?? await programs.CreateAsync(new ProgramInput(kdwp.Id, "Resident Antelope", Species: "Antelope", Method: "Firearm/Muzzleloader", Residency: "Resident", PermitType: "Limited draw"), ct);

            var deer = (await programs.ListAsync(new ProgramFilter(AgencyId: kdwp.Id), ct)).FirstOrDefault(p => p.Name == "Resident Either-Species Deer")
                ?? await programs.CreateAsync(new ProgramInput(kdwp.Id, "Resident Either-Species Deer", Species: "Deer", Method: "Any legal equipment", Residency: "Resident", PermitType: "Limited draw"), ct);

            // History: two unsuccessful applications, then two preference points.
            await SeasonAsync(antelope.Id, 2023, "application-period", "Apply for antelope permit", ActionResolution.Completed, "Applied – not drawn");
            await SeasonAsync(antelope.Id, 2024, "application-period", "Apply for antelope permit", ActionResolution.Completed, "Applied – not drawn");
            await SeasonAsync(antelope.Id, 2025, "preference-point-period", "Buy preference point", ActionResolution.Completed, "Preference point purchased");
            await SeasonAsync(antelope.Id, 2026, "preference-point-period", "Buy preference point", ActionResolution.Completed, "Preference point purchased");

            // Upcoming: 2027 application window and draw results (single date).
            await SeasonAsync(antelope.Id, 2027, "application-period", "Apply or buy preference point", null, null);
            await EnsureEventAsync(new EventInput(antelope.Id, "draw-results", 2027, "2027-06-25",
                Description: "Draw results posted to the KDWP license portal.", SourceUrl: "https://ksoutdoors.com"));

            // Something open right now so the dashboard has an action: a short purchase window around today.
            var today = DateTime.UtcNow.Date;
            await EnsureEventAsync(new EventInput(deer.Id, "license-purchase", today.Year, Iso(today.AddDays(-5)), EndDate: Iso(today.AddDays(9)),
                Description: "Leftover either-species permits (development sample data).",
                Actions: [new ActionInput("Buy leftover deer permit")]));
        }

        private async Task SeasonAsync(Guid programId, int year, string eventType, string actionTitle, ActionResolution? resolution, string? outcome)
        {
            var programEvent = await EnsureEventAsync(new EventInput(programId, eventType, year, $"{year}-05-12", EndDate: $"{year}-06-12",
                SourceUrl: "https://ksoutdoors.com", Verified: year < 2027, Actions: [new ActionInput(actionTitle)]));

            var action = programEvent.Actions.FirstOrDefault(a => !a.IsArchived);
            if (action is null)
            {
                action = await actions.CreateAsync(programEvent.Id, new ActionInput(actionTitle), ct);
            }

            if (resolution is { } decided && !ActionStatusResolver.IsResolved(action.Status))
            {
                await actions.ChangeStatusAsync(action.Id, new StatusChangeInput(decided.ToString(), outcome), ct);
                await output.WriteLineAsync($"  {year} {eventType}: {decided} ({outcome})");
            }
        }

        private async Task<EventDto> EnsureEventAsync(EventInput input)
        {
            var existing = (await events.ListAsync(new EventFilter(input.ProgramId, SeasonYear: input.SeasonYear, EventTypeKey: input.EventTypeKey), ct)).Items
                .FirstOrDefault(e => e.Qualifier == (input.Qualifier ?? ""));
            if (existing is not null)
            {
                return existing;
            }

            var created = await events.CreateAsync(input, ct);
            await output.WriteLineAsync($"  created {created.Name}");
            return created;
        }

        private static string Iso(DateTime date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }
}
