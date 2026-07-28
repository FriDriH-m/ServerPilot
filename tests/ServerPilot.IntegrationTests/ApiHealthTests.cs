using System.Net;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class ApiHealthTests : IAsyncLifetime, IDisposable
{
    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;

    public ApiHealthTests(PostgreSqlDatabaseFixture database)
    {
        factory = new ServerPilotApiFactory(database.ConnectionString);
        client = factory.CreateClient();
    }

    public Task InitializeAsync() => factory.ResetDatabaseAsync(CancellationToken.None);

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        client.Dispose();
        factory.Dispose();
    }

    [Fact]
    public async Task LivenessAndReadinessEndpointsReturnOkForCurrentDatabase()
    {
        using HttpResponseMessage compatibilityResponse = await client.GetAsync(
            "/health",
            CancellationToken.None);
        using HttpResponseMessage livenessResponse = await client.GetAsync(
            "/health/live",
            CancellationToken.None);
        using HttpResponseMessage readinessResponse = await client.GetAsync(
            "/health/ready",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, compatibilityResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, livenessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, readinessResponse.StatusCode);
    }

    [Fact]
    public async Task ReadinessFailsButLivenessSurvivesWhenDatabaseIsUnavailable()
    {
        await factory.DeleteDatabaseAsync(CancellationToken.None);

        using HttpResponseMessage livenessResponse = await client.GetAsync(
            "/health/live",
            CancellationToken.None);
        using HttpResponseMessage readinessResponse = await client.GetAsync(
            "/health/ready",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, livenessResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readinessResponse.StatusCode);
    }
}
