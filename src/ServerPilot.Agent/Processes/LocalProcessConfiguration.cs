namespace ServerPilot.Agent.Processes;

public sealed record LocalProcessConfiguration
{
    private const int MaximumPathLength = 2_048;
    private const int MaximumArgumentsLength = 4096;
    private const int MaximumProcessNameLength = 255;

    private LocalProcessConfiguration(
        string executablePath,
        string arguments,
        string workingDirectory,
        string processName)
    {
        ExecutablePath = executablePath;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        ProcessName = processName;
    }

    public string ExecutablePath { get; }

    public string Arguments { get; }

    public string WorkingDirectory { get; }

    public string ProcessName { get; }

    public static LocalProcessConfigurationResult Create(
        string? executablePath,
        string? arguments,
        string? workingDirectory,
        string? processName)
    {
        string normalizedExecutablePath = executablePath?.Trim() ?? string.Empty;
        string normalizedArguments = arguments?.Trim() ?? string.Empty;
        string normalizedWorkingDirectory = workingDirectory?.Trim() ?? string.Empty;
        string normalizedProcessName = processName?.Trim() ?? string.Empty;

        if (normalizedExecutablePath.Length is 0 or > MaximumPathLength ||
            normalizedExecutablePath.Any(char.IsControl) ||
            !WindowsPath.IsSafeAbsolute(normalizedExecutablePath))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.InvalidExecutablePath);
        }

        if (normalizedWorkingDirectory.Length is 0 or > MaximumPathLength ||
            normalizedWorkingDirectory.Any(char.IsControl) ||
            !WindowsPath.IsSafeAbsolute(normalizedWorkingDirectory))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.InvalidWorkingDirectory);
        }

        if (normalizedArguments.Length > MaximumArgumentsLength ||
            normalizedArguments.Any(char.IsControl))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.InvalidArguments);
        }

        if (normalizedProcessName.Length is 0 or > MaximumProcessNameLength ||
            normalizedProcessName.Any(char.IsControl) ||
            normalizedProcessName.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.InvalidProcessName);
        }

        string executableFileName = WindowsPath.GetFileName(normalizedExecutablePath);
        if (!executableFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.UnsupportedExecutableType);
        }

        if (!ProcessIdentityPolicy.ProcessNamesEqual(executableFileName, normalizedProcessName))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.ProcessNameMismatch);
        }

        return LocalProcessConfigurationResult.Valid(
            new LocalProcessConfiguration(
                normalizedExecutablePath,
                normalizedArguments,
                normalizedWorkingDirectory,
                normalizedProcessName));
    }

    private static class WindowsPath
    {
        public static bool IsSafeAbsolute(string value)
        {
            string normalized = value.Replace('/', '\\');
            if (IsDeviceNamespace(normalized))
            {
                return false;
            }

            bool hasDriveRoot = normalized.Length >= 3 &&
                char.IsAsciiLetter(normalized[0]) &&
                normalized[1] == ':' &&
                normalized[2] == '\\';
            bool hasUncRoot = normalized.StartsWith(@"\\", StringComparison.Ordinal) &&
                HasUncServerAndShare(normalized);

            if (!hasDriveRoot && !hasUncRoot)
            {
                return false;
            }

            return normalized
                .Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .All(segment => segment is not "." and not "..");
        }

        private static bool IsDeviceNamespace(string normalizedPath) =>
            normalizedPath.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            normalizedPath.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            normalizedPath.StartsWith(@"\??\", StringComparison.Ordinal) ||
            normalizedPath.StartsWith(@"\\??\", StringComparison.Ordinal);

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

        public static string GetFileName(string value)
        {
            int lastSeparator = value.LastIndexOfAny(['\\', '/']);
            return lastSeparator < 0 ? value : value[(lastSeparator + 1)..];
        }
    }
}

public enum LocalProcessConfigurationError
{
    None = 0,
    InvalidExecutablePath,
    InvalidWorkingDirectory,
    InvalidArguments,
    InvalidProcessName,
    UnsupportedExecutableType,
    ProcessNameMismatch,
}

public sealed record LocalProcessConfigurationResult(
    bool IsValid,
    LocalProcessConfiguration? Configuration,
    LocalProcessConfigurationError Error)
{
    internal static LocalProcessConfigurationResult Valid(LocalProcessConfiguration configuration) =>
        new(true, configuration, LocalProcessConfigurationError.None);

    internal static LocalProcessConfigurationResult Invalid(LocalProcessConfigurationError error) =>
        new(false, null, error);
}
