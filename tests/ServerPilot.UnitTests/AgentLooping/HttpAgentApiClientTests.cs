using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
                    ServerInstance = new
                    {
                        ExecutablePath = @"C:\Servers\server.exe",
                        Arguments = "--port 16261",
                        WorkingDirectory = @"C:\Servers",
                        ProcessName = "server",
                    },
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
        Assert.Equal(AgentCommandType.StartServer, command.Type);
        Assert.Equal(@"C:\Servers\server.exe", command.ServerInstance.ExecutablePath);
    }

    [Fact]
    public async Task RejectsUnknownCommandTypeBeforeExecution()
    {
        AgentCredential credential = CreateCredential();
        StubHttpMessageHandler handler = new(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Id = Guid.NewGuid(),
                    AgentId = credential.AgentId,
                    ServerInstanceId = Guid.NewGuid(),
                    Type = "RestartServer",
                    CorrelationId = Guid.NewGuid(),
                    DeliveryKind = "New",
                    ServerInstance = new
                    {
                        ExecutablePath = @"C:\Servers\server.exe",
                        Arguments = string.Empty,
                        WorkingDirectory = @"C:\Servers",
                        ProcessName = "server",
                    },
                }),
            });
        HttpAgentApiClient client = CreateClient(handler);

        AgentApiException exception = await Assert.ThrowsAsync<AgentApiException>(
            () => client.ClaimNextCommandAsync(credential, CancellationToken.None));

        Assert.Equal(AgentApiFailureKind.Configuration, exception.FailureKind);
    }

    [Fact]
    public async Task ReportsFailureWithCredentialAndCommandCorrelationId()
    {
        RecordingTransitionHandler handler = new();
        HttpAgentApiClient client = CreateClient(handler);
        AgentCredential credential = CreateCredential();
        ClaimedAgentCommand command = CreateCommand();

        await client.FailCommandAsync(
            credential,
            command,
            "ExecutableNotFound",
            "The local process operation failed.",
            CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/api/commands/{command.Id}/fail", handler.RequestUri?.AbsolutePath);
        Assert.Equal("Agent", handler.Authorization?.Scheme);
        Assert.Equal(credential.Value, handler.Authorization?.Parameter);
        Assert.Equal(command.CorrelationId.ToString("D"), handler.CorrelationId);
        using JsonDocument body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(
            "ExecutableNotFound",
            body.RootElement.GetProperty("errorCode").GetString());
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

    private static ClaimedAgentCommand CreateCommand() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        AgentCommandType.StartServer,
        Guid.NewGuid(),
        "New",
        new ClaimedAgentServerInstance(
            @"C:\Servers\server.exe",
            string.Empty,
            @"C:\Servers",
            "server"));

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class RecordingTransitionHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        public string? CorrelationId { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            CorrelationId = request.Headers.GetValues("X-Correlation-ID").Single();
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }
}
