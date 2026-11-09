using Microsoft.AspNetCore.Mvc.Controllers;

namespace CargoHub.Api.Authentication;

/// <summary>
/// Checks the API_KEY header on every /api request: 401 when the key is missing or
/// unknown, 403 when the user may not use this method on the requested resource.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
{
    public const string HeaderName = "API_KEY";

    public async Task InvokeAsync(HttpContext context, ApiUsersService apiUsersService)
    {
        // Routing errors (404/405) must reach the routing response instead of
        // being mistaken for an authentication or permission failure.
        if (!context.Request.Path.StartsWithSegments("/api") ||
            context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
        {
            await next(context);
            return;
        }

        var apiKey = context.Request.Headers[HeaderName].ToString();
        var user = string.IsNullOrEmpty(apiKey)
            ? null
            : await apiUsersService.GetByApiKeyAsync(apiKey, context.RequestAborted);

        if (user is null)
        {
            await Errors.ApiProblemDetails.WriteAsync(context, StatusCodes.Status401Unauthorized,
                "Missing or invalid API key.");
            return;
        }

        var resource = GetResource(context.Request.Path);
        var method = GetPermissionMethod(context.Request.Method);
        if (resource is null || method is null || !user.HasAccess(resource, method))
        {
            logger.LogWarning("{App} denied {Method} {Path}",
                user.App, context.Request.Method, context.Request.Path);
            await Errors.ApiProblemDetails.WriteAsync(context, StatusCodes.Status403Forbidden,
                "This API key has no access to this endpoint.");
            return;
        }

        context.Items[typeof(ApiUser)] = user;
        await next(context);
    }

	// api/v1/{resource}/... -> resource
    private static string? GetResource(PathString path)
    {
        var segments = path.Value!.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 3 && segments[1] == "v1" ? segments[2] : null;
    }

	// Unknown methods map to null so they are always denied
    private static string? GetPermissionMethod(string method) => method switch
    {
        _ when HttpMethods.IsGet(method) || HttpMethods.IsHead(method) => "get",
        _ when HttpMethods.IsPost(method) => "post",
        _ when HttpMethods.IsPut(method) => "put",
        _ when HttpMethods.IsDelete(method) => "delete",
        _ => null
    };
}
