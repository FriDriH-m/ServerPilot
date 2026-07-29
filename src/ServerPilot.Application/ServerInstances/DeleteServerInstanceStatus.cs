namespace ServerPilot.Application.ServerInstances;

public enum DeleteServerInstanceStatus
{
    Succeeded,
    NotFound,
    Active,
    HasCommandHistory,
}
