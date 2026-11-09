using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CargoHub.Tests.Fixtures;
using Microsoft.AspNetCore.Mvc;

namespace CargoHub.Tests.Controllers;

[Collection("Api")]
public sealed class ErrorResponseTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("/api")]
    [InlineData("/api/v1")]
    [InlineData("/api/v2/suppliers")]
    [InlineData("/api/v1/unknown")]
    [InlineData("/api/v1/suppliers/not-an-id")]
    [InlineData("/api/v1/suppliers/1/unknown")]
    [InlineData("/unknown")]
    public async Task InvalidPath_WithoutApiKey_ReturnsDescriptiveNotFound(string path)
    {
        using var client = fixture.CreateUnauthenticatedClient();
        using var response = await client.GetAsync(path);
        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound, path);
        Assert.Contains("No endpoint matches GET", problem.Detail);
    }

    [Fact]
    public async Task InvalidPath_WithApiKey_ReturnsNotFoundInsteadOfForbidden()
    {
        const string path = "/api/v1/unknown";
        using var response = await fixture.Client.GetAsync(path);
        await AssertProblemAsync(response, HttpStatusCode.NotFound, path);
    }

    [Fact]
    public async Task UnsupportedMethod_ReturnsMethodNotAllowedWithAllowHeader()
    {
        using var client = fixture.CreateUnauthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/v1/suppliers");
        using var response = await client.SendAsync(request);
        await AssertProblemAsync(response, HttpStatusCode.MethodNotAllowed, "/api/v1/suppliers");
        Assert.Contains("GET", response.Content.Headers.Allow);
    }

    [Fact]
    public async Task InvalidPath_WithHtmlAcceptHeader_StillReturnsJsonError()
    {
        using var client = fixture.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        using var response = await client.GetAsync("/unknown");
        await AssertProblemAsync(response, HttpStatusCode.NotFound, "/unknown");
    }

    [Fact]
    public async Task MissingResource_ExplainsWhichRequestFailed()
    {
        const string path = "/api/v1/suppliers/999999";
        using var response = await fixture.Client.GetAsync(path);
        var problem = await AssertProblemAsync(response, HttpStatusCode.NotFound, path);
        Assert.Contains("identifiers", problem.Detail);
    }

    [Fact]
    public async Task MissingFields_PreservesTheServiceValidationMessage()
    {
        using var response = await fixture.Client.PostAsJsonAsync("/api/v1/suppliers", new { });
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "/api/v1/suppliers");
        Assert.Contains("Name", problem.Detail);
    }

    [Fact]
    public async Task InvalidJson_ReturnsValidationDetails()
    {
        using var content = new StringContent("{", Encoding.UTF8, "application/json");
        using var response = await fixture.Client.PostAsync("/api/v1/suppliers", content);
        var problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "/api/v1/suppliers");
        var errors = Assert.IsType<JsonElement>(problem.Extensions["errors"]);
        Assert.NotEmpty(errors.EnumerateObject());
    }

    [Fact]
    public async Task RelatedResourceConflict_PreservesTheBusinessExplanation()
    {
        var id = await fixture.QueryIntAsync("SELECT supplier_id FROM item LIMIT 1");
        var path = $"/api/v1/suppliers/{id}";
        using var response = await fixture.Client.DeleteAsync(path);
        var problem = await AssertProblemAsync(response, HttpStatusCode.Conflict, path);
        Assert.Contains("still has items", problem.Detail);
    }

    [Fact]
    public async Task MissingApiKey_ReturnsActionableAuthenticationError()
    {
        using var client = fixture.CreateUnauthenticatedClient();
        using var response = await client.GetAsync("/api/v1/suppliers");
        var problem = await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "/api/v1/suppliers");
        Assert.Contains("API_KEY", problem.Detail);
    }

    private static async Task<ProblemDetails> AssertProblemAsync(HttpResponseMessage response,
        HttpStatusCode expectedStatus, string path)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.Equal(path, problem.Instance);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        Assert.True(problem.Extensions.ContainsKey("traceId"));
        return problem;
    }
}
