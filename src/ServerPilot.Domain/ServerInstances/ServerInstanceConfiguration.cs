namespace ServerPilot.Domain.ServerInstances;

public sealed class ServerInstanceConfiguration
{
    public const int MaximumNameLength = 100;
    public const int MaximumExecutablePathLength = 2_048;
    public const int MaximumArgumentsLength = 4_096;
    public const int MaximumWorkingDirectoryLength = 2_048;
    public const int MaximumProcessNameLength = 255;

    private ServerInstanceConfiguration(
        string name,
        string executablePath,
        string arguments,
        string workingDirectory,
        string processName)
    {
        Name = name;
        ExecutablePath = executablePath;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        ProcessName = processName;
    }

    public string Name { get; }

    public string ExecutablePath { get; }

    public string Arguments { get; }

    public string WorkingDirectory { get; }

    public string ProcessName { get; }

    public static bool TryCreate(
        string? name,
        string? executablePath,
        string? arguments,
        string? workingDirectory,
        string? processName,
        out ServerInstanceConfiguration? configuration)
    {
        configuration = null;
        if (!TryNormalizeRequired(name, MaximumNameLength, out string normalizedName) ||
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

        configuration = new ServerInstanceConfiguration(
            normalizedName,
            normalizedExecutablePath,
            normalizedArguments,
            normalizedWorkingDirectory,
            normalizedProcessName);
        return true;
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
