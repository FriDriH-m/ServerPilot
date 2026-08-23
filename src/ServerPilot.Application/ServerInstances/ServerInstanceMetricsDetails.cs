namespace ServerPilot.Application.ServerInstances;

public sealed record ServerInstanceMetricsDetails(
    double? CpuUsagePercent,
    long WorkingSetBytes,
    long UptimeSeconds,
    DateTimeOffset ReportedAt);
