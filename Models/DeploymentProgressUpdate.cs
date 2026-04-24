namespace PortableCDriveCleaner.Models;

public sealed class DeploymentProgressUpdate
{
    public int Percent { get; init; }
    public string Message { get; init; } = string.Empty;
    public string DetailText { get; init; } = string.Empty;
    public bool IsIndeterminate { get; init; }
    public long? ProcessedBytes { get; init; }
    public long? TotalBytes { get; init; }
}
