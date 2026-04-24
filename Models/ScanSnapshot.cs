namespace PortableCDriveCleaner.Models;

public sealed class ScanSnapshot
{
    public required IReadOnlyList<CleanupItem> CleanupItems { get; init; }
    public required IReadOnlyList<CDriveOverviewEntry> CDriveOverviewEntries { get; init; }
    public required IReadOnlyList<InfrequentSoftwareEntry> InfrequentSoftwareEntries { get; init; }
    public required IReadOnlyList<MigrationCandidate> MigrationCandidates { get; init; }
    public IReadOnlyList<string> LoadedDrives { get; init; } = [];
    public IReadOnlyList<string> PendingDrives { get; init; } = [];
    public bool IsPartialResult { get; init; }
    public string PhaseLabel { get; init; } = string.Empty;
    public IReadOnlyList<ScanWarning> Warnings { get; init; } = [];
    public required IReadOnlySet<Guid> SafeDefaultSelectionIds { get; init; }
    public required IReadOnlySet<Guid> AdditionalReviewSelectionIds { get; init; }
    public required IReadOnlyDictionary<string, string> InstalledAppMappings { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> DuplicateGroups { get; init; }
    public required IReadOnlyDictionary<string, string> WhitelistHints { get; init; }
}
