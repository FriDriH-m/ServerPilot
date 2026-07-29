namespace ServerPilot.Application.Commands;

public enum CreateServerCommandStatus
{
    Succeeded,
    ServerInstanceNotFound,
    ActiveCommandConflict,
    UnsupportedType,
}
