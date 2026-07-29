using ServerPilot.Domain.Commands;

namespace ServerPilot.Application.Commands;

public sealed class AgentCommandService(
    IServerCommandRepository commands,
    TimeProvider timeProvider)
{
    public Task<ServerCommandDetails?> ClaimNextAsync(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        ValidateAgentId(agentId);
        return commands.ClaimNextAsync(
            agentId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<AgentCommandTransitionStatus> StartAsync(
        Guid agentId,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        ValidateAgentId(agentId);
        if (commandId == Guid.Empty)
        {
            return Task.FromResult(AgentCommandTransitionStatus.NotFound);
        }

        return commands.StartAsync(
            commandId,
            agentId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<AgentCommandTransitionStatus> CompleteAsync(
        Guid agentId,
        Guid commandId,
        CancellationToken cancellationToken)
    {
        ValidateAgentId(agentId);
        if (commandId == Guid.Empty)
        {
            return Task.FromResult(AgentCommandTransitionStatus.NotFound);
        }

        return commands.CompleteAsync(
            commandId,
            agentId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public Task<AgentCommandTransitionStatus> FailAsync(
        Guid agentId,
        Guid commandId,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        ValidateAgentId(agentId);
        if (commandId == Guid.Empty)
        {
            return Task.FromResult(AgentCommandTransitionStatus.NotFound);
        }

        if (!TryNormalizeFailureDetail(
                errorCode,
                ServerCommand.MaximumErrorCodeLength,
                out string normalizedErrorCode) ||
            !TryNormalizeFailureDetail(
                errorMessage,
                ServerCommand.MaximumErrorMessageLength,
                out string normalizedErrorMessage))
        {
            return Task.FromResult(AgentCommandTransitionStatus.InvalidFailureDetails);
        }

        return commands.FailAsync(
            commandId,
            agentId,
            timeProvider.GetUtcNow(),
            normalizedErrorCode,
            normalizedErrorMessage,
            cancellationToken);
    }

    private static void ValidateAgentId(Guid agentId)
    {
        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("Agent ID cannot be empty.", nameof(agentId));
        }
    }

    private static bool TryNormalizeFailureDetail(
        string? value,
        int maximumLength,
        out string normalizedValue)
    {
        normalizedValue = value?.Trim() ?? string.Empty;
        return normalizedValue.Length is > 0 && normalizedValue.Length <= maximumLength;
    }
}
