using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Models;

public sealed class MigrationCandidate
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Category { get; init; } = string.Empty;
    public string PurposeText { get; init; } = string.Empty;
    public string RecommendationText { get; init; } = string.Empty;
    public string IconSourcePath { get; init; } = string.Empty;
    public string IconInstallRoot { get; init; } = string.Empty;
    public string MigrationHintText { get; init; } = string.Empty;
    public MigrationMode MigrationMode { get; init; } = MigrationMode.Protected;
    public MigrationStrategy MigrationStrategy { get; init; } = MigrationStrategy.Protected;
    public string AdapterKey { get; init; } = string.Empty;
    public IReadOnlyList<string> CompanionPaths { get; init; } = [];
    public string ValidationTargetExe { get; init; } = string.Empty;
    public bool CanUseGenericJunctionRelocation { get; init; }
    public bool SelectionEnabled { get; init; }
    public bool CanDelete { get; init; }
    public string InstalledAppName { get; init; } = string.Empty;
    public bool Selected { get; set; }

    public string SizeText => SizeFormatter.Format(SizeBytes);

    public string MigrationModeText => MigrationMode switch
    {
        MigrationMode.AutoSafe when MigrationStrategy == MigrationStrategy.Adapter => "适配后自动迁移",
        MigrationMode.AutoSafe => "可自动迁移",
        MigrationMode.GuideOnly when Category == "第三方已安装应用" => "需卸载重装",
        MigrationMode.GuideOnly when IsKnownUserEntryRoot(SourcePath) => "先打开后迁内容",
        MigrationMode.GuideOnly => "需手动迁移",
        _ => CanDelete ? "高风险确认" : "高风险保护"
    };

    private static bool IsKnownUserEntryRoot(string path)
    {
        var normalized = Normalize(path);
        var knownRoots = new[]
        {
            Normalize(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            Normalize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
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
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
