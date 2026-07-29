namespace ServerPilot.Agent.Execution;

public sealed record AgentCommandOutcome(
    bool Succeeded,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static AgentCommandOutcome Completed() => new(true, null, null);

    public static AgentCommandOutcome Failed(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}
