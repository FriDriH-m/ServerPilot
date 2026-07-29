using ServerPilot.Agent.Api;

namespace ServerPilot.Agent.Execution;

public sealed class AgentCommandExecution(ClaimedAgentCommand command)
{
    public ClaimedAgentCommand Command { get; } =
        command ?? throw new ArgumentNullException(nameof(command));

    public bool RunningReported { get; private set; }

    public AgentCommandOutcome? Outcome { get; private set; }

    public void MarkRunningReported() => RunningReported = true;

    public void RecordOutcome(AgentCommandOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Outcome ??= outcome;
    }
}
