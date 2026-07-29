using ServerPilot.Agent.Configuration;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Registration;

namespace ServerPilot.Agent.Bootstrap;

public sealed class AgentBootstrapService(
    AgentOptions options,
    IAgentCredentialStore credentialStore,
    IAgentRegistrationClient registrationClient,
    ILogger<AgentBootstrapService> logger)
{
    private static readonly Action<ILogger, Guid, Exception?> LogExistingCredentialReused =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(100, nameof(LogExistingCredentialReused)),
            "Reused stored credentials for Agent {AgentId}");
    private static readonly Action<ILogger, Guid, Exception?> LogAgentRegistered =
        LoggerMessage.Define<Guid>(
            LogLevel.Information,
            new EventId(101, nameof(LogAgentRegistered)),
            "Registered and stored credentials for Agent {AgentId}");

    public async Task<AgentBootstrapResult> InitializeAsync(CancellationToken cancellationToken)
    {
        AgentCredential? existingCredential = await credentialStore.ReadAsync(cancellationToken);
        if (existingCredential is not null)
        {
            LogExistingCredentialReused(logger, existingCredential.AgentId, null);
            return new AgentBootstrapResult(existingCredential, false);
        }

        AgentRegistrationRequest request = new(
            options.GetInstallationToken(),
            options.Name!.Trim(),
            Environment.MachineName,
            Environment.OSVersion.VersionString,
            GetAgentVersion());
        AgentCredential credential = await registrationClient.RegisterAsync(request, cancellationToken);

        // The server has consumed the one-time token, so finish the durable handoff even
        // when shutdown was requested after the registration response arrived.
        await credentialStore.SaveAsync(credential, CancellationToken.None);
        cancellationToken.ThrowIfCancellationRequested();

        LogAgentRegistered(logger, credential.AgentId, null);
        return new AgentBootstrapResult(credential, true);
    }

    private static string GetAgentVersion() =>
        typeof(AgentBootstrapService).Assembly.GetName().Version?.ToString() ?? "unknown";
}
