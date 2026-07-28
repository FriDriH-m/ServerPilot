namespace ServerPilot.Application.Agents;

public sealed class AgentQueryService(
    IAgentRepository agents,
    AgentAvailabilityOptions options,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<AgentDetails>> ListAsync(
        Guid userId,
        int page,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidatePagination(page, limit);

        IReadOnlyList<AgentMetadata> ownedAgents = await agents.ListOwnedAsync(
            userId,
            (page - 1) * limit,
            limit,
            cancellationToken);
        DateTimeOffset now = timeProvider.GetUtcNow();

        return ownedAgents.Select(agent => Map(agent, now)).ToArray();
    }

    public async Task<AgentDetails?> GetAsync(
        Guid agentId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty)
        {
            return null;
        }

        ValidateUserId(userId);
        AgentMetadata? agent = await agents.FindOwnedAsync(
            agentId,
            userId,
            cancellationToken);
        return agent is null ? null : Map(agent, timeProvider.GetUtcNow());
    }

    private AgentDetails Map(AgentMetadata agent, DateTimeOffset now) =>
        new(
            agent.Id,
            agent.Name,
            agent.MachineName,
            agent.OperatingSystem,
            agent.Version,
            agent.RegisteredAt,
            agent.LastSeenAt,
            AgentAvailabilityEvaluator.Evaluate(
                agent.LastSeenAt,
                now,
                options.OfflineThreshold));

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }
    }

    private static void ValidatePagination(int page, int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Agent list limit must be between 1 and 100.");
        }

        if (page is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "Agent list page must be between 1 and 1000.");
        }
    }
}
