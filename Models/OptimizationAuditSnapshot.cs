namespace PortableCDriveCleaner.Models;

public sealed class OptimizationAuditSnapshot
{
    public required IReadOnlyList<OptimizationAuditEntry> Entries { get; init; }
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
    public int StartupItemCount => Entries.Count(entry => entry.Category == OptimizationAuditCategory.StartupItem);
    public int HighImpactCount => Entries.Count(entry => entry.ImpactLevel == OptimizationAuditImpact.High);
    public int ReviewCount => Entries.Count(entry => entry.RecommendationLevel == OptimizationAuditRecommendation.Review);
    public int SafeCount => Entries.Count(entry => entry.RecommendationLevel == OptimizationAuditRecommendation.Safe);
}

public sealed class OptimizationAuditEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public OptimizationAuditCategory Category { get; init; } = OptimizationAuditCategory.StartupItem;
    public string Name { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string CommandLine { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string PublisherHint { get; init; } = string.Empty;
    public OptimizationAuditImpact ImpactLevel { get; init; } = OptimizationAuditImpact.Medium;
    public OptimizationAuditRecommendation RecommendationLevel { get; init; } = OptimizationAuditRecommendation.Review;
    public string ReasonText { get; init; } = string.Empty;
    public string SafeActionText { get; init; } = string.Empty;

    public string CategoryText => Category switch
    {
        OptimizationAuditCategory.StartupItem => "启动项",
        OptimizationAuditCategory.ScheduledTask => "计划任务",
        OptimizationAuditCategory.Service => "服务",
        _ => "体检项"
    };

    public string ImpactText => ImpactLevel switch
    {
        OptimizationAuditImpact.High => "高影响",
        OptimizationAuditImpact.Low => "低影响",
        _ => "中影响"
    };

    public string RecommendationText => RecommendationLevel switch
    {
        OptimizationAuditRecommendation.Safe => "建议保留",
        OptimizationAuditRecommendation.Review => "建议确认",
        OptimizationAuditRecommendation.Caution => "谨慎处理",
        _ => "建议确认"
    };

    public string LocationText => string.IsNullOrWhiteSpace(TargetPath) ? Source : TargetPath;
}

public enum OptimizationAuditCategory
{
    StartupItem,
    ScheduledTask,
    Service
}

public enum OptimizationAuditImpact
{
    Low,
    Medium,
    High
}

public enum OptimizationAuditRecommendation
{
    Safe,
    Review,
    Caution
}
