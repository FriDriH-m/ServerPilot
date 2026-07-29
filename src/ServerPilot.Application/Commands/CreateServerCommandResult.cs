namespace ServerPilot.Application.Commands;

public sealed record CreateServerCommandResult(
    CreateServerCommandStatus Status,
    ServerCommandDetails? Command);
