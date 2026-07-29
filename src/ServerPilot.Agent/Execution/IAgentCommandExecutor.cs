using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Execution;

public interface IAgentCommandExecutor
{
    Task ExecuteAsync(
        AgentCredential credential,
        AgentCommandExecution execution,
        CancellationToken cancellationToken);
}
