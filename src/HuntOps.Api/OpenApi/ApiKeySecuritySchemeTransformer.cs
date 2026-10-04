using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HuntOps.Api.OpenApi;

/// <summary>Documents API-key bearer authentication so Swagger UI can send it ("Authorize" button).</summary>
internal sealed class ApiKeySecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeId = "ApiKey";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "HuntOps API",
            Version = "v1",
            Description =
                "Hunting applications, permits, points, deadlines and season operations.\n\n" +
                "Authenticate with a HuntOps API key: `Authorization: Bearer hops_...`. " +
                "Read-only keys can call GET endpoints; write operations need a key with the `write` scope.\n\n" +
                "Dates are `yyyy-MM-dd`, times are 24-hour `HH:mm`, time zones are IANA ids (e.g. `America/Chicago`), " +
                "and instants are ISO-8601 UTC (`2027-06-13T04:59:59.999Z`).",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "hops_<id>_<secret>",
            Description = "HuntOps API key. Create one with: dotnet HuntOps.Worker.dll apikey create --name <name> --scope read|write",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeId, document)] = [],
        });

        return Task.CompletedTask;
    }
}
