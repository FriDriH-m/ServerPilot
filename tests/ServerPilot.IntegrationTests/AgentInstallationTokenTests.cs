using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServerPilot.Application.InstallationTokens;
using ServerPilot.Domain.InstallationTokens;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class AgentInstallationTokenTests : IAsyncLifetime, IDisposable
{
    private const string Password = "correct horse battery staple";
    private const string Endpoint = "/api/agent-installation-tokens";

    private readonly TestLogProvider logProvider = new();
    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;

    public AgentInstallationTokenTests(PostgreSqlDatabaseFixture database)
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
    public async Task EndpointsRequireUserAuthentication()
    {
        using HttpResponseMessage createResponse = await client.PostAsync(
            Endpoint,
            null,
            CancellationToken.None);
        using HttpResponseMessage listResponse = await client.GetAsync(
            Endpoint,
            CancellationToken.None);
        using HttpResponseMessage revokeResponse = await client.DeleteAsync(
            $"{Endpoint}/{Guid.NewGuid()}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revokeResponse.StatusCode);
    }

    [Fact]
    public async Task CreateReturnsRawTokenOnceAndPersistsOnlyItsHash()
    {
        AuthenticationResponse authentication = await RegisterAsync("owner@example.com");
        Authorize(authentication.AccessToken);

        DateTimeOffset beforeCreation = DateTimeOffset.UtcNow;
        CreateInstallationTokenResponse created = await CreateTokenAsync();
        DateTimeOffset afterCreation = DateTimeOffset.UtcNow;

        Assert.StartsWith("spit_", created.Token, StringComparison.Ordinal);
        Assert.Equal(69, created.Token.Length);
        Assert.InRange(created.CreatedAt, beforeCreation, afterCreation);
        Assert.Equal(TimeSpan.FromMinutes(15), created.ExpiresAt - created.CreatedAt);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            AgentInstallationToken persistedToken =
                await dbContext.AgentInstallationTokens.SingleAsync(CancellationToken.None);

            Assert.Equal(created.Id, persistedToken.Id);
            Assert.Equal(authentication.UserId, persistedToken.UserId);
            Assert.Equal(ComputeHash(created.Token), persistedToken.TokenHash);
            Assert.DoesNotContain(created.Token, persistedToken.TokenHash, StringComparison.Ordinal);
        }

        using HttpResponseMessage listResponse = await client.GetAsync(
            Endpoint,
            CancellationToken.None);
        string listPayload = await listResponse.Content.ReadAsStringAsync(CancellationToken.None);
        InstallationTokenMetadata[] listed =
            (await listResponse.Content.ReadFromJsonAsync<InstallationTokenMetadata[]>())!;

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Single(listed);
        Assert.Equal(created.Id, listed[0].Id);
        Assert.Equal("Active", listed[0].State);
        Assert.DoesNotContain(created.Token, listPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("tokenHash", listPayload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(logProvider.Entries, entry =>
            entry.CategoryName ==
                typeof(ServerPilot.Api.Controllers.AgentInstallationTokensController).FullName &&
            entry.Message.Contains(created.Id.ToString(), StringComparison.Ordinal) &&
            entry.CorrelationId is not null);
        Assert.DoesNotContain(logProvider.Entries, entry =>
            entry.Message.Contains(created.Token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UsersCanListAndRevokeOnlyTheirOwnTokens()
    {
        AuthenticationResponse firstUser = await RegisterAsync("first@example.com");
        Authorize(firstUser.AccessToken);
        CreateInstallationTokenResponse firstToken = await CreateTokenAsync();

        AuthenticationResponse secondUser = await RegisterAsync("second@example.com");
        Authorize(secondUser.AccessToken);
        CreateInstallationTokenResponse secondToken = await CreateTokenAsync();

        using HttpResponseMessage secondUserListResponse = await client.GetAsync(
            Endpoint,
            CancellationToken.None);
        InstallationTokenMetadata[] secondUserTokens =
            (await secondUserListResponse.Content
                .ReadFromJsonAsync<InstallationTokenMetadata[]>())!;
        using HttpResponseMessage foreignRevokeResponse = await client.DeleteAsync(
            $"{Endpoint}/{firstToken.Id}",
            CancellationToken.None);

        Assert.Single(secondUserTokens);
        Assert.Equal(secondToken.Id, secondUserTokens[0].Id);
        Assert.Equal(HttpStatusCode.NotFound, foreignRevokeResponse.StatusCode);

        Authorize(firstUser.AccessToken);
        using HttpResponseMessage revokeResponse = await client.DeleteAsync(
            $"{Endpoint}/{firstToken.Id}",
            CancellationToken.None);
        using HttpResponseMessage repeatedRevokeResponse = await client.DeleteAsync(
            $"{Endpoint}/{firstToken.Id}",
            CancellationToken.None);
        using HttpResponseMessage firstUserListResponse = await client.GetAsync(
            Endpoint,
            CancellationToken.None);
        InstallationTokenMetadata[] firstUserTokens =
            (await firstUserListResponse.Content
                .ReadFromJsonAsync<InstallationTokenMetadata[]>())!;

        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedRevokeResponse.StatusCode);
        Assert.Single(firstUserTokens);
        Assert.Equal("Revoked", firstUserTokens[0].State);
        Assert.NotNull(firstUserTokens[0].RevokedAt);
    }

    [Fact]
    public async Task UsedTokenCannotBeRevoked()
    {
        AuthenticationResponse authentication = await RegisterAsync("used@example.com");
        Authorize(authentication.AccessToken);
        CreateInstallationTokenResponse created = await CreateTokenAsync();

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            AgentInstallationToken persistedToken =
                await dbContext.AgentInstallationTokens.SingleAsync(CancellationToken.None);
            Assert.Equal(
                AgentInstallationTokenUseResult.Succeeded,
                persistedToken.TryUse(DateTimeOffset.UtcNow));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using HttpResponseMessage response = await client.DeleteAsync(
            $"{Endpoint}/{created.Id}",
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreateResponseIsNotCacheable()
    {
        AuthenticationResponse authentication = await RegisterAsync("cache@example.com");
        Authorize(authentication.AccessToken);

        using HttpResponseMessage response = await client.PostAsync(
            Endpoint,
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
    }

    [Fact]
    public async Task ConcurrentCreationCannotExceedActiveTokenLimit()
    {
        AuthenticationResponse authentication = await RegisterAsync("limit@example.com");
        Authorize(authentication.AccessToken);

        Task<HttpResponseMessage>[] requests = Enumerable.Range(0, 20)
            .Select(_ => client.PostAsync(Endpoint, null, CancellationToken.None))
            .ToArray();
        HttpResponseMessage[] responses = await Task.WhenAll(requests);
        try
        {
            Assert.Equal(
                AgentInstallationTokenOptions.DefaultMaximumActiveTokensPerUser,
                responses.Count(response => response.StatusCode == HttpStatusCode.Created));
            Assert.Equal(
                20 - AgentInstallationTokenOptions.DefaultMaximumActiveTokensPerUser,
                responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            Assert.Equal(
                AgentInstallationTokenOptions.DefaultMaximumActiveTokensPerUser,
                await dbContext.AgentInstallationTokens.CountAsync(CancellationToken.None));
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
    public async Task ListIsBoundedAndSupportsAValidatedLimit()
    {
        AuthenticationResponse authentication = await RegisterAsync("paging@example.com");
        Authorize(authentication.AccessToken);
        DateTimeOffset createdAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            AgentInstallationToken[] tokens = Enumerable.Range(1, 60)
                .Select(index => AgentInstallationToken.Create(
                    Guid.NewGuid(),
                    authentication.UserId,
                    index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture),
                    createdAt.AddMilliseconds(index),
                    createdAt.AddDays(1)))
                .ToArray();
            dbContext.AgentInstallationTokens.AddRange(tokens);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using HttpResponseMessage defaultResponse = await client.GetAsync(
            Endpoint,
            CancellationToken.None);
        using HttpResponseMessage limitedResponse = await client.GetAsync(
            $"{Endpoint}?limit=5",
            CancellationToken.None);
        using HttpResponseMessage secondPageResponse = await client.GetAsync(
            $"{Endpoint}?limit=10&page=2",
            CancellationToken.None);
        using HttpResponseMessage invalidResponse = await client.GetAsync(
            $"{Endpoint}?limit=101",
            CancellationToken.None);
        InstallationTokenMetadata[] defaultList =
            (await defaultResponse.Content.ReadFromJsonAsync<InstallationTokenMetadata[]>())!;
        InstallationTokenMetadata[] limitedList =
            (await limitedResponse.Content.ReadFromJsonAsync<InstallationTokenMetadata[]>())!;
        InstallationTokenMetadata[] secondPage =
            (await secondPageResponse.Content.ReadFromJsonAsync<InstallationTokenMetadata[]>())!;

        Assert.Equal(50, defaultList.Length);
        Assert.Equal(5, limitedList.Length);
        Assert.Equal(10, secondPage.Length);
        Assert.DoesNotContain(secondPage, item => item.Id == defaultList[0].Id);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task CreatingTokenRemovesExpiredMetadataPastRetentionPeriod()
    {
        AuthenticationResponse authentication = await RegisterAsync("retention@example.com");
        Authorize(authentication.AccessToken);
        DateTimeOffset oldCreatedAt = DateTimeOffset.UtcNow.AddDays(
            -AgentInstallationTokenOptions.DefaultMetadataRetentionDays - 1);
        Guid oldTokenId = Guid.NewGuid();

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            dbContext.AgentInstallationTokens.Add(AgentInstallationToken.Create(
                oldTokenId,
                authentication.UserId,
                new string('b', AgentInstallationToken.TokenHashLength),
                oldCreatedAt,
                oldCreatedAt.AddMinutes(15)));
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        await CreateTokenAsync();

        await using AsyncServiceScope verificationScope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        Assert.False(await verificationContext.AgentInstallationTokens.AnyAsync(
            token => token.Id == oldTokenId,
            CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentRevocationPreservesTheFirstTimestamp()
    {
        AuthenticationResponse authentication = await RegisterAsync("revoke-race@example.com");
        Authorize(authentication.AccessToken);
        CreateInstallationTokenResponse created = await CreateTokenAsync();
        DateTimeOffset firstTimestamp = created.CreatedAt.AddSeconds(1);
        DateTimeOffset secondTimestamp = created.CreatedAt.AddSeconds(2);

        await using AsyncServiceScope firstScope = factory.Services.CreateAsyncScope();
        await using AsyncServiceScope secondScope = factory.Services.CreateAsyncScope();
        IAgentInstallationTokenRepository firstRepository =
            firstScope.ServiceProvider.GetRequiredService<IAgentInstallationTokenRepository>();
        IAgentInstallationTokenRepository secondRepository =
            secondScope.ServiceProvider.GetRequiredService<IAgentInstallationTokenRepository>();

        RevokeAgentInstallationTokenStatus[] results = await Task.WhenAll(
            firstRepository.RevokeOwnedAsync(
                created.Id,
                authentication.UserId,
                firstTimestamp,
                CancellationToken.None),
            secondRepository.RevokeOwnedAsync(
                created.Id,
                authentication.UserId,
                secondTimestamp,
                CancellationToken.None));

        Assert.Contains(RevokeAgentInstallationTokenStatus.Succeeded, results);
        Assert.Contains(RevokeAgentInstallationTokenStatus.AlreadyRevoked, results);

        await using AsyncServiceScope verificationScope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        DateTimeOffset? persistedTimestamp = await verificationContext.AgentInstallationTokens
            .Where(token => token.Id == created.Id)
            .Select(token => token.RevokedAt)
            .SingleAsync(CancellationToken.None);
        DateTimeOffset winnerTimestamp = results[0] == RevokeAgentInstallationTokenStatus.Succeeded
            ? firstTimestamp
            : secondTimestamp;
        Assert.NotNull(persistedTimestamp);
        Assert.InRange(
            persistedTimestamp.Value,
            winnerTimestamp.AddTicks(-10),
            winnerTimestamp.AddTicks(10));
    }

    private async Task<AuthenticationResponse> RegisterAsync(string email)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { Email = email, Password },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
    }

    private async Task<CreateInstallationTokenResponse> CreateTokenAsync()
    {
        using HttpResponseMessage response = await client.PostAsync(
            Endpoint,
            null,
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content
            .ReadFromJsonAsync<CreateInstallationTokenResponse>())!;
    }

    private void Authorize(string accessToken) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

    private static string ComputeHash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)))
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

    private sealed record InstallationTokenMetadata(
        Guid Id,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? UsedAt,
        DateTimeOffset? RevokedAt,
        string State);
}
