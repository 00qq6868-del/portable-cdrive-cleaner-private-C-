namespace PortableCDriveCleaner.Models;

public sealed class CleanupItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string DriveName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string TypeDescription { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string IconSourcePath { get; init; } = string.Empty;
    public string IconInstallRoot { get; init; } = string.Empty;
    public string NormalizedPath { get; init; } = string.Empty;
    public string RuleSource { get; init; } = string.Empty;
    public CleanupCandidateKind CandidateKind { get; init; } = CleanupCandidateKind.SafeJunk;
    public string InstalledAppId { get; init; } = string.Empty;
    public string AppIdentityKey { get; init; } = string.Empty;
    public IReadOnlyList<string> CompanionPaths { get; init; } = [];
    public bool IsApplicationRelated { get; init; }
    public bool DeepCleanupEligible { get; init; }
    public string DuplicateGroupKey { get; init; } = string.Empty;
    public CleanupTargetKind TargetKind { get; init; }
    public string Note { get; init; } = string.Empty;
    public string ImpactText { get; init; } = string.Empty;
    public CleanupImpactSeverity ImpactSeverity { get; init; } = CleanupImpactSeverity.Low;
    public long SizeBytes { get; init; }
    public bool SafeAuto { get; init; }
    public bool Recommended { get; init; }
    public string WhitelistHintText { get; init; } = string.Empty;
    public string BlockerHintText { get; init; } = string.Empty;

    public string RecommendedActionText => ImpactSeverity switch
    {
        CleanupImpactSeverity.Low when SafeAuto => "可安全删",
        CleanupImpactSeverity.High => "谨慎处理",
        _ => "建议确认"
    };
}
