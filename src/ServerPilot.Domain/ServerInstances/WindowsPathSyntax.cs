namespace ServerPilot.Domain.ServerInstances;

public static class WindowsPathSyntax
{
    public static bool IsSafeAbsolutePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string normalized = path.Replace('/', '\\');
        if (IsDeviceNamespace(normalized))
        {
            return false;
        }

        bool isDriveRooted = normalized.Length >= 3 &&
            ((normalized[0] >= 'A' && normalized[0] <= 'Z') ||
             (normalized[0] >= 'a' && normalized[0] <= 'z')) &&
            normalized[1] == ':' &&
            normalized[2] == '\\';
        bool isUncPath = normalized.StartsWith("\\\\", StringComparison.Ordinal) &&
            HasUncServerAndShare(normalized);
        if (!isDriveRooted && !isUncPath)
        {
            return false;
        }

        return !normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");
    }

    private static bool IsDeviceNamespace(string normalizedPath) =>
        normalizedPath.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
        normalizedPath.StartsWith("\\\\.\\", StringComparison.Ordinal) ||
        normalizedPath.StartsWith("\\??\\", StringComparison.Ordinal) ||
        normalizedPath.StartsWith("\\\\??\\", StringComparison.Ordinal);

    private static bool HasUncServerAndShare(string normalizedPath)
    {
        ReadOnlySpan<char> remainder = normalizedPath.AsSpan(2);
        int serverSeparator = remainder.IndexOf('\\');
        if (serverSeparator <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> shareAndPath = remainder[(serverSeparator + 1)..];
        if (shareAndPath.IsEmpty || shareAndPath[0] == '\\')
        {
            return false;
        }

        int shareSeparator = shareAndPath.IndexOf('\\');
        return shareSeparator != 0;
    }
}
