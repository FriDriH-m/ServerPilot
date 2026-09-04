using System.Text;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Logs;

namespace ServerPilot.UnitTests.AgentLogs;

public sealed class BoundedServerLogTailReaderTests : IDisposable
{
    private const string SourceIdentifier =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        $"ServerPilot-logs-{Guid.NewGuid():N}");

    public BoundedServerLogTailReaderTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Fact]
    public async Task ReadsOnlyCompleteUtf8LinesAndRedactsKnownSecrets()
    {
        string path = Path.Combine(directory, "console.txt");
        await File.WriteAllTextAsync(
            path,
            "started\npassword=do-not-transfer\nAuthorization: Bearer abc\nПрив",
            Encoding.UTF8);
        FakeTimeProvider time = new();
        BoundedServerLogTailReader reader = new(time);
        Guid serverInstanceId = Guid.NewGuid();

        AgentServerLogReport first = Assert.IsType<AgentServerLogReport>(
            await reader.ReadAsync(
                serverInstanceId,
                new ServerLogSource(directory, path, SourceIdentifier),
                CancellationToken.None));

        Assert.Equal(AgentServerLogStatus.Available, first.Status);
        Assert.True(first.Reset);
        Assert.Contains("started\n", first.Content, StringComparison.Ordinal);
        Assert.Contains("password=[REDACTED]", first.Content, StringComparison.Ordinal);
        Assert.Contains("Authorization: [REDACTED]", first.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-transfer", first.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer abc", first.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Прив", first.Content, StringComparison.Ordinal);

        time.Advance(TimeSpan.FromSeconds(5));
        await File.AppendAllTextAsync(path, "ет\n", Encoding.UTF8);
        AgentServerLogReport second = Assert.IsType<AgentServerLogReport>(
            await reader.ReadAsync(
                serverInstanceId,
                new ServerLogSource(directory, path, SourceIdentifier),
                CancellationToken.None));

        Assert.Equal("Привет\n", second.Content);
        Assert.False(second.Reset);
    }

    [Fact]
    public async Task ReturnsIncrementalChunksThrottlesReadsAndResetsAfterTruncation()
    {
        string path = Path.Combine(directory, "console.txt");
        await File.WriteAllTextAsync(path, "first\n", Encoding.UTF8);
        FakeTimeProvider time = new();
        BoundedServerLogTailReader reader = new(time);
        Guid serverInstanceId = Guid.NewGuid();
        ServerLogSource source = new(directory, path, SourceIdentifier);

        AgentServerLogReport first = Assert.IsType<AgentServerLogReport>(
            await reader.ReadAsync(serverInstanceId, source, CancellationToken.None));
        Assert.Null(await reader.ReadAsync(serverInstanceId, source, CancellationToken.None));

        time.Advance(TimeSpan.FromSeconds(5));
        await File.AppendAllTextAsync(path, "second\n", Encoding.UTF8);
        AgentServerLogReport appended = Assert.IsType<AgentServerLogReport>(
            await reader.ReadAsync(serverInstanceId, source, CancellationToken.None));

        Assert.False(appended.Reset);
        Assert.Equal(first.ToOffset, appended.FromOffset);
        Assert.Equal("second\n", appended.Content);

        time.Advance(TimeSpan.FromSeconds(5));
        await File.WriteAllTextAsync(path, "new\n", Encoding.UTF8);
        AgentServerLogReport rotated = Assert.IsType<AgentServerLogReport>(
            await reader.ReadAsync(serverInstanceId, source, CancellationToken.None));

        Assert.True(rotated.Reset);
        Assert.NotEqual(first.StreamId, rotated.StreamId);
        Assert.Equal("new\n", rotated.Content);
    }

    [Fact]
    public async Task ReportsMissingWithoutExposingOrAcceptingAnotherPath()
    {
        FakeTimeProvider time = new();
        BoundedServerLogTailReader reader = new(time);
        Guid serverInstanceId = Guid.NewGuid();
        ServerLogSource source = new(
            directory,
            Path.Combine(directory, "console.txt"),
            SourceIdentifier);

        AgentServerLogReport missing = Assert.IsType<AgentServerLogReport>(
            await reader.ReadAsync(serverInstanceId, source, CancellationToken.None));

        Assert.Equal(AgentServerLogStatus.Missing, missing.Status);
        Assert.Null(missing.Content);
        Assert.Null(await reader.ReadAsync(serverInstanceId, source, CancellationToken.None));
    }

    [Fact]
    public async Task LargeFilesReturnOnlyTheBoundedRecentTail()
    {
        string path = Path.Combine(directory, "console.txt");
        string content = string.Concat(
            Enumerable.Range(0, 1_000)
                .Select(index => $"line-{index:D4}-payload\n"));
        await File.WriteAllTextAsync(path, content, Encoding.UTF8);
        BoundedServerLogTailReader reader = new(new FakeTimeProvider());

        AgentServerLogReport report = Assert.IsType<AgentServerLogReport>(
            await reader.ReadAsync(
                Guid.NewGuid(),
                new ServerLogSource(directory, path, SourceIdentifier),
                CancellationToken.None));

        Assert.True(Encoding.UTF8.GetByteCount(report.Content!) <=
            BoundedServerLogTailReader.MaximumChunkBytes);
        Assert.True(report.Content!.Count(character => character == '\n') <=
            BoundedServerLogTailReader.MaximumChunkLines);
        Assert.Contains("line-0999-payload", report.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("line-0000-payload", report.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedactionExpansionStillProducesABoundedChunk()
    {
        string path = Path.Combine(directory, "console.txt");
        string line = string.Concat(Enumerable.Repeat("pwd=x ", 20));
        await File.WriteAllTextAsync(
            path,
            string.Concat(Enumerable.Repeat($"{line}\n", 100)),
            Encoding.UTF8);
        BoundedServerLogTailReader reader = new(new FakeTimeProvider());

        AgentServerLogReport report = Assert.IsType<AgentServerLogReport>(
            await reader.ReadAsync(
                Guid.NewGuid(),
                new ServerLogSource(directory, path, SourceIdentifier),
                CancellationToken.None));

        Assert.True(Encoding.UTF8.GetByteCount(report.Content!) <=
            BoundedServerLogTailReader.MaximumChunkBytes);
        Assert.DoesNotContain("pwd=x", report.Content, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectZomboidSourceIsFixedAndGenericHasNoLogSource()
    {
        AssignedAgentServerInstance projectZomboid = new(
            Guid.NewGuid(),
            "ProjectZomboid",
            @"C:\Servers\PZ\StartServer64.bat",
            string.Empty,
            @"C:\Servers\PZ",
            "java",
            @"C:\Data\PZ",
            AgentServerInstanceStatus.Stopped,
            null,
            DateTimeOffset.UtcNow,
            SourceIdentifier);
        AssignedAgentServerInstance generic = projectZomboid with
        {
            Profile = "Generic",
            DataDirectory = null,
            LogSourceIdentifier = null,
        };

        ServerLogSource source = Assert.IsType<ServerLogSource>(
            ServerLogSource.Create(projectZomboid));

        Assert.Equal(@"C:\Data\PZ\console.txt", source.Path);
        Assert.Null(ServerLogSource.Create(generic));
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow =
            new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
