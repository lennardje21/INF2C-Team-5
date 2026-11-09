using System.Diagnostics;
using System.Text.Json;
using CargoHub.Api.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CargoHub.Tests.Unit;

public sealed class ApiProblemDetailsTests
{
    [Theory]
    [InlineData(400, "request body")]
    [InlineData(401, "API_KEY")]
    [InlineData(403, "permission")]
    [InlineData(405, "Allow")]
    [InlineData(409, "conflicts")]
    [InlineData(415, "application/json")]
    [InlineData(500, "traceId")]
    [InlineData(503, "traceId")]
    public void Customize_DescribesHowToHandleTheError(int status, string expectedHint)
    {
        var context = CreateContext(status);

        ApiProblemDetails.Customize(context);

        Assert.Contains(expectedHint, context.ProblemDetails.Detail);
        Assert.Equal(status, context.ProblemDetails.Status);
        Assert.Equal("/api/v2/suppliers/42", context.ProblemDetails.Instance);
    }

    [Fact]
    public void UnmatchedPath_IncludesMethodPathAndApiVersionHint()
    {
        var context = CreateContext(404);
        context.HttpContext.Request.Method = "POST";
        context.HttpContext.Request.Path = "/api/v2/unknown";

        ApiProblemDetails.Customize(context);

        Assert.Contains("POST /api/v2/unknown", context.ProblemDetails.Detail);
        Assert.Contains("API version", context.ProblemDetails.Detail);
    }

    [Fact]
    public void MissingResource_OnMatchedRoute_DescribesTheMissingIdentifier()
    {
        var context = CreateContext(404);
        context.HttpContext.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            EndpointMetadataCollection.Empty, "Supplier endpoint"));

        ApiProblemDetails.Customize(context);

        Assert.Contains("/api/v2/suppliers/42", context.ProblemDetails.Detail);
        Assert.Contains("identifiers", context.ProblemDetails.Detail);
        Assert.DoesNotContain("No endpoint", context.ProblemDetails.Detail);
    }

    [Fact]
    public void Customize_PreservesSpecificBusinessAndValidationDetails()
    {
        var errors = new Dictionary<string, string[]> { ["Name"] = ["Name is required."] };
        var context = CreateContext(400);
        context.ProblemDetails = new ValidationProblemDetails(errors)
        {
            Status = 400,
            Title = "Invalid supplier",
            Detail = "Supplier name is required.",
            Instance = "/original-request"
        };

        ApiProblemDetails.Customize(context);

        var problem = Assert.IsType<ValidationProblemDetails>(context.ProblemDetails);
        Assert.Equal("Invalid supplier", problem.Title);
        Assert.Equal("Supplier name is required.", problem.Detail);
        Assert.Equal("/original-request", problem.Instance);
        Assert.Equal("Name is required.", Assert.Single(problem.Errors["Name"]));
    }

    [Fact]
    public void Customize_UsesTheActiveActivityForLogCorrelation()
    {
        using var activity = new Activity("error-response-test").Start();
        var context = CreateContext(500);

        ApiProblemDetails.Customize(context);

        Assert.Equal(activity.Id, context.ProblemDetails.Extensions["traceId"]);
    }

    [Fact]
    public void Customize_WithoutActivity_UsesTheRequestTraceIdentifier()
    {
        var previous = Activity.Current;
        try
        {
            Activity.Current = null;
            var context = CreateContext(500);
            context.HttpContext.TraceIdentifier = "request-123";

            ApiProblemDetails.Customize(context);

            Assert.Equal("request-123", context.ProblemDetails.Extensions["traceId"]);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Theory]
    [InlineData("application/json", null, "Not Found")]
    [InlineData("text/html", null, "Not Found")]
    [InlineData("application/xml", "Unknown API route", "Unknown API route")]
    public async Task WriteAsync_ReturnsCompleteJsonRegardlessOfAcceptHeader(
        string accept, string? title, string expectedTitle)
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Path = "/api/v2/unknown";
        httpContext.Request.Method = "GET";
        httpContext.Request.Headers.Accept = accept;
        await using var body = new MemoryStream();
        httpContext.Response.Body = body;

        await ApiProblemDetails.WriteAsync(httpContext, 404, title);

        Assert.Equal(404, httpContext.Response.StatusCode);
        Assert.StartsWith("application/problem+json", httpContext.Response.ContentType);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, json.RootElement.GetProperty("title").GetString());
        Assert.Equal("/api/v2/unknown", json.RootElement.GetProperty("instance").GetString());
        Assert.Contains("GET /api/v2/unknown", json.RootElement.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    private static ProblemDetailsContext CreateContext(int status)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/v2/suppliers/42";
        return new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status }
        };
    }
}
