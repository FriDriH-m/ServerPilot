using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ServerPilot.Api.Http;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

public sealed class ApiConventionsTests : IDisposable
{
    private const string SuppliedCorrelationId = "integration-test-correlation";

    private readonly TestLogProvider logProvider = new();
    private readonly ApiConventionsApiFactory factory;
    private readonly HttpClient client;

    public ApiConventionsTests()
    {
        factory = new ApiConventionsApiFactory(logProvider);
        client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            CorrelationIdMiddleware.HeaderName,
            SuppliedCorrelationId);
    }

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
        logProvider.Dispose();
    }

    [Fact]
    public async Task InvalidRequestReturnsValidationProblemDetails()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/_test/api-conventions/validation",
            new { name = string.Empty },
            CancellationToken.None);

        using JsonDocument problem = await AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "/_test/api-conventions/validation");

        Assert.Equal(
            "One or more validation errors occurred.",
            problem.RootElement.GetProperty("title").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("Name", out _));
    }

    [Theory]
    [InlineData("not-found", HttpStatusCode.NotFound, "Not Found")]
    [InlineData("conflict", HttpStatusCode.Conflict, "Conflict")]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized, "Unauthorized")]
    [InlineData("forbidden", HttpStatusCode.Forbidden, "Forbidden")]
    public async Task ExpectedClientErrorsReturnProblemDetails(
        string route,
        HttpStatusCode expectedStatus,
        string expectedTitle)
    {
        string path = $"/_test/api-conventions/{route}";

        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken.None);
        using JsonDocument problem = await AssertProblemDetailsAsync(
            response,
            expectedStatus,
            path);

        Assert.Equal(expectedTitle, problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnexpectedExceptionReturnsSafeProblemDetailsAndIsLogged()
    {
        const string path = "/_test/api-conventions/unexpected";

        using HttpResponseMessage response = await client.GetAsync(path, CancellationToken.None);
        string payload = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using JsonDocument problem = JsonDocument.Parse(payload);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(
            StatusCodes.Status500InternalServerError,
            problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(path, problem.RootElement.GetProperty("instance").GetString());
        Assert.Equal(
            SuppliedCorrelationId,
            problem.RootElement.GetProperty("correlationId").GetString());
        Assert.DoesNotContain("Sensitive internal failure", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            logProvider.Entries,
            entry => entry.Level == LogLevel.Error &&
                entry.Exception is InvalidOperationException &&
                entry.CorrelationId == SuppliedCorrelationId);
    }

    [Fact]
    public async Task MissingCorrelationIdIsGeneratedAndReturned()
    {
        using HttpClient clientWithoutCorrelationId = factory.CreateClient();

        using HttpResponseMessage response = await clientWithoutCorrelationId.GetAsync(
            "/_test/api-conventions/not-found",
            CancellationToken.None);
        using JsonDocument problem = await ReadProblemDetailsAsync(response);

        string responseCorrelationId = Assert.Single(
            response.Headers.GetValues(CorrelationIdMiddleware.HeaderName));
        Assert.Matches("^[a-f0-9]{32}$", responseCorrelationId);
        Assert.Equal(
            responseCorrelationId,
            problem.RootElement.GetProperty("correlationId").GetString());
    }

    private async Task<JsonDocument> AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedInstance)
    {
        Assert.True(
            expectedStatus == response.StatusCode,
            string.Join(
                Environment.NewLine,
                logProvider.Entries.Select(entry =>
                    $"{entry.Level} {entry.CategoryName}: {entry.Exception}")));
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            SuppliedCorrelationId,
            Assert.Single(response.Headers.GetValues(CorrelationIdMiddleware.HeaderName)));

        JsonDocument problem = await ReadProblemDetailsAsync(response);
        Assert.Equal((int)expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedInstance, problem.RootElement.GetProperty("instance").GetString());
        Assert.Equal(
            SuppliedCorrelationId,
            problem.RootElement.GetProperty("correlationId").GetString());

        return problem;
    }

    private static async Task<JsonDocument> ReadProblemDetailsAsync(HttpResponseMessage response)
    {
        Stream content = await response.Content.ReadAsStreamAsync(CancellationToken.None);
        return await JsonDocument.ParseAsync(content, cancellationToken: CancellationToken.None);
    }
}
