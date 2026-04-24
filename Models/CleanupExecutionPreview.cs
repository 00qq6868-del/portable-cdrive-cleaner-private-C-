using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Models;

public sealed class CleanupExecutionPreview
{
    public string Title { get; init; } = string.Empty;
    public string ConfirmButtonText { get; init; } = "确认处理";
    public string IntroText { get; init; } = string.Empty;
    public string RiskHintText { get; init; } = string.Empty;
    public bool RequiresSecondaryRiskAcknowledgement { get; init; }
    public string SecondaryRiskAcknowledgementText { get; init; } = string.Empty;
    public required IReadOnlyList<CleanupExecutionPreviewItem> Items { get; init; }
    public IReadOnlyList<ResidueAction> ResidueActions { get; init; } = [];
    public long TotalBytes { get; init; }

    public int TotalCount => Items.Count;
    public string TotalBytesText => SizeFormatter.Format(TotalBytes);
    public int ResidueActionCount => ResidueActions.Count;
    public bool HasResidueActions => ResidueActions.Count > 0;
}

public sealed class CleanupExecutionPreviewItem
{
    public required CleanupItem Item { get; init; }

    public string DriveName => Item.DriveName;
    public string Category => Item.Category;
    public string Name => Item.Name;
    public string TypeDescription => Item.TypeDescription;
    public string RecommendationText => Item.RecommendedActionText;
    public string ImpactText => Item.ImpactText;
    public string ImpactSeverityText => Item.ImpactSeverity switch
    {
        CleanupImpactSeverity.Low => "低",
        CleanupImpactSeverity.Medium => "中",
        CleanupImpactSeverity.High => "高",
        _ => "低"
    };
    public string SizeText => SizeFormatter.Format(Item.SizeBytes);
}
