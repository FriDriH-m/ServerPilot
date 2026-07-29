using System.ComponentModel.DataAnnotations;
using ServerPilot.Domain.Commands;

namespace ServerPilot.Api.Contracts.Commands;

public sealed record FailServerCommandRequest(
    [Required, MaxLength(ServerCommand.MaximumErrorCodeLength)] string? ErrorCode,
    [Required, MaxLength(ServerCommand.MaximumErrorMessageLength)] string? ErrorMessage);
