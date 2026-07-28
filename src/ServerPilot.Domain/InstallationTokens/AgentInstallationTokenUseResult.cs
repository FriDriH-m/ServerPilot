namespace ServerPilot.Domain.InstallationTokens;

public enum AgentInstallationTokenUseResult
{
    Succeeded,
    Expired,
    AlreadyUsed,
    Revoked,
}
