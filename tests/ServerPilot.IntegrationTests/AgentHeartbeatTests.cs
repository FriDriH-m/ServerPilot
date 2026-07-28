using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Application.Agents;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class AgentHeartbeatTests : IAsyncLifetime, IDisposable
{
    private const string Password = "correct horse battery staple";
    private static readonly DateTimeOffset InitialTime =
        new(2026, 7, 28, 21, 0, 0, TimeSpan.Zero);

    private readonly MutableTimeProvider timeProvider = new(InitialTime);
    private readonly TestLogProvider logProvider = new();
    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;

    public AgentHeartbeatTests(PostgreSqlDatabaseFixture database)
    {
        factory = new ServerPilotApiFactory(
            database.ConnectionString,
            logProvider,
            timeProvider);
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
    public async Task HeartbeatRequiresAgentAuthenticationAndUpdatesOnlyAuthenticatedAgent()
    {
        AuthenticationResponse owner = await RegisterUserAsync("heartbeat-owner@example.com");
        RegisteredAgent firstAgent = await RegisterAgentAsync(owner.AccessToken, "First Agent");
        RegisteredAgent secondAgent = await RegisterAgentAsync(owner.AccessToken, "Second Agent");

        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage anonymousResponse = await client.PostAsync(
            $"/api/agents/{firstAgent.AgentId}/heartbeat",
            null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage userResponse = await client.PostAsync(
            $"/api/agents/{firstAgent.AgentId}/heartbeat",
            null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, userResponse.StatusCode);

        AuthorizeAgent(firstAgent.Credential);
        using HttpResponseMessage foreignResponse = await client.PostAsync(
            $"/api/agents/{secondAgent.AgentId}/heartbeat",
            null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);

        using HttpResponseMessage ownResponse = await client.PostAsync(
            $"/api/agents/{firstAgent.AgentId}/heartbeat",
            null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, ownResponse.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        DateTimeOffset? firstLastSeen = await dbContext.Agents
            .Where(agent => agent.Id == firstAgent.AgentId)
            .Select(agent => agent.LastSeenAt)
            .SingleAsync(CancellationToken.None);
        DateTimeOffset? secondLastSeen = await dbContext.Agents
            .Where(agent => agent.Id == secondAgent.AgentId)
            .Select(agent => agent.LastSeenAt)
            .SingleAsync(CancellationToken.None);

        Assert.Equal(InitialTime, firstLastSeen);
        Assert.Null(secondLastSeen);
        Assert.Contains(logProvider.Entries, entry =>
            entry.Message.Contains(firstAgent.AgentId.ToString(), StringComparison.Ordinal) &&
            entry.CorrelationId is not null);
        Assert.DoesNotContain(logProvider.Entries, entry =>
            entry.Message.Contains(firstAgent.Credential, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UserQueriesReturnOnlyOwnedSafeAgentMetadata()
    {
        AuthenticationResponse owner = await RegisterUserAsync("query-owner@example.com");
        RegisteredAgent ownedAgent = await RegisterAgentAsync(owner.AccessToken, "Owned Agent");
        AuthenticationResponse otherUser = await RegisterUserAsync("query-other@example.com");
        RegisteredAgent foreignAgent = await RegisterAgentAsync(
            otherUser.AccessToken,
            "Foreign Agent");

        AuthorizeAgent(ownedAgent.Credential);
        using HttpResponseMessage agentCredentialResponse = await client.GetAsync(
            "/api/agents",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, agentCredentialResponse.StatusCode);

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage listResponse = await client.GetAsync(
            "/api/agents",
            CancellationToken.None);
        string listPayload = await listResponse.Content.ReadAsStringAsync(
            CancellationToken.None);
        AgentResponse[] agents = JsonSerializer.Deserialize<AgentResponse[]>(
            listPayload,
            JsonSerializerOptions.Web)!;

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        AgentResponse listedAgent = Assert.Single(agents);
        Assert.Equal(ownedAgent.AgentId, listedAgent.Id);
        Assert.Equal("Owned Agent", listedAgent.Name);
        Assert.Null(listedAgent.LastSeenAt);
        Assert.Equal("Offline", listedAgent.Status);
        Assert.DoesNotContain("credential", listPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ownedAgent.Credential, listPayload, StringComparison.Ordinal);

        using HttpResponseMessage ownedResponse = await client.GetAsync(
            $"/api/agents/{ownedAgent.AgentId}",
            CancellationToken.None);
        AgentResponse owned =
            (await ownedResponse.Content.ReadFromJsonAsync<AgentResponse>())!;
        Assert.Equal(HttpStatusCode.OK, ownedResponse.StatusCode);
        Assert.Equal(ownedAgent.AgentId, owned.Id);

        using HttpResponseMessage foreignResponse = await client.GetAsync(
            $"/api/agents/{foreignAgent.AgentId}",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);

        using HttpResponseMessage invalidPageResponse = await client.GetAsync(
            "/api/agents?limit=101",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageResponse.StatusCode);
    }

    [Fact]
    public async Task AvailabilityUsesConfiguredThresholdBoundary()
    {
        AuthenticationResponse owner = await RegisterUserAsync("threshold@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Threshold Agent");

        AuthorizeUser(owner.AccessToken);
        AgentResponse beforeHeartbeat = await GetAgentAsync(agent.AgentId);
        Assert.Equal("Offline", beforeHeartbeat.Status);
        Assert.Null(beforeHeartbeat.LastSeenAt);

        AuthorizeAgent(agent.Credential);
        using HttpResponseMessage heartbeatResponse = await client.PostAsync(
            $"/api/agents/{agent.AgentId}/heartbeat",
            null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, heartbeatResponse.StatusCode);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        AuthorizeUser(owner.AccessToken);
        AgentResponse atBoundary = await GetAgentAsync(agent.AgentId);
        Assert.Equal("Online", atBoundary.Status);
        Assert.Equal(InitialTime, atBoundary.LastSeenAt);

        timeProvider.Advance(TimeSpan.FromTicks(1));
        AgentResponse beyondBoundary = await GetAgentAsync(agent.AgentId);
        Assert.Equal("Offline", beyondBoundary.Status);
        Assert.Equal(InitialTime, beyondBoundary.LastSeenAt);
    }

    [Fact]
    public async Task ConcurrentHeartbeatsDoNotMoveLastSeenBackward()
    {
        AuthenticationResponse owner = await RegisterUserAsync("heartbeat-race@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Race Agent");
        DateTimeOffset olderHeartbeat = InitialTime.AddSeconds(10);
        DateTimeOffset newerHeartbeat = InitialTime.AddSeconds(20);

        await using AsyncServiceScope firstScope = factory.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = factory.Services.CreateAsyncScope();
        IAgentRepository firstRepository =
            firstScope.ServiceProvider.GetRequiredService<IAgentRepository>();
        IAgentRepository secondRepository =
            secondScope.ServiceProvider.GetRequiredService<IAgentRepository>();

        await Task.WhenAll(
            firstRepository.RecordHeartbeatAsync(
                agent.AgentId,
                olderHeartbeat,
                CancellationToken.None),
            secondRepository.RecordHeartbeatAsync(
                agent.AgentId,
                newerHeartbeat,
                CancellationToken.None));

        await using AsyncServiceScope verificationScope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            verificationScope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        DateTimeOffset? persistedLastSeen = await dbContext.Agents
            .Where(item => item.Id == agent.AgentId)
            .Select(item => item.LastSeenAt)
            .SingleAsync(CancellationToken.None);

        Assert.Equal(newerHeartbeat, persistedLastSeen);
    }

    private async Task<AuthenticationResponse> RegisterUserAsync(string email)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { Email = email, Password },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private async Task<RegisteredAgent> RegisterAgentAsync(
        string accessToken,
        string name)
    {
        AuthorizeUser(accessToken);
        using HttpResponseMessage tokenResponse = await client.PostAsync(
            "/api/agent-installation-tokens",
            null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, tokenResponse.StatusCode);
        CreateInstallationTokenResponse installationToken =
            (await tokenResponse.Content
                .ReadFromJsonAsync<CreateInstallationTokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage registrationResponse = await client.PostAsJsonAsync(
            "/api/agents/register",
            new
            {
                InstallationToken = installationToken.Token,
                Name = name,
                MachineName = $"{name}-HOST",
                OperatingSystem = "Windows 11 Pro",
                Version = "1.0.0",
            },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, registrationResponse.StatusCode);
        return (await registrationResponse.Content.ReadFromJsonAsync<RegisteredAgent>())!;
    }

    private async Task<AgentResponse> GetAgentAsync(Guid agentId)
    {
        using HttpResponseMessage response = await client.GetAsync(
            $"/api/agents/{agentId}",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AgentResponse>())!;
    }

    private void AuthorizeUser(string accessToken) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    private void AuthorizeAgent(string credential) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Agent", credential);

    private sealed record AuthenticationResponse(Guid UserId, string AccessToken);

    private sealed record CreateInstallationTokenResponse(Guid Id, string Token);

    private sealed record RegisteredAgent(Guid AgentId, string Credential);

    private sealed record AgentResponse(
        Guid Id,
        string Name,
        string MachineName,
        string OperatingSystem,
        string Version,
        DateTimeOffset RegisteredAt,
        DateTimeOffset? LastSeenAt,
        string Status);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset currentUtcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => currentUtcNow;

        public void Advance(TimeSpan duration) => currentUtcNow += duration;
    }
}
