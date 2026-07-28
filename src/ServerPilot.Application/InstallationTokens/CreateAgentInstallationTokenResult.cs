namespace ServerPilot.Application.InstallationTokens;

public sealed record CreateAgentInstallationTokenResult(
    CreateAgentInstallationTokenStatus Status,
    CreatedAgentInstallationToken? Token);
