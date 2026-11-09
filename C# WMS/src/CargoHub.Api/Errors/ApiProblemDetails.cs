using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace CargoHub.Api.Errors;

/// <summary>Shared descriptions for controller, routing and middleware errors.</summary>
public static class ApiProblemDetails
{
    public static Task WriteAsync(HttpContext httpContext, int status, string? title = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title ?? ReasonPhrases.GetReasonPhrase(status)
        };
        Customize(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        httpContext.Response.StatusCode = status;
        // Error responses remain JSON even when a browser requests HTML.
        return httpContext.Response.WriteAsJsonAsync(problem, options: null,
            contentType: "application/problem+json", cancellationToken: httpContext.RequestAborted);
    }

    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var request = context.HttpContext.Request;
        problem.Instance ??= request.Path;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        problem.Detail ??= problem.Status switch
        {
            400 => "The request is invalid. Check the request body, parameter types and validation errors.",
            401 => "Provide a valid API key in the API_KEY request header.",
            403 => "This API key does not have permission to perform this operation.",
            404 when context.HttpContext.GetEndpoint() is null =>
                $"No endpoint matches {request.Method} {request.Path}. Check the URL and API version.",
            404 => $"The resource requested at '{request.Path}' was not found. Check the supplied identifiers.",
            405 => $"The method {request.Method} is not supported for '{request.Path}'. Check the Allow response header.",
            409 => "The operation conflicts with existing data or related resources.",
            415 => "The request content type is not supported. Send a JSON body with Content-Type: application/json.",
            >= 500 => "An unexpected server error occurred. Contact support with the traceId from this response.",
            _ => "The request could not be completed. Check the HTTP status code."
        };
    }
}
