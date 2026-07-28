namespace ServerPilot.Application.InstallationTokens;

public enum AddAgentInstallationTokenStatus
{
    Succeeded = 1,
    ActiveLimitReached = 2,
    TokenHashCollision = 3,
}
