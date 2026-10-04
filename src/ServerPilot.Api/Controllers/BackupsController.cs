using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Contracts.Commands;
using ServerPilot.Api.Http;
using ServerPilot.Application.Authentication;
using ServerPilot.Application.Backups;
using ServerPilot.Application.Commands;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting(ApiRateLimitPolicyNames.AuthenticatedUser)]
[Route("api/server-instances/{serverInstanceId:guid}/backups")]
public sealed class BackupsController(BackupService backups, ICurrentUser currentUser, ILogger<BackupsController> logger) : ControllerBase
{
    private static readonly Action<ILogger, Guid, Guid, Guid, Exception?> LogOperation = LoggerMessage.Define<Guid, Guid, Guid>(
        LogLevel.Information, new EventId(1550, "BackupOperationCreated"),
        "User {UserId} created backup maintenance command {CommandId} for ServerInstance {ServerInstanceId}");
    [HttpPost("{backupId:guid}/restore")]
    public Task<IActionResult> Restore(Guid serverInstanceId, Guid backupId, CancellationToken token) =>
        CreateOperation(serverInstanceId, backupId, 1, token);

    [HttpPost("retention")]
    public Task<IActionResult> Retention(Guid serverInstanceId, RetentionRequest request, CancellationToken token) =>
        CreateOperation(serverInstanceId, null, request.KeepCount, token);

    private async Task<IActionResult> CreateOperation(Guid serverInstanceId, Guid? backupId, int keepCount, CancellationToken token)
    {
        if (currentUser.UserId is not Guid userId) return Unauthorized();
        var result = await backups.CreateOperationAsync(serverInstanceId, userId, backupId, keepCount, token);
        if (result.CommandId is Guid commandId) LogOperation(logger, userId, commandId, serverInstanceId, null);
        return result.StatusCode switch
        {
            201 => StatusCode(201, new { result.CommandId }),
            204 => NoContent(),
            404 => NotFound(),
            _ => Problem(statusCode: 409, title: "Conflict",
                detail: "An online Agent, a fresh stopped Project Zomboid server and no active command are required."),
        };
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid serverInstanceId, CancellationToken cancellationToken,
        [FromQuery, Range(1, 100)] int limit = 50, [FromQuery] string? cursor = null)
    {
        if (currentUser.UserId is not Guid userId) return Unauthorized();
        ServerCommandHistoryCursor? after = null;
        if (cursor is not null && !ServerCommandCursorCodec.TryDecode(cursor, out after))
        {
            ModelState.AddModelError(nameof(cursor), "Invalid backup cursor.");
            return ValidationProblem(ModelState);
        }

        BackupPage result = await backups.ListAsync(serverInstanceId, userId, after, limit, cancellationToken);
        if (!result.ServerInstanceFound) return NotFound();
        BackupDetails? last = result.Items.Count == 0 ? null : result.Items[^1];
        string? nextCursor = result.HasMore && last is not null
            ? ServerCommandCursorCodec.Encode(last.CreatedAt, last.Id)
            : null;
        return Ok(new BackupHistoryResponse(result.Items, nextCursor));
    }
}

public sealed record BackupHistoryResponse(IReadOnlyList<BackupDetails> Items, string? NextCursor);
public sealed record RetentionRequest([Range(1, 1000)] int KeepCount);
