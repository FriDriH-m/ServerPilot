namespace ServerPilot.Application.Commands;

public sealed record ServerCommandHistoryCursor(DateTimeOffset CreatedAt, Guid Id);
