using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Contracts.Commands;
using ServerPilot.Application.Agents;
using ServerPilot.Application.Commands;
using ServerPilot.Infrastructure.Authentication;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Authorize(Policy = AgentAuthorizationPolicyNames.Agent)]
[Route("api")]
public sealed class AgentCommandsController(
    AgentCommandService commands,
    ICurrentAgent currentAgent,
    ILogger<AgentCommandsController> logger) : ControllerBase
{
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogCommandClaimed =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1600, nameof(LogCommandClaimed)),
            "Agent {AgentId} claimed ServerCommand {CommandId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogForeignClaimAttempt =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1601, nameof(LogForeignClaimAttempt)),
            "Agent {AgentId} attempted to claim commands for Agent {RequestedAgentId}");
    private static readonly Action<ILogger, Guid, Guid, string, Exception?> LogCommandTransition =
        LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Information,
            new EventId(1602, nameof(LogCommandTransition)),
            "Agent {AgentId} applied transition {Transition} to ServerCommand {CommandId}");
    private static readonly Action<ILogger, Guid, Guid, string, Exception?> LogRejectedTransition =
        LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Warning,
            new EventId(1603, nameof(LogRejectedTransition)),
            "Agent {AgentId} was denied transition {Transition} for missing, foreign or invalid ServerCommand {CommandId}");

    [HttpPost("agents/{agentId:guid}/commands/claim-next")]
    public async Task<ActionResult<ServerCommandResponse>> ClaimNext(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        if (currentAgent.AgentId is not Guid authenticatedAgentId)
        {
            return Unauthorized();
        }

        if (agentId != authenticatedAgentId)
        {
            LogForeignClaimAttempt(logger, authenticatedAgentId, agentId, null);
            return NotFound();
        }

        ServerCommandDetails? command = await commands.ClaimNextAsync(
            authenticatedAgentId,
            cancellationToken);
        if (command is null)
        {
            return NoContent();
        }

        LogCommandClaimed(logger, authenticatedAgentId, command.Id, null);
        return Ok(ToResponse(command));
    }

    [HttpPost("commands/{commandId:guid}/start")]
    public Task<IActionResult> Start(
        Guid commandId,
        CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            commandId,
            "start",
            (agentId, token) => commands.StartAsync(agentId, commandId, token),
            cancellationToken);

    [HttpPost("commands/{commandId:guid}/complete")]
    public Task<IActionResult> Complete(
        Guid commandId,
        CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            commandId,
            "complete",
            (agentId, token) => commands.CompleteAsync(agentId, commandId, token),
            cancellationToken);

    [HttpPost("commands/{commandId:guid}/fail")]
    public Task<IActionResult> Fail(
        Guid commandId,
        FailServerCommandRequest request,
        CancellationToken cancellationToken) =>
        ApplyTransitionAsync(
            commandId,
            "fail",
            (agentId, token) => commands.FailAsync(
                agentId,
                commandId,
                request.ErrorCode,
                request.ErrorMessage,
                token),
            cancellationToken);

    private async Task<IActionResult> ApplyTransitionAsync(
        Guid commandId,
        string transition,
        Func<Guid, CancellationToken, Task<AgentCommandTransitionStatus>> apply,
        CancellationToken cancellationToken)
    {
        if (currentAgent.AgentId is not Guid agentId)
        {
            return Unauthorized();
        }

        AgentCommandTransitionStatus status = await apply(agentId, cancellationToken);
        if (status is AgentCommandTransitionStatus.Succeeded or
            AgentCommandTransitionStatus.AlreadyApplied)
        {
            LogCommandTransition(logger, agentId, commandId, transition, null);
            return NoContent();
        }

        LogRejectedTransition(logger, agentId, commandId, transition, null);
        return status switch
        {
            AgentCommandTransitionStatus.NotFound => NotFound(),
            AgentCommandTransitionStatus.InvalidState => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "The command is not in a valid state for this transition."),
            AgentCommandTransitionStatus.InvalidFailureDetails => Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Failure details must be non-empty and within the allowed lengths."),
            _ => throw new InvalidOperationException(
                $"Unsupported Agent command transition status '{status}'."),
        };
    }

    private static ServerCommandResponse ToResponse(ServerCommandDetails command) =>
        new(
            command.Id,
            command.AgentId,
            command.ServerInstanceId,
            command.Type.ToString(),
            command.Status.ToString(),
            command.CreatedAt,
            command.ClaimedAt,
            command.StartedAt,
            command.CompletedAt,
            command.ErrorCode,
            command.AttemptCount,
            command.CorrelationId);
}
