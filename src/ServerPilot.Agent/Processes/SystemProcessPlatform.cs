using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ServerPilot.Agent.Processes;

public sealed class SystemProcessPlatform : IProcessPlatform, IDisposable
{
    private const int MaximumProjectZomboidLauncherBytes = 64 * 1024;
    private static readonly TimeSpan ProjectZomboidStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProjectZomboidSaveDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ProjectZomboidLaunchPollInterval =
        TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentDictionary<int, ProjectZomboidSession> projectZomboidSessions = [];
    private readonly ConcurrentDictionary<int, ProcessIdentity> orphanedProjectZomboidLaunchers = [];

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public Task<ProcessLaunchResult> LaunchAsync(
        LocalProcessConfiguration configuration,
        CancellationToken cancellationToken) =>
        configuration.Profile == LocalServerProfile.ProjectZomboid
            ? LaunchProjectZomboidAsync(configuration, cancellationToken)
            : Task.FromResult(LaunchNative(configuration));

    private static ProcessLaunchResult LaunchNative(LocalProcessConfiguration configuration)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = configuration.ExecutablePath,
            Arguments = configuration.Arguments,
            WorkingDirectory = configuration.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process? process = null;
        bool identityCaptured = false;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
            }

            ProcessIdentity identity = new(
                process.Id,
                new DateTimeOffset(process.StartTime.ToUniversalTime()),
                configuration.ExecutablePath,
                configuration.ProcessName,
                configuration.Profile);
            identityCaptured = true;

            return new ProcessLaunchResult(ProcessPlatformStatus.Succeeded, identity);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.AccessDenied);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.AccessDenied);
        }
        catch (Win32Exception)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
        }
        catch (InvalidOperationException)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
        }
        catch (NotSupportedException)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
        }
        finally
        {
            if (!identityCaptured && process is not null)
            {
                TryTerminateStartedProcess(process);
            }

            process?.Dispose();
        }
    }

    public ProcessLookupResult Lookup(ProcessIdentity identity)
    {
        try
        {
            using Process process = Process.GetProcessById(identity.ProcessId);
            if (process.HasExited)
            {
                CleanupProjectZomboidLauncher(identity);
                return new ProcessLookupResult(ProcessPlatformStatus.Exited);
            }

            ProcessSnapshot? snapshot = CreateSnapshot(process);
            if (snapshot is null)
            {
                return new ProcessLookupResult(ProcessPlatformStatus.Failed);
            }

            return new ProcessLookupResult(ProcessPlatformStatus.Succeeded, snapshot);
        }
        catch (ArgumentException)
        {
            CleanupProjectZomboidLauncher(identity);
            return new ProcessLookupResult(ProcessPlatformStatus.NotFound);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            return new ProcessLookupResult(ProcessPlatformStatus.AccessDenied);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProcessLookupResult(ProcessPlatformStatus.AccessDenied);
        }
        catch (Win32Exception)
        {
            return new ProcessLookupResult(ProcessPlatformStatus.Failed);
        }
        catch (InvalidOperationException)
        {
            CleanupProjectZomboidLauncher(identity);
            return new ProcessLookupResult(ProcessPlatformStatus.Exited);
        }
        catch (NotSupportedException)
        {
            return new ProcessLookupResult(ProcessPlatformStatus.Failed);
        }
    }

    public Task<ProcessSignalResult> RequestGracefulStopAsync(
        ProcessIdentity identity,
        CancellationToken cancellationToken) =>
        identity.Profile == LocalServerProfile.ProjectZomboid
            ? RequestProjectZomboidGracefulStopAsync(identity, cancellationToken)
            : Task.FromResult(RequestNativeGracefulStop(identity));

    private static ProcessSignalResult RequestNativeGracefulStop(ProcessIdentity identity)
    {
        ProcessPlatformStatus openStatus = OpenExpectedProcess(identity, out Process? process);
        if (openStatus != ProcessPlatformStatus.Succeeded || process is null)
        {
            return new ProcessSignalResult(openStatus);
        }

        using (process)
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    return new ProcessSignalResult(ProcessPlatformStatus.NotSupported);
                }

                return new ProcessSignalResult(
                    process.CloseMainWindow()
                        ? ProcessPlatformStatus.Succeeded
                        : ProcessPlatformStatus.NotSupported);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
            {
                return new ProcessSignalResult(ProcessPlatformStatus.AccessDenied);
            }
            catch (Win32Exception)
            {
                return new ProcessSignalResult(ProcessPlatformStatus.Failed);
            }
            catch (InvalidOperationException)
            {
                return new ProcessSignalResult(ProcessPlatformStatus.Exited);
            }
            catch (NotSupportedException)
            {
                return new ProcessSignalResult(ProcessPlatformStatus.NotSupported);
            }
        }
    }

    public ProcessSignalResult ForceStop(ProcessIdentity identity)
    {
        if (identity.Profile == LocalServerProfile.ProjectZomboid &&
            !projectZomboidSessions.ContainsKey(identity.ProcessId))
        {
            ProcessIdentity? launcher = TryCaptureProjectZomboidLauncher(identity.ProcessId);
            if (launcher is not null)
            {
                orphanedProjectZomboidLaunchers.TryAdd(identity.ProcessId, launcher);
            }
        }

        ProcessPlatformStatus openStatus = OpenExpectedProcess(identity, out Process? process);
        if (openStatus != ProcessPlatformStatus.Succeeded || process is null)
        {
            if (openStatus is ProcessPlatformStatus.NotFound or ProcessPlatformStatus.Exited)
            {
                CleanupProjectZomboidLauncher(identity);
            }

            return new ProcessSignalResult(openStatus);
        }

        using (process)
        {
            try
            {
                process.Kill();
                return new ProcessSignalResult(ProcessPlatformStatus.Succeeded);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
            {
                return new ProcessSignalResult(ProcessPlatformStatus.AccessDenied);
            }
            catch (Win32Exception)
            {
                return new ProcessSignalResult(ProcessPlatformStatus.Failed);
            }
            catch (InvalidOperationException)
            {
                return new ProcessSignalResult(ProcessPlatformStatus.Exited);
            }
            catch (NotSupportedException)
            {
                return new ProcessSignalResult(ProcessPlatformStatus.Failed);
            }
        }
    }

    public async Task<ProcessWaitResult> WaitForExitAsync(
        ProcessIdentity identity,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ProcessPlatformStatus openStatus = OpenExpectedProcess(identity, out Process? process);
        if (openStatus is ProcessPlatformStatus.NotFound or ProcessPlatformStatus.Exited)
        {
            return new ProcessWaitResult(ProcessPlatformStatus.Exited);
        }

        if (openStatus != ProcessPlatformStatus.Succeeded || process is null)
        {
            return new ProcessWaitResult(openStatus);
        }

        using (process)
        using (CancellationTokenSource timeoutSource = new(timeout))
        using (CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                   cancellationToken,
                   timeoutSource.Token))
        {
            try
            {
                await process.WaitForExitAsync(linkedSource.Token);
                CleanupProjectZomboidLauncher(identity);
                return new ProcessWaitResult(ProcessPlatformStatus.Exited);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new ProcessWaitResult(ProcessPlatformStatus.TimedOut);
            }
            catch (Win32Exception)
            {
                return new ProcessWaitResult(ProcessPlatformStatus.Failed);
            }
            catch (InvalidOperationException)
            {
                CleanupProjectZomboidLauncher(identity);
                return new ProcessWaitResult(ProcessPlatformStatus.Exited);
            }
            catch (NotSupportedException)
            {
                return new ProcessWaitResult(ProcessPlatformStatus.Failed);
            }
        }
    }

    public void Dispose()
    {
        foreach ((_, ProjectZomboidSession session) in projectZomboidSessions)
        {
            session.Dispose();
        }

        projectZomboidSessions.Clear();
        orphanedProjectZomboidLaunchers.Clear();
    }

    private async Task<ProcessLaunchResult> LaunchProjectZomboidAsync(
        LocalProcessConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!ProjectZomboidLauncherForwardsArguments(configuration.ExecutablePath))
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.InvalidConfiguration);
        }

        string commandInterpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        ProcessStartInfo startInfo = new()
        {
            FileName = commandInterpreter,
            Arguments = CreateProjectZomboidCommandArguments(configuration),
            WorkingDirectory = configuration.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
        };

        Process? launcher = null;
        bool sessionCaptured = false;
        try
        {
            launcher = Process.Start(startInfo);
            if (launcher is null)
            {
                return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
            }

            DateTimeOffset deadline = TimeProvider.System.GetUtcNow() +
                ProjectZomboidStartupTimeout;
            while (TimeProvider.System.GetUtcNow() < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (launcher.HasExited)
                {
                    return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
                }

                ProcessSnapshot? child = FindExpectedDescendant(
                    launcher.Id,
                    configuration.ManagedExecutablePath,
                    configuration.ProcessName);
                if (child is not null)
                {
                    ProcessIdentity identity = new(
                        child.ProcessId,
                        child.StartedAtUtc,
                        child.ExecutablePath,
                        child.ProcessName,
                        LocalServerProfile.ProjectZomboid);
                    ProjectZomboidSession session = new(launcher);
                    if (!projectZomboidSessions.TryAdd(identity.ProcessId, session))
                    {
                        session.Dispose();
                        return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
                    }

                    sessionCaptured = true;
                    return new ProcessLaunchResult(ProcessPlatformStatus.Succeeded, identity);
                }

                await Task.Delay(ProjectZomboidLaunchPollInterval, cancellationToken);
            }

            return new ProcessLaunchResult(ProcessPlatformStatus.TimedOut);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.AccessDenied);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.AccessDenied);
        }
        catch (Win32Exception)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
        }
        catch (InvalidOperationException)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
        }
        catch (NotSupportedException)
        {
            return new ProcessLaunchResult(ProcessPlatformStatus.Failed);
        }
        finally
        {
            if (!sessionCaptured && launcher is not null)
            {
                TryTerminateStartedProcess(launcher, entireProcessTree: true);
                launcher.Dispose();
            }
        }
    }

    private async Task<ProcessSignalResult> RequestProjectZomboidGracefulStopAsync(
        ProcessIdentity identity,
        CancellationToken cancellationToken)
    {
        ProcessPlatformStatus openStatus = OpenExpectedProcess(identity, out Process? process);
        process?.Dispose();
        if (openStatus != ProcessPlatformStatus.Succeeded)
        {
            return new ProcessSignalResult(openStatus);
        }

        if (!projectZomboidSessions.TryGetValue(
                identity.ProcessId,
                out ProjectZomboidSession? session))
        {
            return new ProcessSignalResult(ProcessPlatformStatus.NotSupported);
        }

        try
        {
            session.WriteCommand("save");
            await Task.Delay(ProjectZomboidSaveDelay, cancellationToken);

            openStatus = OpenExpectedProcess(identity, out process);
            process?.Dispose();
            if (openStatus != ProcessPlatformStatus.Succeeded)
            {
                return new ProcessSignalResult(openStatus);
            }

            session.WriteCommand("quit");
            return new ProcessSignalResult(ProcessPlatformStatus.Succeeded);
        }
        catch (IOException)
        {
            return new ProcessSignalResult(ProcessPlatformStatus.Failed);
        }
        catch (ObjectDisposedException)
        {
            return new ProcessSignalResult(ProcessPlatformStatus.Failed);
        }
        catch (InvalidOperationException)
        {
            return new ProcessSignalResult(ProcessPlatformStatus.Failed);
        }
    }

    private static string CreateProjectZomboidCommandArguments(
        LocalProcessConfiguration configuration) =>
        $"/d /s /c \"\"{configuration.ExecutablePath}\" " +
        $"\"-cachedir={configuration.DataDirectory}\"\"";

    private static bool ProjectZomboidLauncherForwardsArguments(string path)
    {
        try
        {
            FileInfo file = new(path);
            if (file.Length is <= 0 or > MaximumProjectZomboidLauncherBytes)
            {
                return false;
            }

            string launcher = File.ReadAllText(path);
            return launcher
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("::", StringComparison.Ordinal))
                .Any(line =>
                    line.Contains("zombie.network.GameServer", StringComparison.Ordinal) &&
                    (line.Contains("%1", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("%*", StringComparison.OrdinalIgnoreCase)));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ProcessSnapshot? FindExpectedDescendant(
        int rootProcessId,
        string expectedExecutablePath,
        string expectedProcessName)
    {
        HashSet<int> descendants = WindowsProcessTree.GetDescendants(rootProcessId);
        foreach (int processId in descendants)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    continue;
                }

                ProcessSnapshot? snapshot = CreateSnapshot(process);
                if (snapshot is not null &&
                    ProcessIdentityPolicy.ExecutablePathsEqual(
                        expectedExecutablePath,
                        snapshot.ExecutablePath) &&
                    ProcessIdentityPolicy.ProcessNamesEqual(
                        expectedProcessName,
                        snapshot.ProcessName))
                {
                    return snapshot;
                }
            }
            catch (ArgumentException)
            {
                // The candidate exited while the process tree was inspected.
            }
            catch (Win32Exception)
            {
                // An inaccessible descendant is not the expected managed process.
            }
            catch (InvalidOperationException)
            {
                // The candidate exited while its identity was captured.
            }
        }

        return null;
    }

    private static ProcessIdentity? TryCaptureProjectZomboidLauncher(int processId)
    {
        int? parentProcessId = WindowsProcessTree.GetParentProcessId(processId);
        if (parentProcessId is null)
        {
            return null;
        }

        try
        {
            using Process parent = Process.GetProcessById(parentProcessId.Value);
            ProcessSnapshot? snapshot = CreateSnapshot(parent);
            string expectedCommandInterpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            return snapshot is not null &&
                ProcessIdentityPolicy.ExecutablePathsEqual(
                    expectedCommandInterpreter,
                    snapshot.ExecutablePath) &&
                ProcessIdentityPolicy.ProcessNamesEqual("cmd", snapshot.ProcessName)
                    ? new ProcessIdentity(
                        snapshot.ProcessId,
                        snapshot.StartedAtUtc,
                        snapshot.ExecutablePath,
                        snapshot.ProcessName)
                    : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void CleanupProjectZomboidLauncher(ProcessIdentity identity)
    {
        if (identity.Profile != LocalServerProfile.ProjectZomboid)
        {
            return;
        }

        if (projectZomboidSessions.TryRemove(
                identity.ProcessId,
                out ProjectZomboidSession? session))
        {
            session.StopLauncher();
            session.Dispose();
            return;
        }

        if (orphanedProjectZomboidLaunchers.TryRemove(
                identity.ProcessId,
                out ProcessIdentity? launcherIdentity))
        {
            TryStopExpectedProcess(launcherIdentity);
        }
    }

    private static void TryStopExpectedProcess(ProcessIdentity identity)
    {
        ProcessPlatformStatus status = OpenExpectedProcess(identity, out Process? process);
        if (status != ProcessPlatformStatus.Succeeded || process is null)
        {
            return;
        }

        using (process)
        {
            TryTerminateStartedProcess(process, entireProcessTree: true);
        }
    }

    private static ProcessPlatformStatus OpenExpectedProcess(
        ProcessIdentity expected,
        out Process? process)
    {
        process = null;

        try
        {
            Process candidate = Process.GetProcessById(expected.ProcessId);
            if (candidate.HasExited)
            {
                candidate.Dispose();
                return ProcessPlatformStatus.Exited;
            }

            ProcessSnapshot? snapshot = CreateSnapshot(candidate);
            if (snapshot is null)
            {
                candidate.Dispose();
                return ProcessPlatformStatus.Failed;
            }

            if (!ProcessIdentityPolicy.Matches(expected, snapshot))
            {
                candidate.Dispose();
                return ProcessPlatformStatus.IdentityMismatch;
            }

            process = candidate;
            return ProcessPlatformStatus.Succeeded;
        }
        catch (ArgumentException)
        {
            return ProcessPlatformStatus.NotFound;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            return ProcessPlatformStatus.AccessDenied;
        }
        catch (UnauthorizedAccessException)
        {
            return ProcessPlatformStatus.AccessDenied;
        }
        catch (Win32Exception)
        {
            return ProcessPlatformStatus.Failed;
        }
        catch (InvalidOperationException)
        {
            return ProcessPlatformStatus.Exited;
        }
        catch (NotSupportedException)
        {
            return ProcessPlatformStatus.Failed;
        }
    }

    private static ProcessSnapshot? CreateSnapshot(Process process)
    {
        string? executablePath = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        return new ProcessSnapshot(
            process.Id,
            new DateTimeOffset(process.StartTime.ToUniversalTime()),
            executablePath,
            process.ProcessName);
    }

    private static void TryTerminateStartedProcess(
        Process process,
        bool entireProcessTree = false)
    {
        try
        {
            if (!process.HasExited)
            {
                if (entireProcessTree)
                {
                    process.Kill(entireProcessTree: true);
                }
                else
                {
                    process.Kill();
                }
            }
        }
        catch (Win32Exception)
        {
            // Launch already failed; the caller receives a typed failure.
        }
        catch (InvalidOperationException)
        {
            // The process exited while its failed launch was being cleaned up.
        }
        catch (NotSupportedException)
        {
            // Launch already failed; the caller receives a typed failure.
        }
    }

    private sealed class ProjectZomboidSession(Process launcher) : IDisposable
    {
        public void WriteCommand(string command)
        {
            launcher.StandardInput.WriteLine(command);
            launcher.StandardInput.Flush();
        }

        public void StopLauncher() =>
            TryTerminateStartedProcess(launcher, entireProcessTree: true);

        public void Dispose()
        {
            launcher.StandardInput.Dispose();
            launcher.Dispose();
        }
    }

    private static class WindowsProcessTree
    {
        public static HashSet<int> GetDescendants(int rootProcessId)
        {
            if (!OperatingSystem.IsWindows())
            {
                return [];
            }

            Dictionary<int, List<int>> childrenByParent = [];
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    int? parentId = TryGetParentProcessId(process);
                    if (parentId is null)
                    {
                        continue;
                    }

                    if (!childrenByParent.TryGetValue(parentId.Value, out List<int>? children))
                    {
                        children = [];
                        childrenByParent.Add(parentId.Value, children);
                    }

                    children.Add(process.Id);
                }
            }

            HashSet<int> descendants = [];
            Queue<int> pending = new();
            pending.Enqueue(rootProcessId);
            while (pending.TryDequeue(out int parentId))
            {
                if (!childrenByParent.TryGetValue(parentId, out List<int>? children))
                {
                    continue;
                }

                foreach (int childId in children)
                {
                    if (descendants.Add(childId))
                    {
                        pending.Enqueue(childId);
                    }
                }
            }

            return descendants;
        }

        public static int? GetParentProcessId(int processId)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                using Process process = Process.GetProcessById(processId);
                return TryGetParentProcessId(process);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static int? TryGetParentProcessId(Process process)
        {
            try
            {
                int status = NtQueryInformationProcess(
                    process.SafeHandle,
                    processInformationClass: 0,
                    out ProcessBasicInformation information,
                    (uint)Marshal.SizeOf<ProcessBasicInformation>(),
                    out _);
                long parentId = information.InheritedFromUniqueProcessId.ToInt64();
                return status == 0 && parentId is > 0 and <= int.MaxValue
                    ? (int)parentId
                    : null;
            }
            catch (Win32Exception)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtQueryInformationProcess(
        SafeProcessHandle processHandle,
        int processInformationClass,
        out ProcessBasicInformation processInformation,
        uint processInformationLength,
        out uint returnLength);
}
