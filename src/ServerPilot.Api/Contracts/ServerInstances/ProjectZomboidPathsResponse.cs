namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record ProjectZomboidPathsResponse(
    string ConfigurationDirectory,
    string MainConfigurationPath,
    string SandboxConfigurationPath,
    string SpawnPointsPath,
    string SpawnRegionsPath,
    string LogsDirectory,
    string ConsoleLogPath,
    string SaveDirectory);
