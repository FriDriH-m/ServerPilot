namespace ServerPilot.Domain.ServerInstances;

public sealed record ProjectZomboidServerPaths(
    string ConfigurationDirectory,
    string MainConfigurationPath,
    string SandboxConfigurationPath,
    string SpawnPointsPath,
    string SpawnRegionsPath,
    string LogsDirectory,
    string ConsoleLogPath,
    string SaveDirectory)
{
    public const string DefaultServerName = "servertest";

    public static ProjectZomboidServerPaths Create(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        string root = dataDirectory.TrimEnd('\\', '/');
        string configurationDirectory = $"{root}\\Server";
        return new ProjectZomboidServerPaths(
            configurationDirectory,
            $"{configurationDirectory}\\{DefaultServerName}.ini",
            $"{configurationDirectory}\\{DefaultServerName}_SandboxVars.lua",
            $"{configurationDirectory}\\{DefaultServerName}_spawnpoints.lua",
            $"{configurationDirectory}\\{DefaultServerName}_spawnregions.lua",
            $"{root}\\Logs",
            $"{root}\\console.txt",
            $"{root}\\Saves\\Multiplayer\\{DefaultServerName}");
    }
}
