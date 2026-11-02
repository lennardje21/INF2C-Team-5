using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CargoHub.Api.Authentication;

/// <summary>
/// Adds the API_KEY header to the OpenAPI document, so the docs page can send it
/// with every "Try it" request.
/// </summary>
public sealed class ApiKeyDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[ApiKeyMiddleware.HeaderName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyMiddleware.HeaderName
        };

        document.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(ApiKeyMiddleware.HeaderName, document)] = []
            }
        ];
        return Task.CompletedTask;
    }
}
