namespace ServerPilot.Agent.Processes;

public sealed record ProcessIdentity(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string ExecutablePath,
    string ProcessName);

public sealed record ProcessSnapshot(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string ExecutablePath,
    string ProcessName);

public static class ProcessIdentityPolicy
{
    public static bool Matches(ProcessIdentity expected, ProcessSnapshot actual) =>
        expected.ProcessId == actual.ProcessId &&
        expected.StartedAtUtc == actual.StartedAtUtc &&
        PathsEqual(expected.ExecutablePath, actual.ExecutablePath) &&
        ProcessNamesEqual(expected.ProcessName, actual.ProcessName);

    public static bool ProcessNamesEqual(string left, string right) =>
        string.Equals(
            NormalizeProcessName(left),
            NormalizeProcessName(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.Replace('/', '\\').TrimEnd('\\'),
            right.Replace('/', '\\').TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProcessName(string value)
    {
        string normalized = value.Trim();
        return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^4]
            : normalized;
    }
}
