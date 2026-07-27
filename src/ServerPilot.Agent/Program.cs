using ServerPilot.Agent;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<AgentWorker>();

IHost host = builder.Build();
await host.RunAsync();
