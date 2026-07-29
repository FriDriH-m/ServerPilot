namespace ServerPilot.Application.Commands;

public sealed record ServerCommandHistoryResult(
    bool ServerInstanceFound,
    IReadOnlyList<ServerCommandDetails> Commands);
