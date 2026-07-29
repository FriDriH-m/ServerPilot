using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Credentials;

namespace ServerPilot.UnitTests.AgentLooping;

public sealed class HttpAgentApiClientTests
{
    [Fact]
    public async Task SendsHeartbeatWithTheStoredAgentCredential()
    {
        AuthenticationHeaderValue? authorization = null;
        Uri? requestUri = null;
        StubHttpMessageHandler handler = new(request =>
        {
            authorization = request.Headers.Authorization;
            requestUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        HttpAgentApiClient client = CreateClient(handler);
        AgentCredential credential = CreateCredential();

        await client.SendHeartbeatAsync(credential, CancellationToken.None);

        Assert.Equal("Agent", authorization?.Scheme);
        Assert.Equal(credential.Value, authorization?.Parameter);
        Assert.Equal($"/api/agents/{credential.AgentId}/heartbeat", requestUri?.AbsolutePath);
    }

    [Fact]
    public async Task ReturnsClaimedCommandOnlyForTheAuthenticatedAgent()
    {
        AgentCredential credential = CreateCredential();
        Guid commandId = Guid.NewGuid();
        Guid serverInstanceId = Guid.NewGuid();
        Guid correlationId = Guid.NewGuid();
        StubHttpMessageHandler handler = new(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Id = commandId,
                    AgentId = credential.AgentId,
                    ServerInstanceId = serverInstanceId,
                    Type = "StartServer",
                    CorrelationId = correlationId,
                    DeliveryKind = "New",
                }),
            });
        HttpAgentApiClient client = CreateClient(handler);

        ClaimedAgentCommand? command = await client.ClaimNextCommandAsync(
            credential,
            CancellationToken.None);

        Assert.NotNull(command);
        Assert.Equal(commandId, command.Id);
        Assert.Equal(serverInstanceId, command.ServerInstanceId);
        Assert.Equal(correlationId, command.CorrelationId);
        Assert.Equal("StartServer", command.Type);
    }

    [Fact]
    public async Task ClassifiesUnauthorizedResponseAsAuthenticationFailure()
    {
        HttpAgentApiClient client = CreateClient(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        AgentApiException exception = await Assert.ThrowsAsync<AgentApiException>(
            () => client.SendHeartbeatAsync(CreateCredential(), CancellationToken.None));

        Assert.Equal(AgentApiFailureKind.Authentication, exception.FailureKind);
    }

    [Fact]
    public async Task ClassifiesServerFailureAsTransient()
    {
        HttpAgentApiClient client = CreateClient(
            new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        AgentApiException exception = await Assert.ThrowsAsync<AgentApiException>(
            () => client.ClaimNextCommandAsync(CreateCredential(), CancellationToken.None));

        Assert.Equal(AgentApiFailureKind.Transient, exception.FailureKind);
    }

    private static HttpAgentApiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.example.test/"),
        });

    private static AgentCredential CreateCredential() => AgentCredential.Create(
        Guid.NewGuid(),
        "spac_0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        AgentCredential.ExpectedAuthorizationScheme);

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
