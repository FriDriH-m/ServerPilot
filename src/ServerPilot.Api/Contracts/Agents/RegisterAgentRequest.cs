using System.ComponentModel.DataAnnotations;

namespace ServerPilot.Api.Contracts.Agents;

public sealed class RegisterAgentRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public string? InstallationToken { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string? Name { get; init; }

    [Required, StringLength(255, MinimumLength = 1)]
    public string? MachineName { get; init; }

    [Required, StringLength(255, MinimumLength = 1)]
    public string? OperatingSystem { get; init; }

    [Required, StringLength(64, MinimumLength = 1)]
    public string? Version { get; init; }
}
