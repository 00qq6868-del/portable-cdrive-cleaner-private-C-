namespace PortableCDriveCleaner.Models;

public sealed class AppUsageEvidence
{
    public string Source { get; init; } = string.Empty;
    public DateTime? LastUsedUtc { get; init; }
    public AppUsageConfidence Confidence { get; init; } = AppUsageConfidence.Unknown;
    public string Details { get; init; } = string.Empty;
}
