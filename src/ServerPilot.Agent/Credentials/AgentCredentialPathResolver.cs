namespace ServerPilot.Agent.Credentials;

internal static class AgentCredentialPathResolver
{
    private const string CredentialFileName = "agent-credential.dat";

    internal static string GetCredentialPath(bool isWindowsService)
    {
        Environment.SpecialFolder folder = isWindowsService
            ? Environment.SpecialFolder.CommonApplicationData
            : Environment.SpecialFolder.LocalApplicationData;
        string baseDirectory = Environment.GetFolderPath(folder);

        return GetCredentialPath(isWindowsService, baseDirectory);
    }

    internal static string GetCredentialPath(bool isWindowsService, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            string location = isWindowsService
                ? "common application-data"
                : "current user's local application-data";
            throw new InvalidOperationException($"The {location} directory is unavailable.");
        }

        return isWindowsService
            ? Path.Combine(baseDirectory, "ServerPilot", "Agent", CredentialFileName)
            : Path.Combine(baseDirectory, "ServerPilot", CredentialFileName);
    }
}
