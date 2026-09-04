using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Logs;
using ServerPilot.Agent.Looping;
using ServerPilot.Agent.Processes;

namespace ServerPilot.UnitTests.AgentProcesses;

public sealed class AgentProcessStateReconcilerTests
{
    private static readonly ProcessIdentity Identity = new(
        42,
        new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
        @"C:\Servers\server.exe",
        "server");

    [Fact]
    public async Task RestoresPersistedIdentityAndReportsRunning()
    {
        RecordingApiClient apiClient = new(CreateAssignment(AgentServerInstanceStatus.Running));
        RecordingRegistry registry = new(
            new FakeSupervisor(new ProcessSupervisorResult(
                ProcessSupervisorStatus.Running,
                Identity,
                Snapshot: CreateSnapshot())));
        AgentProcessStateReconciler reconciler = CreateReconciler(apiClient, registry);

        await reconciler.ReconcileAsync(CreateCredential(), CancellationToken.None);

        Assert.Equal(Identity, registry.Request?.TrackedIdentity);
        Assert.Equal(AgentServerInstanceStatus.Running, apiClient.Report?.Status);
        Assert.Equal(Identity, apiClient.Report?.Identity);
        Assert.Equal(128 * 1024 * 1024, apiClient.Report?.Metrics?.WorkingSetBytes);
    }

    [Theory]
    [InlineData(ProcessSupervisorStatus.NotRunning)]
    [InlineData(ProcessSupervisorStatus.StaleProcessId)]
    public async Task MissingPreviouslyRunningProcessIsReportedCrashed(
        ProcessSupervisorStatus inspectionStatus)
    {
        RecordingApiClient apiClient = new(CreateAssignment(AgentServerInstanceStatus.Running));
        RecordingRegistry registry = new(
            new FakeSupervisor(new ProcessSupervisorResult(inspectionStatus, Identity)));
        AgentProcessStateReconciler reconciler = CreateReconciler(apiClient, registry);

        await reconciler.ReconcileAsync(CreateCredential(), CancellationToken.None);

        Assert.Equal(AgentServerInstanceStatus.Crashed, apiClient.Report?.Status);
        Assert.Null(apiClient.Report?.Identity);
    }

    [Fact]
    public async Task InspectionFailureDoesNotFabricateStoppedState()
    {
        RecordingApiClient apiClient = new(CreateAssignment(
            AgentServerInstanceStatus.Running,
            projectZomboid: true));
        RecordingRegistry registry = new(
            new FakeSupervisor(new ProcessSupervisorResult(
                ProcessSupervisorStatus.Failed,
                Identity,
                ProcessSupervisorFailure.AccessDenied)));
        RecordingLogTailReader logTailReader = new();
        AgentProcessStateReconciler reconciler = CreateReconciler(
            apiClient,
            registry,
            logTailReader);

        await reconciler.ReconcileAsync(CreateCredential(), CancellationToken.None);

        Assert.Null(apiClient.Report);
        Assert.Equal(0, logTailReader.ReadCount);
    }

    [Fact]
    public async Task SuccessfulInspectionReadsAndReportsConfiguredLog()
    {
        RecordingApiClient apiClient = new(CreateAssignment(
            AgentServerInstanceStatus.Stopped,
            projectZomboid: true));
        RecordingRegistry registry = new(
            new FakeSupervisor(new ProcessSupervisorResult(
                ProcessSupervisorStatus.NotRunning)));
        AgentServerLogReport expectedLog = new(
            AgentServerLogStatus.Missing,
            new string('A', 64),
            null,
            null,
            null,
            false,
            null);
        RecordingLogTailReader logTailReader = new(expectedLog);
        AgentProcessStateReconciler reconciler = CreateReconciler(
            apiClient,
            registry,
            logTailReader);

        await reconciler.ReconcileAsync(CreateCredential(), CancellationToken.None);

        Assert.Equal(1, logTailReader.ReadCount);
        Assert.Equal(expectedLog, apiClient.Report?.Log);
    }

    private static AgentProcessStateReconciler CreateReconciler(
        RecordingApiClient apiClient,
        RecordingRegistry registry,
        IServerLogTailReader? logTailReader = null) => new(
            apiClient,
            new AgentRetryExecutor(new ImmediateDelay()),
            registry,
            new ProcessMetricsSampler(TimeProvider.System),
            logTailReader ?? new EmptyLogTailReader(),
            NullLogger<AgentProcessStateReconciler>.Instance);

    private static AssignedAgentServerInstance CreateAssignment(
        AgentServerInstanceStatus status,
        bool projectZomboid = false) => new(
            Guid.NewGuid(),
            projectZomboid ? "ProjectZomboid" : "Generic",
            Identity.ExecutablePath,
            string.Empty,
            @"C:\Servers",
            Identity.ProcessName,
            projectZomboid ? @"C:\Zomboid" : null,
            status,
            status == AgentServerInstanceStatus.Running ? Identity : null,
            DateTimeOffset.UtcNow,
            projectZomboid ? new string('A', 64) : null);

    private static AgentCredential CreateCredential() => AgentCredential.Create(
        Guid.NewGuid(),
        "spac_0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        AgentCredential.ExpectedAuthorizationScheme);

    private static ProcessSnapshot CreateSnapshot() => new(
        Identity.ProcessId,
        Identity.StartedAtUtc,
        Identity.ExecutablePath,
        Identity.ProcessName,
        TimeSpan.FromSeconds(5),
        128 * 1024 * 1024);

    private sealed class RecordingApiClient(AssignedAgentServerInstance assignment)
        : IAgentApiClient
    {
        public AgentProcessStateReport? Report { get; private set; }

        public Task SendHeartbeatAsync(
            AgentCredential credential,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AssignedAgentServerInstance>> ListServerInstancesAsync(
            AgentCredential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedAgentServerInstance>>([assignment]);

        public Task ReportServerInstanceStateAsync(
            AgentCredential credential,
            Guid serverInstanceId,
            AgentProcessStateReport report,
            CancellationToken cancellationToken)
        {
            Report = report;
            return Task.CompletedTask;
        }

        public Task<ClaimedAgentCommand?> ClaimNextCommandAsync(
            AgentCredential credential,
            CancellationToken cancellationToken) => Task.FromResult<ClaimedAgentCommand?>(null);

        public Task MarkCommandRunningAsync(
            AgentCredential credential,
            ClaimedAgentCommand command,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CompleteCommandAsync(
            AgentCredential credential,
            ClaimedAgentCommand command,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task FailCommandAsync(
            AgentCredential credential,
            ClaimedAgentCommand command,
            string errorCode,
            string errorMessage,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingRegistry(IProcessSupervisor supervisor)
        : IProcessSupervisorRegistry
    {
        public ProcessSupervisorRequest? Request { get; private set; }

        public ProcessSupervisorResolution Resolve(
            Guid serverInstanceId,
            ProcessSupervisorRequest request)
        {
            Request = request;
            return ProcessSupervisorResolution.Succeeded(supervisor);
        }
    }

    private sealed class FakeSupervisor(ProcessSupervisorResult inspection)
        : IProcessSupervisor
    {
        public Task<ProcessSupervisorResult> StartAsync(CancellationToken cancellationToken) =>
            Task.FromResult(inspection);

        public Task<ProcessSupervisorResult> InspectAsync(CancellationToken cancellationToken) =>
            Task.FromResult(inspection);

        public Task<ProcessSupervisorResult> StopAsync(CancellationToken cancellationToken) =>
            Task.FromResult(inspection);
    }

    private sealed class ImmediateDelay : IAgentDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class EmptyLogTailReader : IServerLogTailReader
    {
        public Task<AgentServerLogReport?> ReadAsync(
            Guid serverInstanceId,
            ServerLogSource source,
            CancellationToken cancellationToken) =>
            Task.FromResult<AgentServerLogReport?>(null);

        public void Retain(IReadOnlySet<Guid> serverInstanceIds)
        {
        }
    }

    private sealed class RecordingLogTailReader(AgentServerLogReport? report = null)
        : IServerLogTailReader
    {
        public int ReadCount { get; private set; }

        public Task<AgentServerLogReport?> ReadAsync(
            Guid serverInstanceId,
            ServerLogSource source,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(report);
        }

        public void Retain(IReadOnlySet<Guid> serverInstanceIds)
        {
        }
    }
}
