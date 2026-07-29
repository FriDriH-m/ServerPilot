using ServerPilot.Agent.Api;

namespace ServerPilot.Agent.Execution;

public sealed record AgentCommandOutcome(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage,
    AgentProcessStateReport? ProcessState)
{
    public static AgentCommandOutcome Completed(AgentProcessStateReport processState) =>
        new(true, null, null, processState);

    public static AgentCommandOutcome Failed(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, null);
}
