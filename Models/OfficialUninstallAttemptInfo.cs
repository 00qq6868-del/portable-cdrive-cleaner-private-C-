namespace PortableCDriveCleaner.Models;

public sealed class OfficialUninstallAttemptInfo
{
    public Guid ItemId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string AppDisplayName { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Arguments { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public bool Succeeded { get; init; }
    public bool TimedOut { get; init; }
    public long DurationMs { get; init; }
    public string FailureReason { get; init; } = string.Empty;
}
