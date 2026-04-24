namespace PortableCDriveCleaner.Models;

public sealed class CleanupResult
{
    public Guid ItemId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string NormalizedPath { get; init; } = string.Empty;
    public CleanupTargetKind TargetKind { get; init; }
    public long OriginalBytes { get; init; }
    public long FreedBytes { get; init; }
    public long RemainingBytes { get; init; }
    public string SizeText { get; init; } = string.Empty;
    public string FreedText { get; init; } = string.Empty;
    public string RemainingText { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public bool ExistsAfter { get; init; }
    public int DeletedEntryCount { get; init; }
    public int RemainingEntryCount { get; init; }
    public IReadOnlyList<string> BlockingProcessNames { get; init; } = [];
    public bool RetrySuggested { get; init; }
    public string? Message { get; init; }
}
