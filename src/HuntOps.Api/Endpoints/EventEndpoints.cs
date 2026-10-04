using HuntOps.Application.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HuntOps.Api.Endpoints;

/// <summary>Events, required actions, action status and action items. Thin adapters over the application services.</summary>
internal static class EventEndpoints
{
    public static void MapEvents(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/events").WithTags("Events");

        group.MapGet("/", (Guid? programId, Guid? agencyId, Guid? jurisdictionId, string? species, int? seasonYear,
                    string? eventType, string? from, string? to, bool? includeArchived, int? skip, int? take,
                    ProgramEventService service, CancellationToken ct) =>
                service.ListAsync(
                    new EventFilter(programId, agencyId, jurisdictionId, species, seasonYear, eventType, from, to, includeArchived ?? false, skip, take),
                    ct))
            .WithName("ListEvents")
            .WithSummary("List events (paged; from/to are yyyy-MM-dd and match events overlapping the range)")
            .ProducesValidationProblem();

        group.MapGet("/upcoming", (int? days, Guid? programId, Guid? agencyId, Guid? jurisdictionId, string? species,
                    int? seasonYear, string? eventType, ProgramEventService service, CancellationToken ct) =>
                service.UpcomingAsync(days, new EventFilter(programId, agencyId, jurisdictionId, species, seasonYear, eventType), ct))
            .WithName("ListUpcomingEvents")
            .WithSummary("Events starting within the next N days (default 60) plus windows open now")
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", (Guid id, ProgramEventService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetEvent").WithSummary("Get an event with its actions and their status").WithNotFound();

        group.MapPost("/", async (EventInput input, ProgramEventService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(input, ct);
                return TypedResults.Created($"/api/events/{created.Id}", created);
            })
            .WithName("CreateEvent")
            .WithSummary("Create an event (optionally with actions). Time zone defaults to the jurisdiction's.")
            .WriteOperation();

        group.MapPut("/{id:guid}", (Guid id, EventInput input, ProgramEventService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdateEvent")
            .WithSummary("Update an event (send the current version). Changing dates clears verification unless verified=true.")
            .WriteOperation().WithNotFound();

        group.MapDelete("/{id:guid}", (Guid id, ProgramEventService service, CancellationToken ct) => service.ArchiveAsync(id, ct))
            .WithName("ArchiveEvent").WithSummary("Archive (soft-delete) an event; action history is kept").WriteOperation().WithNotFound();

        group.MapPost("/{id:guid}/restore", (Guid id, ProgramEventService service, CancellationToken ct) => service.RestoreAsync(id, ct))
            .WithName("RestoreEvent").WithSummary("Restore an archived event").WriteOperation().WithNotFound();

        group.MapPost("/{id:guid}/verify", (Guid id, ProgramEventService service, CancellationToken ct) => service.VerifyAsync(id, ct))
            .WithName("VerifyEvent").WithSummary("Confirm the event's dates are correct (human verification)").WriteOperation().WithNotFound();

        group.MapPost("/{id:guid}/complete", (Guid id, CompleteEventInput? input, ActionService service, CancellationToken ct) =>
                service.CompleteEventAsync(id, input, ct))
            .WithName("CompleteEvent")
            .WithSummary("Mark the event's action completed (actionId required when the event has several)")
            .WriteOperation().WithNotFound();

        group.MapGet("/{id:guid}/actions", (Guid id, bool? includeArchived, ActionService service, CancellationToken ct) =>
                service.ListForEventAsync(id, includeArchived ?? false, ct))
            .WithName("ListEventActions").WithSummary("List an event's required actions").WithNotFound();

        group.MapPost("/{id:guid}/actions", async (Guid id, ActionInput input, ActionService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(id, input, ct);
                return TypedResults.Created($"/api/actions/{created.Id}", created);
            })
            .WithName("CreateEventAction").WithSummary("Add a required action to an event").WriteOperation().WithNotFound();
    }

    public static void MapActions(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/actions").WithTags("Actions");

        group.MapGet("/{id:guid}", (Guid id, ActionService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetAction").WithSummary("Get an action and its effective status").WithNotFound();

        group.MapPut("/{id:guid}", (Guid id, ActionInput input, ActionService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdateAction").WithSummary("Update an action (send the current version)").WriteOperation().WithNotFound();

        group.MapDelete("/{id:guid}", (Guid id, ActionService service, CancellationToken ct) => service.ArchiveAsync(id, ct))
            .WithName("ArchiveAction").WithSummary("Archive an action; its status history is kept").WriteOperation().WithNotFound();

        group.MapPost("/{id:guid}/restore", (Guid id, ActionService service, CancellationToken ct) => service.RestoreAsync(id, ct))
            .WithName("RestoreAction").WithSummary("Restore an archived action").WriteOperation().WithNotFound();

        group.MapPost("/{id:guid}/status", (Guid id, StatusChangeInput input, ActionService service, CancellationToken ct) =>
                service.ChangeStatusAsync(id, input, ct))
            .WithName("ChangeActionStatus")
            .WithSummary("Record a decision: completed, notApplicable, cancelled or reopened (appended to history)")
            .WriteOperation().WithNotFound();

        group.MapGet("/{id:guid}/history", (Guid id, ActionService service, CancellationToken ct) => service.HistoryAsync(id, ct))
            .WithName("GetActionHistory").WithSummary("The action's status history, newest first").WithNotFound();

        api.MapGet("/action-items", (int? days, bool? includeResolved, ActionService service, CancellationToken ct) =>
                service.ActionItemsAsync(days, includeResolved ?? false, ct))
            .WithTags("Actions")
            .WithName("ListActionItems")
            .WithSummary("What needs attention: actions due within N days (default 60), open now, or recently missed")
            .ProducesValidationProblem();
    }
}
