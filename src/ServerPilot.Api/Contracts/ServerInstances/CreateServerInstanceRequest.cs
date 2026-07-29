using System.ComponentModel.DataAnnotations;
using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed class CreateServerInstanceRequest
{
    public Guid AgentId { get; init; }

    [Required, StringLength(ServerInstanceConfiguration.MaximumNameLength, MinimumLength = 1)]
    public string? Name { get; init; }

    [Required, StringLength(ServerInstanceConfiguration.MaximumExecutablePathLength, MinimumLength = 1)]
    public string? ExecutablePath { get; init; }

    [StringLength(ServerInstanceConfiguration.MaximumArgumentsLength)]
    public string? Arguments { get; init; }

    [Required, StringLength(ServerInstanceConfiguration.MaximumWorkingDirectoryLength, MinimumLength = 1)]
    public string? WorkingDirectory { get; init; }

    [Required, StringLength(ServerInstanceConfiguration.MaximumProcessNameLength, MinimumLength = 1)]
    public string? ProcessName { get; init; }
}
