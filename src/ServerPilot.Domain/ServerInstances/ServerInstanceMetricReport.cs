namespace ServerPilot.Domain.ServerInstances;

public sealed record ServerInstanceMetricReport(
    double? CpuUsagePercent,
    long WorkingSetBytes,
    long UptimeSeconds)
{
    public bool IsValid =>
        (!CpuUsagePercent.HasValue ||
         (double.IsFinite(CpuUsagePercent.Value) &&
          CpuUsagePercent.Value is >= 0d and <= 100d)) &&
        WorkingSetBytes >= 0 &&
        UptimeSeconds >= 0;
}
