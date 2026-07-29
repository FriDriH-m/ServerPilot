namespace ServerPilot.Domain.ServerInstances;

public enum ServerInstanceStateReportResult
{
    Succeeded = 0,
    AlreadyApplied,
    InvalidState,
    InvalidProcessIdentity,
    StaleReport,
}
