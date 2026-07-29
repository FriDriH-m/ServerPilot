namespace ServerPilot.Application.ServerInstances;

public sealed record ServerInstanceCreateResult(
    ServerInstanceCreateStatus Status,
    ServerInstanceDetails? ServerInstance);
