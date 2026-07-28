namespace ServerPilot.Application.Agents;

public static class AgentAvailabilityEvaluator
{
    public static AgentAvailabilityStatus Evaluate(
        DateTimeOffset? lastSeenAt,
        DateTimeOffset now,
        TimeSpan offlineThreshold)
    {
        if (offlineThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offlineThreshold),
                "Agent offline threshold must be positive.");
        }

        if (!lastSeenAt.HasValue)
        {
            return AgentAvailabilityStatus.Offline;
        }

        DateTimeOffset onlineBoundary = now.ToUniversalTime() - offlineThreshold;
        return lastSeenAt.Value.ToUniversalTime() >= onlineBoundary
            ? AgentAvailabilityStatus.Online
            : AgentAvailabilityStatus.Offline;
    }
}
