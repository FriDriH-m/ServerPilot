using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServerPilot.Application.Commands;
using ServerPilot.Domain.Commands;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class AgentCommandProcessingTests : IAsyncLifetime, IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly MutableTimeProvider timeProvider;
    private readonly TestLogProvider logProvider = new();
    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;

    public AgentCommandProcessingTests(PostgreSqlDatabaseFixture database)
    {
        DateTimeOffset utcNow = TimeProvider.System.GetUtcNow();
        timeProvider = new MutableTimeProvider(utcNow.AddTicks(-(utcNow.Ticks % 10)));
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
    public async Task AuthenticatedAgentClaimsOnlyItsOldestPendingCommand()
    {
        AuthenticationResponse owner = await RegisterUserAsync("claim-owner@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Claim Agent");
        ServerInstanceResponse firstServer = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "First Claim Server");
        ServerCommandResponse firstCommand = await CreateCommandAsync(
            owner.AccessToken,
            firstServer.Id,
            "start");
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        ServerInstanceResponse secondServer = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Second Claim Server");
        ServerCommandResponse secondCommand = await CreateCommandAsync(
            owner.AccessToken,
            secondServer.Id,
            "stop");

        using HttpResponseMessage response = await SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        string payload = await response.Content.ReadAsStringAsync(CancellationToken.None);
        ServerCommandResponse claimed = JsonSerializer.Deserialize<ServerCommandResponse>(
            payload,
            JsonSerializerOptions.Web)!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(firstCommand.Id, claimed.Id);
        Assert.Equal(ServerCommandStatus.Claimed.ToString(), claimed.Status);
        Assert.Equal(AgentCommandDeliveryKind.New.ToString(), claimed.DeliveryKind);
        Assert.Equal(1, claimed.AttemptCount);
        Assert.Equal(timeProvider.GetUtcNow(), claimed.ClaimedAt);
        Assert.NotNull(claimed.ServerInstance);
        Assert.Equal(
            $@"C:\Servers\{firstServer.Name}.exe",
            claimed.ServerInstance.ExecutablePath);
        Assert.Equal("--port 16261", claimed.ServerInstance.Arguments);
        Assert.Equal(@"C:\Servers", claimed.ServerInstance.WorkingDirectory);
        Assert.Equal(firstServer.Name, claimed.ServerInstance.ProcessName);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerCommand remaining = await dbContext.ServerCommands
            .AsNoTracking()
            .SingleAsync(command => command.Id == secondCommand.Id, CancellationToken.None);
        Assert.Equal(ServerCommandStatus.Pending, remaining.Status);

        using HttpResponseMessage recoveryResponse = await SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        ServerCommandResponse recovered =
            (await recoveryResponse.Content.ReadFromJsonAsync<ServerCommandResponse>())!;
        Assert.Equal(HttpStatusCode.OK, recoveryResponse.StatusCode);
        Assert.Equal(firstCommand.Id, recovered.Id);
        Assert.Equal(AgentCommandDeliveryKind.Recovery.ToString(), recovered.DeliveryKind);
        Assert.Equal(1, recovered.AttemptCount);
    }

    [Fact]
    public async Task ConcurrentClaimsReturnOneNewDeliveryAndOneRecovery()
    {
        AuthenticationResponse owner = await RegisterUserAsync("claim-race@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Race Claim Agent");
        ServerInstanceResponse server = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Race Claim Server");
        ServerCommandResponse pending = await CreateCommandAsync(
            owner.AccessToken,
            server.Id,
            "start");
        ServerInstanceResponse secondServer = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Second Race Claim Server");
        ServerCommandResponse secondPending = await CreateCommandAsync(
            owner.AccessToken,
            secondServer.Id,
            "stop");

        Task<HttpResponseMessage> firstClaim = SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        Task<HttpResponseMessage> secondClaim = SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        HttpResponseMessage[] responses = await Task.WhenAll(firstClaim, secondClaim);
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            ServerCommandResponse?[] payloads = await Task.WhenAll(responses.Select(response =>
                response.Content.ReadFromJsonAsync<ServerCommandResponse>()));
            ServerCommandResponse[] deliveries = payloads
                .Select(Assert.IsType<ServerCommandResponse>)
                .ToArray();
            Guid deliveredId = deliveries[0].Id;
            Assert.Contains(deliveredId, new[] { pending.Id, secondPending.Id });
            Assert.All(deliveries, delivery => Assert.Equal(deliveredId, delivery.Id));
            Assert.Single(deliveries, delivery =>
                delivery.DeliveryKind == AgentCommandDeliveryKind.New.ToString());
            Assert.Single(deliveries, delivery =>
                delivery.DeliveryKind == AgentCommandDeliveryKind.Recovery.ToString());
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
        ServerCommand[] commands = await dbContext.ServerCommands
            .AsNoTracking()
            .Where(command => command.Id == pending.Id || command.Id == secondPending.Id)
            .OrderBy(command => command.CreatedAt)
            .ToArrayAsync(CancellationToken.None);
        ServerCommand claimed = Assert.Single(
            commands,
            command => command.Status == ServerCommandStatus.Claimed);
        ServerCommand stillPending = Assert.Single(
            commands,
            command => command.Status == ServerCommandStatus.Pending);
        Assert.NotEqual(claimed.Id, stillPending.Id);
        Assert.Contains(claimed.Id, new[] { pending.Id, secondPending.Id });
        Assert.Contains(stillPending.Id, new[] { pending.Id, secondPending.Id });
        Assert.Equal(ServerCommandStatus.Claimed, claimed.Status);
        Assert.Equal(1, claimed.AttemptCount);
    }

    [Fact]
    public async Task DatabaseRejectsMultipleClaimedCommandsForOneAgent()
    {
        AuthenticationResponse owner = await RegisterUserAsync("claim-constraint@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Constraint Agent");
        ServerInstanceResponse firstServer = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "First Constraint Server");
        ServerInstanceResponse secondServer = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Second Constraint Server");
        ServerCommandResponse first = await CreateCommandAsync(
            owner.AccessToken,
            firstServer.Id,
            "start");
        ServerCommandResponse second = await CreateCommandAsync(
            owner.AccessToken,
            secondServer.Id,
            "start");

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerCommand[] commands = await dbContext.ServerCommands
            .Where(command => command.Id == first.Id || command.Id == second.Id)
            .ToArrayAsync(CancellationToken.None);
        Assert.All(commands, command => Assert.True(command.TryClaim(timeProvider.GetUtcNow())));

        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            dbContext.SaveChangesAsync(CancellationToken.None));
        PostgresException postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("ux_server_commands_active_agent_id", postgresException.ConstraintName);
    }

    [Fact]
    public async Task RunningCommandIsRedeliveredWithoutClaimingAnotherPendingCommand()
    {
        AuthenticationResponse owner = await RegisterUserAsync("running-recovery@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Recovery Agent");
        ServerInstanceResponse firstServer = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Running Recovery Server");
        ServerCommandResponse firstCommand = await CreateCommandAsync(
            owner.AccessToken,
            firstServer.Id,
            "start");
        using HttpResponseMessage initialClaim = await SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        using HttpResponseMessage start = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{firstCommand.Id}/start");
        Assert.Equal(HttpStatusCode.OK, initialClaim.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, start.StatusCode);

        ServerInstanceResponse secondServer = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Pending Recovery Server");
        ServerCommandResponse secondCommand = await CreateCommandAsync(
            owner.AccessToken,
            secondServer.Id,
            "stop");

        using HttpResponseMessage recovery = await SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        ServerCommandResponse redelivered =
            (await recovery.Content.ReadFromJsonAsync<ServerCommandResponse>())!;
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        Assert.Equal(firstCommand.Id, redelivered.Id);
        Assert.Equal(ServerCommandStatus.Running.ToString(), redelivered.Status);
        Assert.Equal(AgentCommandDeliveryKind.Recovery.ToString(), redelivered.DeliveryKind);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerCommand pending = await dbContext.ServerCommands
            .AsNoTracking()
            .SingleAsync(command => command.Id == secondCommand.Id, CancellationToken.None);
        Assert.Equal(ServerCommandStatus.Pending, pending.Status);
    }

    [Fact]
    public async Task AgentCannotClaimOrUpdateAnotherAgentsCommand()
    {
        AuthenticationResponse owner = await RegisterUserAsync("agent-owner@example.com");
        RegisteredAgent ownerAgent = await RegisterAgentAsync(owner.AccessToken, "Owner Agent");
        ServerInstanceResponse server = await CreateServerInstanceAsync(
            owner.AccessToken,
            ownerAgent.AgentId,
            "Owned Agent Server");
        ServerCommandResponse command = await CreateCommandAsync(
            owner.AccessToken,
            server.Id,
            "start");
        AuthenticationResponse otherUser = await RegisterUserAsync("agent-other@example.com");
        RegisteredAgent otherAgent = await RegisterAgentAsync(
            otherUser.AccessToken,
            "Other Agent");

        using HttpResponseMessage anonymousClaim = await client.PostAsync(
            $"/api/agents/{ownerAgent.AgentId}/commands/claim-next",
            null,
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousClaim.StatusCode);

        using HttpResponseMessage foreignRouteClaim = await SendAgentPostAsync(
            otherAgent.Credential,
            $"/api/agents/{ownerAgent.AgentId}/commands/claim-next");
        Assert.Equal(HttpStatusCode.NotFound, foreignRouteClaim.StatusCode);

        using HttpResponseMessage claim = await SendAgentPostAsync(
            ownerAgent.Credential,
            $"/api/agents/{ownerAgent.AgentId}/commands/claim-next");
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        using HttpResponseMessage foreignStart = await SendAgentPostAsync(
            otherAgent.Credential,
            $"/api/commands/{command.Id}/start");
        Assert.Equal(HttpStatusCode.NotFound, foreignStart.StatusCode);

        using HttpResponseMessage ownStart = await SendAgentPostAsync(
            ownerAgent.Credential,
            $"/api/commands/{command.Id}/start");
        Assert.Equal(HttpStatusCode.NoContent, ownStart.StatusCode);
    }

    [Fact]
    public async Task ProgressAndCompletionAreValidatedAndIdempotent()
    {
        AuthenticationResponse owner = await RegisterUserAsync("complete-owner@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Complete Agent");
        ServerInstanceResponse server = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Complete Server");
        ServerCommandResponse command = await CreateCommandAsync(
            owner.AccessToken,
            server.Id,
            "start");
        using HttpResponseMessage claim = await SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);

        using HttpResponseMessage prematureComplete = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/complete");
        Assert.Equal(HttpStatusCode.Conflict, prematureComplete.StatusCode);
        JsonElement problem = await prematureComplete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(409, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("correlationId", out _));

        using HttpResponseMessage start = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/start");
        using HttpResponseMessage duplicateStart = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/start");
        Assert.Equal(HttpStatusCode.NoContent, start.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, duplicateStart.StatusCode);

        using HttpResponseMessage complete = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/complete");
        using HttpResponseMessage duplicateComplete = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/complete");
        using HttpResponseMessage lateStart = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/start");
        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, duplicateComplete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, lateStart.StatusCode);

        using HttpResponseMessage conflictingFailure = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/fail",
            new { ErrorCode = "LateFailure", ErrorMessage = "Too late." });
        Assert.Equal(HttpStatusCode.Conflict, conflictingFailure.StatusCode);
    }

    [Fact]
    public async Task ClockRegressionReturnsConflictWithoutViolatingTimestampConstraints()
    {
        AuthenticationResponse owner = await RegisterUserAsync("clock-owner@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Clock Agent");
        ServerInstanceResponse server = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Clock Server");
        ServerCommandResponse command = await CreateCommandAsync(
            owner.AccessToken,
            server.Id,
            "start");

        timeProvider.Advance(TimeSpan.FromMinutes(-1));
        using HttpResponseMessage claim = await SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        ServerCommandResponse claimed =
            (await claim.Content.ReadFromJsonAsync<ServerCommandResponse>())!;
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Equal(command.CreatedAt, claimed.ClaimedAt);

        using HttpResponseMessage regressedStart = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/start");
        Assert.Equal(HttpStatusCode.Conflict, regressedStart.StatusCode);

        timeProvider.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(1)));
        using HttpResponseMessage start = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/start");
        Assert.Equal(HttpStatusCode.NoContent, start.StatusCode);

        timeProvider.Advance(TimeSpan.FromMinutes(-2));
        using HttpResponseMessage regressedComplete = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/complete");
        using HttpResponseMessage regressedFail = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/fail",
            new { ErrorCode = "ClockRegression", ErrorMessage = "Clock moved backwards." });
        Assert.Equal(HttpStatusCode.Conflict, regressedComplete.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, regressedFail.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        ServerCommand persisted = await dbContext.ServerCommands
            .AsNoTracking()
            .SingleAsync(item => item.Id == command.Id, CancellationToken.None);
        Assert.Equal(ServerCommandStatus.Running, persisted.Status);
        Assert.Null(persisted.CompletedAt);
        Assert.Null(persisted.ErrorCode);
    }

    [Fact]
    public async Task FailureResultIsBoundedNormalizedAndIdempotentOnlyWhenIdentical()
    {
        AuthenticationResponse owner = await RegisterUserAsync("failure-owner@example.com");
        RegisteredAgent agent = await RegisterAgentAsync(owner.AccessToken, "Failure Agent");
        ServerInstanceResponse server = await CreateServerInstanceAsync(
            owner.AccessToken,
            agent.AgentId,
            "Failure Server");
        ServerCommandResponse command = await CreateCommandAsync(
            owner.AccessToken,
            server.Id,
            "start");
        await ClaimAndStartAsync(agent, command.Id);

        const string sensitiveFailure = " Process C:\\Sensitive\\server.exe failed. ";
        using HttpResponseMessage fail = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/fail",
            new { ErrorCode = " ProcessFailed ", ErrorMessage = sensitiveFailure });
        using HttpResponseMessage duplicateFail = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/fail",
            new { ErrorCode = "ProcessFailed", ErrorMessage = sensitiveFailure.Trim() });
        using HttpResponseMessage differentFail = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{command.Id}/fail",
            new { ErrorCode = "ProcessFailed", ErrorMessage = "Different failure." });
        Assert.Equal(HttpStatusCode.NoContent, fail.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, duplicateFail.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, differentFail.StatusCode);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            ServerCommand failed = await dbContext.ServerCommands
                .AsNoTracking()
                .SingleAsync(item => item.Id == command.Id, CancellationToken.None);
            Assert.Equal(ServerCommandStatus.Failed, failed.Status);
            Assert.Equal("ProcessFailed", failed.ErrorCode);
            Assert.Equal(sensitiveFailure.Trim(), failed.ErrorMessage);
        }

        Assert.DoesNotContain(logProvider.Entries, entry =>
            entry.Message.Contains(sensitiveFailure.Trim(), StringComparison.Ordinal));
        Assert.DoesNotContain(logProvider.Entries, entry =>
            entry.Message.Contains(agent.Credential, StringComparison.Ordinal));

        ServerCommandResponse nextCommand = await CreateCommandAsync(
            owner.AccessToken,
            server.Id,
            "start");
        await ClaimAndStartAsync(agent, nextCommand.Id);
        using HttpResponseMessage oversizedFailure = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{nextCommand.Id}/fail",
            new
            {
                ErrorCode = new string('x', ServerCommand.MaximumErrorCodeLength + 1),
                ErrorMessage = "Failure.",
            });
        Assert.Equal(HttpStatusCode.BadRequest, oversizedFailure.StatusCode);
    }

    private async Task ClaimAndStartAsync(RegisteredAgent agent, Guid commandId)
    {
        using HttpResponseMessage claim = await SendAgentPostAsync(
            agent.Credential,
            $"/api/agents/{agent.AgentId}/commands/claim-next");
        using HttpResponseMessage start = await SendAgentPostAsync(
            agent.Credential,
            $"/api/commands/{commandId}/start");
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, start.StatusCode);
    }

    private async Task<AuthenticationResponse> RegisterUserAsync(string email)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { Email = email, Password },
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private async Task<RegisteredAgent> RegisterAgentAsync(string accessToken, string name)
    {
        using HttpResponseMessage tokenResponse = await SendUserPostAsync(
            accessToken,
            "/api/agent-installation-tokens");
        Assert.Equal(HttpStatusCode.Created, tokenResponse.StatusCode);
        CreateInstallationTokenResponse installationToken =
            (await tokenResponse.Content
                .ReadFromJsonAsync<CreateInstallationTokenResponse>())!;

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
        Guid agentId,
        string name)
    {
        using HttpResponseMessage response = await SendUserPostAsync(
            accessToken,
            "/api/server-instances",
            new
            {
                AgentId = agentId,
                Name = name,
                ExecutablePath = $"C:\\Servers\\{name}.exe",
                Arguments = "--port 16261",
                WorkingDirectory = "C:\\Servers",
                ProcessName = name,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServerInstanceResponse>())!;
    }

    private async Task<ServerCommandResponse> CreateCommandAsync(
        string accessToken,
        Guid serverInstanceId,
        string operation)
    {
        using HttpResponseMessage response = await SendUserPostAsync(
            accessToken,
            $"/api/server-instances/{serverInstanceId}/commands/{operation}");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServerCommandResponse>())!;
    }

    private Task<HttpResponseMessage> SendUserPostAsync(
        string accessToken,
        string path,
        object? body = null) =>
        SendPostAsync("Bearer", accessToken, path, body);

    private Task<HttpResponseMessage> SendAgentPostAsync(
        string credential,
        string path,
        object? body = null) =>
        SendPostAsync("Agent", credential, path, body);

    private Task<HttpResponseMessage> SendPostAsync(
        string scheme,
        string credential,
        string path,
        object? body)
    {
        HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, credential);
        return client.SendAsync(request, CancellationToken.None);
    }

    private sealed record AuthenticationResponse(Guid UserId, string AccessToken);

    private sealed record CreateInstallationTokenResponse(Guid Id, string Token);

    private sealed record RegisteredAgent(Guid AgentId, string Credential);

    private sealed record ServerInstanceResponse(Guid Id, Guid AgentId, string Name);

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
        Guid CorrelationId,
        string? DeliveryKind = null,
        ServerInstanceExecutionResponse? ServerInstance = null);

    private sealed record ServerInstanceExecutionResponse(
        string ExecutablePath,
        string Arguments,
        string WorkingDirectory,
        string ProcessName);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset currentUtcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => currentUtcNow;

        public void Advance(TimeSpan duration) => currentUtcNow += duration;
    }
}
