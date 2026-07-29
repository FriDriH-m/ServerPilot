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
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);

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
            Guid.NewGuid(),
            cancellationToken);
    }

    public Task<ServerCommandHistoryResult> ListAsync(
        Guid userId,
        Guid serverInstanceId,
        int page,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidatePagination(page, limit);

        if (serverInstanceId == Guid.Empty)
        {
            return Task.FromResult(new ServerCommandHistoryResult(false, []));
        }

        return commands.ListOwnedAsync(
            serverInstanceId,
            userId,
            (page - 1) * limit,
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

    private static void ValidatePagination(int page, int limit)
    {
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Server command history limit must be between 1 and 100.");
        }

        if (page is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "Server command history page must be between 1 and 1000.");
        }
    }
}
