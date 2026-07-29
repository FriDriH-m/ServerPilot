namespace ServerPilot.Agent.Registration;

public sealed record AgentRegistrationRequest(
    string InstallationToken,
    string Name,
    string MachineName,
    string OperatingSystem,
    string Version);
