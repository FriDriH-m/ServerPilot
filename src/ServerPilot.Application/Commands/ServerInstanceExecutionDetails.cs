namespace ServerPilot.Application.Commands;

public sealed record ServerInstanceExecutionDetails(
    string ExecutablePath,
    string Arguments,
    string WorkingDirectory,
    string ProcessName);
