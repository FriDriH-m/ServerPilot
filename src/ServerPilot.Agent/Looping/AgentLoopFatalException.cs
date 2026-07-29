using ServerPilot.Agent.Api;

namespace ServerPilot.Agent.Looping;

public sealed class AgentLoopFatalException(AgentApiException failure)
    : Exception("Agent loop stopped because the Agent API rejected the request.", failure)
{
    public AgentApiFailureKind FailureKind { get; } = failure.FailureKind;
}
