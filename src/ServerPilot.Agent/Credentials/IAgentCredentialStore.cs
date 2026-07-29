namespace ServerPilot.Agent.Credentials;

public interface IAgentCredentialStore
{
    Task<AgentCredential?> ReadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AgentCredential credential, CancellationToken cancellationToken);
}
