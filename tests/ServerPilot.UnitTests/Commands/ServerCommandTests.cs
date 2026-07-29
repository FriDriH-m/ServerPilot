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

    [Theory]
    [MemberData(nameof(TransitionMatrix))]
    public void TransitionMatrixEnforcesTheCompleteCommandLifecycle(
        ServerCommandStatus initialStatus,
        CommandTransition transition,
        bool expectedResult,
        ServerCommandStatus expectedStatus)
    {
        ServerCommand command = CreateCommandInState(initialStatus);
        CommandSnapshot before = CommandSnapshot.From(command);

        bool actualResult = ApplyTransition(command, transition);

        Assert.Equal(expectedResult, actualResult);
        Assert.Equal(expectedStatus, command.Status);
        if (expectedResult && transition == CommandTransition.Claim)
        {
            Assert.Equal(1, command.AttemptCount);
        }

        if (!expectedResult)
        {
            Assert.Equal(before, CommandSnapshot.From(command));
        }
    }

    [Fact]
    public void FailureDetailsAreTrimmedAndBounded()
    {
        ServerCommand command = CreateCommand(CreatedAt);
        Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
        Assert.True(command.TryStart(CreatedAt.AddSeconds(2)));

        Assert.True(command.TryFail(CreatedAt.AddSeconds(3), " code ", " message "));

        Assert.Equal("code", command.ErrorCode);
        Assert.Equal("message", command.ErrorMessage);
    }

    [Fact]
    public void CreateRejectsUnsupportedCommandType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ServerCommand.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            (ServerCommandType)999,
            CreatedAt,
            Guid.NewGuid()));
    }

    [Theory]
    [InlineData(ServerCommandType.StartServer)]
    [InlineData(ServerCommandType.StopServer)]
    public void CreateSupportsMvpCommandTypes(ServerCommandType commandType)
    {
        ServerCommand command = ServerCommand.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            commandType,
            CreatedAt,
            Guid.NewGuid());

        Assert.Equal(commandType, command.Type);
    }

    [Fact]
    public void FailureDetailsHaveBoundedLengths()
    {
        ServerCommand command = CreateCommand(CreatedAt);
        Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
        Assert.True(command.TryStart(CreatedAt.AddSeconds(2)));

        Assert.Throws<ArgumentOutOfRangeException>(() => command.TryFail(
            CreatedAt.AddSeconds(3),
            new string('x', ServerCommand.MaximumErrorCodeLength + 1),
            "message"));
        Assert.Throws<ArgumentOutOfRangeException>(() => command.TryFail(
            CreatedAt.AddSeconds(3),
            "code",
            new string('x', ServerCommand.MaximumErrorMessageLength + 1)));

        Assert.Equal(ServerCommandStatus.Running, command.Status);
        Assert.Null(command.CompletedAt);
        Assert.Null(command.ErrorCode);
        Assert.Null(command.ErrorMessage);
    }

    [Fact]
    public void TransitionTimestampsMustNotPrecedeTheCurrentState()
    {
        ServerCommand command = CreateCommand(CreatedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() => command.TryClaim(CreatedAt.AddTicks(-1)));
        Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => command.TryTimeout(CreatedAt));
    }

    public static IEnumerable<object[]> TransitionMatrix()
    {
        foreach (ServerCommandStatus initialStatus in Enum.GetValues<ServerCommandStatus>())
        {
            yield return Row(
                initialStatus,
                CommandTransition.Claim,
                initialStatus == ServerCommandStatus.Pending,
                ServerCommandStatus.Claimed);
            yield return Row(
                initialStatus,
                CommandTransition.Start,
                initialStatus == ServerCommandStatus.Claimed,
                ServerCommandStatus.Running);
            yield return Row(
                initialStatus,
                CommandTransition.Complete,
                initialStatus == ServerCommandStatus.Running,
                ServerCommandStatus.Completed);
            yield return Row(
                initialStatus,
                CommandTransition.Fail,
                initialStatus == ServerCommandStatus.Running,
                ServerCommandStatus.Failed);
            yield return Row(
                initialStatus,
                CommandTransition.Cancel,
                initialStatus == ServerCommandStatus.Pending,
                ServerCommandStatus.Cancelled);
            yield return Row(
                initialStatus,
                CommandTransition.Timeout,
                initialStatus is ServerCommandStatus.Pending or
                    ServerCommandStatus.Claimed or
                    ServerCommandStatus.Running,
                ServerCommandStatus.TimedOut);
        }
    }

    private static object[] Row(
        ServerCommandStatus initialStatus,
        CommandTransition transition,
        bool expectedResult,
        ServerCommandStatus successfulStatus) =>
        [
            initialStatus,
            transition,
            expectedResult,
            expectedResult ? successfulStatus : initialStatus,
        ];

    private static ServerCommand CreateCommandInState(ServerCommandStatus status)
    {
        ServerCommand command = CreateCommand(CreatedAt);
        switch (status)
        {
            case ServerCommandStatus.Pending:
                return command;
            case ServerCommandStatus.Claimed:
                Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
                return command;
            case ServerCommandStatus.Running:
                Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
                Assert.True(command.TryStart(CreatedAt.AddSeconds(2)));
                return command;
            case ServerCommandStatus.Completed:
                Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
                Assert.True(command.TryStart(CreatedAt.AddSeconds(2)));
                Assert.True(command.TryComplete(CreatedAt.AddSeconds(3)));
                return command;
            case ServerCommandStatus.Failed:
                Assert.True(command.TryClaim(CreatedAt.AddSeconds(1)));
                Assert.True(command.TryStart(CreatedAt.AddSeconds(2)));
                Assert.True(command.TryFail(CreatedAt.AddSeconds(3), "failure", "Failure."));
                return command;
            case ServerCommandStatus.Cancelled:
                Assert.True(command.TryCancel(CreatedAt.AddSeconds(1)));
                return command;
            case ServerCommandStatus.TimedOut:
                Assert.True(command.TryTimeout(CreatedAt.AddSeconds(1)));
                return command;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    private static bool ApplyTransition(ServerCommand command, CommandTransition transition)
    {
        DateTimeOffset transitionAt = CreatedAt.AddMinutes(1);
        return transition switch
        {
            CommandTransition.Claim => command.TryClaim(transitionAt),
            CommandTransition.Start => command.TryStart(transitionAt),
            CommandTransition.Complete => command.TryComplete(transitionAt),
            CommandTransition.Fail => command.TryFail(transitionAt, "failure", "Failure."),
            CommandTransition.Cancel => command.TryCancel(transitionAt),
            CommandTransition.Timeout => command.TryTimeout(transitionAt),
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, null),
        };
    }

    private static ServerCommand CreateCommand(DateTimeOffset createdAt)
    {
        return ServerCommand.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServerCommandType.StartServer,
            createdAt,
            Guid.NewGuid());
    }

    public enum CommandTransition
    {
        Claim,
        Start,
        Complete,
        Fail,
        Cancel,
        Timeout,
    }

    private sealed record CommandSnapshot(
        ServerCommandStatus Status,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt,
        string? ErrorCode,
        string? ErrorMessage,
        int AttemptCount)
    {
        public static CommandSnapshot From(ServerCommand command) =>
            new(
                command.Status,
                command.ClaimedAt,
                command.StartedAt,
                command.CompletedAt,
                command.ErrorCode,
                command.ErrorMessage,
                command.AttemptCount);
    }
}
