using System.Text;

namespace ServerPilot.Domain.ServerInstances;

public sealed record ServerInstanceLogReport(
    ServerInstanceLogStatus Status,
    string SourceIdentifier,
    Guid? StreamId,
    long? FromOffset,
    long? ToOffset,
    bool Reset,
    string? Content)
{
    public const int MaximumChunkBytes = 16 * 1024;
    public const int MaximumChunkLines = 200;
    public const int MaximumWindowBytes = 32 * 1024;
    public const int MaximumWindowLines = 400;

    public bool IsValid
    {
        get
        {
            if (!Enum.IsDefined(Status) ||
                !ServerLogSourceIdentifier.IsValid(SourceIdentifier))
            {
                return false;
            }

            if (Status != ServerInstanceLogStatus.Available)
            {
                return StreamId is null &&
                    FromOffset is null &&
                    ToOffset is null &&
                    !Reset &&
                    Content is null;
            }

            return StreamId is Guid streamId &&
                streamId != Guid.Empty &&
                FromOffset is >= 0 &&
                ToOffset >= FromOffset &&
                Content is not null &&
                (Content.Length == 0 || Content.EndsWith('\n')) &&
                HasSafeCharacters(Content) &&
                Encoding.UTF8.GetByteCount(Content) <= MaximumChunkBytes &&
                CountLines(Content) <= MaximumChunkLines;
        }
    }

    internal static int CountLines(string content) =>
        content.Count(character => character == '\n');

    private static bool HasSafeCharacters(string content) =>
        content.All(character =>
            !char.IsControl(character) || character is '\n' or '\t');
}
