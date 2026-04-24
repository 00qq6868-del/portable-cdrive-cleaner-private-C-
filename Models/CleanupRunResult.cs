namespace PortableCDriveCleaner.Models;

public sealed class CleanupRunResult
{
    public Guid JobId { get; init; }
    public string Phase { get; init; } = string.Empty;
    public required IReadOnlyList<CleanupResult> Results { get; init; }
    public required long FreedBytes { get; init; }
    public required string LogPath { get; init; }
    public int DeletedItemCount { get; init; }
    public int SkippedLockedCount { get; init; }
    public bool ExistsAfter { get; init; }
    public bool VerifiedClean { get; init; }
    public bool InvokedOfficialUninstaller { get; init; }
    public bool OfficialUninstallAttempted { get; init; }
    public int OfficialUninstallSucceededCount { get; init; }
    public int OfficialUninstallFailedCount { get; init; }
    public bool DeepVerificationPassed { get; init; }
    public int RegistryResidueRemovedCount { get; init; }
    public int ShortcutResidueRemovedCount { get; init; }
    public int TaskResidueRemovedCount { get; init; }
    public int ServiceResidueRemovedCount { get; init; }
    public int FirewallResidueRemovedCount { get; init; }
    public IReadOnlyList<string> ResidualWarnings { get; init; } = [];
    public IReadOnlyList<ResidueAction> RemainingResidueActions { get; init; } = [];
    public int RemainingRegistryResidueCount { get; init; }
    public int RemainingShortcutResidueCount { get; init; }
    public int RemainingTaskResidueCount { get; init; }
    public int RemainingServiceResidueCount { get; init; }
    public int RemainingFirewallResidueCount { get; init; }
    public int RemainingDirectoryResidueCount { get; init; }
    public int RemainingFileResidueCount { get; init; }
    public IReadOnlyList<OfficialUninstallAttemptInfo> OfficialUninstallAttempts { get; init; } = [];
    public IReadOnlyList<string> FollowUpRecommendations { get; init; } = [];
    public IReadOnlyList<string> BlockingProcesses { get; init; } = [];
    public IReadOnlyList<ResidueAction> ResidueActions { get; init; } = [];
}
