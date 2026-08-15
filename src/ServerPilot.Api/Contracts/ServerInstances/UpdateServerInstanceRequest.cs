using System.ComponentModel.DataAnnotations;
using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed class UpdateServerInstanceRequest
{
    public string? Profile { get; init; }

    [Required, StringLength(ServerInstanceConfiguration.MaximumNameLength, MinimumLength = 1)]
    public string? Name { get; init; }

    [Required, StringLength(ServerInstanceConfiguration.MaximumExecutablePathLength, MinimumLength = 1)]
    public string? ExecutablePath { get; init; }

    [StringLength(ServerInstanceConfiguration.MaximumArgumentsLength)]
    public string? Arguments { get; init; }

    [StringLength(ServerInstanceConfiguration.MaximumWorkingDirectoryLength)]
    public string? WorkingDirectory { get; init; }

    [StringLength(ServerInstanceConfiguration.MaximumProcessNameLength)]
    public string? ProcessName { get; init; }

    [StringLength(ServerInstanceConfiguration.MaximumWorkingDirectoryLength)]
    public string? DataDirectory { get; init; }
}
