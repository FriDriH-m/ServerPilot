using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Execution;
using ServerPilot.Agent.Looping;
using ServerPilot.Agent.Processes;
using ServerPilot.UnitTests.Infrastructure;

namespace ServerPilot.UnitTests.AgentExecution;

public sealed class AgentCommandExecutorTests
{
    [Fact]
    public async Task StartMarksRunningExecutesVerifiesAndCompletesInOrder()
    {
        List<string> events = [];
        RecordingApiClient apiClient = new(events);
        FakeProcessSupervisor supervisor = new(events)
        {
            StartResult = new ProcessSupervisorResult(ProcessSupervisorStatus.Started),
        };
        supervisor.InspectionResults.Enqueue(
            new ProcessSupervisorResult(
                ProcessSupervisorStatus.Running,
                CreateIdentity()));
        AgentCommandExecutor executor = CreateExecutor(apiClient, supervisor);

        await executor.ExecuteAsync(
            CreateCredential(),
            new AgentCommandExecution(CreateCommand(AgentCommandType.StartServer)),
            CancellationToken.None);

        Assert.Equal(
            ["api:start", "process:start", "process:inspect", "api:state", "api:complete"],
            events);
    }

    [Fact]
    public async Task StopAlreadyStoppedIsVerifiedAndCompleted()
    {
        List<string> events = [];
        RecordingApiClient apiClient = new(events);
        FakeProcessSupervisor supervisor = new(events)
        {
            StopResult = new ProcessSupervisorResult(ProcessSupervisorStatus.AlreadyStopped),
        };
        supervisor.InspectionResults.Enqueue(
            new ProcessSupervisorResult(ProcessSupervisorStatus.NotRunning));
        AgentCommandExecutor executor = CreateExecutor(apiClient, supervisor);

        await executor.ExecuteAsync(
            CreateCredential(),
            new AgentCommandExecution(CreateCommand(AgentCommandType.StopServer)),
            CancellationToken.None);

        Assert.Equal(
            ["api:start", "process:stop", "process:inspect", "api:state", "api:complete"],
            events);
    }

    [Fact]
    public async Task ProcessFailureIsReportedWithoutSensitiveDetails()
    {
        List<string> events = [];
        RecordingApiClient apiClient = new(events);
        FakeProcessSupervisor supervisor = new(events)
        {
            StartResult = new ProcessSupervisorResult(
                ProcessSupervisorStatus.Failed,
                Failure: ProcessSupervisorFailure.ExecutableNotFound),
        };
        AgentCommandExecutor executor = CreateExecutor(apiClient, supervisor);

        await executor.ExecuteAsync(
            CreateCredential(),
            new AgentCommandExecution(CreateCommand(AgentCommandType.StartServer)),
            CancellationToken.None);

        Assert.Equal(["api:start", "process:start", "api:fail"], events);
        Assert.Equal("ExecutableNotFound", apiClient.ErrorCode);
        Assert.Equal(
            "The local process operation did not reach the required state.",
            apiClient.ErrorMessage);
    }

    [Fact]
    public async Task LostCompletionResponseRetriesOnlyTheReportNotTheProcessAction()
    {
        List<string> events = [];
        RecordingApiClient apiClient = new(events) { CompleteFailuresRemaining = 4 };
        FakeProcessSupervisor supervisor = new(events)
        {
            StartResult = new ProcessSupervisorResult(ProcessSupervisorStatus.Started),
        };
        supervisor.InspectionResults.Enqueue(
            new ProcessSupervisorResult(
                ProcessSupervisorStatus.Running,
                CreateIdentity()));
        AgentCommandExecutor executor = CreateExecutor(apiClient, supervisor);
        AgentCommandExecution execution = new(CreateCommand(AgentCommandType.StartServer));

        AgentRetryExhaustedException exception =
            await Assert.ThrowsAsync<AgentRetryExhaustedException>(() => executor.ExecuteAsync(
                CreateCredential(),
                execution,
                CancellationToken.None));
        await executor.ExecuteAsync(CreateCredential(), execution, CancellationToken.None);

        Assert.Equal(4, exception.Attempts);
        Assert.Equal(1, supervisor.StartCalls);
        Assert.Equal(1, supervisor.InspectCalls);
        Assert.Equal(1, apiClient.StartCalls);
        Assert.Equal(1, apiClient.StateReportCalls);
        Assert.Equal(5, apiClient.CompleteCalls);
    }

    [Fact]
    public async Task UnsupportedCommandTypeIsFailedWithoutProcessExecution()
    {
        List<string> events = [];
        RecordingApiClient apiClient = new(events);
        FakeProcessSupervisor supervisor = new(events);
        AgentCommandExecutor executor = CreateExecutor(apiClient, supervisor);

        await executor.ExecuteAsync(
            CreateCredential(),
            new AgentCommandExecution(CreateCommand((AgentCommandType)999)),
            CancellationToken.None);

        Assert.Equal(["api:start", "api:fail"], events);
        Assert.Equal("UnsupportedCommandType", apiClient.ErrorCode);
        Assert.Equal(0, supervisor.StartCalls);
        Assert.Equal(0, supervisor.StopCalls);
    }

    [Fact]
    public async Task InvalidStoredConfigurationIsFailedWithoutResolvingAProcess()
    {
        List<string> events = [];
        RecordingApiClient apiClient = new(events);
        IProcessSupervisorRegistry registry = new FailedRegistry(
            ProcessSupervisorResolutionFailure.InvalidConfiguration);
        AgentCommandExecutor executor = new(
            apiClient,
            new AgentRetryExecutor(new ImmediateDelay()),
            registry,
            NullLogger<AgentCommandExecutor>.Instance);

        await executor.ExecuteAsync(
            CreateCredential(),
            new AgentCommandExecution(CreateCommand(AgentCommandType.StartServer)),
            CancellationToken.None);

        Assert.Equal(["api:start", "api:fail"], events);
        Assert.Equal("InvalidProcessConfiguration", apiClient.ErrorCode);
    }

    [Fact]
    public async Task CommandLifecycleLogsUseCommandCorrelationScope()
    {
        List<string> events = [];
        RecordingApiClient apiClient = new(events);
        FakeProcessSupervisor supervisor = new(events)
        {
            StartResult = new ProcessSupervisorResult(ProcessSupervisorStatus.Started),
        };
        supervisor.InspectionResults.Enqueue(
            new ProcessSupervisorResult(
                ProcessSupervisorStatus.Running,
                CreateIdentity()));
        TestLogProvider logProvider = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging =>
            logging.AddProvider(logProvider));
        AgentCommandExecutor executor = new(
            apiClient,
            new AgentRetryExecutor(new ImmediateDelay()),
            new SuccessfulRegistry(supervisor),
            loggerFactory.CreateLogger<AgentCommandExecutor>());
        ClaimedAgentCommand command = CreateCommand(AgentCommandType.StartServer);

        await executor.ExecuteAsync(
            CreateCredential(),
            new AgentCommandExecution(command),
            CancellationToken.None);

        TestLogEntry[] lifecycleEntries = logProvider.Entries
            .Where(entry => entry.CategoryName.EndsWith(
                nameof(AgentCommandExecutor),
                StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(lifecycleEntries);
        Assert.All(
            lifecycleEntries,
            entry => Assert.Equal(command.CorrelationId.ToString("D"), entry.CorrelationId));
    }

    private static AgentCommandExecutor CreateExecutor(
        RecordingApiClient apiClient,
        IProcessSupervisor supervisor) =>
        new(
            apiClient,
            new AgentRetryExecutor(new ImmediateDelay()),
            new SuccessfulRegistry(supervisor),
            NullLogger<AgentCommandExecutor>.Instance);

    private static AgentCredential CreateCredential() => AgentCredential.Create(
        Guid.NewGuid(),
        "spac_0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        AgentCredential.ExpectedAuthorizationScheme);

    private static ClaimedAgentCommand CreateCommand(AgentCommandType type) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        type,
        Guid.NewGuid(),
        "New",
        new ClaimedAgentServerInstance(
            @"C:\Servers\server.exe",
            "--port 16261",
            @"C:\Servers",
            "server"));

    private static ProcessIdentity CreateIdentity() => new(
        42,
        new DateTimeOffset(2026, 7, 29, 12, 0, 0, TimeSpan.Zero),
        @"C:\Servers\server.exe",
        "server");

    private sealed class RecordingApiClient(List<string> events) : IAgentApiClient
    {
        public int CompleteFailuresRemaining { get; set; }

        public int StartCalls { get; private set; }

        public int CompleteCalls { get; private set; }

        public int StateReportCalls { get; private set; }

        public string? ErrorCode { get; private set; }

        public string? ErrorMessage { get; private set; }

        public Task SendHeartbeatAsync(
            AgentCredential credential,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<AssignedAgentServerInstance>> ListServerInstancesAsync(
            AgentCredential credential,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AssignedAgentServerInstance>>([]);

        public Task ReportServerInstanceStateAsync(
            AgentCredential credential,
            Guid serverInstanceId,
            AgentProcessStateReport report,
            CancellationToken cancellationToken)
        {
            events.Add("api:state");
            StateReportCalls++;
            return Task.CompletedTask;
        }

        public Task<ClaimedAgentCommand?> ClaimNextCommandAsync(
            AgentCredential credential,
            CancellationToken cancellationToken) => Task.FromResult<ClaimedAgentCommand?>(null);

        public Task MarkCommandRunningAsync(
            AgentCredential credential,
            ClaimedAgentCommand command,
            CancellationToken cancellationToken)
        {
            events.Add("api:start");
            StartCalls++;
            return Task.CompletedTask;
        }

        public Task CompleteCommandAsync(
            AgentCredential credential,
            ClaimedAgentCommand command,
            CancellationToken cancellationToken)
        {
            events.Add("api:complete");
            CompleteCalls++;
            if (CompleteFailuresRemaining > 0)
            {
                CompleteFailuresRemaining--;
                return Task.FromException(new AgentApiException(HttpStatusCode.ServiceUnavailable));
            }

            return Task.CompletedTask;
        }

        public Task FailCommandAsync(
            AgentCredential credential,
            ClaimedAgentCommand command,
            string errorCode,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            events.Add("api:fail");
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessSupervisor(List<string> events) : IProcessSupervisor
    {
        public ProcessSupervisorResult StartResult { get; init; } =
            new(ProcessSupervisorStatus.Failed);

        public ProcessSupervisorResult StopResult { get; init; } =
            new(ProcessSupervisorStatus.Failed);

        public Queue<ProcessSupervisorResult> InspectionResults { get; } = new();

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public int InspectCalls { get; private set; }

        public Task<ProcessSupervisorResult> StartAsync(CancellationToken cancellationToken)
        {
            events.Add("process:start");
            StartCalls++;
            return Task.FromResult(StartResult);
        }

        public Task<ProcessSupervisorResult> InspectAsync(CancellationToken cancellationToken)
        {
            events.Add("process:inspect");
            InspectCalls++;
            return Task.FromResult(InspectionResults.Dequeue());
        }

        public Task<ProcessSupervisorResult> StopAsync(CancellationToken cancellationToken)
        {
            events.Add("process:stop");
            StopCalls++;
            return Task.FromResult(StopResult);
        }
    }

    private sealed class SuccessfulRegistry(IProcessSupervisor supervisor)
        : IProcessSupervisorRegistry
    {
        public ProcessSupervisorResolution Resolve(
            Guid serverInstanceId,
            ProcessSupervisorRequest request) => ProcessSupervisorResolution.Succeeded(supervisor);
    }

    private sealed class FailedRegistry(ProcessSupervisorResolutionFailure failure)
        : IProcessSupervisorRegistry
    {
        public ProcessSupervisorResolution Resolve(
            Guid serverInstanceId,
            ProcessSupervisorRequest request) => ProcessSupervisorResolution.Failed(failure);
    }

    private sealed class ImmediateDelay : IAgentDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
