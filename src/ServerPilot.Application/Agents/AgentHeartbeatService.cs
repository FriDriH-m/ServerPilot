namespace ServerPilot.Application.Agents;

public sealed class AgentHeartbeatService(
    IAgentRepository agents,
    TimeProvider timeProvider)
{
    public Task RecordAsync(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent ID cannot be empty.", nameof(agentId));
        }

        return agents.RecordHeartbeatAsync(
            agentId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
