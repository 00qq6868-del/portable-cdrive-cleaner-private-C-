using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Models;

public sealed class CDriveOverviewEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Category { get; init; } = string.Empty;
    public string PurposeText { get; init; } = string.Empty;
    public CDriveProtectionLevel ProtectionLevel { get; init; } = CDriveProtectionLevel.SystemProtected;
    public string RecommendationText { get; init; } = string.Empty;
    public string IconSourcePath { get; init; } = string.Empty;
    public string IconInstallRoot { get; init; } = string.Empty;
    public bool CanDelete { get; init; }
    public bool CanRelocate { get; init; }
    public bool CanAutoMigrate { get; init; }
    public MigrationMode MigrationMode { get; init; } = MigrationMode.Protected;
    public MigrationStrategy MigrationStrategy { get; init; } = MigrationStrategy.Protected;
    public string AdapterKey { get; init; } = string.Empty;
    public IReadOnlyList<string> CompanionPaths { get; init; } = [];
    public string ValidationTargetExe { get; init; } = string.Empty;
    public bool CanUseGenericJunctionRelocation { get; init; }
    public string MigrationHintText { get; init; } = string.Empty;
    public bool SelectionEnabled { get; init; }
    public string InstalledAppName { get; init; } = string.Empty;
    public bool Selected { get; set; }

    public string SizeText => SizeFormatter.Format(SizeBytes);
    public string ProtectionText => ProtectionLevel switch
    {
        CDriveProtectionLevel.SystemProtected => "系统保护",
        CDriveProtectionLevel.SharedComponent => "共享组件",
        CDriveProtectionLevel.MoveRecommended => "建议迁移",
        CDriveProtectionLevel.ReviewBeforeDelete => "先确认",
        _ => "系统保护"
    };

    public string OperationHintText => MigrationMode switch
    {
        MigrationMode.AutoSafe when MigrationStrategy == MigrationStrategy.Adapter => "适配后自动迁移",
        MigrationMode.AutoSafe => "可自动迁移",
        MigrationMode.GuideOnly when Category == "第三方已安装应用" => "需卸载重装",
        MigrationMode.GuideOnly when IsKnownUserEntryRoot(Path) => "先打开后迁内容",
        MigrationMode.GuideOnly => "需手动迁移",
        _ => CanDelete ? "高风险确认" : "高风险保护"
    };

    private static bool IsKnownUserEntryRoot(string path)
    {
        var normalized = Normalize(path);
        var knownRoots = new[]
        {
            Normalize(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            Normalize(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            Normalize(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            Normalize(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))
        };

        return knownRoots.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
    }
}
