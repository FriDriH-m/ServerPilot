namespace ServerPilot.Application.Agents;

public sealed class AgentManagementService(
    IAgentRepository agents,
    TimeProvider timeProvider)
{
    public Task<RevokeAgentCredentialStatus> RevokeCredentialsAsync(
        Guid agentId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty)
        {
            return Task.FromResult(RevokeAgentCredentialStatus.NotFound);
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        return agents.RevokeOwnedCredentialsAsync(
            agentId,
            userId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
