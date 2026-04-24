namespace PortableCDriveCleaner.Models;

public sealed class OperationProgress
{
    public int Percent { get; init; }
    public string Phase { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool IsIndeterminate { get; init; }
    public long? ProcessedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public string JobScope { get; init; } = string.Empty;
}
