namespace PortableCDriveCleaner.Models;

public sealed class OperationJob
{
    public Guid Id { get; init; }
    public OperationJobKind Kind { get; init; }
    public OperationJobState State { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int Percent { get; init; }
    public bool IsIndeterminate { get; init; }
    public long? ProcessedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public string JobScope { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string SummaryText { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public bool HasWarnings { get; init; }
    public int WarningCount { get; init; }
    public string WarningSummary { get; init; } = string.Empty;
}

public sealed class OperationQueueState
{
    public IReadOnlyList<OperationJob> Jobs { get; init; } = [];
    public int RunningCount { get; init; }
    public int QueuedCount { get; init; }
    public int CompletedCount { get; init; }
    public int WarningCompletedCount { get; init; }
}
