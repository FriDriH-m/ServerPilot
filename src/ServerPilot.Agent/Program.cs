using ServerPilot.Agent;
using ServerPilot.Agent.Bootstrap;
using ServerPilot.Agent.Configuration;
using ServerPilot.Agent.Credentials;
using ServerPilot.Agent.Registration;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

AgentOptions agentOptions = builder.Configuration
    .GetSection(AgentOptions.SectionName)
    .Get<AgentOptions>() ?? new AgentOptions();
agentOptions.Validate();

builder.Services.AddSingleton(agentOptions);
builder.Services.AddSingleton<IAgentCredentialStore, WindowsProtectedAgentCredentialStore>();
builder.Services.AddSingleton<IAgentRegistrationClient, HttpAgentRegistrationClient>();
builder.Services.AddSingleton<AgentBootstrapService>();
builder.Services.AddSingleton(new HttpClient
{
    BaseAddress = agentOptions.GetApiBaseUri(),
});
builder.Services.AddHostedService<AgentWorker>();

IHost host = builder.Build();
AgentBootstrapService bootstrap = host.Services.GetRequiredService<AgentBootstrapService>();
await bootstrap.InitializeAsync(CancellationToken.None);
await host.RunAsync();
