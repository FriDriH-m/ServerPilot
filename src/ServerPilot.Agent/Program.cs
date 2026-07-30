using Microsoft.Extensions.Hosting.WindowsServices;
using ServerPilot.Agent;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Bootstrap;
using ServerPilot.Agent.Configuration;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Execution;
using ServerPilot.Agent.Looping;
using ServerPilot.Agent.Processes;
using ServerPilot.Agent.Registration;
using ServerPilot.Agent.Runtime;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
bool isWindowsService = WindowsServiceHelpers.IsWindowsService();

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ServerPilot.Agent";
});

AgentOptions agentOptions = builder.Configuration
    .GetSection(AgentOptions.SectionName)
    .Get<AgentOptions>() ?? new AgentOptions();
agentOptions.Validate();

builder.Services.AddSingleton(agentOptions);
builder.Services.AddSingleton<IAgentCredentialStore>(_ =>
    new WindowsProtectedAgentCredentialStore(isWindowsService));
builder.Services.AddSingleton<IAgentRegistrationClient, HttpAgentRegistrationClient>();
builder.Services.AddSingleton<IAgentApiClient, HttpAgentApiClient>();
builder.Services.AddSingleton<IAgentDelay, SystemAgentDelay>();
builder.Services.AddSingleton<AgentRetryExecutor>();
builder.Services.AddSingleton<PeriodicAgentLoop>();
builder.Services.AddSingleton<IProcessPlatform, SystemProcessPlatform>();
builder.Services.AddSingleton<IProcessSupervisorRegistry, LocalProcessSupervisorRegistry>();
builder.Services.AddSingleton<IAgentProcessStateReconciler, AgentProcessStateReconciler>();
builder.Services.AddSingleton<IAgentCommandExecutor, AgentCommandExecutor>();
builder.Services.AddSingleton<AgentLoopService>();
builder.Services.AddSingleton<AgentRuntime>();
builder.Services.AddSingleton<AgentBootstrapService>();
builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = agentOptions.GetApiBaseUri(),
});
builder.Services.AddHostedService<AgentWorker>();

IHost host = builder.Build();
await host.RunAsync();
