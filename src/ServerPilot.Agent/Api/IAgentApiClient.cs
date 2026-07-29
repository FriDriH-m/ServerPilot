using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Api;

public interface IAgentApiClient
{
    Task SendHeartbeatAsync(AgentCredential credential, CancellationToken cancellationToken);

    Task<ClaimedAgentCommand?> ClaimNextCommandAsync(
        AgentCredential credential,
        CancellationToken cancellationToken);
}
