using System.Drawing;
using System.Text.Json.Serialization;
using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Models;

public sealed class InfrequentSoftwareEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string AppIdentityKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string PrimaryDrive { get; init; } = string.Empty;
    public string InstallRoot { get; init; } = string.Empty;
    public string IconSourcePath { get; init; } = string.Empty;
    public IReadOnlyList<string> CompanionPaths { get; init; } = [];
    public long SizeBytes { get; init; }
    public DateTime? LastUsedUtc { get; init; }
    public int? UnusedDays { get; init; }
    public AppUsageConfidence UsageConfidence { get; init; } = AppUsageConfidence.Unknown;
    public string UsageSourceText { get; init; } = "无可靠记录";
    public string UsageEvidenceText { get; init; } = string.Empty;
    public string RecommendationText { get; init; } = string.Empty;
    public string DeletionImpactText { get; init; } = string.Empty;
    public bool CanDeepDelete { get; init; }
    public bool IsProtected { get; init; }
    public bool IsWhitelisted { get; init; }
    public bool IsCurrentlyRunning { get; init; }
    public string RunningProcessText { get; init; } = string.Empty;
    public bool RecommendedForDeletion { get; init; }
    public InfrequentSoftwareStatusKind StatusKind { get; init; } = InfrequentSoftwareStatusKind.ReviewOnly;
    public bool Selected { get; set; }
    [JsonIgnore]
    public Image? IconImage { get; set; }

    public string SizeText => SizeFormatter.Format(SizeBytes);

    public string LastUsedText => LastUsedUtc.HasValue
        ? IsCurrentlyRunning
            ? "当前正在运行"
            : LastUsedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd")
        : "未检测到可靠使用记录";

    public string UnusedDaysText => IsCurrentlyRunning
        ? "正在使用中"
        : UnusedDays.HasValue
        ? $"{UnusedDays.Value} 天"
        : "未检测到";

    public string UsageConfidenceText => UsageConfidence switch
    {
        AppUsageConfidence.High => "高",
        AppUsageConfidence.Medium => "中",
        AppUsageConfidence.Low => "低",
        _ => "未知"
    };

    public string StatusText => StatusKind switch
    {
        InfrequentSoftwareStatusKind.RecommendedDeletion => "建议删除",
        InfrequentSoftwareStatusKind.CurrentRunning => "当前正在用",
        InfrequentSoftwareStatusKind.ActiveRecent => "当前常用",
        InfrequentSoftwareStatusKind.NoReliableUsageRecord => "无可靠记录",
        InfrequentSoftwareStatusKind.Whitelisted => "已白名单",
        InfrequentSoftwareStatusKind.Protected => "系统保护",
        _ => "建议确认"
    };
}
