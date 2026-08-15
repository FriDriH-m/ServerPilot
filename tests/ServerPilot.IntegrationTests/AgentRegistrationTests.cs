using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Application.Agents;
using ServerPilot.Domain.InstallationTokens;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;
using AgentEntity = ServerPilot.Domain.Agents.Agent;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class AgentRegistrationTests : IAsyncLifetime, IDisposable
{
    private const string Password = "correct horse battery staple";
    private const string RegistrationEndpoint = "/api/agents/register";

    private readonly TestLogProvider logProvider = new();
    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;

    public AgentRegistrationTests(PostgreSqlDatabaseFixture database)
    {
        factory = new ServerPilotApiFactory(database.ConnectionString, logProvider);
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
    public async Task RegistrationConsumesTokenAndReturnsCredentialOnlyOnce()
    {
        AuthenticationResponse owner = await RegisterUserAsync("agent-owner@example.com");
        CreateInstallationTokenResponse installationToken =
            await CreateInstallationTokenAsync(owner.AccessToken);

        using HttpResponseMessage response = await RegisterAgentRequestAsync(
            installationToken.Token);
        RegisterAgentResponse registered =
            (await response.Content.ReadFromJsonAsync<RegisterAgentResponse>())!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        Assert.StartsWith(AgentCredentialFormat.Prefix, registered.Credential);
        Assert.Equal(AgentCredentialFormat.RawCredentialLength, registered.Credential.Length);
        Assert.Equal("Agent", registered.AuthorizationScheme);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        AgentEntity persistedAgent = await dbContext.Agents.SingleAsync(
            CancellationToken.None);
        AgentInstallationToken persistedToken =
            await dbContext.AgentInstallationTokens.SingleAsync(CancellationToken.None);

        Assert.Equal(registered.AgentId, persistedAgent.Id);
        Assert.Equal(owner.UserId, persistedAgent.UserId);
        Assert.Equal("Primary Agent", persistedAgent.Name);
        Assert.Equal("GAME-HOST", persistedAgent.MachineName);
        Assert.Equal("Windows 11 Pro", persistedAgent.OperatingSystem);
        Assert.Equal("1.0.0", persistedAgent.Version);
        Assert.Equal(ComputeHash(registered.Credential), persistedAgent.CredentialHash);
        Assert.NotNull(persistedToken.UsedAt);
        Assert.Null(persistedToken.RevokedAt);
        Assert.DoesNotContain(
            registered.Credential,
            persistedAgent.CredentialHash,
            StringComparison.Ordinal);
        Assert.Contains(logProvider.Entries, entry =>
            entry.CategoryName == typeof(ServerPilot.Api.Controllers.AgentsController).FullName &&
            entry.Message.Contains(registered.AgentId.ToString(), StringComparison.Ordinal) &&
            entry.Message.Contains(owner.UserId.ToString(), StringComparison.Ordinal) &&
            entry.CorrelationId is not null);
        Assert.DoesNotContain(logProvider.Entries, entry =>
            entry.Message.Contains(installationToken.Token, StringComparison.Ordinal) ||
            entry.Message.Contains(registered.Credential, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConcurrentRegistrationConsumesInstallationTokenExactlyOnce()
    {
        AuthenticationResponse owner = await RegisterUserAsync("race@example.com");
        CreateInstallationTokenResponse installationToken =
            await CreateInstallationTokenAsync(owner.AccessToken);

        Task<HttpResponseMessage>[] requests =
        [
            RegisterAgentRequestAsync(installationToken.Token),
            RegisterAgentRequestAsync(installationToken.Token),
        ];
        HttpResponseMessage[] responses = await Task.WhenAll(requests);
        try
        {
            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Created);
            Assert.Single(
                responses,
                response => response.StatusCode == HttpStatusCode.Unauthorized);

            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            Assert.Equal(1, await dbContext.Agents.CountAsync(CancellationToken.None));
            DateTimeOffset? usedAt = await dbContext.AgentInstallationTokens
                .Select(token => token.UsedAt)
                .SingleAsync(CancellationToken.None);
            Assert.NotNull(usedAt);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task RegistrationRejectsControlCharactersBeforeConsumingToken()
    {
        AuthenticationResponse owner = await RegisterUserAsync("metadata-validation@example.com");
        CreateInstallationTokenResponse installationToken =
            await CreateInstallationTokenAsync(owner.AccessToken);

        client.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage invalidResponse = await client.PostAsJsonAsync(
            RegistrationEndpoint,
            new
            {
                InstallationToken = installationToken.Token,
                Name = "Primary\u0000Agent",
                MachineName = "GAME-HOST",
                OperatingSystem = "Windows 11 Pro",
                Version = "1.0.0",
            },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        using HttpResponseMessage retryResponse = await RegisterAgentRequestAsync(
            installationToken.Token);
        Assert.Equal(HttpStatusCode.Created, retryResponse.StatusCode);
    }

    [Fact]
    public async Task CredentialCollisionRollsBackInstallationTokenConsumption()
    {
        AuthenticationResponse owner = await RegisterUserAsync("rollback@example.com");
        CreateInstallationTokenResponse installationToken =
            await CreateInstallationTokenAsync(owner.AccessToken);
        DateTimeOffset registeredAt = DateTimeOffset.UtcNow;
        string duplicateCredentialHash = new('c', AgentEntity.CredentialHashLength);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            dbContext.Agents.Add(AgentEntity.Create(
                Guid.NewGuid(),
                owner.UserId,
                "Existing Agent",
                "EXISTING-HOST",
                "Windows",
                "1.0.0",
                duplicateCredentialHash,
                registeredAt.AddMinutes(-1)));
            await dbContext.SaveChangesAsync(CancellationToken.None);

            IAgentRepository repository =
                scope.ServiceProvider.GetRequiredService<IAgentRepository>();
            AgentEntity collidingAgent = AgentEntity.Create(
                Guid.NewGuid(),
                owner.UserId,
                "Colliding Agent",
                "COLLIDING-HOST",
                "Windows",
                "1.0.0",
                duplicateCredentialHash,
                registeredAt);
            RegisterAgentPersistenceStatus status = await repository.TryRegisterAsync(
                collidingAgent,
                installationToken.Id,
                ComputeHash(installationToken.Token),
                registeredAt,
                CancellationToken.None);

            Assert.Equal(RegisterAgentPersistenceStatus.CredentialHashCollision, status);
            Assert.Null(await dbContext.AgentInstallationTokens
                .Where(token => token.Id == installationToken.Id)
                .Select(token => token.UsedAt)
                .SingleAsync(CancellationToken.None));
            Assert.Equal(1, await dbContext.Agents.CountAsync(CancellationToken.None));
        }

        using HttpResponseMessage retryResponse = await RegisterAgentRequestAsync(
            installationToken.Token);
        Assert.Equal(HttpStatusCode.Created, retryResponse.StatusCode);
    }

    [Fact]
    public async Task ConcurrentCredentialRevocationPreservesWinningTimestamp()
    {
        AuthenticationResponse owner = await RegisterUserAsync("revoke-race@example.com");
        CreateInstallationTokenResponse installationToken =
            await CreateInstallationTokenAsync(owner.AccessToken);
        using HttpResponseMessage registrationResponse = await RegisterAgentRequestAsync(
            installationToken.Token);
        Assert.Equal(HttpStatusCode.Created, registrationResponse.StatusCode);
        RegisterAgentResponse registered =
            (await registrationResponse.Content.ReadFromJsonAsync<RegisterAgentResponse>())!;
        DateTimeOffset firstTimestamp = registered.RegisteredAt.AddSeconds(1);
        DateTimeOffset secondTimestamp = registered.RegisteredAt.AddSeconds(2);

        await using AsyncServiceScope firstScope = factory.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = factory.Services.CreateAsyncScope();
        IAgentRepository firstRepository =
            firstScope.ServiceProvider.GetRequiredService<IAgentRepository>();
        IAgentRepository secondRepository =
            secondScope.ServiceProvider.GetRequiredService<IAgentRepository>();

        RevokeAgentCredentialStatus[] results = await Task.WhenAll(
            firstRepository.RevokeOwnedCredentialsAsync(
                registered.AgentId,
                owner.UserId,
                firstTimestamp,
                CancellationToken.None),
            secondRepository.RevokeOwnedCredentialsAsync(
                registered.AgentId,
                owner.UserId,
                secondTimestamp,
                CancellationToken.None));

        Assert.Contains(RevokeAgentCredentialStatus.Succeeded, results);
        Assert.Contains(RevokeAgentCredentialStatus.AlreadyRevoked, results);
        DateTimeOffset winningTimestamp =
            results[0] == RevokeAgentCredentialStatus.Succeeded
                ? firstTimestamp
                : secondTimestamp;

        await using AsyncServiceScope verificationScope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        DateTimeOffset? persistedTimestamp = await verificationContext.Agents
            .Where(agent => agent.Id == registered.AgentId)
            .Select(agent => agent.CredentialRevokedAt)
            .SingleAsync(CancellationToken.None);
        Assert.NotNull(persistedTimestamp);
        Assert.InRange(
            persistedTimestamp.Value,
            winningTimestamp.AddTicks(-10),
            winningTimestamp.AddTicks(10));
    }

    [Fact]
    public async Task InvalidExpiredRevokedAndUsedInstallationTokensAreRejected()
    {
        AuthenticationResponse owner = await RegisterUserAsync("inactive@example.com");
        string missingToken = "spit_" + new string('A', 64);
        string expiredToken = "spit_" + new string('B', 64);

        DateTimeOffset expiredCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            dbContext.AgentInstallationTokens.Add(AgentInstallationToken.Create(
                Guid.NewGuid(),
                owner.UserId,
                ComputeHash(expiredToken),
                expiredCreatedAt,
                expiredCreatedAt.AddMinutes(15)));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        CreateInstallationTokenResponse revokedToken =
            await CreateInstallationTokenAsync(owner.AccessToken);
        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage revokeTokenResponse = await client.DeleteAsync(
            $"/api/agent-installation-tokens/{revokedToken.Id}",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, revokeTokenResponse.StatusCode);

        CreateInstallationTokenResponse usedToken =
            await CreateInstallationTokenAsync(owner.AccessToken);
        using HttpResponseMessage initialRegistration = await RegisterAgentRequestAsync(
            usedToken.Token);
        Assert.Equal(HttpStatusCode.Created, initialRegistration.StatusCode);

        using HttpResponseMessage missingResponse = await RegisterAgentRequestAsync(missingToken);
        using HttpResponseMessage expiredResponse = await RegisterAgentRequestAsync(expiredToken);
        using HttpResponseMessage revokedResponse = await RegisterAgentRequestAsync(
            revokedToken.Token);
        using HttpResponseMessage usedResponse = await RegisterAgentRequestAsync(usedToken.Token);

        HttpResponseMessage[] rejected =
            [missingResponse, expiredResponse, revokedResponse, usedResponse];
        Assert.All(rejected, response =>
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        string[] payloads = await Task.WhenAll(rejected.Select(response =>
            response.Content.ReadAsStringAsync(CancellationToken.None)));
        Assert.All(payloads, payload =>
            Assert.Contains("invalid or inactive", payload, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AgentAuthenticationIsDistinctAndOwnerCanRevokeCredentials()
    {
        AuthenticationResponse owner = await RegisterUserAsync("owner@example.com");
        CreateInstallationTokenResponse installationToken =
            await CreateInstallationTokenAsync(owner.AccessToken);
        using HttpResponseMessage registrationResponse = await RegisterAgentRequestAsync(
            installationToken.Token);
        Assert.Equal(HttpStatusCode.Created, registrationResponse.StatusCode);
        RegisterAgentResponse registered =
            (await registrationResponse.Content.ReadFromJsonAsync<RegisterAgentResponse>())!;
        AuthenticationResponse otherUser = await RegisterUserAsync("other@example.com");

        AuthorizeAgent(AgentCredentialFormat.Prefix + new string('F', 64));
        using HttpResponseMessage invalidCredentialResponse = await client.GetAsync(
            "/api/agents/me",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidCredentialResponse.StatusCode);

        AuthorizeAgent(registered.Credential);
        using HttpResponseMessage currentAgentResponse = await client.GetAsync(
            "/api/agents/me",
            CancellationToken.None);
        CurrentAgentResponse currentAgent =
            (await currentAgentResponse.Content.ReadFromJsonAsync<CurrentAgentResponse>())!;
        Assert.Equal(HttpStatusCode.OK, currentAgentResponse.StatusCode);
        Assert.Equal(registered.AgentId, currentAgent.AgentId);

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage userJwtOnAgentEndpoint = await client.GetAsync(
            "/api/agents/me",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, userJwtOnAgentEndpoint.StatusCode);

        AuthorizeAgent(registered.Credential);
        using HttpResponseMessage agentCredentialOnUserEndpoint = await client.GetAsync(
            "/api/auth/me",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, agentCredentialOnUserEndpoint.StatusCode);

        AuthorizeUser(otherUser.AccessToken);
        using HttpResponseMessage foreignRevoke = await client.DeleteAsync(
            $"/api/agents/{registered.AgentId}/credentials",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, foreignRevoke.StatusCode);

        AuthorizeUser(owner.AccessToken);
        using HttpResponseMessage ownerRevoke = await client.DeleteAsync(
            $"/api/agents/{registered.AgentId}/credentials",
            CancellationToken.None);
        using HttpResponseMessage repeatedRevoke = await client.DeleteAsync(
            $"/api/agents/{registered.AgentId}/credentials",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.NoContent, ownerRevoke.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedRevoke.StatusCode);

        AuthorizeAgent(registered.Credential);
        using HttpResponseMessage revokedCredentialResponse = await client.GetAsync(
            "/api/agents/me",
            CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedCredentialResponse.StatusCode);
        Assert.Equal("Agent", revokedCredentialResponse.Headers.WwwAuthenticate.Single().Scheme);
        Assert.DoesNotContain(logProvider.Entries, entry =>
            entry.Message.Contains(registered.Credential, StringComparison.Ordinal));
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

    private async Task<CreateInstallationTokenResponse> CreateInstallationTokenAsync(
        string accessToken)
    {
        AuthorizeUser(accessToken);
        using HttpResponseMessage response = await client.PostAsync(
            "/api/agent-installation-tokens",
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content
            .ReadFromJsonAsync<CreateInstallationTokenResponse>())!;
    }

    private Task<HttpResponseMessage> RegisterAgentRequestAsync(string installationToken)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return client.PostAsJsonAsync(
            RegistrationEndpoint,
            new
            {
                InstallationToken = installationToken,
                Name = "  Primary Agent  ",
                MachineName = "  GAME-HOST  ",
                OperatingSystem = "  Windows 11 Pro  ",
                Version = "  1.0.0  ",
            },
            CancellationToken.None);
    }

    private void AuthorizeUser(string accessToken) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    private void AuthorizeAgent(string credential) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Agent", credential);

    private static string ComputeHash(string rawCredential) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawCredential)))
            .ToLowerInvariant();

    private sealed record AuthenticationResponse(
        Guid UserId,
        string Email,
        string AccessToken,
        DateTimeOffset ExpiresAt);

    private sealed record CreateInstallationTokenResponse(
        Guid Id,
        string Token,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);

    private sealed record RegisterAgentResponse(
        Guid AgentId,
        string Credential,
        string AuthorizationScheme,
        DateTimeOffset RegisteredAt);

    private sealed record CurrentAgentResponse(Guid AgentId);
}
