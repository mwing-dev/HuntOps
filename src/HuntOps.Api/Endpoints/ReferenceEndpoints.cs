using HuntOps.Application.Reference;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HuntOps.Api.Endpoints;

/// <summary>Jurisdictions, agencies, programs and event types. Thin adapters over the application services.</summary>
internal static class ReferenceEndpoints
{
    public static void MapJurisdictions(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/jurisdictions").WithTags("Jurisdictions");

        group.MapGet("/", (bool? includeArchived, JurisdictionService service, CancellationToken ct) =>
                service.ListAsync(includeArchived ?? false, ct))
            .WithName("ListJurisdictions").WithSummary("List jurisdictions");

        group.MapGet("/{id:guid}", (Guid id, JurisdictionService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetJurisdiction").WithSummary("Get a jurisdiction").WithNotFound();

        group.MapPost("/", async (JurisdictionInput input, JurisdictionService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(input, ct);
                return TypedResults.Created($"/api/jurisdictions/{created.Id}", created);
            })
            .WithName("CreateJurisdiction").WithSummary("Create a jurisdiction").WriteOperation();

        group.MapPut("/{id:guid}", (Guid id, JurisdictionInput input, JurisdictionService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdateJurisdiction").WithSummary("Update a jurisdiction (send the current version)").WriteOperation().WithNotFound();

        group.MapDelete("/{id:guid}", (Guid id, JurisdictionService service, CancellationToken ct) => service.ArchiveAsync(id, ct))
            .WithName("ArchiveJurisdiction").WithSummary("Archive (soft-delete) a jurisdiction").WriteOperation().WithNotFound();

        group.MapPost("/{id:guid}/restore", (Guid id, JurisdictionService service, CancellationToken ct) => service.RestoreAsync(id, ct))
            .WithName("RestoreJurisdiction").WithSummary("Restore an archived jurisdiction").WriteOperation().WithNotFound();
    }

    public static void MapAgencies(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/agencies").WithTags("Agencies");

        group.MapGet("/", (Guid? jurisdictionId, bool? includeArchived, AgencyService service, CancellationToken ct) =>
                service.ListAsync(jurisdictionId, includeArchived ?? false, ct))
            .WithName("ListAgencies").WithSummary("List agencies");

        group.MapGet("/{id:guid}", (Guid id, AgencyService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetAgency").WithSummary("Get an agency").WithNotFound();

        group.MapPost("/", async (AgencyInput input, AgencyService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(input, ct);
                return TypedResults.Created($"/api/agencies/{created.Id}", created);
            })
            .WithName("CreateAgency").WithSummary("Create an agency").WriteOperation();

        group.MapPut("/{id:guid}", (Guid id, AgencyInput input, AgencyService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdateAgency").WithSummary("Update an agency (send the current version)").WriteOperation().WithNotFound();

        group.MapDelete("/{id:guid}", (Guid id, AgencyService service, CancellationToken ct) => service.ArchiveAsync(id, ct))
            .WithName("ArchiveAgency").WithSummary("Archive (soft-delete) an agency").WriteOperation().WithNotFound();

        group.MapPost("/{id:guid}/restore", (Guid id, AgencyService service, CancellationToken ct) => service.RestoreAsync(id, ct))
            .WithName("RestoreAgency").WithSummary("Restore an archived agency").WriteOperation().WithNotFound();
    }

    public static void MapPrograms(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/programs").WithTags("Programs");

        group.MapGet("/", (Guid? jurisdictionId, Guid? agencyId, string? species, string? q, bool? includeArchived,
                    ProgramService service, CancellationToken ct) =>
                service.ListAsync(new ProgramFilter(jurisdictionId, agencyId, species, q, includeArchived ?? false), ct))
            .WithName("ListPrograms").WithSummary("List programs (filter by jurisdiction, agency, species or text)");

        group.MapGet("/{id:guid}", (Guid id, ProgramService service, CancellationToken ct) => service.GetAsync(id, ct))
            .WithName("GetProgram").WithSummary("Get a program").WithNotFound();

        group.MapGet("/{id:guid}/history", (Guid id, ProgramService service, Application.Common.ICurrentActor actor, CancellationToken ct) =>
                service.GetHistoryAsync(id, actor.UserId, ct))
            .WithName("GetProgramHistory")
            .WithSummary("Events and action outcomes by season year, including umbrella-program events")
            .WithNotFound();

        group.MapPost("/", async (ProgramInput input, ProgramService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(input, ct);
                return TypedResults.Created($"/api/programs/{created.Id}", created);
            })
            .WithName("CreateProgram").WithSummary("Create a program").WriteOperation();

        group.MapPut("/{id:guid}", (Guid id, ProgramInput input, ProgramService service, CancellationToken ct) =>
                service.UpdateAsync(id, input, ct))
            .WithName("UpdateProgram").WithSummary("Update a program (send the current version)").WriteOperation().WithNotFound();

        group.MapDelete("/{id:guid}", (Guid id, ProgramService service, CancellationToken ct) => service.ArchiveAsync(id, ct))
            .WithName("ArchiveProgram").WithSummary("Archive (soft-delete) a program; its events and history are kept").WriteOperation().WithNotFound();

        group.MapPost("/{id:guid}/restore", (Guid id, ProgramService service, CancellationToken ct) => service.RestoreAsync(id, ct))
            .WithName("RestoreProgram").WithSummary("Restore an archived program").WriteOperation().WithNotFound();
    }

    public static void MapEventTypes(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/event-types").WithTags("Event types");

        group.MapGet("/", (bool? includeArchived, EventTypeService service, CancellationToken ct) =>
                service.ListAsync(includeArchived ?? false, ct))
            .WithName("ListEventTypes").WithSummary("List event types");

        group.MapGet("/{key}", (string key, EventTypeService service, CancellationToken ct) => service.GetAsync(key, ct))
            .WithName("GetEventType").WithSummary("Get an event type").WithNotFound();

        group.MapPost("/", async (EventTypeInput input, EventTypeService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(input, ct);
                return TypedResults.Created($"/api/event-types/{created.Key}", created);
            })
            .WithName("CreateEventType").WithSummary("Add a custom event type").WriteOperation();

        group.MapPut("/{key}", (string key, EventTypeInput input, EventTypeService service, CancellationToken ct) =>
                service.UpdateAsync(key, input, ct))
            .WithName("UpdateEventType").WithSummary("Update an event type's display fields (key is permanent)").WriteOperation().WithNotFound();

        group.MapDelete("/{key}", (string key, EventTypeService service, CancellationToken ct) => service.ArchiveAsync(key, ct))
            .WithName("ArchiveEventType").WithSummary("Archive an event type (existing events keep it)").WriteOperation().WithNotFound();

        group.MapPost("/{key}/restore", (string key, EventTypeService service, CancellationToken ct) => service.RestoreAsync(key, ct))
            .WithName("RestoreEventType").WithSummary("Restore an archived event type").WriteOperation().WithNotFound();
    }
}
