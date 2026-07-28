using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerPilot.Domain.Users;
using ServerPilot.Infrastructure.Persistence;
using ServerPilot.IntegrationTests.Infrastructure;

namespace ServerPilot.IntegrationTests;

[Collection(PostgreSqlTestGroup.Name)]
public sealed class AuthenticationTests : IAsyncLifetime, IDisposable
{
    private const string Password = "correct horse battery staple";

    private readonly TestLogProvider logProvider = new();
    private readonly ServerPilotApiFactory factory;
    private readonly HttpClient client;

    public AuthenticationTests(PostgreSqlDatabaseFixture database)
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
    public async Task RegisterPersistsHashedPasswordAndReturnsWorkingAccessToken()
    {
        using HttpResponseMessage response = await RegisterAsync(
            "  User.Name@Example.COM  ",
            Password);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AuthenticationResponse body = (await response.Content.ReadFromJsonAsync<AuthenticationResponse>())!;
        Assert.Equal("User.Name@Example.COM", body.Email);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.True(response.Headers.CacheControl?.NoStore);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        User persistedUser = await dbContext.Users.SingleAsync(CancellationToken.None);

        Assert.Equal(body.UserId, persistedUser.Id);
        Assert.Equal("USER.NAME@EXAMPLE.COM", persistedUser.NormalizedEmail);
        Assert.NotEqual(Password, persistedUser.PasswordHash);
        Assert.DoesNotContain(Password, persistedUser.PasswordHash, StringComparison.Ordinal);

        using HttpRequestMessage meRequest = new(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        using HttpResponseMessage meResponse = await client.SendAsync(
            meRequest,
            CancellationToken.None);
        CurrentUserResponse currentUser =
            (await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>())!;

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.Equal(body.UserId, currentUser.UserId);
        Assert.Contains(logProvider.Entries, entry =>
            entry.CategoryName == typeof(ServerPilot.Api.Controllers.AuthController).FullName &&
            entry.Message.Contains(body.UserId.ToString(), StringComparison.Ordinal) &&
            entry.CorrelationId is not null);
        Assert.DoesNotContain(logProvider.Entries, entry =>
            entry.Message.Contains(Password, StringComparison.Ordinal) ||
            entry.Message.Contains(body.AccessToken, StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateNormalizedEmailReturnsConflict()
    {
        using HttpResponseMessage firstResponse = await RegisterAsync(
            "user@example.com",
            Password);
        using HttpResponseMessage duplicateResponse = await RegisterAsync(
            " USER@example.com ",
            Password);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        await AssertProblemAsync(
            duplicateResponse,
            HttpStatusCode.Conflict,
            "A user with this email already exists.");
    }

    [Fact]
    public async Task ConcurrentDuplicateRegistrationCreatesExactlyOneUser()
    {
        using HttpClient secondClient = factory.CreateClient();

        Task<HttpResponseMessage> firstRequest = RegisterAsync(
            "race@example.com",
            Password);
        Task<HttpResponseMessage> secondRequest = secondClient.PostAsJsonAsync(
            "/api/auth/register",
            new { Email = " RACE@example.com ", Password },
            CancellationToken.None);

        HttpResponseMessage[] responses = await Task.WhenAll(firstRequest, secondRequest);
        using HttpResponseMessage firstResponse = responses[0];
        using HttpResponseMessage secondResponse = responses[1];

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Conflict);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext dbContext =
            scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        Assert.Equal(1, await dbContext.Users.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoginReturnsAccessTokenForValidCredentials()
    {
        using HttpResponseMessage registerResponse = await RegisterAsync(
            "login@example.com",
            Password);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        using HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = " LOGIN@example.com ", Password },
            CancellationToken.None);
        AuthenticationResponse body =
            (await loginResponse.Content.ReadFromJsonAsync<AuthenticationResponse>())!;

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body.AccessToken));
        Assert.True(loginResponse.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task LoginDoesNotApplyRegistrationMinimumPasswordLength()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = "unknown@example.com", Password = "short" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LoginUpgradesLegacyIdentityPasswordHash()
    {
        const string legacyPassword = "legacy password value";
        PasswordHasher<object> legacyHasher = new(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV2,
        }));
        string legacyHash = legacyHasher.HashPassword(new object(), legacyPassword);
        User user = User.Create(
            Guid.NewGuid(),
            "legacy@example.com",
            "LEGACY@EXAMPLE.COM",
            legacyHash,
            DateTimeOffset.UtcNow);
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            ServerPilotDbContext dbContext =
                scope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = user.Email, Password = legacyPassword },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using AsyncServiceScope verificationScope = factory.Services.CreateAsyncScope();
        ServerPilotDbContext verificationContext =
            verificationScope.ServiceProvider.GetRequiredService<ServerPilotDbContext>();
        string updatedHash = await verificationContext.Users
            .Where(storedUser => storedUser.Id == user.Id)
            .Select(storedUser => storedUser.PasswordHash)
            .SingleAsync(CancellationToken.None);
        Assert.NotEqual(legacyHash, updatedHash);
    }

    [Fact]
    public async Task AuthenticationEndpointsAreRateLimitedPerClient()
    {
        List<HttpResponseMessage> responses = [];
        try
        {
            for (int attempt = 0; attempt < 11; attempt++)
            {
                responses.Add(await client.PostAsJsonAsync(
                    "/api/auth/login",
                    new { Email = "unknown@example.com", Password },
                    CancellationToken.None));
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, responses[^1].StatusCode);
            Assert.Equal(
                "application/problem+json",
                responses[^1].Content.Headers.ContentType?.MediaType);
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
    public async Task InvalidCredentialsReturnSameGenericUnauthorizedProblem()
    {
        using HttpResponseMessage registerResponse = await RegisterAsync(
            "known@example.com",
            Password);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        using HttpResponseMessage wrongPasswordResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = "known@example.com", Password = "incorrect password value" },
            CancellationToken.None);
        using HttpResponseMessage unknownUserResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { Email = "unknown@example.com", Password = "incorrect password value" },
            CancellationToken.None);

        await AssertProblemAsync(
            wrongPasswordResponse,
            HttpStatusCode.Unauthorized,
            "Invalid email or password.");
        await AssertProblemAsync(
            unknownUserResponse,
            HttpStatusCode.Unauthorized,
            "Invalid email or password.");
    }

    [Fact]
    public async Task ProtectedEndpointRejectsMissingAndInvalidTokens()
    {
        using HttpResponseMessage missingTokenResponse = await client.GetAsync(
            "/api/auth/me",
            CancellationToken.None);
        using HttpRequestMessage invalidTokenRequest = new(HttpMethod.Get, "/api/auth/me");
        invalidTokenRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-valid-token");
        using HttpResponseMessage invalidTokenResponse = await client.SendAsync(
            invalidTokenRequest,
            CancellationToken.None);

        await AssertStatusProblemAsync(
            missingTokenResponse,
            HttpStatusCode.Unauthorized);
        await AssertStatusProblemAsync(
            invalidTokenResponse,
            HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InvalidRegistrationRequestReturnsValidationProblem()
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { Email = "not-an-email", Password = "short" },
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    private Task<HttpResponseMessage> RegisterAsync(string email, string password) =>
        client.PostAsJsonAsync(
            "/api/auth/register",
            new { Email = email, Password = password },
            CancellationToken.None);

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedDetail)
    {
        string payload = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using JsonDocument problem = JsonDocument.Parse(payload);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedDetail, problem.RootElement.GetProperty("detail").GetString());
    }

    private static async Task AssertStatusProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus)
    {
        string payload = await response.Content.ReadAsStringAsync(CancellationToken.None);
        using JsonDocument problem = JsonDocument.Parse(payload);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)expectedStatus, problem.RootElement.GetProperty("status").GetInt32());
    }

    private sealed record AuthenticationResponse(
        Guid UserId,
        string Email,
        string AccessToken,
        DateTimeOffset ExpiresAt);

    private sealed record CurrentUserResponse(Guid UserId);
}
