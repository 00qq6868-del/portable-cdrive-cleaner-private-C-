namespace PortableCDriveCleaner.Models;

public sealed class ResidueVerificationResult
{
    public bool VerifiedClean { get; init; }
    public int RemainingResidueCount { get; init; }
    public IReadOnlyList<string> RemainingResidues { get; init; } = [];
    public IReadOnlyList<ResidueAction> RemainingActions { get; init; } = [];
    public int RemainingRegistryResidueCount { get; init; }
    public int RemainingShortcutResidueCount { get; init; }
    public int RemainingTaskResidueCount { get; init; }
    public int RemainingServiceResidueCount { get; init; }
    public int RemainingFirewallResidueCount { get; init; }
    public int RemainingDirectoryResidueCount { get; init; }
    public int RemainingFileResidueCount { get; init; }
}
