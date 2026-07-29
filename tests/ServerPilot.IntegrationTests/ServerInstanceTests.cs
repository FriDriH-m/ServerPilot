using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Domain.Commands;
using ServerPilot.Domain.ServerInstances;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class ServerInstanceTests : IAsyncLifetime, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;

    public ServerInstanceTests(PostgreSqlDatabaseFixture database)
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
    public async Task OwnerCanManageConfigurationAndListDoesNotExposeLocalPaths()
    {
        AuthenticationResponse owner = await RegisterUserAsync("server-owner@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Owner Agent");
        AuthenticationResponse otherUser = await RegisterUserAsync("server-other@example.com");
        ServerInstanceRequest createRequest = CreateRequest(agent.AgentId);

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/server-instances",
            createRequest,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ServerInstanceResponse created =
            (await createResponse.Content.ReadFromJsonAsync<ServerInstanceResponse>())!;
        Assert.Equal(agent.AgentId, created.AgentId);
        Assert.Equal(createRequest.ExecutablePath, created.ExecutablePath);
        Assert.Equal(ServerInstanceStatus.Unknown.ToString(), created.Status);

        using HttpResponseMessage listResponse = await client.GetAsync(
            "/api/server-instances",
            CancellationToken.None);
        string listPayload = await listResponse.Content.ReadAsStringAsync(CancellationToken.None);
        ServerInstanceListResponse[] listed = JsonSerializer.Deserialize<ServerInstanceListResponse[]>(
            listPayload,
            JsonSerializerOptions.Web)!;
        ServerInstanceListResponse item = Assert.Single(listed);
        Assert.Equal(created.Id, item.Id);
        Assert.DoesNotContain(createRequest.ExecutablePath, listPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(createRequest.Arguments, listPayload, StringComparison.Ordinal);
        Assert.DoesNotContain(createRequest.WorkingDirectory, listPayload, StringComparison.Ordinal);

        using HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/server-instances/{created.Id}",
            CancellationToken.None);
        ServerInstanceResponse fetched =
            (await getResponse.Content.ReadFromJsonAsync<ServerInstanceResponse>())!;
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(createRequest.WorkingDirectory, fetched.WorkingDirectory);

        AuthorizeUser(otherUser.AccessToken);
        using HttpResponseMessage foreignGetResponse = await client.GetAsync(
            $"/api/server-instances/{created.Id}",
            CancellationToken.None);
        using HttpResponseMessage foreignUpdateResponse = await client.PutAsJsonAsync(
            $"/api/server-instances/{created.Id}",
            CreateRequest(Guid.Empty) with { Name = "Foreign edit" },
            CancellationToken.None);
        using HttpResponseMessage foreignDeleteResponse = await client.DeleteAsync(
            $"/api/server-instances/{created.Id}",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, foreignGetResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignUpdateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignDeleteResponse.StatusCode);

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage updateResponse = await client.PutAsJsonAsync(
            $"/api/server-instances/{created.Id}",
            CreateRequest(Guid.Empty) with { Name = "Updated Server" },
            CancellationToken.None);
        ServerInstanceResponse updated =
            (await updateResponse.Content.ReadFromJsonAsync<ServerInstanceResponse>())!;
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Updated Server", updated.Name);
        Assert.Equal(created.AgentId, updated.AgentId);

        using HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/server-instances/{created.Id}",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task CreateRejectsInvalidConfigurationAndForeignAgent()
    {
        AuthenticationResponse owner = await RegisterUserAsync("server-validation@example.com");
        RegisteredAgent ownedAgent = await RegisterAgentAsync(owner.AccessToken, "Validation Agent");
        AuthenticationResponse otherUser = await RegisterUserAsync("server-foreign@example.com");
        RegisteredAgent foreignAgent = await RegisterAgentAsync(
            otherUser.AccessToken,
            "Foreign Agent");

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage invalidResponse = await client.PostAsJsonAsync(
            "/api/server-instances",
            CreateRequest(ownedAgent.AgentId) with { ExecutablePath = "server.exe" },
            CancellationToken.None);
        using HttpResponseMessage foreignResponse = await client.PostAsJsonAsync(
            "/api/server-instances",
            CreateRequest(foreignAgent.AgentId),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
    }

    [Theory]
    [InlineData("//?/C:/Servers/server.exe")]
    [InlineData("//./C:/Servers/server.exe")]
    [InlineData("\\/?/C:\\Servers/server.exe")]
    [InlineData("\\\\\\share\\server.exe")]
    [InlineData("\\\\server\\\\server.exe")]
    public async Task CreateRejectsDeviceAndMalformedUncPathVariants(string executablePath)
    {
        AuthenticationResponse owner = await RegisterUserAsync($"path-{Guid.NewGuid():N}@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Path Agent");

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/server-instances",
            CreateRequest(agent.AgentId) with { ExecutablePath = executablePath },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(ServerCommandStatus.Pending)]
    [InlineData(ServerCommandStatus.Claimed)]
    [InlineData(ServerCommandStatus.Running)]
    public async Task ProcessConfigurationCannotChangeWhileCommandIsActive(
        ServerCommandStatus commandStatus)
    {
        AuthenticationResponse owner = await RegisterUserAsync(
            $"active-command-{commandStatus}@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Command Agent");
        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/server-instances",
            CreateRequest(agent.AgentId),
            CancellationToken.None);
        ServerInstanceResponse created =
            (await createResponse.Content.ReadFromJsonAsync<ServerInstanceResponse>())!;

        DateTimeOffset createdAt = TimeProvider.System.GetUtcNow();
        ServerCommand command = ServerCommand.Create(
            Guid.NewGuid(),
            agent.AgentId,
            created.Id,
            ServerCommandType.StartServer,
            createdAt,
            Guid.NewGuid());
        if (commandStatus is ServerCommandStatus.Claimed or ServerCommandStatus.Running)
        {
            Assert.True(command.TryClaim(createdAt.AddSeconds(1)));
        }

        if (commandStatus == ServerCommandStatus.Running)
        {
            Assert.True(command.TryStart(createdAt.AddSeconds(2)));
        }

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            dbContext.ServerCommands.Add(command);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage blocked = await client.PutAsJsonAsync(
            $"/api/server-instances/{created.Id}",
            CreateRequest(Guid.Empty) with
            {
                ExecutablePath = "C:\\Servers\\replacement.exe",
            },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);

        using HttpResponseMessage nameOnly = await client.PutAsJsonAsync(
            $"/api/server-instances/{created.Id}",
            CreateRequest(Guid.Empty) with { Name = "Renamed Server" },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, nameOnly.StatusCode);

        await using AsyncServiceScope verificationScope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerInstance persisted = await verificationContext.ServerInstances
            .AsNoTracking()
            .SingleAsync(item => item.Id == created.Id, CancellationToken.None);
        Assert.Equal("Renamed Server", persisted.Name);
        Assert.Equal(created.ExecutablePath, persisted.ExecutablePath);
    }

    [Fact]
    public async Task ActiveServerInstanceCannotBeDeleted()
    {
        AuthenticationResponse owner = await RegisterUserAsync("server-active@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Active Agent");

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/server-instances",
            CreateRequest(agent.AgentId),
            CancellationToken.None);
        ServerInstanceResponse created =
            (await createResponse.Content.ReadFromJsonAsync<ServerInstanceResponse>())!;
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            ServerInstance instance = await dbContext.ServerInstances.SingleAsync(
                item => item.Id == created.Id,
                CancellationToken.None);
            instance.RecordProcessState(
                ServerInstanceStatus.Running,
                1_234,
                TimeProvider.System.GetUtcNow());
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/server-instances/{created.Id}",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);

        using HttpResponseMessage updateResponse = await client.PutAsJsonAsync(
            $"/api/server-instances/{created.Id}",
            CreateRequest(Guid.Empty) with
            {
                ExecutablePath = "C:\\Servers\\replacement.exe",
            },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, updateResponse.StatusCode);

        await using AsyncServiceScope verificationScope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerInstanceStatus? status = await verificationContext.ServerInstances
            .Where(item => item.Id == created.Id)
            .Select(item => (ServerInstanceStatus?)item.Status)
            .SingleOrDefaultAsync(CancellationToken.None);
        Assert.Equal(ServerInstanceStatus.Running, status);
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

    private async Task<RegisteredAgent> RegisterAgentAsync(string accessToken, string name)
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

    private void AuthorizeUser(string accessToken) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    private static ServerInstanceRequest CreateRequest(Guid agentId) =>
        new(
            agentId,
            "Test Server",
            "C:\\Servers\\test-server.exe",
            "--port 16261",
            "C:\\Servers",
            "test-server.exe");

    private sealed record AuthenticationResponse(Guid UserId, string AccessToken);

    private sealed record CreateInstallationTokenResponse(Guid Id, string Token);

    private sealed record RegisteredAgent(Guid AgentId, string Credential);

    private sealed record ServerInstanceRequest(
        Guid AgentId,
        string Name,
        string ExecutablePath,
        string Arguments,
        string WorkingDirectory,
        string ProcessName);

    private sealed record ServerInstanceResponse(
        Guid Id,
        Guid AgentId,
        string Name,
        string ExecutablePath,
        string Arguments,
        string WorkingDirectory,
        string ProcessName,
        string Status,
        int? LastProcessId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ServerInstanceListResponse(
        Guid Id,
        Guid AgentId,
        string Name,
        string Status,
        int? LastProcessId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
