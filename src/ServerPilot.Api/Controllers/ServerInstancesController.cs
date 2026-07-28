using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Contracts.ServerInstances;
using ServerPilot.Api.Http;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.ServerInstances;
using ServerPilot.Domain.ServerInstances;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting(ApiRateLimitPolicyNames.AuthenticatedUser)]
[Route("api/server-instances")]
public sealed class ServerInstancesController(
    ServerInstanceService serverInstances,
    ICurrentUser currentUser,
    ILogger<ServerInstancesController> logger) : ControllerBase
{
    private const int DefaultListLimit = 50;
    private const int MaximumListLimit = 100;
    private const int MaximumListPage = 1_000;

    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?>
        LogServerInstanceCreated = LoggerMessage.Define<Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(1400, nameof(LogServerInstanceCreated)),
            "User {UserId} created ServerInstance {ServerInstanceId} for Agent {AgentId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?>
        LogServerInstanceUpdated = LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1401, nameof(LogServerInstanceUpdated)),
            "User {UserId} updated ServerInstance {ServerInstanceId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?>
        LogServerInstanceDeleted = LoggerMessage.Define<Guid, Guid>(
            LogLevel.Information,
            new EventId(1402, nameof(LogServerInstanceDeleted)),
            "User {UserId} deleted ServerInstance {ServerInstanceId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?>
        LogServerInstanceNotFound = LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1403, nameof(LogServerInstanceNotFound)),
            "User {UserId} attempted to access missing or foreign ServerInstance {ServerInstanceId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?>
        LogForeignAgent = LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1404, nameof(LogForeignAgent)),
            "User {UserId} attempted to create ServerInstance for missing or foreign Agent {AgentId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?>
        LogActiveServerInstanceDeletion = LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1405, nameof(LogActiveServerInstanceDeletion)),
            "User {UserId} attempted to delete active ServerInstance {ServerInstanceId}");

    [HttpPost]
    public async Task<ActionResult<ServerInstanceResponse>> Create(
        CreateServerInstanceRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        if (request.AgentId == Guid.Empty || !TryCreateConfiguration(
                request.Name,
                request.ExecutablePath,
                request.Arguments,
                request.WorkingDirectory,
                request.ProcessName,
                out ServerInstanceConfiguration? configuration))
        {
            return InvalidConfiguration();
        }

        ServerInstanceCreateResult result = await serverInstances.CreateAsync(
            userId,
            request.AgentId,
            configuration!,
            cancellationToken);
        if (result.Status == ServerInstanceCreateStatus.AgentNotFound)
        {
            LogForeignAgent(logger, userId, request.AgentId, null);
            return NotFound();
        }

        ServerInstanceDetails serverInstance = result.ServerInstance!;
        LogServerInstanceCreated(
            logger,
            userId,
            serverInstance.Id,
            serverInstance.AgentId,
            null);
        return CreatedAtAction(
            nameof(Get),
            new { id = serverInstance.Id },
            ToResponse(serverInstance));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServerInstanceListResponse>>> List(
        CancellationToken cancellationToken,
        [FromQuery, Range(1, MaximumListLimit)] int limit = DefaultListLimit,
        [FromQuery, Range(1, MaximumListPage)] int page = 1)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        IReadOnlyList<ServerInstanceListItem> items =
            await serverInstances.ListAsync(userId, page, limit, cancellationToken);
        return Ok(items.Select(ToListResponse).ToArray());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ServerInstanceResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        ServerInstanceDetails? serverInstance = await serverInstances.GetAsync(
            id,
            userId,
            cancellationToken);
        if (serverInstance is null)
        {
            LogServerInstanceNotFound(logger, userId, id, null);
            return NotFound();
        }

        return Ok(ToResponse(serverInstance));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ServerInstanceResponse>> Update(
        Guid id,
        UpdateServerInstanceRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        if (!TryCreateConfiguration(
                request.Name,
                request.ExecutablePath,
                request.Arguments,
                request.WorkingDirectory,
                request.ProcessName,
                out ServerInstanceConfiguration? configuration))
        {
            return InvalidConfiguration();
        }

        ServerInstanceDetails? serverInstance = await serverInstances.UpdateAsync(
            id,
            userId,
            configuration!,
            cancellationToken);
        if (serverInstance is null)
        {
            LogServerInstanceNotFound(logger, userId, id, null);
            return NotFound();
        }

        LogServerInstanceUpdated(logger, userId, id, null);
        return Ok(ToResponse(serverInstance));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Unauthorized();
        }

        DeleteServerInstanceStatus status = await serverInstances.DeleteAsync(
            id,
            userId,
            cancellationToken);
        if (status == DeleteServerInstanceStatus.Succeeded)
        {
            LogServerInstanceDeleted(logger, userId, id, null);
            return NoContent();
        }

        if (status == DeleteServerInstanceStatus.Active)
        {
            LogActiveServerInstanceDeletion(logger, userId, id, null);
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "An active ServerInstance cannot be deleted.");
        }

        LogServerInstanceNotFound(logger, userId, id, null);
        return NotFound();
    }

    private static bool TryCreateConfiguration(
        string? name,
        string? executablePath,
        string? arguments,
        string? workingDirectory,
        string? processName,
        out ServerInstanceConfiguration? configuration) =>
        ServerInstanceConfiguration.TryCreate(
            name,
            executablePath,
            arguments,
            workingDirectory,
            processName,
            out configuration);

    private ObjectResult InvalidConfiguration() =>
        Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid ServerInstance configuration",
            detail: "Use non-empty values, absolute Windows or UNC paths, and a process name without a path.");

    private static ServerInstanceListResponse ToListResponse(
        ServerInstanceListItem serverInstance) =>
        new(
            serverInstance.Id,
            serverInstance.AgentId,
            serverInstance.Name,
            serverInstance.Status.ToString(),
            serverInstance.LastProcessId,
            serverInstance.CreatedAt,
            serverInstance.UpdatedAt);

    private static ServerInstanceResponse ToResponse(ServerInstanceDetails serverInstance) =>
        new(
            serverInstance.Id,
            serverInstance.AgentId,
            serverInstance.Name,
            serverInstance.ExecutablePath,
            serverInstance.Arguments,
            serverInstance.WorkingDirectory,
            serverInstance.ProcessName,
            serverInstance.Status.ToString(),
            serverInstance.LastProcessId,
            serverInstance.CreatedAt,
            serverInstance.UpdatedAt);
}
