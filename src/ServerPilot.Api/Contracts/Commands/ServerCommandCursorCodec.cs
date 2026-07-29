using System.Text.Json;
using ServerPilot.Application.Commands;

namespace ServerPilot.Api.Contracts.Commands;

internal static class ServerCommandCursorCodec
{
    private const int CurrentVersion = 1;
    private const int MaximumEncodedLength = 512;

    public static string Encode(ServerCommandDetails command)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new CursorPayload(CurrentVersion, command.CreatedAt.ToUniversalTime(), command.Id),
            JsonSerializerOptions.Web);
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? encoded, out ServerCommandHistoryCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > MaximumEncodedLength)
        {
            return false;
        }

        try
        {
            string base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            CursorPayload? payload = JsonSerializer.Deserialize<CursorPayload>(
                Convert.FromBase64String(base64),
                JsonSerializerOptions.Web);
            if (payload is not { Version: CurrentVersion } || payload.Id == Guid.Empty)
            {
                return false;
            }

            cursor = new ServerCommandHistoryCursor(
                payload.CreatedAt.ToUniversalTime(),
                payload.Id);
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private sealed record CursorPayload(int Version, DateTimeOffset CreatedAt, Guid Id);
}
