namespace ServerPilot.Domain.Commands;

public enum ServerCommandType
{
    StartServer = 1,
    StopServer = 2,
    CreateBackup = 3,
    RestoreBackup = 4,
    PruneBackups = 5,
}
