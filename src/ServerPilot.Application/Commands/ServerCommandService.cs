using ServerPilot.Domain.Commands;

namespace ServerPilot.Application.Commands;

public sealed class ServerCommandService(
    IServerCommandRepository commands,
    TimeProvider timeProvider)
{
    public Task<CreateServerCommandResult> CreateAsync(
        Guid userId,
        Guid serverInstanceId,
        ServerCommandType type,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("Correlation ID cannot be empty.", nameof(correlationId));
        }

        if (serverInstanceId == Guid.Empty)
        {
            return Task.FromResult(new CreateServerCommandResult(
                CreateServerCommandStatus.ServerInstanceNotFound,
                null));
        }

        if (!Enum.IsDefined(type))
        {
            return Task.FromResult(new CreateServerCommandResult(
                CreateServerCommandStatus.UnsupportedType,
                null));
        }

        return commands.CreateOwnedAsync(
            serverInstanceId,
            userId,
            type,
            timeProvider.GetUtcNow(),
            correlationId,
            cancellationToken);
    }

    public Task<ServerCommandHistoryResult> ListAsync(
        Guid userId,
        Guid serverInstanceId,
        ServerCommandHistoryCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidateLimit(limit);

        if (serverInstanceId == Guid.Empty)
        {
            return Task.FromResult(new ServerCommandHistoryResult(false, [], false));
        }

        return commands.ListOwnedAsync(
            serverInstanceId,
            userId,
            after,
            limit,
            cancellationToken);
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Server command history limit must be between 1 and 100.");
        }
    }
}
