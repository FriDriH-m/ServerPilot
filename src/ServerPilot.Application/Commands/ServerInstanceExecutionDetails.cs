namespace ServerPilot.Application.Commands;

public sealed record ServerInstanceExecutionDetails(
    string Profile,
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName,
    string? DataDirectory);
