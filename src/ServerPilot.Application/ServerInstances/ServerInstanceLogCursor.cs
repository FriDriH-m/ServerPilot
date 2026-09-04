using System.Globalization;

namespace ServerPilot.Application.ServerInstances;

public sealed record ServerInstanceLogCursor(Guid StreamId, long Offset)
{
    public override string ToString() =>
        $"{StreamId:N}:{Offset.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(
        string? value,
        out ServerInstanceLogCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Length > 128)
        {
            return false;
        }

        string[] parts = value.Split(':', StringSplitOptions.None);
        if (parts.Length != 2 ||
            !Guid.TryParseExact(parts[0], "N", out Guid streamId) ||
            streamId == Guid.Empty ||
            !long.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long offset) ||
            offset < 0)
        {
            return false;
        }

        cursor = new ServerInstanceLogCursor(streamId, offset);
        return true;
    }
}
