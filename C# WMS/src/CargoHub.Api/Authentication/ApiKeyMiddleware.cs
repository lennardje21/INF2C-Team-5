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
        if (!context.Request.Path.StartsWithSegments("/api"))
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
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Missing or invalid API key.").ExecuteAsync(context);
            return;
        }

        var resource = GetResource(context.Request.Path);
        var method = GetPermissionMethod(context.Request.Method);
        if (resource is null || method is null || !user.HasAccess(resource, method))
        {
            logger.LogWarning("{App} denied {Method} {Path}",
                user.App, context.Request.Method, context.Request.Path);
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "This API key has no access to this endpoint.").ExecuteAsync(context);
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
