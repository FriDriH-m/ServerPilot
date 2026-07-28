using ServerPilot.Domain.Agents;

namespace ServerPilot.UnitTests.Agents;

public sealed class AgentTests
{
    private static readonly DateTimeOffset RegisteredAt =
        new(2026, 7, 28, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateTrimsMetadataAndPreservesIdentity()
    {
        Guid agentId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Agent agent = Agent.Create(
            agentId,
            userId,
            "  Primary Agent  ",
            "  GAME-HOST  ",
            "  Windows 11  ",
            "  1.0.0  ",
            new string('a', Agent.CredentialHashLength),
            RegisteredAt);

        Assert.Equal(agentId, agent.Id);
        Assert.Equal(userId, agent.UserId);
        Assert.Equal("Primary Agent", agent.Name);
        Assert.Equal("GAME-HOST", agent.MachineName);
        Assert.Equal("Windows 11", agent.OperatingSystem);
        Assert.Equal("1.0.0", agent.Version);
        Assert.Equal(RegisteredAt, agent.RegisteredAt);
    }

    [Fact]
    public void CreateRejectsInvalidCredentialHash()
    {
        Assert.Throws<ArgumentException>(() => Agent.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Agent",
            "HOST",
            "Windows",
            "1.0.0",
            new string('A', Agent.CredentialHashLength),
            RegisteredAt));
    }

    [Fact]
    public void RecordHeartbeatPreservesLatestTimestamp()
    {
        Agent agent = CreateAgent();
        DateTimeOffset latestHeartbeat = RegisteredAt.AddSeconds(20);

        bool firstRecorded = agent.RecordHeartbeat(latestHeartbeat);
        bool staleRecorded = agent.RecordHeartbeat(RegisteredAt.AddSeconds(10));
        bool duplicateRecorded = agent.RecordHeartbeat(latestHeartbeat);

        Assert.True(firstRecorded);
        Assert.False(staleRecorded);
        Assert.False(duplicateRecorded);
        Assert.Equal(latestHeartbeat, agent.LastSeenAt);
    }

    [Fact]
    public void RecordHeartbeatRejectsTimestampBeforeRegistration()
    {
        Agent agent = CreateAgent();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            agent.RecordHeartbeat(RegisteredAt.AddTicks(-1)));
    }

    [Fact]
    public void RevokeCredentialsIsIdempotentAndPreservesFirstTimestamp()
    {
        Agent agent = CreateAgent();
        DateTimeOffset firstRevocation = RegisteredAt.AddMinutes(1);

        AgentCredentialRevocationResult first = agent.RevokeCredentials(firstRevocation);
        AgentCredentialRevocationResult second = agent.RevokeCredentials(
            firstRevocation.AddMinutes(1));

        Assert.Equal(AgentCredentialRevocationResult.Succeeded, first);
        Assert.Equal(AgentCredentialRevocationResult.AlreadyRevoked, second);
        Assert.Equal(firstRevocation, agent.CredentialRevokedAt);
    }

    [Fact]
    public void RevokeCredentialsRejectsTimestampBeforeRegistration()
    {
        Agent agent = CreateAgent();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            agent.RevokeCredentials(RegisteredAt.AddTicks(-1)));
    }

    private static Agent CreateAgent() => Agent.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Agent",
        "HOST",
        "Windows",
        "1.0.0",
        new string('a', Agent.CredentialHashLength),
        RegisteredAt);
}
