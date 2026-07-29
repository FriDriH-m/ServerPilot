namespace ServerPilot.Api.Contracts.Commands;

public sealed record ServerCommandHistoryResponse(
    IReadOnlyList<ServerCommandResponse> Items,
    string? NextCursor);
