using HuntOps.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HuntOps.Api.Endpoints;

internal static class EndpointConventions
{
    /// <summary>Marks an endpoint as a write operation: requires the 'write' scope and documents the error shapes.</summary>
    public static RouteHandlerBuilder WriteOperation(this RouteHandlerBuilder builder) =>
        builder
            .RequireAuthorization(ApiPolicies.Write)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

    public static RouteHandlerBuilder WithNotFound(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status404NotFound);
}
