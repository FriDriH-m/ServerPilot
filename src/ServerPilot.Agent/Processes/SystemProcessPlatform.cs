using System.ComponentModel;
using System.Diagnostics;

namespace ServerPilot.Agent.Processes;

public sealed class SystemProcessPlatform : IProcessPlatform
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public ProcessLaunchResult Launch(LocalProcessConfiguration configuration)
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
                configuration.ProcessName);
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

    public ProcessLookupResult Lookup(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
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
            return new ProcessLookupResult(ProcessPlatformStatus.Exited);
        }
        catch (NotSupportedException)
        {
            return new ProcessLookupResult(ProcessPlatformStatus.Failed);
        }
    }

    public ProcessSignalResult RequestGracefulStop(ProcessIdentity identity)
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
        ProcessPlatformStatus openStatus = OpenExpectedProcess(identity, out Process? process);
        if (openStatus != ProcessPlatformStatus.Succeeded || process is null)
        {
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
                return new ProcessWaitResult(ProcessPlatformStatus.Exited);
            }
            catch (NotSupportedException)
            {
                return new ProcessWaitResult(ProcessPlatformStatus.Failed);
            }
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

    private static void TryTerminateStartedProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
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
}
