using System.Buffers;

namespace ServerPilot.Agent.Processes;

public sealed record LocalProcessConfiguration
{
    private const int MaximumPathLength = 2_048;
    private const int MaximumArgumentsLength = 4096;
    private const int MaximumProcessNameLength = 255;

    private LocalProcessConfiguration(
        LocalServerProfile profile,
        string executablePath,
        string arguments,
        string workingDirectory,
        string processName,
        string? dataDirectory)
    {
        Profile = profile;
        ExecutablePath = executablePath;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        ProcessName = processName;
        DataDirectory = dataDirectory;
        ManagedExecutablePath = profile == LocalServerProfile.ProjectZomboid
            ? $"{workingDirectory.TrimEnd('\\', '/')}\\jre64\\bin\\java.exe"
            : executablePath;
        ProjectZomboidConfigurationPath = profile == LocalServerProfile.ProjectZomboid
            ? $"{dataDirectory!.TrimEnd('\\', '/')}\\Server\\servertest.ini"
            : null;
    }

    public LocalServerProfile Profile { get; }

    public string ExecutablePath { get; }

    public string Arguments { get; }

    public string WorkingDirectory { get; }

    public string ProcessName { get; }

    public string? DataDirectory { get; }

    public string ManagedExecutablePath { get; }

    public string? ProjectZomboidConfigurationPath { get; }

    public static LocalProcessConfigurationResult Create(
        string? executablePath,
        string? arguments,
        string? workingDirectory,
        string? processName)
        => Create(
            LocalServerProfile.Generic.ToString(),
            executablePath,
            arguments,
            workingDirectory,
            processName,
            dataDirectory: null);

    public static LocalProcessConfigurationResult Create(
        string? profileValue,
        string? executablePath,
        string? arguments,
        string? workingDirectory,
        string? processName,
        string? dataDirectory)
    {
        if (!Enum.TryParse(profileValue, ignoreCase: false, out LocalServerProfile profile) ||
            !Enum.IsDefined(profile))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.UnsupportedProfile);
        }

        string normalizedExecutablePath = executablePath?.Trim() ?? string.Empty;
        string normalizedArguments = arguments?.Trim() ?? string.Empty;
        string normalizedWorkingDirectory = workingDirectory?.Trim() ?? string.Empty;
        string normalizedProcessName = processName?.Trim() ?? string.Empty;
        string? normalizedDataDirectory = dataDirectory?.Trim();

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
        if (profile == LocalServerProfile.Generic &&
            !executableFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.UnsupportedExecutableType);
        }

        if (profile == LocalServerProfile.Generic &&
            !ProcessIdentityPolicy.ProcessNamesEqual(executableFileName, normalizedProcessName))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.ProcessNameMismatch);
        }

        if (profile == LocalServerProfile.ProjectZomboid &&
            !IsValidProjectZomboidConfiguration(
                normalizedExecutablePath,
                normalizedArguments,
                normalizedWorkingDirectory,
                normalizedProcessName,
                normalizedDataDirectory))
        {
            return LocalProcessConfigurationResult.Invalid(
                LocalProcessConfigurationError.InvalidProjectZomboidConfiguration);
        }

        return LocalProcessConfigurationResult.Valid(
            new LocalProcessConfiguration(
                profile,
                normalizedExecutablePath,
                normalizedArguments,
                normalizedWorkingDirectory,
                normalizedProcessName,
                normalizedDataDirectory));
    }

    private static bool IsValidProjectZomboidConfiguration(
        string executablePath,
        string arguments,
        string workingDirectory,
        string processName,
        string? dataDirectory)
    {
        string? launcherDirectory = WindowsPath.GetDirectoryName(executablePath);
        return string.Equals(
                WindowsPath.GetFileName(executablePath),
                "StartServer64.bat",
                StringComparison.OrdinalIgnoreCase) &&
            launcherDirectory is not null &&
            WindowsPath.PathsEqual(launcherDirectory, workingDirectory) &&
            arguments.Length == 0 &&
            string.Equals(processName, "java", StringComparison.OrdinalIgnoreCase) &&
            dataDirectory is not null &&
            dataDirectory.Length is > 0 and <= MaximumPathLength &&
            !dataDirectory.Any(char.IsControl) &&
            WindowsPath.IsSafeCommandArgument(executablePath) &&
            WindowsPath.IsSafeCommandArgument(workingDirectory) &&
            WindowsPath.IsSafeCommandArgument(dataDirectory);
    }

    private static class WindowsPath
    {
        private static readonly SearchValues<char> CommandInterpreterMetacharacters =
            SearchValues.Create(['"', '%', '!', '&', '|', '<', '>', '^']);

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

        public static string? GetDirectoryName(string value)
        {
            int lastSeparator = value.LastIndexOfAny(['\\', '/']);
            return lastSeparator <= 0 ? null : value[..lastSeparator];
        }

        public static bool PathsEqual(string left, string right) =>
            string.Equals(
                left.Replace('/', '\\').TrimEnd('\\'),
                right.Replace('/', '\\').TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);

        public static bool IsSafeCommandArgument(string value) =>
            IsSafeAbsolute(value) &&
            value.AsSpan().IndexOfAny(CommandInterpreterMetacharacters) < 0;
    }
}

public enum LocalServerProfile
{
    Generic = 0,
    ProjectZomboid,
}

public enum LocalProcessConfigurationError
{
    None = 0,
    UnsupportedProfile,
    InvalidExecutablePath,
    InvalidWorkingDirectory,
    InvalidArguments,
    InvalidProcessName,
    UnsupportedExecutableType,
    ProcessNameMismatch,
    InvalidProjectZomboidConfiguration,
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
