namespace ServerPilot.Agent.Processes;

public sealed record ProcessIdentity(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string ExecutablePath,
    string ProcessName,
    LocalServerProfile Profile = LocalServerProfile.Generic);

public sealed record ProcessSnapshot(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string ExecutablePath,
    string ProcessName);

public static class ProcessIdentityPolicy
{
    private const long PersistedTimestampPrecisionTicks = TimeSpan.TicksPerMicrosecond;

    public static bool Matches(ProcessIdentity expected, ProcessSnapshot actual) =>
        expected.ProcessId == actual.ProcessId &&
        StartTimesMatch(expected.StartedAtUtc, actual.StartedAtUtc) &&
        ExecutablePathsEqual(expected.ExecutablePath, actual.ExecutablePath) &&
        ProcessNamesEqual(expected.ProcessName, actual.ProcessName);

    public static bool ExecutablePathsEqual(string left, string right) =>
        string.Equals(
            left.Replace('/', '\\').TrimEnd('\\'),
            right.Replace('/', '\\').TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    public static bool ProcessNamesEqual(string left, string right) =>
        string.Equals(
            NormalizeProcessName(left),
            NormalizeProcessName(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool StartTimesMatch(DateTimeOffset expected, DateTimeOffset actual) =>
        Math.Abs(expected.ToUniversalTime().Ticks - actual.ToUniversalTime().Ticks) <
        PersistedTimestampPrecisionTicks;

    private static string NormalizeProcessName(string value)
    {
        string normalized = value.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }
}
