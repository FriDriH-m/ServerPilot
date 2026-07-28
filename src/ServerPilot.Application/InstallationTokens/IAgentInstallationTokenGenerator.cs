namespace ServerPilot.Application.InstallationTokens;

public interface IAgentInstallationTokenGenerator
{
    GeneratedAgentInstallationToken Generate();

    string ComputeHash(string rawToken);
}
