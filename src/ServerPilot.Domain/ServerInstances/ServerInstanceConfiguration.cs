namespace ServerPilot.Domain.ServerInstances;

public sealed class ServerInstanceConfiguration
{
    public const int MaximumNameLength = 100;
    public const int MaximumExecutablePathLength = 2_048;
    public const int MaximumArgumentsLength = 4_096;
    public const int MaximumWorkingDirectoryLength = 2_048;
    public const int MaximumProcessNameLength = 255;

    private ServerInstanceConfiguration(
        ServerInstanceProfile profile,
        string name,
        string executablePath,
        string arguments,
        string workingDirectory,
        string processName,
        string? dataDirectory)
    {
        Profile = profile;
        Name = name;
        ExecutablePath = executablePath;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        ProcessName = processName;
        DataDirectory = dataDirectory;
    }

    public ServerInstanceProfile Profile { get; }

    public string Name { get; }

    public string ExecutablePath { get; }

    public string Arguments { get; }

    public string WorkingDirectory { get; }

    public string ProcessName { get; }

    public string? DataDirectory { get; }

    public static bool TryCreate(
        string? name,
        string? executablePath,
        string? arguments,
        string? workingDirectory,
        string? processName,
        out ServerInstanceConfiguration? configuration)
        => TryCreate(
            ServerInstanceProfile.Generic,
            name,
            executablePath,
            arguments,
            workingDirectory,
            processName,
            dataDirectory: null,
            out configuration);

    public static bool TryCreate(
        ServerInstanceProfile profile,
        string? name,
        string? executablePath,
        string? arguments,
        string? workingDirectory,
        string? processName,
        string? dataDirectory,
        out ServerInstanceConfiguration? configuration)
    {
        configuration = null;
        if (!Enum.IsDefined(profile) ||
            !TryNormalizeRequired(name, MaximumNameLength, out string normalizedName) ||
            !TryNormalizeWindowsPath(
                executablePath,
                MaximumExecutablePathLength,
                out string normalizedExecutablePath) ||
            !TryNormalizeOptional(arguments, MaximumArgumentsLength, out string normalizedArguments) ||
            !TryNormalizeWindowsPath(
                workingDirectory,
                MaximumWorkingDirectoryLength,
                out string normalizedWorkingDirectory) ||
            !TryNormalizeProcessName(processName, out string normalizedProcessName))
        {
            return false;
        }

        string? normalizedDataDirectory = null;
        if (profile == ServerInstanceProfile.Generic)
        {
            if (!string.IsNullOrWhiteSpace(dataDirectory))
            {
                return false;
            }
        }
        else if (!TryNormalizeWindowsPath(
                     dataDirectory,
                     MaximumWorkingDirectoryLength,
                     out normalizedDataDirectory) ||
                 !IsValidProjectZomboidConfiguration(
                     normalizedExecutablePath,
                     normalizedArguments,
                     normalizedWorkingDirectory,
                     normalizedProcessName,
                     normalizedDataDirectory))
        {
            return false;
        }

        configuration = new ServerInstanceConfiguration(
            profile,
            normalizedName,
            normalizedExecutablePath,
            normalizedArguments,
            normalizedWorkingDirectory,
            normalizedProcessName,
            normalizedDataDirectory);
        return true;
    }

    private static bool IsValidProjectZomboidConfiguration(
        string executablePath,
        string arguments,
        string workingDirectory,
        string processName,
        string dataDirectory)
    {
        string? launcherDirectory = WindowsPathSyntax.GetDirectoryName(executablePath);
        return string.Equals(
                WindowsPathSyntax.GetFileName(executablePath),
                "StartServer64.bat",
                StringComparison.OrdinalIgnoreCase) &&
            launcherDirectory is not null &&
            WindowsPathSyntax.PathsEqual(launcherDirectory, workingDirectory) &&
            arguments.Length == 0 &&
            string.Equals(processName, "java", StringComparison.OrdinalIgnoreCase) &&
            WindowsPathSyntax.IsSafeCommandArgumentPath(executablePath) &&
            WindowsPathSyntax.IsSafeCommandArgumentPath(workingDirectory) &&
            WindowsPathSyntax.IsSafeCommandArgumentPath(dataDirectory);
    }

    private static bool TryNormalizeWindowsPath(
        string? value,
        int maximumLength,
        out string normalizedValue)
    {
        if (!TryNormalizeRequired(value, maximumLength, out normalizedValue))
        {
            return false;
        }

        if (!WindowsPathSyntax.IsSafeAbsolutePath(normalizedValue))
        {
            normalizedValue = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryNormalizeProcessName(
        string? value,
        out string normalizedValue)
    {
        if (!TryNormalizeRequired(value, MaximumProcessNameLength, out normalizedValue))
        {
            return false;
        }

        if (normalizedValue.Contains('\\') ||
            normalizedValue.Contains('/') ||
            normalizedValue.Contains(':'))
        {
            normalizedValue = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryNormalizeRequired(
        string? value,
        int maximumLength,
        out string normalizedValue)
    {
        normalizedValue = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            return false;
        }

        normalizedValue = normalized;
        return true;
    }

    private static bool TryNormalizeOptional(
        string? value,
        int maximumLength,
        out string normalizedValue)
    {
        normalizedValue = string.Empty;
        if (value is null)
        {
            return true;
        }

        string normalized = value.Trim();
        if (normalized.Length > maximumLength ||
            normalized.Any(char.IsControl))
        {
            normalizedValue = string.Empty;
            return false;
        }

        normalizedValue = normalized;
        return true;
    }
}
