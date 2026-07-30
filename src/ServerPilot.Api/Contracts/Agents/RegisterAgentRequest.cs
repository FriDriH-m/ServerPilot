using System.ComponentModel.DataAnnotations;

namespace ServerPilot.Api.Contracts.Agents;

public sealed class RegisterAgentRequest : IValidatableObject
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach ((string? value, string memberName) in Metadata())
        {
            if (value?.Any(char.IsControl) == true)
            {
                yield return new ValidationResult(
                    "Agent metadata must not contain control characters.",
                    [memberName]);
            }
        }
    }

    private IEnumerable<(string? Value, string MemberName)> Metadata()
    {
        yield return (Name, nameof(Name));
        yield return (MachineName, nameof(MachineName));
        yield return (OperatingSystem, nameof(OperatingSystem));
        yield return (Version, nameof(Version));
    }
}
