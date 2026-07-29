using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Application.ServerInstances;

public sealed class AgentServerInstanceService(
    IServerInstanceRepository serverInstances,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyList<AssignedServerInstanceDetails>> ListAsync(
        Guid agentId,
        int page,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateAgentId(agentId);
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (page is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        return serverInstances.ListAssignedAsync(
            agentId,
            (page - 1) * limit,
            limit,
            cancellationToken);
    }

    public Task<ServerInstanceStateReportResult> ReportAsync(
        Guid agentId,
        Guid serverInstanceId,
        ServerInstanceStatus status,
        int? processId,
        DateTimeOffset? processStartedAt,
        CancellationToken cancellationToken)
    {
        ValidateAgentId(agentId);
        if (serverInstanceId == Guid.Empty)
        {
            return Task.FromResult(ServerInstanceStateReportResult.NotFound);
        }

        return serverInstances.RecordProcessStateAsync(
            agentId,
            serverInstanceId,
            status,
            processId,
            processStartedAt,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static void ValidateAgentId(Guid agentId)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent ID cannot be empty.", nameof(agentId));
        }
    }
}
