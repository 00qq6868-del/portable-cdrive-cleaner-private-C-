namespace PortableCDriveCleaner.Models;

public sealed class RegressionAuditEntry
{
    public int Order { get; init; }
    public string Category { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public RegressionAuditStatus Status { get; init; } = RegressionAuditStatus.Partial;
    public string ConclusionText { get; init; } = string.Empty;
    public string NextActionText { get; init; } = string.Empty;
    public string NotesText { get; init; } = string.Empty;
    public bool IsCoreOpen => Status == RegressionAuditStatus.Partial;
    public bool CountsAsClosed => Status != RegressionAuditStatus.Partial;

    public string StatusText => Status switch
    {
        RegressionAuditStatus.Resolved => "已修复",
        RegressionAuditStatus.Partial => "仍在推进",
        RegressionAuditStatus.ProtectedRule => "保护规则",
        RegressionAuditStatus.FollowUp => "主体完成",
        _ => "待确认"
    };
}
