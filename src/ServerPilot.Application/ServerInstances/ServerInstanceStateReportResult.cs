namespace ServerPilot.Application.ServerInstances;

public enum ServerInstanceStateReportResult
{
    Succeeded = 0,
    AlreadyApplied,
    NotFound,
    InvalidState,
    InvalidProcessIdentity,
    InvalidMetrics,
    StaleReport,
}
