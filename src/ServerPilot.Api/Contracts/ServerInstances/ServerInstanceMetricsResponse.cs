namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed record ServerInstanceMetricsResponse(
    double? CpuUsagePercent,
    long WorkingSetBytes,
    long UptimeSeconds,
    DateTimeOffset ReportedAt,
    bool IsStale);
