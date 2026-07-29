using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Api;

public interface IAgentApiClient
{
    Task SendHeartbeatAsync(AgentCredential credential, CancellationToken cancellationToken);

    Task<ClaimedAgentCommand?> ClaimNextCommandAsync(
        AgentCredential credential,
        CancellationToken cancellationToken);

    Task MarkCommandRunningAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        CancellationToken cancellationToken);

    Task CompleteCommandAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        CancellationToken cancellationToken);

    Task FailCommandAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);
}
