using ServerPilot.Agent.Credentials;

namespace ServerPilot.Agent.Api;

public interface IAgentApiClient
{
    Task SendHeartbeatAsync(AgentCredential credential, CancellationToken cancellationToken);

    Task<IReadOnlyList<AssignedAgentServerInstance>> ListServerInstancesAsync(
        AgentCredential credential,
        CancellationToken cancellationToken);

    Task ReportServerInstanceStateAsync(
        AgentCredential credential,
        Guid serverInstanceId,
        AgentProcessStateReport report,
        CancellationToken cancellationToken);

    Task ReportServerInstanceStateAsync(
        AgentCredential credential,
        Guid serverInstanceId,
        AgentProcessStateReport report,
        Guid correlationId,
        CancellationToken cancellationToken) =>
        ReportServerInstanceStateAsync(
            credential,
            serverInstanceId,
            report,
            cancellationToken);

    Task<ClaimedAgentCommand?> ClaimNextCommandAsync(
        AgentCredential credential,
        CancellationToken cancellationToken);

    Task MarkCommandRunningAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        CancellationToken cancellationToken);

    Task CompleteCommandAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        CancellationToken cancellationToken);

    Task FailCommandAsync(
        AgentCredential credential,
        ClaimedAgentCommand command,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);

    Task CompleteBackupAsync(AgentCredential credential, ClaimedAgentCommand command,
        BackupArtifact artifact, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Backup reporting is not implemented by this client.");
}

public sealed record BackupArtifact(long SizeBytes, string Checksum);
