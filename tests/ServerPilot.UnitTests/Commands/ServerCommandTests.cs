using ServerPilot.Domain.Commands;

namespace ServerPilot.UnitTests.Commands;

public sealed class ServerCommandTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewCommandStartsPendingWithUtcTimestamp()
    {
        ServerCommand command = CreateCommand(CreatedAt.ToOffset(TimeSpan.FromHours(3)));

        Assert.Equal(ServerCommandStatus.Pending, command.Status);
        Assert.Equal(TimeSpan.Zero, command.CreatedAt.Offset);
        Assert.Equal(0, command.AttemptCount);
    }

    [Fact]
    public void ValidLifecycleCompletesCommand()
    {
        ServerCommand command = CreateCommand(CreatedAt);

        Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
        Assert.True(command.TryStart(CreatedAt.AddSeconds(2)));
        Assert.True(command.TryComplete(CreatedAt.AddSeconds(3)));

        Assert.Equal(ServerCommandStatus.Completed, command.Status);
        Assert.Equal(1, command.AttemptCount);
        Assert.Equal(CreatedAt.AddSeconds(3), command.CompletedAt);
    }

    [Fact]
    public void InvalidTransitionDoesNotChangeState()
    {
        ServerCommand command = CreateCommand(CreatedAt);

        Assert.False(command.TryStart(CreatedAt.AddSeconds(1)));

        Assert.Equal(ServerCommandStatus.Pending, command.Status);
        Assert.Null(command.StartedAt);
    }

    [Fact]
    public void RepeatedClaimDoesNotIncrementAttemptCount()
    {
        ServerCommand command = CreateCommand(CreatedAt);

        Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
        Assert.False(command.TryClaim(CreatedAt.AddSeconds(2)));

        Assert.Equal(1, command.AttemptCount);
    }

    private static ServerCommand CreateCommand(DateTimeOffset createdAt)
    {
        return new ServerCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServerCommandType.StartServer,
            createdAt,
            Guid.NewGuid());
    }
}
