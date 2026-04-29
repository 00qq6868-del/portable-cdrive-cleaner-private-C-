using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Infrastructure;

public enum IconSemanticKind
{
    Application,
    Directory,
    Drive,
    UserData,
    Cache,
    Logs,
    Package,
    Duplicate,
    Cleanup,
    System,
    Document
}

public readonly record struct IconLookupRequest(
    string? IconSourcePath,
    string? InstallRoot,
    IconSemanticKind SemanticKind)
{
    public string BuildCacheKey()
    {
        return $"{SemanticKind}|{NormalizeFragment(IconSourcePath)}|{NormalizeFragment(InstallRoot)}";
    }

    private static string NormalizeFragment(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "-"
            : value.Trim();
    }
}

public static class IconSemanticResolver
{
    public static IconLookupRequest DefaultCleanup()
    {
        return new IconLookupRequest(null, null, IconSemanticKind.Document);
    }

    public static IconLookupRequest DefaultOverview()
    {
        return new IconLookupRequest(null, null, IconSemanticKind.Directory);
    }

    public static IconLookupRequest DefaultInfrequentApp()
    {
        return new IconLookupRequest(null, null, IconSemanticKind.Application);
    }

    public static IconLookupRequest ForCleanupRow(
        CleanupSelectionRow row,
        string? iconSourcePath,
        string? installRoot)
    {
        return new IconLookupRequest(iconSourcePath, installRoot, ResolveCleanupSemantic(row));
    }

    public static IconLookupRequest ForOverviewEntry(
        CDriveOverviewEntry entry,
        string? iconSourcePath,
        string? installRoot)
    {
        return new IconLookupRequest(iconSourcePath, installRoot, ResolveOverviewSemantic(entry));
    }

    public static IconLookupRequest ForMigrationCandidate(
        MigrationCandidate candidate,
        string? iconSourcePath,
        string? installRoot)
    {
        return new IconLookupRequest(iconSourcePath, installRoot, ResolveMigrationSemantic(candidate));
    }

    public static IconLookupRequest ForInfrequentApp(InfrequentSoftwareEntry entry)
    {
        return new IconLookupRequest(entry.IconSourcePath, entry.InstallRoot, IconSemanticKind.Application);
    }

    private static IconSemanticKind ResolveCleanupSemantic(CleanupSelectionRow row)
    {
        var item = row.Item;
        if (IsFileTarget(item.TargetKind))
        {
            return ResolveFileCleanupSemantic(row);
        }

        if (item.IsApplicationRelated || item.CandidateKind == CleanupCandidateKind.AppResidue)
        {
            return IconSemanticKind.Application;
        }

        return item.CandidateKind switch
        {
            CleanupCandidateKind.AppCache => ClassifyFromText(item.Name, item.Category, item.RuleSource, row.Path, preferCache: true),
            CleanupCandidateKind.BrowserCache => IconSemanticKind.Cache,
            CleanupCandidateKind.ChatCache => IconSemanticKind.Cache,
            CleanupCandidateKind.PrivacyTrace => IconSemanticKind.Document,
            CleanupCandidateKind.Package => IconSemanticKind.Package,
            CleanupCandidateKind.DuplicateFile => IconSemanticKind.Duplicate,
            CleanupCandidateKind.LargeDirectory => IsTrueUserDataDirectory(row.Path, item.Name)
                ? IconSemanticKind.UserData
                : IconSemanticKind.Directory,
            CleanupCandidateKind.LargeFile => ClassifyLargeFile(row.Path, item.Name, item.TypeDescription),
            CleanupCandidateKind.Aggregate => IconSemanticKind.Cleanup,
            CleanupCandidateKind.SafeJunk => ClassifyFromText(item.Name, item.Category, item.RuleSource, row.Path, preferCache: false),
            _ => IconSemanticKind.Cleanup
        };
    }

    private static IconSemanticKind ResolveFileCleanupSemantic(CleanupSelectionRow row)
    {
        var item = row.Item;
        if (item.IsApplicationRelated || item.CandidateKind == CleanupCandidateKind.AppResidue)
        {
            return IconSemanticKind.Application;
        }

        return item.CandidateKind switch
        {
            CleanupCandidateKind.Package => IconSemanticKind.Package,
            CleanupCandidateKind.DuplicateFile => IconSemanticKind.Duplicate,
            CleanupCandidateKind.LargeFile => ClassifyLargeFile(row.Path, item.Name, item.TypeDescription),
            CleanupCandidateKind.PrivacyTrace => IconSemanticKind.Document,
            CleanupCandidateKind.AppCache or CleanupCandidateKind.BrowserCache or CleanupCandidateKind.ChatCache =>
                ClassifyFileFromPath(row.Path, item.Name, item.TypeDescription, item.RuleSource),
            CleanupCandidateKind.SafeJunk => ClassifyFileFromPath(row.Path, item.Name, item.TypeDescription, item.RuleSource),
            _ => ClassifyFileFromPath(row.Path, item.Name, item.TypeDescription, item.RuleSource)
        };
    }

    private static IconSemanticKind ResolveOverviewSemantic(CDriveOverviewEntry entry)
    {
        if (File.Exists(entry.Path))
        {
            return ClassifyFileFromPath(entry.Path, entry.Name, entry.PurposeText, entry.Category);
        }

        return entry.Category switch
        {
            "第三方已安装应用" => IconSemanticKind.Application,
            "用户数据目录" => IconSemanticKind.UserData,
            "系统核心" => IconSemanticKind.System,
            "系统共享组件" => IconSemanticKind.System,
            "应用缓存/数据" => IconSemanticKind.Cache,
            "未知大目录" => IsDriveRoot(entry.Path)
                ? IconSemanticKind.Drive
                : IconSemanticKind.Directory,
            _ => IsDriveRoot(entry.Path)
                ? IconSemanticKind.Drive
                : IconSemanticKind.Directory
        };
    }

    private static IconSemanticKind ResolveMigrationSemantic(MigrationCandidate candidate)
    {
        return candidate.Category switch
        {
            "第三方已安装应用" => IconSemanticKind.Application,
            "用户数据目录" => IconSemanticKind.UserData,
            "应用缓存/数据" => IconSemanticKind.Cache,
            "系统核心" => IconSemanticKind.System,
            "系统共享组件" => IconSemanticKind.System,
            _ => IsDriveRoot(candidate.SourcePath)
                ? IconSemanticKind.Drive
                : IconSemanticKind.Directory
        };
    }

    private static IconSemanticKind ClassifyLargeFile(string path, string name, string typeDescription)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (PackageExtensions.Contains(extension))
        {
            return IconSemanticKind.Package;
        }

        if (LogExtensions.Contains(extension))
        {
            return IconSemanticKind.Logs;
        }

        if (name.Contains("安装包", StringComparison.OrdinalIgnoreCase)
            || typeDescription.Contains("安装包", StringComparison.OrdinalIgnoreCase)
            || name.Contains("压缩", StringComparison.OrdinalIgnoreCase)
            || typeDescription.Contains("压缩", StringComparison.OrdinalIgnoreCase))
        {
            return IconSemanticKind.Package;
        }

        return IconSemanticKind.Document;
    }

    private static IconSemanticKind ClassifyFileFromPath(
        string path,
        string name,
        string typeDescription,
        string ruleSource)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (PackageExtensions.Contains(extension)
            || ContainsAny($"{name} {typeDescription} {ruleSource}", "安装包", "压缩", "package", "installer", "setup"))
        {
            return IconSemanticKind.Package;
        }

        if (LogExtensions.Contains(extension)
            || ContainsAny($"{name} {typeDescription} {ruleSource}", "日志", "log", "dump", "转储", "report"))
        {
            return IconSemanticKind.Logs;
        }

        // Unknown, extensionless, inaccessible, or still-loading file targets must fall back to
        // the Windows generic document icon, never a folder.
        return IconSemanticKind.Document;
    }

    private static IconSemanticKind ClassifyFromText(
        string name,
        string category,
        string ruleSource,
        string path,
        bool preferCache)
    {
        var text = $"{name} {category} {ruleSource} {path}";
        if (ContainsAny(text, "日志", "log", "dump", "转储", "panther", "wer", "report", "minidump", "memory.dmp"))
        {
            return IconSemanticKind.Logs;
        }

        if (ContainsAny(text, "下载", "download", "update", "升级包", "安装包", "package", ".zip", ".7z", ".rar", ".iso", ".msi"))
        {
            return IconSemanticKind.Package;
        }

        if (ContainsAny(text, "缓存", "cache", "cacheddata", "service worker", "shader", "dxcache", "glcache", "computecache", "deliveryoptimization"))
        {
            return IconSemanticKind.Cache;
        }

        if (ContainsAny(text, "temp", "临时", "tmp", "softwaredistribution", "sysreset", "旧系统版本备份"))
        {
            return IconSemanticKind.Cleanup;
        }

        if (IsTrueUserDataDirectory(path, name))
        {
            return IconSemanticKind.UserData;
        }

        return preferCache ? IconSemanticKind.Cache : IconSemanticKind.Cleanup;
    }

    private static bool ContainsAny(string source, params string[] tokens)
    {
        return tokens.Any(token => source.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFileTarget(CleanupTargetKind targetKind)
    {
        return targetKind is CleanupTargetKind.FilePermanent or CleanupTargetKind.FileRecycle;
    }

    private static bool IsTrueUserDataDirectory(string path, string name)
    {
        if (IsDriveRoot(path))
        {
            return false;
        }

        if (ContainsAny(name, "桌面", "下载", "文档", "视频", "图片", "音乐"))
        {
            return true;
        }

        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var knownRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        }
        .Select(NormalizePath)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();

        return knownRoots.Any(root => normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDriveRoot(string path)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var root = Path.GetPathRoot(normalized);
        return !string.IsNullOrWhiteSpace(root)
            && string.Equals(
                normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
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
            return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static readonly HashSet<string> PackageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip",
        ".7z",
        ".rar",
        ".iso",
        ".img",
        ".cab",
        ".msi",
        ".exe"
    };

    private static readonly HashSet<string> LogExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log",
        ".dmp",
        ".etl",
        ".wer"
    };
}
