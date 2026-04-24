namespace PortableCDriveCleaner.Models;

public sealed class ScanWarning
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public string Phase { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string AffectedPath { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public ScanWarningSeverity Severity { get; init; } = ScanWarningSeverity.Warning;
}
