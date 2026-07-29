namespace ServerPilot.Application.ServerInstances;

public sealed record UpdateServerInstanceResult(
    UpdateServerInstanceStatus Status,
    ServerInstanceDetails? ServerInstance);
