namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed class ReportServerInstanceStateRequest
{
    public string? Status { get; init; }

    public int? ProcessId { get; init; }

    public DateTimeOffset? ProcessStartedAt { get; init; }

    public double? CpuUsagePercent { get; init; }

    public long? WorkingSetBytes { get; init; }

    public long? UptimeSeconds { get; init; }
}
