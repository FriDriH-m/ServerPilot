using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ServerPilot.Agent.Api;

namespace ServerPilot.Agent.Logs;

public interface IServerLogTailReader
{
    Task<AgentServerLogReport?> ReadAsync(
        Guid serverInstanceId,
        ServerLogSource source,
        CancellationToken cancellationToken);

    void Retain(IReadOnlySet<Guid> serverInstanceIds);
}

public sealed partial class BoundedServerLogTailReader(TimeProvider timeProvider)
    : IServerLogTailReader
{
    public const int MaximumChunkBytes = 16 * 1024;
    public const int MaximumChunkLines = 200;

    private const int FingerprintBytes = 64;
    private static readonly TimeSpan MinimumReadInterval = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    private readonly Dictionary<Guid, TailState> states = [];

    public async Task<AgentServerLogReport?> ReadAsync(
        Guid serverInstanceId,
        ServerLogSource source,
        CancellationToken cancellationToken)
    {
        if (serverInstanceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Server instance ID cannot be empty.",
                nameof(serverInstanceId));
        }

        ArgumentNullException.ThrowIfNull(source);
        DateTimeOffset now = timeProvider.GetUtcNow();
        states.TryGetValue(serverInstanceId, out TailState? previous);
        if (previous is not null &&
            string.Equals(previous.SourceIdentifier, source.Identifier, StringComparison.Ordinal) &&
            now < previous.NextReadAt)
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(source.RootDirectory) || !File.Exists(source.Path))
            {
                states[serverInstanceId] = TailState.Pending(
                    source.Identifier,
                    now + MinimumReadInterval);
                return Unavailable(source.Identifier, AgentServerLogStatus.Missing);
            }

            if (IsReparsePoint(source.RootDirectory) || IsReparsePoint(source.Path))
            {
                states[serverInstanceId] = TailState.Pending(
                    source.Identifier,
                    now + MinimumReadInterval);
                return Unavailable(source.Identifier, AgentServerLogStatus.Unavailable);
            }

            await using FileStream stream = new(
                source.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4_096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long length = stream.Length;
            DateTime creationTimeUtc = File.GetCreationTimeUtc(source.Path);
            string? prefixFingerprint = await ReadPrefixFingerprintAsync(
                stream,
                length,
                cancellationToken);

            bool reset = previous?.StreamId is null ||
                !string.Equals(
                    previous.SourceIdentifier,
                    source.Identifier,
                    StringComparison.Ordinal) ||
                (previous.CreationTimeUtc != creationTimeUtc &&
                 length == previous.Offset) ||
                length < previous.Offset ||
                (previous.PrefixFingerprint is not null &&
                 prefixFingerprint is not null &&
                 !string.Equals(
                     previous.PrefixFingerprint,
                     prefixFingerprint,
                     StringComparison.Ordinal));
            Guid streamId = reset ? Guid.NewGuid() : previous!.StreamId!.Value;
            long fromOffset = reset
                ? Math.Max(0, length - MaximumChunkBytes)
                : previous!.Offset;
            bool discardUntilNewline = reset
                ? fromOffset > 0
                : previous!.DiscardUntilNewline;

            ReadChunk chunk = await ReadChunkAsync(
                stream,
                fromOffset,
                discardUntilNewline,
                cancellationToken);
            states[serverInstanceId] = new TailState(
                source.Identifier,
                streamId,
                chunk.ToOffset,
                creationTimeUtc,
                prefixFingerprint ?? previous?.PrefixFingerprint,
                chunk.DiscardUntilNewline,
                now + MinimumReadInterval);
            return new AgentServerLogReport(
                AgentServerLogStatus.Available,
                source.Identifier,
                streamId,
                fromOffset,
                chunk.ToOffset,
                reset,
                chunk.Content);
        }
        catch (FileNotFoundException)
        {
            states[serverInstanceId] = TailState.Pending(
                source.Identifier,
                now + MinimumReadInterval);
            return Unavailable(source.Identifier, AgentServerLogStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            states[serverInstanceId] = TailState.Pending(
                source.Identifier,
                now + MinimumReadInterval);
            return Unavailable(source.Identifier, AgentServerLogStatus.Missing);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if (previous is null)
            {
                states[serverInstanceId] = TailState.Pending(
                    source.Identifier,
                    now + MinimumReadInterval);
            }
            else
            {
                states[serverInstanceId] = previous with
                {
                    NextReadAt = now + MinimumReadInterval,
                };
            }

            return Unavailable(source.Identifier, AgentServerLogStatus.Unavailable);
        }
    }

    public void Retain(IReadOnlySet<Guid> serverInstanceIds)
    {
        ArgumentNullException.ThrowIfNull(serverInstanceIds);
        foreach (Guid serverInstanceId in states.Keys
                     .Where(id => !serverInstanceIds.Contains(id))
                     .ToArray())
        {
            states.Remove(serverInstanceId);
        }
    }

    private static async Task<string?> ReadPrefixFingerprintAsync(
        FileStream stream,
        long length,
        CancellationToken cancellationToken)
    {
        if (length < FingerprintBytes)
        {
            return null;
        }

        byte[] prefix = new byte[FingerprintBytes];
        stream.Position = 0;
        int read = await stream.ReadAsync(prefix, cancellationToken);
        return read == FingerprintBytes
            ? Convert.ToHexString(SHA256.HashData(prefix))
            : null;
    }

    private static async Task<ReadChunk> ReadChunkAsync(
        FileStream stream,
        long fromOffset,
        bool discardUntilNewline,
        CancellationToken cancellationToken)
    {
        long available = Math.Max(0, stream.Length - fromOffset);
        int requested = (int)Math.Min(MaximumChunkBytes, available);
        if (requested == 0)
        {
            return new ReadChunk(string.Empty, fromOffset, discardUntilNewline);
        }

        byte[] buffer = new byte[requested];
        stream.Position = fromOffset;
        int totalRead = 0;
        while (totalRead < requested)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, requested - totalRead),
                cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead == 0)
        {
            return new ReadChunk(string.Empty, fromOffset, discardUntilNewline);
        }

        int contentStart = 0;
        if (discardUntilNewline)
        {
            int firstNewline = Array.IndexOf(buffer, (byte)'\n', 0, totalRead);
            if (firstNewline < 0)
            {
                return new ReadChunk(
                    string.Empty,
                    fromOffset + totalRead,
                    true);
            }

            contentStart = firstNewline + 1;
            discardUntilNewline = false;
        }

        int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', totalRead - 1, totalRead);
        if (lastNewline < contentStart)
        {
            bool oversizedLine = totalRead == MaximumChunkBytes;
            return new ReadChunk(
                string.Empty,
                oversizedLine ? fromOffset + totalRead : fromOffset + contentStart,
                oversizedLine);
        }

        int contentLength = lastNewline - contentStart + 1;
        string decoded = Utf8.GetString(buffer, contentStart, contentLength)
            .TrimStart('\uFEFF')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        string sanitized = Sanitize(decoded);
        string redacted = ServerLogRedactor.Redact(sanitized);
        return new ReadChunk(
            KeepBoundedLines(redacted),
            fromOffset + lastNewline + 1,
            discardUntilNewline);
    }

    private static string Sanitize(string content)
    {
        string withoutAnsi = AnsiEscapeRegex().Replace(content, string.Empty);
        StringBuilder result = new(withoutAnsi.Length);
        foreach (char character in withoutAnsi)
        {
            if (!char.IsControl(character) || character is '\n' or '\t')
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }

    private static string KeepBoundedLines(string content)
    {
        int lineCount = content.Count(character => character == '\n');
        int start = 0;
        while (lineCount > MaximumChunkLines ||
               Utf8.GetByteCount(content.AsSpan(start)) > MaximumChunkBytes)
        {
            int separator = content.IndexOf('\n', start);
            if (separator < 0)
            {
                return string.Empty;
            }

            start = separator + 1;
            lineCount--;
        }

        return start == 0 ? content : content[start..];
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static AgentServerLogReport Unavailable(
        string sourceIdentifier,
        AgentServerLogStatus status) =>
        new(status, sourceIdentifier, null, null, null, false, null);

    [GeneratedRegex(
        @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex AnsiEscapeRegex();

    private sealed record TailState(
        string SourceIdentifier,
        Guid? StreamId,
        long Offset,
        DateTime CreationTimeUtc,
        string? PrefixFingerprint,
        bool DiscardUntilNewline,
        DateTimeOffset NextReadAt)
    {
        public static TailState Pending(
            string sourceIdentifier,
            DateTimeOffset nextReadAt) =>
            new(
                sourceIdentifier,
                null,
                0,
                default,
                null,
                false,
                nextReadAt);
    }

    private sealed record ReadChunk(
        string Content,
        long ToOffset,
        bool DiscardUntilNewline);
}
