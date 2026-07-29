using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Contracts.Commands;
using ServerPilot.Api.Http;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.Commands;
using ServerPilot.Domain.Commands;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting(ApiRateLimitPolicyNames.AuthenticatedUser)]
[Route("api/server-instances/{serverInstanceId:guid}/commands")]
public sealed class ServerCommandsController(
    ServerCommandService commands,
    ICurrentUser currentUser,
    ILogger<ServerCommandsController> logger) : ControllerBase
{
    private const int DefaultListLimit = 50;
    private const int MaximumListLimit = 100;

    private static readonly Action<ILogger, Guid, Guid, ServerCommandType, Guid, Guid, Exception?>
        LogServerCommandCreated = LoggerMessage.Define<Guid, Guid, ServerCommandType, Guid, Guid>(
            LogLevel.Information,
            new EventId(1500, nameof(LogServerCommandCreated)),
            "User {UserId} created ServerCommand {CommandId} of type {CommandType} for ServerInstance {ServerInstanceId} with CorrelationId {CorrelationId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogServerInstanceNotFound =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1501, nameof(LogServerInstanceNotFound)),
            "User {UserId} attempted to access commands for missing or foreign ServerInstance {ServerInstanceId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogActiveCommandConflict =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1502, nameof(LogActiveCommandConflict)),
            "User {UserId} attempted to create a conflicting command for ServerInstance {ServerInstanceId}");

    [HttpPost("start")]
    public Task<ActionResult<ServerCommandResponse>> Start(
        Guid serverInstanceId,
        CancellationToken cancellationToken) =>
        CreateAsync(serverInstanceId, ServerCommandType.StartServer, cancellationToken);

    [HttpPost("stop")]
    public Task<ActionResult<ServerCommandResponse>> Stop(
        Guid serverInstanceId,
        CancellationToken cancellationToken) =>
        CreateAsync(serverInstanceId, ServerCommandType.StopServer, cancellationToken);

    [HttpGet]
    public async Task<ActionResult<ServerCommandHistoryResponse>> List(
        Guid serverInstanceId,
        CancellationToken cancellationToken,
        [FromQuery, Range(1, MaximumListLimit)] int limit = DefaultListLimit,
        [FromQuery] string? cursor = null,
        [FromQuery] int? page = null)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        if (page.HasValue)
        {
            ModelState.AddModelError(
                nameof(page),
                "Page-number pagination is not supported. Use the cursor parameter.");
            return ValidationProblem(ModelState);
        }

        ServerCommandHistoryCursor? after = null;
        if (cursor is not null && !ServerCommandCursorCodec.TryDecode(cursor, out after))
        {
            ModelState.AddModelError(nameof(cursor), "The command history cursor is invalid.");
            return ValidationProblem(ModelState);
        }

        ServerCommandHistoryResult result = await commands.ListAsync(
            userId,
            serverInstanceId,
            after,
            limit,
            cancellationToken);
        if (!result.ServerInstanceFound)
        {
            LogServerInstanceNotFound(logger, userId, serverInstanceId, null);
            return NotFound();
        }

        string? nextCursor = result.HasMore && result.Commands.Count > 0
            ? ServerCommandCursorCodec.Encode(result.Commands[^1])
            : null;
        return Ok(new ServerCommandHistoryResponse(
            result.Commands.Select(ToResponse).ToArray(),
            nextCursor));
    }

    private async Task<ActionResult<ServerCommandResponse>> CreateAsync(
        Guid serverInstanceId,
        ServerCommandType type,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        Guid correlationId = Guid.NewGuid();
        CreateServerCommandResult result = await commands.CreateAsync(
            userId,
            serverInstanceId,
            type,
            correlationId,
            cancellationToken);
        if (result.Status == CreateServerCommandStatus.ServerInstanceNotFound)
        {
            LogServerInstanceNotFound(logger, userId, serverInstanceId, null);
            return NotFound();
        }

        if (result.Status == CreateServerCommandStatus.ActiveCommandConflict)
        {
            LogActiveCommandConflict(logger, userId, serverInstanceId, null);
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "An active command already exists for this ServerInstance.");
        }

        if (result.Status != CreateServerCommandStatus.Succeeded || result.Command is null)
        {
            throw new InvalidOperationException(
                $"Unsupported ServerCommand creation status '{result.Status}'.");
        }

        ServerCommandDetails command = result.Command;
        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["UserId"] = userId,
            ["AgentId"] = command.AgentId,
            ["ServerInstanceId"] = command.ServerInstanceId,
            ["CommandId"] = command.Id,
            ["CorrelationId"] = command.CorrelationId,
        }))
        {
            LogServerCommandCreated(
                logger,
                userId,
                command.Id,
                command.Type,
                command.ServerInstanceId,
                command.CorrelationId,
                null);
        }
        return StatusCode(StatusCodes.Status201Created, ToResponse(command));
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
