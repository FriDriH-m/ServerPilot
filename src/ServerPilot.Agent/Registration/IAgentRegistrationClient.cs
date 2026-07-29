using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Registration;

public interface IAgentRegistrationClient
{
    Task<AgentCredential> RegisterAsync(
        AgentRegistrationRequest request,
        CancellationToken cancellationToken);
}
