using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerPilot.Api.Authentication;
using ServerPilot.Api.Contracts.ServerInstances;
using ServerPilot.Application.Agents;
using ServerPilot.Application.ServerInstances;
using ServerPilot.Domain.ServerInstances;
using StateReportResult =
    ServerPilot.Application.ServerInstances.ServerInstanceStateReportResult;

namespace ServerPilot.Api.Controllers;

[ApiController]
[Authorize(Policy = AgentAuthorizationPolicyNames.Agent)]
[Route("api/agents/{agentId:guid}/server-instances")]
public sealed class AgentServerInstancesController(
    AgentServerInstanceService serverInstances,
    ICurrentAgent currentAgent,
    ILogger<AgentServerInstancesController> logger) : ControllerBase
{
    private const int PageSize = 100;

    private static readonly Action<ILogger, Guid, Guid, string, Exception?> LogStateReported =
        LoggerMessage.Define<Guid, Guid, string>(
            LogLevel.Debug,
            new EventId(1700, nameof(LogStateReported)),
            "Agent {AgentId} reported ServerInstance {ServerInstanceId} state {ProcessState}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogForeignAgentRoute =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1701, nameof(LogForeignAgentRoute)),
            "Agent {AgentId} attempted ServerInstance reconciliation for Agent {RequestedAgentId}");
    private static readonly Action<ILogger, Guid, Guid, Exception?> LogRejectedStateReport =
        LoggerMessage.Define<Guid, Guid>(
            LogLevel.Warning,
            new EventId(1702, nameof(LogRejectedStateReport)),
            "Agent {AgentId} submitted an invalid state report for ServerInstance {ServerInstanceId}");

    [HttpGet]
    public async Task<IActionResult> List(
        Guid agentId,
        CancellationToken cancellationToken,
        [FromQuery, Range(1, 10_000)] int page = 1)
    {
        if (!TryAuthorizeRoute(agentId, out IActionResult? failure))
        {
            return failure!;
        }

        IReadOnlyList<AssignedServerInstanceDetails> items = await serverInstances.ListAsync(
            agentId,
            page,
            PageSize,
            cancellationToken);
        return Ok(items.Select(item => new AgentServerInstanceResponse(
            item.Id,
            item.Profile.ToString(),
            item.ExecutablePath,
            item.Arguments,
            item.WorkingDirectory,
            item.ProcessName,
            item.DataDirectory,
            item.ReportedStatus.ToString(),
            item.LastProcessId,
            item.LastProcessStartedAt,
            item.LastStatusReportedAt)).ToArray());
    }

    [HttpPost("{serverInstanceId:guid}/status")]
    public async Task<IActionResult> Report(
        Guid agentId,
        Guid serverInstanceId,
        ReportServerInstanceStateRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorizeRoute(agentId, out IActionResult? failure))
        {
            return failure!;
        }

        if (!Enum.TryParse(request.Status, ignoreCase: false, out ServerInstanceStatus status) ||
            status is not (ServerInstanceStatus.Running or
                ServerInstanceStatus.Stopped or
                ServerInstanceStatus.Crashed))
        {
            return InvalidReport();
        }

        StateReportResult result = await serverInstances.ReportAsync(
            agentId,
            serverInstanceId,
            status,
            request.ProcessId,
            request.ProcessStartedAt,
            cancellationToken);
        if (result is StateReportResult.Succeeded or StateReportResult.AlreadyApplied)
        {
            LogStateReported(logger, agentId, serverInstanceId, status.ToString(), null);
            return NoContent();
        }

        if (result == StateReportResult.NotFound)
        {
            return NotFound();
        }

        LogRejectedStateReport(logger, agentId, serverInstanceId, null);
        return result == StateReportResult.InvalidProcessIdentity
            ? InvalidReport()
            : Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflict",
                detail: "The process-state report is stale or violates the ServerInstance transition rules.");
    }

    private bool TryAuthorizeRoute(Guid requestedAgentId, out IActionResult? failure)
    {
        if (currentAgent.AgentId is not Guid authenticatedAgentId)
        {
            failure = Unauthorized();
            return false;
        }

        if (requestedAgentId != authenticatedAgentId)
        {
            LogForeignAgentRoute(logger, authenticatedAgentId, requestedAgentId, null);
            failure = NotFound();
            return false;
        }

        failure = null;
        return true;
    }

    private ObjectResult InvalidReport() => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid process-state report",
        detail: "Running requires a positive PID and process start time; Stopped and Crashed require neither.");
}
