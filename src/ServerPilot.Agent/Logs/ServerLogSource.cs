using ServerPilot.Agent.Api;

namespace ServerPilot.Agent.Logs;

public sealed record ServerLogSource(
    string RootDirectory,
    string Path,
    string Identifier)
{
    public static ServerLogSource? Create(AssignedAgentServerInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!string.Equals(
                instance.Profile,
                "ProjectZomboid",
                StringComparison.Ordinal))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(instance.DataDirectory) ||
            !IsValidIdentifier(instance.LogSourceIdentifier))
        {
            throw new InvalidOperationException(
                "The Project Zomboid log source is missing its validated configuration.");
        }

        string root = instance.DataDirectory.TrimEnd('\\', '/');
        return new ServerLogSource(
            root,
            $"{root}\\console.txt",
            instance.LogSourceIdentifier!);
    }

    internal static bool IsValidIdentifier(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
