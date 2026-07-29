namespace ServerPilot.Agent.Processes;

public sealed record LocalProcessConfiguration
{
    private const int MaximumPathLength = 1024;
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
            !WindowsPath.IsSafeAbsolute(normalizedExecutablePath))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.InvalidExecutablePath);
        }

        if (normalizedWorkingDirectory.Length is 0 or > MaximumPathLength ||
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
            if (value.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                value.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return false;
            }

            string normalized = value.Replace('/', '\\');
            bool hasDriveRoot = normalized.Length >= 3 &&
                char.IsAsciiLetter(normalized[0]) &&
                normalized[1] == ':' &&
                normalized[2] == '\\';
            bool hasUncRoot = normalized.StartsWith(@"\\", StringComparison.Ordinal) &&
                normalized.Length > 2 &&
                normalized[2] != '\\';

            if (!hasDriveRoot && !hasUncRoot)
            {
                return false;
            }

            return normalized
                .Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .All(segment => segment is not "." and not "..");
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
