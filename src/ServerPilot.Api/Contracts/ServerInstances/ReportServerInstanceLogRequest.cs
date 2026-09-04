namespace ServerPilot.Api.Contracts.ServerInstances;

public sealed class ReportServerInstanceLogRequest
{
    public string? Status { get; init; }

    public string? SourceIdentifier { get; init; }

    public Guid? StreamId { get; init; }

    public long? FromOffset { get; init; }

    public long? ToOffset { get; init; }

    public bool Reset { get; init; }

    public string? Content { get; init; }
}
