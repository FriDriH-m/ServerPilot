using ServerPilot.Agent;
using ServerPilot.Agent.Api;
using ServerPilot.Agent.Bootstrap;
using ServerPilot.Agent.Configuration;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Looping;
using ServerPilot.Agent.Registration;
using ServerPilot.Agent.Runtime;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

AgentOptions agentOptions = builder.Configuration
    .GetSection(AgentOptions.SectionName)
    .Get<AgentOptions>() ?? new AgentOptions();
agentOptions.Validate();

builder.Services.AddSingleton(agentOptions);
builder.Services.AddSingleton<IAgentCredentialStore, WindowsProtectedAgentCredentialStore>();
builder.Services.AddSingleton<IAgentRegistrationClient, HttpAgentRegistrationClient>();
builder.Services.AddSingleton<IAgentApiClient, HttpAgentApiClient>();
builder.Services.AddSingleton<IAgentDelay, SystemAgentDelay>();
builder.Services.AddSingleton<AgentRetryExecutor>();
builder.Services.AddSingleton<PeriodicAgentLoop>();
builder.Services.AddSingleton<AgentLoopService>();
builder.Services.AddSingleton<AgentRuntime>();
builder.Services.AddSingleton<AgentBootstrapService>();
builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = agentOptions.GetApiBaseUri(),
});
builder.Services.AddHostedService<AgentWorker>();

IHost host = builder.Build();
AgentBootstrapService bootstrap = host.Services.GetRequiredService<AgentBootstrapService>();
AgentBootstrapResult bootstrapResult = await bootstrap.InitializeAsync(CancellationToken.None);
host.Services.GetRequiredService<AgentRuntime>().Initialize(bootstrapResult.Credential);
await host.RunAsync();
