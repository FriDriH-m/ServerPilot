using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Bootstrap;

public sealed record AgentBootstrapResult(AgentCredential Credential, bool RegisteredDuringStartup);
