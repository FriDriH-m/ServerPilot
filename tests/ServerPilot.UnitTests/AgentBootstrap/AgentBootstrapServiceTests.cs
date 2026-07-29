using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Bootstrap;
using ServerPilot.Agent.Configuration;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Registration;

namespace ServerPilot.UnitTests.AgentBootstrap;

public sealed class AgentBootstrapServiceTests
{
    [Fact]
    public async Task RegistersAndPersistsCredentialWhenStoreIsEmpty()
    {
        AgentCredential expectedCredential = CreateCredential();
        InMemoryCredentialStore store = new();
        RecordingRegistrationClient registrationClient = new(expectedCredential);
        AgentBootstrapService service = CreateService(store, registrationClient);

        AgentBootstrapResult result = await service.InitializeAsync(CancellationToken.None);

        Assert.True(result.RegisteredDuringStartup);
        Assert.Same(expectedCredential, result.Credential);
        Assert.Same(expectedCredential, store.Credential);
        Assert.NotNull(registrationClient.Request);
        Assert.Equal("installation-token", registrationClient.Request.InstallationToken);
        Assert.Equal("test-agent", registrationClient.Request.Name);
    }

    [Fact]
    public async Task ReusesStoredCredentialWithoutRegistrationOrInstallationToken()
    {
        AgentCredential storedCredential = CreateCredential();
        InMemoryCredentialStore store = new(storedCredential);
        RecordingRegistrationClient registrationClient = new(CreateCredential());
        AgentBootstrapService service = CreateService(
            store,
            registrationClient,
            installationToken: null);

        AgentBootstrapResult result = await service.InitializeAsync(CancellationToken.None);

        Assert.False(result.RegisteredDuringStartup);
        Assert.Same(storedCredential, result.Credential);
        Assert.Null(registrationClient.Request);
    }

    [Fact]
    public async Task RejectsFirstStartupWithoutInstallationToken()
    {
        InMemoryCredentialStore store = new();
        RecordingRegistrationClient registrationClient = new(CreateCredential());
        AgentBootstrapService service = CreateService(store, registrationClient, installationToken: null);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InitializeAsync(CancellationToken.None));

        Assert.Equal(
            "Agent:InstallationToken is required when no stored Agent credential exists.",
            exception.Message);
        Assert.Null(registrationClient.Request);
    }

    private static AgentBootstrapService CreateService(
        IAgentCredentialStore store,
        IAgentRegistrationClient registrationClient,
        string? installationToken = "installation-token")
    {
        AgentOptions options = new()
        {
            ApiBaseUrl = "https://api.example.test",
            Name = "test-agent",
            InstallationToken = installationToken,
        };
        options.Validate();

        return new AgentBootstrapService(
            options,
            store,
            registrationClient,
            NullLogger<AgentBootstrapService>.Instance);
    }

    private static AgentCredential CreateCredential() => AgentCredential.Create(
        Guid.NewGuid(),
        "spac_0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        AgentCredential.ExpectedAuthorizationScheme);

    private sealed class InMemoryCredentialStore(AgentCredential? credential = null) : IAgentCredentialStore
    {
        public AgentCredential? Credential { get; private set; } = credential;

        public Task<AgentCredential?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Credential);

        public Task SaveAsync(AgentCredential credential, CancellationToken cancellationToken)
        {
            Credential = credential;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRegistrationClient(AgentCredential credential) : IAgentRegistrationClient
    {
        public AgentRegistrationRequest? Request { get; private set; }

        public Task<AgentCredential> RegisterAsync(
            AgentRegistrationRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(credential);
        }
    }
}
