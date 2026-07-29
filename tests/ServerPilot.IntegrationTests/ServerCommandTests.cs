using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Domain.Commands;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class ServerCommandTests : IAsyncLifetime, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;

    public ServerCommandTests(PostgreSqlDatabaseFixture database)
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
    public async Task OwnerCanCreateCommandsAndReadPaginatedSafeHistory()
    {
        AuthenticationResponse owner = await RegisterUserAsync("command-owner@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Command Owner Agent");
        ServerInstanceResponse serverInstance = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId);

        using HttpResponseMessage startResponse = await PostCommandAsync(
            owner.AccessToken,
            serverInstance.Id,
            "start");
        ServerCommandResponse start =
            (await startResponse.Content.ReadFromJsonAsync<ServerCommandResponse>())!;
        Assert.Equal(HttpStatusCode.Created, startResponse.StatusCode);
        Assert.Equal(ServerCommandType.StartServer.ToString(), start.Type);
        Assert.Equal(ServerCommandStatus.Pending.ToString(), start.Status);
        Assert.Equal(serverInstance.Id, start.ServerInstanceId);
        Assert.Equal(agent.AgentId, start.AgentId);

        using HttpResponseMessage conflictingStopResponse = await PostCommandAsync(
            owner.AccessToken,
            serverInstance.Id,
            "stop");
        Assert.Equal(HttpStatusCode.Conflict, conflictingStopResponse.StatusCode);

        await CancelCommandAsync(start.Id, start.CreatedAt.AddSeconds(1));

        using HttpResponseMessage stopResponse = await PostCommandAsync(
            owner.AccessToken,
            serverInstance.Id,
            "stop");
        ServerCommandResponse stop =
            (await stopResponse.Content.ReadFromJsonAsync<ServerCommandResponse>())!;
        Assert.Equal(HttpStatusCode.Created, stopResponse.StatusCode);
        await CompleteCommandAsync(stop.Id, stop.CreatedAt);

        Guid failedCommandId = await AddFailedCommandAsync(
            agent.AgentId,
            serverInstance.Id,
            stop.CreatedAt.AddMinutes(1));

        using HttpResponseMessage firstPageResponse = await GetCommandHistoryAsync(
            owner.AccessToken,
            serverInstance.Id,
            limit: 2);
        string firstPagePayload = await firstPageResponse.Content.ReadAsStringAsync(CancellationToken.None);
        ServerCommandHistoryResponse firstPage = JsonSerializer.Deserialize<ServerCommandHistoryResponse>(
            firstPagePayload,
            JsonSerializerOptions.Web)!;
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);
        Assert.Equal([failedCommandId, stop.Id], firstPage.Items.Select(command => command.Id));
        Assert.NotNull(firstPage.NextCursor);
        ServerCommandResponse failed = firstPage.Items[0];
        Assert.Equal(ServerCommandStatus.Failed.ToString(), failed.Status);
        Assert.Equal("ProcessFailed", failed.ErrorCode);
        Assert.DoesNotContain("C:\\Sensitive", firstPagePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("errorMessage", firstPagePayload, StringComparison.OrdinalIgnoreCase);

        await AddPendingCommandAsync(
            agent.AgentId,
            serverInstance.Id,
            stop.CreatedAt.AddMinutes(2));

        using HttpResponseMessage secondPageResponse = await GetCommandHistoryAsync(
            owner.AccessToken,
            serverInstance.Id,
            limit: 2,
            cursor: firstPage.NextCursor);
        ServerCommandHistoryResponse secondPage =
            (await secondPageResponse.Content.ReadFromJsonAsync<ServerCommandHistoryResponse>())!;
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
        ServerCommandResponse remaining = Assert.Single(secondPage.Items);
        Assert.Equal(start.Id, remaining.Id);
        Assert.Null(secondPage.NextCursor);

        using HttpResponseMessage invalidCursorResponse = await GetCommandHistoryAsync(
            owner.AccessToken,
            serverInstance.Id,
            limit: 2,
            cursor: "not-a-cursor");
        Assert.Equal(HttpStatusCode.BadRequest, invalidCursorResponse.StatusCode);

        using HttpRequestMessage obsoletePageRequest = new(
            HttpMethod.Get,
            $"/api/server-instances/{serverInstance.Id}/commands?page=2&limit=2");
        obsoletePageRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using HttpResponseMessage obsoletePageResponse = await client.SendAsync(
            obsoletePageRequest,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.BadRequest, obsoletePageResponse.StatusCode);

        using HttpRequestMessage deleteRequest = new(
            HttpMethod.Delete,
            $"/api/server-instances/{serverInstance.Id}");
        deleteRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        using HttpResponseMessage deleteResponse = await client.SendAsync(
            deleteRequest,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task ForeignUserCannotCreateOrReadCommandsForAnotherUsersServerInstance()
    {
        AuthenticationResponse owner = await RegisterUserAsync("command-resource-owner@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Owned Command Agent");
        ServerInstanceResponse serverInstance = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId);
        AuthenticationResponse otherUser = await RegisterUserAsync("command-resource-other@example.com");

        using HttpResponseMessage createResponse = await PostCommandAsync(
            otherUser.AccessToken,
            serverInstance.Id,
            "start");
        using HttpResponseMessage listResponse = await GetCommandHistoryAsync(
            otherUser.AccessToken,
            serverInstance.Id,
            limit: 50);

        Assert.Equal(HttpStatusCode.NotFound, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, listResponse.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        Assert.Equal(0, await dbContext.ServerCommands.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentCreationAllowsExactlyOneActiveCommand()
    {
        AuthenticationResponse owner = await RegisterUserAsync("command-concurrent@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Concurrent Command Agent");
        ServerInstanceResponse serverInstance = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId);

        Task<HttpResponseMessage> startTask = PostCommandAsync(
            owner.AccessToken,
            serverInstance.Id,
            "start");
        Task<HttpResponseMessage> stopTask = PostCommandAsync(
            owner.AccessToken,
            serverInstance.Id,
            "stop");
        HttpResponseMessage[] responses = await Task.WhenAll(startTask, stopTask);
        try
        {
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerCommand command = await dbContext.ServerCommands.SingleAsync(
            item => item.ServerInstanceId == serverInstance.Id,
            CancellationToken.None);
        Assert.Equal(ServerCommandStatus.Pending, command.Status);
    }

    [Fact]
    public async Task CursorPaginationDoesNotSkipCommandsWithIdenticalCreationTimes()
    {
        AuthenticationResponse owner = await RegisterUserAsync("command-cursor-tie@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Cursor Tie Agent");
        ServerInstanceResponse serverInstance = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId);
        DateTimeOffset createdAt = TimeProvider.System.GetUtcNow();
        Guid[] expectedIds = await AddCancelledCommandsAsync(
            agent.AgentId,
            serverInstance.Id,
            createdAt,
            count: 3);

        List<Guid> receivedIds = [];
        string? cursor = null;
        do
        {
            using HttpResponseMessage response = await GetCommandHistoryAsync(
                owner.AccessToken,
                serverInstance.Id,
                limit: 1,
                cursor);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            ServerCommandHistoryResponse page =
                (await response.Content.ReadFromJsonAsync<ServerCommandHistoryResponse>())!;
            receivedIds.Add(Assert.Single(page.Items).Id);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(expectedIds.Order(), receivedIds.Order());
        Assert.Equal(3, receivedIds.Distinct().Count());
    }

    private async Task CancelCommandAsync(Guid commandId, DateTimeOffset cancelledAt)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerCommand command = await dbContext.ServerCommands.SingleAsync(
            item => item.Id == commandId,
            CancellationToken.None);
        Assert.True(command.TryCancel(cancelledAt));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task CompleteCommandAsync(Guid commandId, DateTimeOffset createdAt)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerCommand command = await dbContext.ServerCommands.SingleAsync(
            item => item.Id == commandId,
            CancellationToken.None);
        Assert.True(command.TryClaim(createdAt.AddSeconds(1)));
        Assert.True(command.TryStart(createdAt.AddSeconds(2)));
        Assert.True(command.TryComplete(createdAt.AddSeconds(3)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid> AddFailedCommandAsync(
        Guid agentId,
        Guid serverInstanceId,
        DateTimeOffset createdAt)
    {
        ServerCommand command = ServerCommand.Create(
            Guid.NewGuid(),
            agentId,
            serverInstanceId,
            ServerCommandType.StartServer,
            createdAt,
            Guid.NewGuid());
        Assert.True(command.TryClaim(createdAt.AddSeconds(1)));
        Assert.True(command.TryStart(createdAt.AddSeconds(2)));
        Assert.True(command.TryFail(
            createdAt.AddSeconds(3),
            "ProcessFailed",
            "Process C:\\Sensitive\\server.exe failed to start."));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        dbContext.ServerCommands.Add(command);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return command.Id;
    }

    private async Task AddPendingCommandAsync(
        Guid agentId,
        Guid serverInstanceId,
        DateTimeOffset createdAt)
    {
        ServerCommand command = ServerCommand.Create(
            Guid.NewGuid(),
            agentId,
            serverInstanceId,
            ServerCommandType.StartServer,
            createdAt,
            Guid.NewGuid());

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        dbContext.ServerCommands.Add(command);
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<Guid[]> AddCancelledCommandsAsync(
        Guid agentId,
        Guid serverInstanceId,
        DateTimeOffset createdAt,
        int count)
    {
        ServerCommand[] commands = Enumerable.Range(0, count)
            .Select(_ => ServerCommand.Create(
                Guid.NewGuid(),
                agentId,
                serverInstanceId,
                ServerCommandType.StartServer,
                createdAt,
                Guid.NewGuid()))
            .ToArray();
        Assert.All(commands, command => Assert.True(command.TryCancel(createdAt)));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        dbContext.ServerCommands.AddRange(commands);
        await dbContext.SaveChangesAsync(CancellationToken.None);
        return commands.Select(command => command.Id).ToArray();
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
        using HttpResponseMessage tokenResponse = await PostAuthenticatedAsync(
            accessToken,
            "/api/agent-installation-tokens");
        Assert.Equal(HttpStatusCode.Created, tokenResponse.StatusCode);
        CreateInstallationTokenResponse installationToken =
            (await tokenResponse.Content.ReadFromJsonAsync<CreateInstallationTokenResponse>())!;

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

    private async Task<ServerInstanceResponse> CreateServerInstanceAsync(
        string accessToken,
        Guid agentId)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            "/api/server-instances")
        {
            Content = JsonContent.Create(new ServerInstanceRequest(
                agentId,
                "Command Test Server",
                "C:\\Servers\\command-test.exe",
                "--port 16261",
                "C:\\Servers",
                "command-test.exe")),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await client.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServerInstanceResponse>())!;
    }

    private async Task<HttpResponseMessage> PostCommandAsync(
        string accessToken,
        Guid serverInstanceId,
        string operation)
    {
        HttpRequestMessage request = new(
            HttpMethod.Post,
            $"/api/server-instances/{serverInstanceId}/commands/{operation}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, CancellationToken.None);
    }

    private async Task<HttpResponseMessage> GetCommandHistoryAsync(
        string accessToken,
        Guid serverInstanceId,
        int limit,
        string? cursor = null)
    {
        string cursorQuery = cursor is null
            ? string.Empty
            : $"&cursor={Uri.EscapeDataString(cursor)}";
        HttpRequestMessage request = new(
            HttpMethod.Get,
            $"/api/server-instances/{serverInstanceId}/commands?limit={limit}{cursorQuery}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, CancellationToken.None);
    }

    private async Task<HttpResponseMessage> PostAuthenticatedAsync(string accessToken, string path)
    {
        HttpRequestMessage request = new(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request, CancellationToken.None);
    }

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

    private sealed record ServerInstanceResponse(Guid Id, Guid AgentId);

    private sealed record ServerCommandResponse(
        Guid Id,
        Guid AgentId,
        Guid ServerInstanceId,
        string Type,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        string? ErrorCode,
        int AttemptCount,
        Guid CorrelationId);

    private sealed record ServerCommandHistoryResponse(
        IReadOnlyList<ServerCommandResponse> Items,
        string? NextCursor);
}
