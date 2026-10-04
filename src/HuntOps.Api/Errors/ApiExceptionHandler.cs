using System.Text.Json;
using HuntOps.Application.Abstractions;
using HuntOps.Application.Common;
using HuntOps.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HuntOps.Api.Errors;

/// <summary>
/// Converts exceptions on /api requests into RFC 9457 problem details. Messages are written for API users;
/// stack traces, exception types, SQL and connection details are never included.
/// </summary>
internal sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetails,
    IDatabaseErrorClassifier databaseErrors,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Dictionary<string, string> ConstraintMessages = new(StringComparer.Ordinal)
    {
        ["ux_jurisdictions_code_active"] = "An active jurisdiction with this code already exists.",
        ["ux_jurisdictions_name_active"] = "An active jurisdiction with this name already exists.",
        ["ux_agencies_jurisdiction_name_active"] = "An active agency with this name already exists in the jurisdiction.",
        ["ux_programs_agency_name_active"] = "An active program with this name already exists for the agency.",
        ["ux_programs_slug_active"] = "An active program with this slug already exists.",
        ["ux_program_events_natural_key_active"] = "An active event with the same program, season year, event type and qualifier already exists.",
        ["pk_event_types"] = "An event type with this key already exists.",
    };

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var problem = Map(exception);
        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = null, // never let the writer attach exception details
        });
    }

    private ProblemDetails Map(Exception exception)
    {
        switch (exception)
        {
            case RequestValidationException validation:
                return Validation(validation.Errors);
            case DomainValidationException domain:
                return Validation(domain.Errors);
            case NotFoundException notFound:
                return Problem(StatusCodes.Status404NotFound, "Not found", notFound.Message);
            case ConflictException conflict:
                return Problem(StatusCodes.Status409Conflict, "Conflict", conflict.Message);
            case BadHttpRequestException badRequest:
                return BadRequest(badRequest);
        }

        if (databaseErrors.Classify(exception) is { } dbError)
        {
            var constraintMessage = dbError.Constraint is { } name && ConstraintMessages.TryGetValue(name, out var m) ? m : null;
            return dbError.Kind switch
            {
                DatabaseErrorKind.ConcurrencyConflict => Problem(StatusCodes.Status409Conflict, "Concurrent modification",
                    "The resource was changed by someone else. Reload it and retry with the current version."),
                DatabaseErrorKind.UniqueViolation => Problem(StatusCodes.Status409Conflict, "Conflict",
                    constraintMessage ?? "A record with the same unique values already exists."),
                DatabaseErrorKind.CheckViolation => Problem(StatusCodes.Status400BadRequest, "Invalid data",
                    $"The data violates a database rule ({dbError.Constraint ?? "check constraint"})."),
                DatabaseErrorKind.ForeignKeyViolation or DatabaseErrorKind.RestrictViolation => Problem(StatusCodes.Status409Conflict, "Conflict",
                    "The operation would break a reference between records."),
                _ => ServerError(),
            };
        }

        return ServerError();
    }

    private static ProblemDetails BadRequest(BadHttpRequestException exception)
    {
        if (exception.InnerException is JsonException json)
        {
            var path = string.IsNullOrEmpty(json.Path) ? "$" : json.Path;
            return Validation(new Dictionary<string, string[]>
            {
                [path] = ["The value is not valid JSON for this field (check its type and format)."],
            }, "The request body could not be read.");
        }

        var detail = exception.StatusCode == StatusCodes.Status415UnsupportedMediaType
            ? "Send the request body as application/json."
            : "The request is malformed. Check required route/query values and that the body is valid JSON.";
        return Problem(exception.StatusCode, "Bad request", detail);
    }

    private static HttpValidationProblemDetails Validation(IReadOnlyDictionary<string, string[]> errors, string? detail = null) =>
        new(errors.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Detail = detail,
        };

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static ProblemDetails ServerError() =>
        Problem(StatusCodes.Status500InternalServerError, "Unexpected error",
            "An unexpected error occurred. The details were logged on the server; quote the traceId when reporting it.");

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);
}
