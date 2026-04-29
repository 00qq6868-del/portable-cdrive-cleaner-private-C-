using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class ScanService
{
    private static readonly string[] DriveRootJunkFolders = ["Temp", "tmp", "Logs", "logs", "Log", "log"];
    private static readonly string[] DuplicateFolderNames =
    [
        "Downloads", "Download", "Desktop", "桌面", "安装包", "Install", "Installer", "Installers",
        "Software", "软件", "Packages", "Package", "Drivers", "Driver", "镜像", "ISO", "Tools"
    ];
    private static readonly string[] RootProtectedNames =
    [
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "Recovery", "System Volume Information",
        "$Recycle.Bin", "$RECYCLE.BIN", "$WinREAgent", "$360Section", "WindowsApps", "WpSystem", "Documents and Settings",
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp"
    ];
    private static readonly string[] RootCandidateKeywords =
    [
        "cache", "log", "logs", "temp", "tmp", "download", "downloads", "package", "packages", "backup", "old",
        "update", "updates", "cachefile", "installer", "installers", "setup", "residue", "residual"
    ];
    private static readonly string[] BroadFolderKeywords =
    [
        "download", "downloads", "desktop", "documents", "document", "videos", "video", "music", "software",
        "tools", "package", "packages", "install", "installer", "cache", "log", "temp", "backup", "old"
    ];
    private static readonly string[] GenericUserFolderNames =
    [
        "Users", "Desktop", "Downloads", "Documents", "Videos", "Pictures", "Music", "AppData", "ProgramData", "Temp"
    ];
    private static readonly string[] PackageExtensions = [".exe", ".msi", ".msix", ".zip", ".7z", ".rar", ".iso", ".cab"];
    private static readonly string[] SharedRuntimeNames =
    [
        "Common Files", "dotnet", "Microsoft", "Windows Defender", "WindowsApps", "Packages",
        "MSBuild", "Reference Assemblies", "Internet Explorer", "ModifiableWindowsApps",
        "Windows Subsystem for Linux", "WSL", "WSLg", "Visual C++", "VC++", "Runtime", "Redistributable"
    ];
    private static readonly string[] DriverBoundPublisherTokens =
    [
        "nvidia", "intel", "oem", "anti", "anticheat", "huorong", "realtek", "bonjour", "steelseries", "razer",
        "driver", "wsl"
    ];
    private static readonly string[] GenericMigrationBlockedTokens =
    [
        "installshield", "package cache", "redistributable", "runtime", "vulkanrt",
        "driver", "drivers", "anticheat", "anti-cheat", "anti cheat",
        "service", "services", "bonjour", "synchronization services", "windows kits", "sdk"
    ];
    private static readonly string[] OverviewProtectedFiles =
    [
        "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp"
    ];
    private static readonly string[] DevelopmentWorkspaceDirectoryMarkers =
    [
        ".git", ".vs", ".idea", ".vscode", "node_modules", ".venv", "venv", "src", "tests", "test"
    ];
    private static readonly string[] DevelopmentWorkspaceFileMarkers =
    [
        "package.json", "pnpm-lock.yaml", "yarn.lock", "package-lock.json", "pyproject.toml",
        "requirements.txt", "Cargo.toml", "go.mod", "pom.xml", "build.gradle", "composer.json"
    ];

    private const long DuplicateMinFileSizeBytes = 20L * 1024 * 1024;
    private const int DuplicateMaxCandidateCount = 2000;
    private const int DuplicateMaxDirectoryCount = 640;
    private const long PackageMinFileSizeBytes = 20L * 1024 * 1024;
    private const long LargeFileThresholdBytes = 512L * 1024 * 1024;
    private const long LargeDirectoryThresholdBytes = 1024L * 1024 * 1024;
    private const int BroadScanMaxDirectories = 900;
    private const int BroadScanMaxFiles = 3500;

    private readonly PortableContext? _context;
    private readonly HashSet<string> _protectedExactPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyList<MigrationAdapter> MigrationAdapters =
    [
        new MigrationAdapter
        {
            Key = "tencent-family",
            DisplayName = "Tencent 系",
            MatchTokens = ["tencent", "wechat", "weixin", "qq", "qqmusic", "wetype"],
            CompanionDirectoryNames = ["Tencent", "WeChat", "Weixin", "QQ", "QQMusic", "WeType"],
            ValidationTargetExeNames = ["WeChat.exe", "Weixin.exe", "QQ.exe", "QQMusic.exe", "WeType.exe"],
            AllowAutomaticMigration = true
        },
        new MigrationAdapter
        {
            Key = "netease-family",
            DisplayName = "NetEase 系",
            MatchTokens = ["netease", "cloudmusic"],
            CompanionDirectoryNames = ["NetEase", "CloudMusic"],
            ValidationTargetExeNames = ["cloudmusic.exe"],
            AllowAutomaticMigration = true
        },
        new MigrationAdapter
        {
            Key = "bilibili",
            DisplayName = "bilibili",
            MatchTokens = ["bilibili"],
            CompanionDirectoryNames = ["bilibili"],
            ValidationTargetExeNames = ["bilibili.exe"],
            AllowAutomaticMigration = true
        },
        new MigrationAdapter
        {
            Key = "jetbrains",
            DisplayName = "JetBrains",
            MatchTokens = ["jetbrains", "pycharm", "idea", "webstorm"],
            CompanionDirectoryNames = ["JetBrains", "PyCharm"],
            ValidationTargetExeNames = ["pycharm64.exe", "idea64.exe"],
            AllowAutomaticMigration = true
        },
        new MigrationAdapter
        {
            Key = "git-tools",
            DisplayName = "Git 工具",
            MatchTokens = ["git", "github desktop"],
            CompanionDirectoryNames = ["Git", "GitHubDesktop"],
            ValidationTargetExeNames = ["git.exe", "GitHubDesktop.exe"],
            AllowAutomaticMigration = true
        },
        new MigrationAdapter
        {
            Key = "clash-family",
            DisplayName = "Clash 系",
            MatchTokens = ["clash", "flclash"],
            CompanionDirectoryNames = ["Clash", "FlClash"],
            ValidationTargetExeNames = ["clash.exe", "Clash Verge.exe"],
            AllowAutomaticMigration = true
        },
        new MigrationAdapter
        {
            Key = "editor-family",
            DisplayName = "编辑器系",
            MatchTokens = ["visual studio code", "vscode", "cursor"],
            CompanionDirectoryNames = ["Microsoft VS Code", "Cursor"],
            ValidationTargetExeNames = ["Code.exe", "Cursor.exe"],
            AllowAutomaticMigration = true
        }
    ];

    public ScanService(PortableContext? context = null)
    {
        _context = context;
        if (context is null)
        {
            return;
        }

        _protectedExactPaths.Add(NormalizePath(context.AppRoot));
        _protectedExactPaths.Add(NormalizePath(Path.Combine(context.AppRoot, "data")));
    }

    public IReadOnlyList<DriveStatus> GetFixedDriveStatuses()
    {
        return GetFixedDrives()
            .Select(drive => GetDriveStatus(drive.Name))
            .ToList();
    }

    public DriveStatus GetDriveStatus(string driveName = "C")
    {
        var root = driveName.EndsWith(':') ? driveName + @"\" : driveName + @":\";
        var drive = new DriveInfo(root);
        return new DriveStatus
        {
            Name = GetDriveName(root),
            UsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
            FreeBytes = drive.AvailableFreeSpace,
            TotalBytes = drive.TotalSize
        };
    }

    public ScanSnapshot ScanSnapshot(AppSettings settings)
    {
        return ScanSnapshot(settings, ScanDriveScope.AllFixedDrives);
    }

    public ScanSnapshot ScanStartupCQuickSnapshot(
        AppSettings settings,
        IProgress<OperationProgress>? progress = null,
        int startPercent = 0,
        int endPercent = 100,
        string jobScope = "")
    {
        var runState = CreateScanRunState();
        var allFixedDrives = TryBuildScanPhase("枚举固定磁盘", GetFixedDrives, (IReadOnlyCollection<DriveInfo>)[], runState);
        return BuildSnapshot(settings, ScanDriveScope.SystemDriveOnly, allFixedDrives, runState, SnapshotBuildProfile.QuickCFirst, progress, startPercent, endPercent, jobScope);
    }

    public ScanSnapshot ScanStartupCAdviceSeedSnapshot(AppSettings settings)
    {
        var runState = CreateScanRunState();
        var allFixedDrives = TryBuildScanPhase("枚举固定磁盘", GetFixedDrives, (IReadOnlyCollection<DriveInfo>)[], runState);
        var selectedDrives = SelectDrivesByScope(allFixedDrives, ScanDriveScope.SystemDriveOnly);
        var loadedDrives = selectedDrives
            .Select(drive => GetDriveName(drive.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetDrivePriority)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var pendingDrives = allFixedDrives
            .Select(drive => GetDriveName(drive.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Except(loadedDrives, StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetDrivePriority)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var whitelistPaths = NormalizeWhitelistPaths(settings.WhitelistedPaths);
        var emptyApps = new InstalledAppSnapshot(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            []);
        var overviewEntries = TryBuildScanPhase(
            "首批 C盘建议",
            () => BuildCDriveOverviewEntries(emptyApps, quickOnly: true)
                .Where(entry => !IsWhitelisted(entry.Path, whitelistPaths))
                .ToList(),
            [],
            runState);
        var migrationCandidates = TryBuildScanPhase(
            "首批 C盘建议候选",
            () => BuildMigrationCandidates(overviewEntries),
            [],
            runState);

        return new ScanSnapshot
        {
            CleanupItems = [],
            CDriveOverviewEntries = overviewEntries,
            InfrequentSoftwareEntries = [],
            MigrationCandidates = migrationCandidates,
            LoadedDrives = loadedDrives,
            PendingDrives = pendingDrives,
            IsPartialResult = pendingDrives.Count > 0 || migrationCandidates.Count == 0,
            PhaseLabel = migrationCandidates.Count > 0
                ? $"已先整理第一批 {GetSystemDriveName()}盘建议，后续会继续补全"
                : $"正在整理第一批 {GetSystemDriveName()}盘建议，后续会继续补全",
            Warnings = runState.Warnings.ToList(),
            SafeDefaultSelectionIds = new HashSet<Guid>(),
            AdditionalReviewSelectionIds = new HashSet<Guid>(),
            InstalledAppMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DuplicateGroups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            WhitelistHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    public ScanSnapshot ScanStartupCCleanupSnapshot(
        AppSettings settings,
        IProgress<OperationProgress>? progress = null,
        int startPercent = 0,
        int endPercent = 100,
        string jobScope = "")
    {
        var runState = CreateScanRunState();
        var allFixedDrives = TryBuildScanPhase("枚举固定磁盘", GetFixedDrives, (IReadOnlyCollection<DriveInfo>)[], runState);
        return BuildSnapshot(settings, ScanDriveScope.SystemDriveOnly, allFixedDrives, runState, SnapshotBuildProfile.QuickCleanupOnly, progress, startPercent, endPercent, jobScope);
    }

    public ScanSnapshot ScanSnapshot(
        AppSettings settings,
        ScanDriveScope scope,
        IProgress<OperationProgress>? progress = null,
        int startPercent = 0,
        int endPercent = 100,
        string jobScope = "")
    {
        var runState = CreateScanRunState();
        var allFixedDrives = TryBuildScanPhase("枚举固定磁盘", GetFixedDrives, (IReadOnlyCollection<DriveInfo>)[], runState);
        return BuildSnapshot(settings, scope, allFixedDrives, runState, SnapshotBuildProfile.Full, progress, startPercent, endPercent, jobScope);
    }

    public List<CleanupItem> Scan(AppSettings settings)
    {
        return ScanSnapshot(settings).CleanupItems.ToList();
    }

    private ScanSnapshot BuildSnapshot(
        AppSettings settings,
        ScanDriveScope scope,
        IReadOnlyCollection<DriveInfo> allFixedDrives,
        ScanRunState runState,
        SnapshotBuildProfile profile,
        IProgress<OperationProgress>? progress = null,
        int startPercent = 0,
        int endPercent = 100,
        string jobScope = "")
    {
        var selectedDrives = SelectDrivesByScope(allFixedDrives, scope);
        var selectedDriveNames = selectedDrives
            .Select(drive => GetDriveName(drive.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetDrivePriority)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var includesSystemDrive = IncludesSystemDrive(selectedDriveNames);
        var includeRootHeuristics = profile == SnapshotBuildProfile.Full;
        var includeHeavyCandidates = profile == SnapshotBuildProfile.Full;
        var includeOverview = includesSystemDrive;
        var quickOverviewOnly = profile == SnapshotBuildProfile.QuickCFirst;
        var includeInfrequentSoftware = profile == SnapshotBuildProfile.Full;
        var includeReviewCandidates = includesSystemDrive && profile != SnapshotBuildProfile.QuickCleanupOnly;
        var includeInstalledApps = includeRootHeuristics || includeHeavyCandidates || includeOverview || includeInfrequentSoftware;
        var totalPhases = (includeInstalledApps ? 1 : 0)
            + 1
            + (includesSystemDrive ? 1 : 0)
            + (includeReviewCandidates ? 1 : 0)
            + (includeRootHeuristics ? 1 : 0)
            + (includeHeavyCandidates ? 2 : 0)
            + (includeOverview ? 1 : 0)
            + (includeInfrequentSoftware ? 1 : 0)
            + (includesSystemDrive && includeOverview ? 1 : 0)
            + 1;
        var completedPhases = 0;
        void ReportProgress(string phaseName, string message)
        {
            completedPhases++;
            ReportSnapshotProgress(progress, jobScope, completedPhases, totalPhases, startPercent, endPercent, phaseName, message);
        }

        var installedApps = includeInstalledApps
            ? TryBuildScanPhase(
                "读取已安装应用目录映射",
                LoadInstalledAppSnapshot,
                new InstalledAppSnapshot(
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    []),
                runState)
            : new InstalledAppSnapshot(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                []);
        if (includeInstalledApps)
        {
            ReportProgress("读取已安装应用目录映射", "已完成应用目录映射读取");
        }
        var whitelistPaths = NormalizeWhitelistPaths(settings.WhitelistedPaths);
        var items = new Dictionary<string, CleanupItem>(StringComparer.OrdinalIgnoreCase);

        TryRunScanPhase("已知安全垃圾", () => AddPerDriveJunkItems(items, selectedDrives), runState);
        ReportProgress("已知安全垃圾", $"已整理 {selectedDriveNames.Count} 个盘符中的低风险垃圾");
        if (includesSystemDrive)
        {
            TryRunScanPhase("系统垃圾候选", () => AddSystemJunkItems(items), runState);
            ReportProgress("系统垃圾候选", "已整理 C盘系统垃圾候选");
            if (includeReviewCandidates)
            {
                TryRunScanPhase("需确认缓存候选", () => AddReviewCacheItems(items), runState);
                ReportProgress("需确认缓存候选", "已整理需确认缓存候选");
            }
        }

        if (includeRootHeuristics)
        {
            TryRunScanPhase("根目录启发式候选", () => AddRootHeuristicCandidates(items, selectedDrives, installedApps, runState), runState);
            ReportProgress("根目录启发式候选", "已跳过系统保护目录并整理根目录候选");
        }
        if (includeHeavyCandidates)
        {
            TryRunScanPhase("宽范围文件候选", () => AddBroadFileCandidates(items, selectedDrives, installedApps, selectedDriveNames, runState), runState);
            ReportProgress("宽范围文件候选", "已完成宽范围文件候选整理");
            TryRunScanPhase("重复安装包与压缩包候选", () => AddDuplicateInstallerItems(items, settings, selectedDrives, selectedDriveNames, runState), runState);
            ReportProgress("重复安装包与压缩包候选", "已完成重复安装包与压缩包识别");
        }

        var cleanupItems = items.Values
            .Where(item => !IsWhitelisted(item.Path, whitelistPaths))
            .OrderBy(item => GetDrivePriority(item.DriveName))
            .ThenBy(item => item.ImpactSeverity)
            .ThenByDescending(item => item.Recommended)
            .ThenByDescending(item => item.SizeBytes)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var overviewEntries = includesSystemDrive && includeOverview
            ? TryBuildScanPhase(
                "C 盘总览",
                () => BuildCDriveOverviewEntries(installedApps, quickOnly: quickOverviewOnly)
                    .Where(entry => !IsWhitelisted(entry.Path, whitelistPaths))
                .ToList(),
                [],
                runState)
            : [];
        if (includeOverview)
        {
            ReportProgress(
                "C 盘总览",
                quickOverviewOnly
                    ? "已先整理第一批 C盘建议目录，后续会继续补全完整总览"
                    : "已整理 C盘可操作目录，总览中不再加入系统保护目录");
        }
        var infrequentEntries = includeInfrequentSoftware
            ? TryBuildScanPhase(
                "长期未用软件",
                () => BuildInfrequentSoftwareEntries(settings, selectedDrives, installedApps, selectedDriveNames, runState)
                    .Where(entry => !IsWhitelisted(entry.InstallRoot, whitelistPaths))
                    .ToList(),
                [],
                runState)
            : [];
        if (includeInfrequentSoftware)
        {
            ReportProgress("长期未用软件", "已完成长期未用软件整理");
        }
        var migrationCandidates = includesSystemDrive && includeOverview
            ? TryBuildScanPhase(
                "迁移候选",
                () => BuildMigrationCandidates(overviewEntries),
                [],
                runState)
            : [];
        if (includesSystemDrive && includeOverview)
        {
            ReportProgress(
                "迁移候选",
                quickOverviewOnly
                    ? "已先整理第一批 C盘建议，这个列表会随着扫描继续补齐"
                    : "已整理 C盘建议，可迁移和可确认目录已就绪");
        }
        var installedAppMappings = cleanupItems
            .Where(item => item.IsApplicationRelated && !string.IsNullOrWhiteSpace(item.AppIdentityKey))
            .GroupBy(item => item.AppIdentityKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Path, StringComparer.OrdinalIgnoreCase);
        var duplicateGroups = cleanupItems
            .Where(item => item.CandidateKind == CleanupCandidateKind.DuplicateFile && !string.IsNullOrWhiteSpace(item.DuplicateGroupKey))
            .GroupBy(item => item.DuplicateGroupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.Path).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var whitelistHints = cleanupItems
            .Where(item => !string.IsNullOrWhiteSpace(item.WhitelistHintText))
            .ToDictionary(item => item.NormalizedPath, item => item.WhitelistHintText, StringComparer.OrdinalIgnoreCase);

        var safeDefaultSelectionIds = BuildSafeDefaultSelectionIds(cleanupItems);
        var additionalReviewSelectionIds = BuildAdditionalReviewSelectionIds(cleanupItems, safeDefaultSelectionIds);
        var pendingDrives = scope == ScanDriveScope.SystemDriveOnly
            ? allFixedDrives
                .Select(drive => GetDriveName(drive.Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Except(selectedDriveNames, StringComparer.OrdinalIgnoreCase)
                .OrderBy(GetDrivePriority)
                .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        var isPartialResult = scope == ScanDriveScope.SystemDriveOnly && pendingDrives.Count > 0;
        ReportProgress(
            "整理结果",
            scope == ScanDriveScope.SystemDriveOnly && pendingDrives.Count > 0
                ? $"已先整理出 {cleanupItems.Count} 个候选，正在等待其它盘补全"
                : $"已整理出 {cleanupItems.Count} 个候选项目");

        return new ScanSnapshot
        {
            CleanupItems = cleanupItems,
            CDriveOverviewEntries = overviewEntries,
            InfrequentSoftwareEntries = infrequentEntries,
            MigrationCandidates = migrationCandidates,
            LoadedDrives = selectedDriveNames,
            PendingDrives = pendingDrives,
            IsPartialResult = isPartialResult,
            PhaseLabel = BuildPhaseLabel(scope, selectedDriveNames, pendingDrives, profile),
            Warnings = runState.Warnings.ToList(),
            SafeDefaultSelectionIds = safeDefaultSelectionIds,
            AdditionalReviewSelectionIds = additionalReviewSelectionIds,
            InstalledAppMappings = installedAppMappings,
            DuplicateGroups = duplicateGroups,
            WhitelistHints = whitelistHints
        };
    }

    private T TryBuildScanPhase<T>(string phaseName, Func<T> builder, T fallback, ScanRunState runState)
    {
        try
        {
            return builder();
        }
        catch (Exception ex)
        {
            RecordScanWarning(runState, phaseName, ex, severity: ScanWarningSeverity.Warning, message: $"扫描阶段“{phaseName}”已降级，主列表仍可继续显示。");
            return fallback;
        }
    }

    private void TryRunScanPhase(string phaseName, Action action, ScanRunState runState)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            RecordScanWarning(runState, phaseName, ex, severity: ScanWarningSeverity.Warning, message: $"扫描阶段“{phaseName}”已降级，主列表仍可继续显示。");
        }
    }

    private ScanRunState CreateScanRunState()
    {
        var logPath = string.Empty;
        if (_context is not null)
        {
            try
            {
                PortableContext.EnsureDirectory(_context.LogsRoot);
                logPath = Path.Combine(_context.LogsRoot, $"scan-warning-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            }
            catch
            {
                logPath = string.Empty;
            }
        }

        return new ScanRunState(logPath);
    }

    private void RecordScanWarning(ScanRunState runState, string phaseName, Exception exception, string? affectedPath = null, ScanWarningSeverity severity = ScanWarningSeverity.Warning, string? message = null)
    {
        var normalizedPath = NormalizePath(affectedPath ?? ExtractAffectedPath(exception.Message));
        var warningMessage = string.IsNullOrWhiteSpace(message)
            ? $"扫描阶段“{phaseName}”已降级：{exception.Message}"
            : message;
        var warning = new ScanWarning
        {
            TimestampUtc = DateTime.UtcNow,
            Phase = phaseName,
            Message = warningMessage,
            AffectedPath = normalizedPath,
            LogPath = runState.LogPath,
            Severity = severity
        };

        var warningKey = $"{warning.Phase}|{warning.AffectedPath}|{warning.Message}|{warning.Severity}";
        if (runState.WarningKeys.Add(warningKey))
        {
            runState.Warnings.Add(warning);
        }

        if (!string.IsNullOrWhiteSpace(runState.LogPath))
        {
            try
            {
                var logEntry = new
                {
                    warning.TimestampUtc,
                    warning.Phase,
                    warning.Message,
                    warning.AffectedPath,
                    warning.LogPath,
                    warning.Severity,
                    ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
                    ExceptionMessage = exception.Message,
                    ExceptionStackTrace = exception.StackTrace ?? string.Empty
                };
                File.AppendAllText(runState.LogPath, JsonSerializer.Serialize(logEntry) + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    private Action<string, Exception> CreateEnumerationWarningSink(ScanRunState runState, string phaseName)
    {
        return (path, exception) =>
        {
            var severity = ClassifyEnumerationWarningSeverity(path, exception);
            RecordScanWarning(
                runState,
                phaseName,
                exception,
                path,
                severity,
                $"扫描阶段“{phaseName}”在受限目录处已跳过，主列表仍可继续显示。");
        };
    }

    private static ScanWarningSeverity ClassifyEnumerationWarningSeverity(string? path, Exception exception)
    {
        var normalizedPath = NormalizePath(path ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return ScanWarningSeverity.Warning;
        }

        if (IsKnownShellLibraryProxyPath(normalizedPath) || IsVolatileTransientPath(normalizedPath))
        {
            return ScanWarningSeverity.Info;
        }

        return exception switch
        {
            DirectoryNotFoundException => ScanWarningSeverity.Info,
            FileNotFoundException => ScanWarningSeverity.Info,
            _ => ScanWarningSeverity.Warning
        };
    }

    private static string ExtractAffectedPath(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var firstQuote = message.IndexOf('\'');
        if (firstQuote < 0)
        {
            return string.Empty;
        }

        var secondQuote = message.IndexOf('\'', firstQuote + 1);
        if (secondQuote <= firstQuote + 1)
        {
            return string.Empty;
        }

        return message[(firstQuote + 1)..secondQuote];
    }

    private void AddItem(IDictionary<string, CleanupItem> items, CleanupItem? item)
    {
        if (item is null || item.SizeBytes <= 0)
        {
            return;
        }

        var key = $"{item.TargetKind}|{item.NormalizedPath}";
        if (!items.TryGetValue(key, out var existing))
        {
            items[key] = item;
            return;
        }

        if (ShouldReplace(existing, item))
        {
            items[key] = item;
        }
    }

    private static bool ShouldReplace(CleanupItem existing, CleanupItem incoming)
    {
        if (incoming.ImpactSeverity != existing.ImpactSeverity)
        {
            return incoming.ImpactSeverity > existing.ImpactSeverity;
        }

        if (incoming.SafeAuto != existing.SafeAuto)
        {
            return !incoming.SafeAuto;
        }

        if (incoming.IsApplicationRelated != existing.IsApplicationRelated)
        {
            return incoming.IsApplicationRelated;
        }

        return incoming.SizeBytes > existing.SizeBytes;
    }

    private static IReadOnlySet<Guid> BuildSafeDefaultSelectionIds(IEnumerable<CleanupItem> items)
    {
        return items
            .Where(item => item.ImpactSeverity == CleanupImpactSeverity.Low)
            .Where(item => item.CandidateKind is CleanupCandidateKind.SafeJunk or CleanupCandidateKind.DuplicateFile or CleanupCandidateKind.AppCache)
            .Select(item => item.Id)
            .ToHashSet();
    }

    private static IReadOnlySet<Guid> BuildAdditionalReviewSelectionIds(IEnumerable<CleanupItem> items, IReadOnlySet<Guid> safeDefaultSelectionIds)
    {
        return items
            .Where(item => !safeDefaultSelectionIds.Contains(item.Id))
            .Where(item => item.Recommended || item.ImpactSeverity != CleanupImpactSeverity.Low)
            .Select(item => item.Id)
            .ToHashSet();
    }

    private void AddPerDriveJunkItems(IDictionary<string, CleanupItem> items, IReadOnlyCollection<DriveInfo> fixedDrives)
    {
        foreach (var drive in fixedDrives)
        {
            var driveName = GetDriveName(drive.Name);
            var root = drive.RootDirectory.FullName;

            foreach (var folderName in DriveRootJunkFolders)
            {
                var isLogFolder = folderName.StartsWith("log", StringComparison.OrdinalIgnoreCase);
                AddItem(
                    items,
                    CreateDirectoryItem(
                        "明确垃圾",
                        isLogFolder ? $"{driveName} 盘根目录日志" : $"{driveName} 盘根目录临时目录",
                        isLogFolder ? "各盘根目录日志目录" : "各盘根目录临时目录",
                        Path.Combine(root, folderName),
                        CleanupTargetKind.DirectoryContents,
                        isLogFolder
                            ? $"这是 {driveName} 盘根目录下的日志目录，通常是程序或脚本跑完后留下的历史日志。"
                            : $"这是 {driveName} 盘根目录下的临时目录，通常是安装器、压缩包或脚本留下的中转文件。",
                        isLogFolder
                            ? "会清掉旧日志记录，不影响程序继续运行；如果你正准备排查问题，建议先保留。"
                            : "会释放这类中转文件占用，之后程序需要时会重新生成。",
                        CleanupImpactSeverity.Low,
                        safeAuto: true,
                        recommended: true,
                        driveName: driveName,
                        ruleSource: $"DriveRoot:{folderName}",
                        candidateKind: CleanupCandidateKind.SafeJunk));
            }
        }

        var recycleBytes = RecycleBinHelper.GetRecycleBinSizeBytes();
        if (recycleBytes > 0)
        {
            AddItem(
                items,
                new CleanupItem
                {
                    DriveName = "全部盘",
                    Category = "明确垃圾",
                    Name = "全部固定盘回收站",
                    TypeDescription = "已删除但仍占空间的文件",
                    Path = "全部固定盘回收站",
                    NormalizedPath = "virtual://fixed/recycle-bin",
                    RuleSource = "Aggregate:RecycleBin",
                    CandidateKind = CleanupCandidateKind.Aggregate,
                    TargetKind = CleanupTargetKind.RecycleBin,
                    Note = "会一起清空 C、D、E 等固定磁盘中的回收站内容，清空后不能再从回收站恢复。",
                    ImpactText = "回收站里的文件会被彻底清空，之后不能再从系统回收站直接恢复。",
                    ImpactSeverity = CleanupImpactSeverity.High,
                    SizeBytes = recycleBytes,
                    SafeAuto = true,
                    Recommended = true
                });
        }
    }

    private void AddSystemJunkItems(IDictionary<string, CleanupItem> items)
    {
        var currentUser = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var systemDrive = GetDriveName(systemRoot);

        var systemItems = new CleanupItem?[]
        {
            CreateDirectoryItem("明确垃圾", "用户临时文件", "程序运行时生成的临时文件", Path.GetTempPath(), CleanupTargetKind.DirectoryContents, "这些文件通常可以直接删除，程序需要时会重新生成。", "会释放临时文件占用，程序在下次运行时会重新生成需要的临时内容。", CleanupImpactSeverity.Low, true, true, ruleSource: "Known:SystemTemp", candidateKind: CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "Windows 临时目录", "系统临时缓存", Path.Combine(systemRoot, "Windows", "Temp"), CleanupTargetKind.DirectoryContents, "系统和安装程序留下的临时文件。", "会清掉系统和安装器的临时缓存，不影响系统正常使用。", CleanupImpactSeverity.Low, true, true, systemDrive, "Known:WindowsTemp", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "Windows 更新下载缓存", "系统更新安装缓存", Path.Combine(systemRoot, "Windows", "SoftwareDistribution", "Download"), CleanupTargetKind.DirectoryContents, "Windows 更新包已下载后留下的缓存。", "会删除已下载的更新缓存；以后需要时，Windows 可能重新下载对应内容。", CleanupImpactSeverity.Low, true, true, systemDrive, "Known:SoftwareDistribution", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "Windows 升级日志", "系统安装和升级日志", Path.Combine(systemRoot, "Windows", "Panther"), CleanupTargetKind.DirectoryContents, "只在排查系统升级问题时有用。", "会丢失历史升级日志，不影响系统运行。", CleanupImpactSeverity.Low, true, true, systemDrive, "Known:Panther", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "Windows 日志目录", "系统运行日志", Path.Combine(systemRoot, "Windows", "Logs"), CleanupTargetKind.DirectoryContents, "系统日志通常会持续重新生成。", "会清掉旧系统日志，之后系统仍会继续生成新日志。", CleanupImpactSeverity.Low, true, true, systemDrive, "Known:WindowsLogs", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "系统性能日志", "PerfLogs 性能日志", Path.Combine(systemRoot, "PerfLogs"), CleanupTargetKind.DirectoryContents, "Windows 性能日志属于历史记录，清完后系统会按需重建。", "会删除旧的性能日志历史，不影响 Windows 正常使用。", CleanupImpactSeverity.Low, true, true, systemDrive, "Known:PerfLogs", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "显卡驱动下载缓存", "NVIDIA 驱动安装缓存", Path.Combine(systemRoot, "ProgramData", "NVIDIA Corporation", "Downloader"), CleanupTargetKind.DirectoryContents, "驱动安装后遗留的下载包。", "会清掉已经下载过的驱动安装缓存，后续需要时可重新下载。", CleanupImpactSeverity.Low, true, true, systemDrive, "Known:NvidiaDownloader", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "系统重置残留", "系统重置/升级残留", Path.Combine(systemRoot, "$SysReset"), CleanupTargetKind.DirectoryTree, "系统重置或升级结束后留下的残留目录。", "会删除系统重置或升级后的残留文件，通常不影响继续使用，但会失去这部分历史残留。", CleanupImpactSeverity.Medium, true, true, systemDrive, "Known:SysReset", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "旧系统版本备份", "Windows.old 升级备份", Path.Combine(systemRoot, "Windows.old"), CleanupTargetKind.DirectoryTree, "这是系统升级后的旧版本备份。若近期不打算回退系统，可手动确认后清理。", "会删除旧系统回退备份；清理后将不能再依赖这份备份回退到升级前版本。", CleanupImpactSeverity.High, false, true, systemDrive, "Known:WindowsOld", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "崩溃转储", "程序崩溃调试文件", Path.Combine(currentUser, "AppData", "Local", "CrashDumps"), CleanupTargetKind.DirectoryContents, "只有在分析崩溃时才有价值。", "会丢失程序崩溃时留下的排障文件，不影响软件继续使用。", CleanupImpactSeverity.Medium, true, true, ruleSource: "Known:CrashDumps", candidateKind: CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "显卡 DX 缓存", "图形渲染缓存", Path.Combine(currentUser, "AppData", "Local", "NVIDIA", "DXCache"), CleanupTargetKind.DirectoryContents, "显卡运行游戏和程序时产生的缓存。", "会让图形缓存重新生成，首次打开部分程序时可能稍慢一点。", CleanupImpactSeverity.Low, true, true, ruleSource: "Known:DXCache", candidateKind: CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "显卡 OpenGL 缓存", "图形着色器缓存", Path.Combine(currentUser, "AppData", "Local", "NVIDIA", "GLCache"), CleanupTargetKind.DirectoryContents, "可重新生成，不影响驱动和程序。", "会删除旧着色器缓存，之后程序会自动重建。", CleanupImpactSeverity.Low, true, true, ruleSource: "Known:GLCache", candidateKind: CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "显卡计算缓存", "GPU 计算缓存", Path.Combine(currentUser, "AppData", "Local", "NVIDIA", "ComputeCache"), CleanupTargetKind.DirectoryContents, "GPU 计算任务使用过的缓存。", "会清空 GPU 计算缓存，后续相关任务会重新生成缓存。", CleanupImpactSeverity.Low, true, true, ruleSource: "Known:ComputeCache", candidateKind: CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "投递优化缓存", "Windows 投递优化缓存", Path.Combine(systemRoot, "ProgramData", "Microsoft", "Windows", "DeliveryOptimization", "Cache"), CleanupTargetKind.DirectoryContents, "系统下载共享缓存，删除不影响正常使用。", "会删除 Windows 下载共享缓存，之后系统需要时会重新建立缓存。", CleanupImpactSeverity.Low, true, true, systemDrive, "Known:DeliveryOptimization", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "Windows 错误报告队列", "WER 错误报告缓存", Path.Combine(systemRoot, "ProgramData", "Microsoft", "Windows", "WER", "ReportQueue"), CleanupTargetKind.DirectoryContents, "程序和系统错误报告打包完成后留下的待上传缓存。", "会删除待上传的错误报告缓存，之后将失去这批待排障记录。", CleanupImpactSeverity.Medium, true, true, systemDrive, "Known:WerQueue", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "Windows 错误报告归档", "WER 历史错误报告", Path.Combine(systemRoot, "ProgramData", "Microsoft", "Windows", "WER", "ReportArchive"), CleanupTargetKind.DirectoryContents, "历史错误报告的归档副本，通常只在排障时查看。", "会清掉历史错误报告归档，不影响系统运行，但会丢失排障历史。", CleanupImpactSeverity.Low, true, true, systemDrive, "Known:WerArchive", CleanupCandidateKind.SafeJunk),
            CreateDirectoryItem("明确垃圾", "系统小转储", "蓝屏或崩溃小转储", Path.Combine(systemRoot, "Windows", "Minidump"), CleanupTargetKind.DirectoryContents, "排障完成后通常可清理。", "会丢失蓝屏和崩溃的小转储记录，不影响电脑继续使用。", CleanupImpactSeverity.Medium, true, true, systemDrive, "Known:Minidump", CleanupCandidateKind.SafeJunk),
            CreateFileItem("明确垃圾", "系统内存转储", "蓝屏内存转储文件", Path.Combine(systemRoot, "Windows", "MEMORY.DMP"), CleanupTargetKind.FilePermanent, "这是蓝屏或严重崩溃后的完整内存转储。若近期不需要排障，可手动确认后删除。", "会永久删除完整蓝屏内存转储，之后将无法再用它分析严重故障。", CleanupImpactSeverity.High, false, true, systemDrive, "Known:MemoryDmp", CleanupCandidateKind.SafeJunk)
        };

        foreach (var item in systemItems)
        {
            AddItem(items, item);
        }
    }

    private void AddReviewCacheItems(IDictionary<string, CleanupItem> items)
    {
        var currentUser = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var reviewItems = new CleanupItem?[]
        {
            CreateDirectoryItem("应用缓存", "网易云音乐缓存", "网易云离线或试听缓存", Path.Combine(currentUser, "AppData", "Local", "NetEase", "CloudMusic", "Cache"), CleanupTargetKind.DirectoryContents, "删除后不会影响账号和歌单，但缓存的音乐要重新下载。", "会删除已经缓存的音乐内容，之后相关音频需要重新加载或重新下载。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CloudMusicCache", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "网易云资源缓存", "网易云图片和页面缓存", Path.Combine(currentUser, "AppData", "Local", "NetEase", "CloudMusic", "cacheresource"), CleanupTargetKind.DirectoryContents, "属于应用资源缓存，可重新生成。", "会清掉应用资源缓存，界面资源下次使用时会重新下载。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CloudMusicResource", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "网易云更新包", "网易云升级下载包", Path.Combine(currentUser, "AppData", "Local", "NetEase", "CloudMusic", "update"), CleanupTargetKind.DirectoryContents, "软件升级完成后留下的安装缓存。", "会删除更新下载包，需要再次安装或回看时可能要重新下载。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CloudMusicUpdate", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "腾讯视频播放缓存", "腾讯视频播放和下载缓存", Path.Combine(currentUser, "AppData", "Roaming", "Tencent", "QQLive", "CacheFile"), CleanupTargetKind.DirectoryContents, "删除后不会影响账号，但缓存视频需要重新加载。", "会删除视频播放缓存，之后视频内容需要重新加载。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:QQLiveCache", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "腾讯视频图片缓存", "腾讯视频封面缓存", Path.Combine(currentUser, "AppData", "Roaming", "Tencent", "QQLive", "Image"), CleanupTargetKind.DirectoryContents, "只影响封面和缩略图重载。", "会清掉封面和缩略图缓存，界面图片会在下次打开时重新加载。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:QQLiveImage", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "腾讯视频网页缓存", "内置浏览器缓存", Path.Combine(currentUser, "AppData", "Roaming", "Tencent", "QQLive", "webkit_cache"), CleanupTargetKind.DirectoryContents, "只影响界面资源重新加载。", "会清掉内置网页缓存，之后内嵌页面会重新缓存。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:QQLiveWebCache", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "腾讯视频日志", "腾讯视频运行日志", Path.Combine(currentUser, "AppData", "Roaming", "Tencent", "QQLive", "Log"), CleanupTargetKind.DirectoryContents, "排查问题时才有价值。", "会删除腾讯视频历史日志，不影响继续使用，但会丢失排障记录。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:QQLiveLog", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("聊天缓存", "微信旧版更新包", "微信升级缓存", Path.Combine(currentUser, "AppData", "Roaming", "Tencent", "xwechat", "update"), CleanupTargetKind.DirectoryContents, "更新完成后留下的安装缓存，不包含聊天数据库。", "会删除微信升级缓存，之后若要再次使用这份安装包可能需要重新下载。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:XwechatUpdate", candidateKind: CleanupCandidateKind.ChatCache),
            CreateDirectoryItem("聊天缓存", "微信旧版日志", "微信运行日志", Path.Combine(currentUser, "AppData", "Roaming", "Tencent", "xwechat", "log"), CleanupTargetKind.DirectoryContents, "排查问题时才有参考价值，不包含聊天数据库。", "会清掉微信历史日志，不影响聊天数据，但会失去排障记录。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:XwechatLog", candidateKind: CleanupCandidateKind.ChatCache),
            CreateDirectoryItem("聊天缓存", "微信旧版插件缓存", "微信插件和扩展缓存", Path.Combine(currentUser, "AppData", "Roaming", "Tencent", "xwechat", "xplugin"), CleanupTargetKind.DirectoryContents, "删除后可能会在下次打开时重新下载插件，不包含聊天数据库。", "会删除插件缓存，下次用到相关功能时可能重新下载或重新初始化。", CleanupImpactSeverity.Medium, false, false, ruleSource: "Known:XwechatPlugin", candidateKind: CleanupCandidateKind.ChatCache),
            CreateDirectoryItem("聊天缓存", "微信旧版组件缓存", "微信组件缓存", Path.Combine(currentUser, "AppData", "Roaming", "Tencent", "xwechat", "radium"), CleanupTargetKind.DirectoryContents, "通常可再生，但默认不自动勾选；不包含聊天数据库。", "会清掉组件缓存，微信下次启动时可能重新生成相关组件。", CleanupImpactSeverity.Medium, false, false, ruleSource: "Known:XwechatComponent", candidateKind: CleanupCandidateKind.ChatCache),
            CreateDirectoryItem("应用缓存", "VS Code 缓存", "VS Code 资源缓存", Path.Combine(currentUser, "AppData", "Roaming", "Code", "Cache"), CleanupTargetKind.DirectoryContents, "删除后只会让 VS Code 重新生成缓存，不影响扩展和设置。", "会清掉编辑器资源缓存，VS Code 下次启动时会重新生成。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CodeCache", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "VS Code 已编译缓存", "VS Code CachedData", Path.Combine(currentUser, "AppData", "Roaming", "Code", "CachedData"), CleanupTargetKind.DirectoryContents, "属于编辑器加速缓存，删除后会重新构建。", "会让 VS Code 的加速缓存失效，首次再次打开时可能稍慢。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CodeCachedData", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "VS Code Service Worker 缓存", "VS Code 网页缓存", Path.Combine(currentUser, "AppData", "Roaming", "Code", "Service Worker", "CacheStorage"), CleanupTargetKind.DirectoryContents, "只影响内嵌网页和扩展面板缓存。", "会删除内嵌网页缓存，扩展面板内容将重新加载。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CodeServiceWorker", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "Cursor 缓存", "Cursor 资源缓存", Path.Combine(currentUser, "AppData", "Roaming", "Cursor", "Cache"), CleanupTargetKind.DirectoryContents, "删除后只会让 Cursor 重新生成缓存。", "会清掉 Cursor 资源缓存，之后应用会重新生成。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CursorCache", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "Cursor 已编译缓存", "Cursor CachedData", Path.Combine(currentUser, "AppData", "Roaming", "Cursor", "CachedData"), CleanupTargetKind.DirectoryContents, "属于编辑器加速缓存，删除后会重新构建。", "会让 Cursor 的加速缓存失效，首次再次打开时可能稍慢。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CursorCachedData", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("应用缓存", "Cursor Service Worker 缓存", "Cursor 网页缓存", Path.Combine(currentUser, "AppData", "Roaming", "Cursor", "Service Worker", "CacheStorage"), CleanupTargetKind.DirectoryContents, "只影响应用内网页缓存。", "会删除应用内网页缓存，相关页面会重新加载。", CleanupImpactSeverity.Medium, false, true, ruleSource: "Known:CursorServiceWorker", candidateKind: CleanupCandidateKind.AppCache),
            CreateDirectoryItem("浏览器缓存", "Edge 浏览器缓存", "网页图片、脚本、视频缓存", Path.Combine(currentUser, "AppData", "Local", "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data"), CleanupTargetKind.DirectoryContents, "不会删除书签和密码，只会让页面重新缓存。", "会清掉网页缓存，页面图片和脚本需要重新缓存，但不会影响书签和密码。", CleanupImpactSeverity.Low, false, true, ruleSource: "Known:EdgeCache", candidateKind: CleanupCandidateKind.BrowserCache),
            CreateDirectoryItem("浏览器缓存", "Chrome 浏览器缓存", "网页图片、脚本、视频缓存", Path.Combine(currentUser, "AppData", "Local", "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data"), CleanupTargetKind.DirectoryContents, "不会删除账号和收藏，只影响缓存重建。", "会清掉网页缓存，页面资源会重新缓存，但不会影响账号和收藏。", CleanupImpactSeverity.Low, false, true, ruleSource: "Known:ChromeCache", candidateKind: CleanupCandidateKind.BrowserCache)
        };

        foreach (var item in reviewItems)
        {
            AddItem(items, item);
        }

        AddBrowserProfileCacheItems(items, currentUser);
        AddWindowsPrivacyTraceItems(items, currentUser);
        AddExplorerVisualCacheItems(items, currentUser);
    }

    private void AddBrowserProfileCacheItems(IDictionary<string, CleanupItem> items, string currentUser)
    {
        var browserRoots = new[]
        {
            ("Edge", Path.Combine(currentUser, "AppData", "Local", "Microsoft", "Edge", "User Data")),
            ("Chrome", Path.Combine(currentUser, "AppData", "Local", "Google", "Chrome", "User Data"))
        };

        foreach (var (browserName, userDataRoot) in browserRoots)
        {
            foreach (var profilePath in EnumerateBrowserProfiles(userDataRoot))
            {
                var profileName = Path.GetFileName(profilePath);
                AddItem(items, CreateDirectoryItem("浏览器缓存", $"{browserName} {profileName} GPU 缓存", "浏览器 GPU/合成缓存", Path.Combine(profilePath, "GPUCache"), CleanupTargetKind.DirectoryContents, "只清理浏览器可重建的图形缓存，不删除书签、密码、历史记录。", "会让浏览器重新生成 GPU 缓存；首次打开某些网页可能稍慢。", CleanupImpactSeverity.Low, false, true, ruleSource: $"Known:{browserName}GpuCache", candidateKind: CleanupCandidateKind.BrowserCache));
                AddItem(items, CreateDirectoryItem("浏览器缓存", $"{browserName} {profileName} 代码缓存", "网页脚本编译缓存", Path.Combine(profilePath, "Code Cache"), CleanupTargetKind.DirectoryContents, "只清理可重建的脚本编译缓存，不删除账号和收藏。", "网页脚本缓存会重建，少数页面首次加载可能稍慢。", CleanupImpactSeverity.Low, false, true, ruleSource: $"Known:{browserName}CodeCache", candidateKind: CleanupCandidateKind.BrowserCache));
                AddItem(items, CreateDirectoryItem("浏览器缓存", $"{browserName} {profileName} Service Worker 缓存", "网页离线资源缓存", Path.Combine(profilePath, "Service Worker", "CacheStorage"), CleanupTargetKind.DirectoryContents, "清理网页离线资源缓存，不删除浏览器账号、收藏或密码。", "部分网页离线内容和缓存资源会重新下载。", CleanupImpactSeverity.Medium, false, true, ruleSource: $"Known:{browserName}ServiceWorkerCache", candidateKind: CleanupCandidateKind.BrowserCache));
            }
        }
    }

    private static IEnumerable<string> EnumerateBrowserProfiles(string userDataRoot)
    {
        if (string.IsNullOrWhiteSpace(userDataRoot) || !Directory.Exists(userDataRoot))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(userDataRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.Equals("Default", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase);
                })
                .Take(16)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void AddWindowsPrivacyTraceItems(IDictionary<string, CleanupItem> items, string currentUser)
    {
        var recentRoot = Path.Combine(currentUser, "AppData", "Roaming", "Microsoft", "Windows", "Recent");
        AddItem(items, CreateDirectoryItem("隐私痕迹", "最近打开记录", "Windows 最近文档快捷记录", recentRoot, CleanupTargetKind.DirectoryContents, "这只清理最近打开记录，不删除原始文档。", "会清空资源管理器和部分应用显示的最近项目记录；原文件不会被删除。", CleanupImpactSeverity.Medium, false, false, ruleSource: "Known:RecentItems", candidateKind: CleanupCandidateKind.PrivacyTrace));
        AddItem(items, CreateDirectoryItem("隐私痕迹", "跳转列表记录", "Windows 任务栏跳转列表", Path.Combine(recentRoot, "AutomaticDestinations"), CleanupTargetKind.DirectoryContents, "这只清理任务栏/开始菜单的跳转列表记录，不删除原始文件。", "会重置部分应用右键菜单中的最近文件列表。", CleanupImpactSeverity.Medium, false, false, ruleSource: "Known:AutomaticDestinations", candidateKind: CleanupCandidateKind.PrivacyTrace));
        AddItem(items, CreateDirectoryItem("隐私痕迹", "自定义跳转列表记录", "Windows 自定义跳转列表", Path.Combine(recentRoot, "CustomDestinations"), CleanupTargetKind.DirectoryContents, "这只清理应用自定义跳转列表记录，不删除原始文件。", "会重置部分应用的固定/最近跳转列表，建议确认后再处理。", CleanupImpactSeverity.Medium, false, false, ruleSource: "Known:CustomDestinations", candidateKind: CleanupCandidateKind.PrivacyTrace));
    }

    private void AddExplorerVisualCacheItems(IDictionary<string, CleanupItem> items, string currentUser)
    {
        var explorerRoot = Path.Combine(currentUser, "AppData", "Local", "Microsoft", "Windows", "Explorer");
        if (!Directory.Exists(explorerRoot))
        {
            return;
        }

        foreach (var file in EnumerateVisualCacheFiles(explorerRoot))
        {
            var fileName = Path.GetFileName(file);
            var isIconCache = fileName.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase);
            AddItem(items, CreateFileItem(
                "系统缓存",
                isIconCache ? $"资源管理器图标缓存 {fileName}" : $"资源管理器缩略图缓存 {fileName}",
                isIconCache ? "Windows 图标缓存文件" : "Windows 缩略图缓存文件",
                file,
                CleanupTargetKind.FilePermanent,
                "这是 Windows 可重建的视觉缓存；文件可能被系统占用，清理失败时会自动跳过。",
                isIconCache ? "会让 Windows 重新生成图标缓存，可用于修复旧图标残留，但短时间内资源管理器可能重新加载图标。" : "会让 Windows 重新生成缩略图缓存，首次打开图片/视频目录时可能稍慢。",
                CleanupImpactSeverity.Low,
                false,
                true,
                ruleSource: isIconCache ? "Known:ExplorerIconCache" : "Known:ExplorerThumbCache",
                candidateKind: CleanupCandidateKind.SafeJunk));
        }
    }

    private static IEnumerable<string> EnumerateVisualCacheFiles(string explorerRoot)
    {
        try
        {
            return Directory.EnumerateFiles(explorerRoot, "*cache_*.db", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase);
                })
                .Take(64)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void AddRootHeuristicCandidates(IDictionary<string, CleanupItem> items, IReadOnlyCollection<DriveInfo> fixedDrives, InstalledAppSnapshot installedApps, ScanRunState runState)
    {
        foreach (var drive in fixedDrives)
        {
            foreach (var directory in FileSystemHelper.EnumerateDirectoriesSafe(
                         drive.RootDirectory.FullName,
                         onError: CreateEnumerationWarningSink(runState, "根目录启发式候选"))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (IsProtectedPath(directory))
                {
                    continue;
                }

                var name = Path.GetFileName(directory);
                var normalizedName = name.ToLowerInvariant();
                var sizeBytes = FileSystemHelper.GetPathSizeBytes(directory);
                if (sizeBytes <= 0)
                {
                    continue;
                }

                if (HasKeyword(normalizedName, "cache", "log", "logs", "temp", "tmp") && sizeBytes >= 16L * 1024 * 1024)
                {
                    AddItem(
                        items,
                        CreateDirectoryItem(
                            "明确垃圾",
                            $"{name}（{GetDriveName(directory)} 盘）",
                            "各盘根目录缓存/日志目录",
                            directory,
                            CleanupTargetKind.DirectoryContents,
                            "这是各盘根目录下明显的缓存、临时文件或日志目录。",
                            "会清掉这类缓存或日志内容；程序之后需要时通常会重新生成。",
                            CleanupImpactSeverity.Low,
                            safeAuto: false,
                            recommended: true,
                            driveName: GetDriveName(directory),
                            ruleSource: $"Heuristic:RootJunk:{name}",
                            candidateKind: CleanupCandidateKind.SafeJunk));
                    continue;
                }

                if (HasKeyword(normalizedName, "download", "downloads", "package", "packages", "installer", "installers", "setup", "update", "backup", "old")
                    && sizeBytes >= 64L * 1024 * 1024
                    && !DirectoryLooksLikeApplicationRoot(directory))
                {
                    AddItem(
                        items,
                        CreateDirectoryItem(
                            "大目录",
                            $"{name}（{GetDriveName(directory)} 盘）",
                            "根目录下的大型下载/备份目录",
                            directory,
                            CleanupTargetKind.DirectoryTree,
                            "这是盘根目录下体积较大的下载包、备份或更新残留目录。",
                            "会连同目录中的内容一起删除；如果里面还有你需要的安装包或备份文件，就不该删。",
                            CleanupImpactSeverity.Medium,
                            safeAuto: false,
                            recommended: false,
                            driveName: GetDriveName(directory),
                            ruleSource: $"Heuristic:LargeRootDirectory:{name}",
                            candidateKind: CleanupCandidateKind.LargeDirectory));
                    continue;
                }

                if (sizeBytes >= LargeDirectoryThresholdBytes
                    && IsLikelyOrphanApplicationDirectory(directory, installedApps))
                {
                    AddItem(
                        items,
                        CreateDirectoryItem(
                            "应用残留",
                            $"{name}（{GetDriveName(directory)} 盘）",
                            "未注册的大型旧应用目录",
                            directory,
                            CleanupTargetKind.DirectoryTree,
                            "这是一个看起来像应用目录、但没有发现对应安装注册信息的旧目录。",
                            "会整目录删除；如果你还在直接运行里面的程序，这样做会让那个程序失效。",
                            CleanupImpactSeverity.High,
                            safeAuto: false,
                            recommended: false,
                            driveName: GetDriveName(directory),
                            ruleSource: $"Heuristic:OrphanApp:{name}",
                            candidateKind: CleanupCandidateKind.AppResidue,
                            isApplicationRelated: true));
                }
            }
        }
    }

    private void AddBroadFileCandidates(
        IDictionary<string, CleanupItem> items,
        IReadOnlyCollection<DriveInfo> fixedDrives,
        InstalledAppSnapshot installedApps,
        IReadOnlyCollection<string> selectedDriveNames,
        ScanRunState runState)
    {
        var budget = new BroadScanBudget();
        foreach (var target in GetBroadScanTargets(fixedDrives, selectedDriveNames, runState))
        {
            AddDirectoryCandidateIfNeeded(items, target.Path, installedApps);

            foreach (var filePath in EnumerateFilesWithDepth(target.Path, target.MaxDepth, budget, includeAllExtensions: true, runState, "宽范围文件候选"))
            {
                if (budget.ScannedFiles >= BroadScanMaxFiles)
                {
                    return;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(filePath);
                }
                catch
                {
                    continue;
                }

                budget.ScannedFiles++;
                if (ShouldSkipFile(info))
                {
                    continue;
                }

                if (IsProtectedPath(info.FullName) || IsProtectedSystemTreePath(info.FullName))
                {
                    continue;
                }

                if (PackageExtensions.Contains(info.Extension, StringComparer.OrdinalIgnoreCase) && info.Length >= PackageMinFileSizeBytes)
                {
                    AddItem(
                        items,
                        CreateFileItem(
                            "下载包/压缩包",
                            info.Name,
                            GetInstallerTypeDescription(info.Extension),
                            info.FullName,
                            CleanupTargetKind.FileRecycle,
                            "这是较大的安装包、更新包或压缩包候选，通常出现在桌面、下载或工具目录里。",
                            "会删除这份安装包或压缩包；以后如果还要离线安装，就需要重新下载或保留其它副本。",
                            CleanupImpactSeverity.Medium,
                            safeAuto: false,
                            recommended: true,
                            driveName: GetDriveName(info.FullName),
                            ruleSource: "Heuristic:PackageCandidate",
                            candidateKind: CleanupCandidateKind.Package));
                    continue;
                }

                if (info.Length >= LargeFileThresholdBytes)
                {
                    AddItem(
                        items,
                        CreateFileItem(
                            "大文件",
                            info.Name,
                            "大体积普通文件",
                            info.FullName,
                            CleanupTargetKind.FileRecycle,
                            "这是扫描中发现的体积较大的单个文件，常见于桌面、下载和工具目录。",
                            "会删除这个大文件；如果它是你仍要使用的视频、镜像或备份，删除后就需要重新获取。",
                            CleanupImpactSeverity.Medium,
                            safeAuto: false,
                            recommended: false,
                            driveName: GetDriveName(info.FullName),
                            ruleSource: "Heuristic:LargeFile",
                            candidateKind: CleanupCandidateKind.LargeFile));
                }
            }
        }
    }

    private void AddDirectoryCandidateIfNeeded(IDictionary<string, CleanupItem> items, string path, InstalledAppSnapshot installedApps)
    {
        if (IsProtectedPath(path) || IsProtectedSystemTreePath(path) || !Directory.Exists(path))
        {
            return;
        }

        if (DirectoryLooksLikeApplicationRoot(path) || !IsLikelyLargeContentDirectory(path))
        {
            if (IsLikelyOrphanApplicationDirectory(path, installedApps))
            {
                AddItem(
                    items,
                    CreateDirectoryItem(
                        "应用残留",
                        Path.GetFileName(path),
                        "未注册的大型旧应用目录",
                        path,
                        CleanupTargetKind.DirectoryTree,
                        "这是一个较旧、未发现注册安装信息的大型应用目录候选。",
                        "会连同目录内容一起删除；如果你仍然直接从这个目录运行程序，删除后它将无法再用。",
                        CleanupImpactSeverity.High,
                        safeAuto: false,
                        recommended: false,
                        driveName: GetDriveName(path),
                        ruleSource: "Heuristic:OrphanApplicationDirectory",
                        candidateKind: CleanupCandidateKind.AppResidue,
                        isApplicationRelated: true));
            }

            return;
        }

        var sizeBytes = FileSystemHelper.GetPathSizeBytes(path);
        if (sizeBytes < LargeDirectoryThresholdBytes)
        {
            return;
        }

        AddItem(
            items,
            CreateDirectoryItem(
                "大目录",
                Path.GetFileName(path),
                "用户目录中的大型目录",
                path,
                CleanupTargetKind.DirectoryTree,
                "这是一个体积较大的用户内容目录候选。",
                "会连目录一起删除；如果里面还有你需要的资料、视频或安装包，就不应该删。",
                CleanupImpactSeverity.Medium,
                safeAuto: false,
                recommended: false,
                driveName: GetDriveName(path),
                ruleSource: "Heuristic:LargeDirectory",
                candidateKind: CleanupCandidateKind.LargeDirectory));
    }

    private void AddDuplicateInstallerItems(
        IDictionary<string, CleanupItem> items,
        AppSettings settings,
        IReadOnlyCollection<DriveInfo> fixedDrives,
        IReadOnlyCollection<string> selectedDriveNames,
        ScanRunState runState)
    {
        if (!settings.ScanDownloadsForDuplicates)
        {
            return;
        }

        var candidates = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        var budget = new BroadScanBudget();

        foreach (var target in GetDuplicateScanTargets(fixedDrives, selectedDriveNames))
        {
            foreach (var filePath in EnumerateFilesWithDepth(target.Path, target.MaxDepth, budget, includeAllExtensions: false, runState, "重复安装包与压缩包候选"))
            {
                if (candidates.Count >= DuplicateMaxCandidateCount)
                {
                    break;
                }

                try
                {
                    var info = new FileInfo(filePath);
                    if (info.Length >= DuplicateMinFileSizeBytes && PackageExtensions.Contains(info.Extension, StringComparer.OrdinalIgnoreCase))
                    {
                        candidates[info.FullName] = info;
                    }
                }
                catch
                {
                }
            }

            if (candidates.Count >= DuplicateMaxCandidateCount)
            {
                break;
            }
        }

        var groups = candidates.Values.GroupBy(file => file.Length).Where(group => group.Count() > 1);
        foreach (var sizeGroup in groups)
        {
            var hashGroups = sizeGroup
                .Select(file =>
                {
                    try
                    {
                        using var stream = file.OpenRead();
                        var hash = Convert.ToHexString(SHA256.HashData(stream));
                        return new { File = file, Hash = hash };
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(entry => entry is not null)!
                .GroupBy(entry => entry!.Hash)
                .Where(group => group.Count() > 1);

            foreach (var hashGroup in hashGroups)
            {
                var sorted = hashGroup.OrderByDescending(entry => entry!.File.LastWriteTimeUtc).ToList();
                var keep = sorted.First()!.File;
                foreach (var duplicate in sorted.Skip(1))
                {
                    if (duplicate is null)
                    {
                        continue;
                    }

                    AddItem(
                        items,
                        new CleanupItem
                        {
                            DriveName = GetDriveName(duplicate.File.FullName),
                            Category = "重复文件",
                            Name = duplicate.File.Name,
                            TypeDescription = $"完全重复的{GetInstallerTypeDescription(duplicate.File.Extension)}",
                            Path = duplicate.File.FullName,
                            NormalizedPath = NormalizePath(duplicate.File.FullName),
                            RuleSource = "DuplicateHash",
                            CandidateKind = CleanupCandidateKind.DuplicateFile,
                            DuplicateGroupKey = hashGroup.Key,
                            TargetKind = CleanupTargetKind.FileRecycle,
                            Note = $"这是重复副本，和“{keep.Name}”内容完全相同。系统会优先保留较新的那一份，你也可以点“打开位置”先看看它是什么文件。",
                            ImpactText = $"只删除这份完全重复的副本，并保留较新的“{keep.Name}”作为主副本。",
                            ImpactSeverity = CleanupImpactSeverity.Low,
                            SizeBytes = duplicate.File.Length,
                            SafeAuto = true,
                            Recommended = true
                        });
                }
            }
        }
    }

    private IReadOnlyList<(string Path, int MaxDepth)> GetBroadScanTargets(
        IReadOnlyCollection<DriveInfo> fixedDrives,
        IReadOnlyCollection<string> selectedDriveNames,
        ScanRunState runState)
    {
        var targets = new List<(string Path, int MaxDepth)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTarget(string path, int maxDepth)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            var normalized = NormalizePath(path);
            if (IsProtectedPath(normalized) || IsProtectedSystemTreePath(normalized) || ShouldSilentlySkipBroadScanPath(normalized) || !seen.Add(normalized))
            {
                return;
            }

            targets.Add((normalized, maxDepth));
        }

        foreach (var path in GetScopedUserFolderRoots(selectedDriveNames))
        {
            AddTarget(path, 2);
        }

        foreach (var drive in fixedDrives)
        {
            AddTarget(drive.RootDirectory.FullName, 1);

            foreach (var directory in FileSystemHelper.EnumerateDirectoriesSafe(
                         drive.RootDirectory.FullName,
                         onError: CreateEnumerationWarningSink(runState, "宽范围文件候选")))
            {
                var name = Path.GetFileName(directory);
                if (RootCandidateKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    || BroadFolderKeywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    AddTarget(directory, 2);
                }
            }
        }

        return targets
            .OrderBy(target => GetDrivePriority(GetDriveName(target.Path)))
            .ThenBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<(string Path, int MaxDepth)> GetDuplicateScanTargets(
        IReadOnlyCollection<DriveInfo> fixedDrives,
        IReadOnlyCollection<string> selectedDriveNames)
    {
        var results = new List<(string Path, int MaxDepth)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTarget(string path, int maxDepth)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            var normalized = NormalizePath(path);
            if (IsProtectedPath(normalized) || !seen.Add(normalized))
            {
                return;
            }

            results.Add((normalized, maxDepth));
        }

        foreach (var path in GetScopedUserFolderRoots(selectedDriveNames))
        {
            var maxDepth = path.EndsWith(@"\Desktop", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            AddTarget(path, maxDepth);
        }

        foreach (var drive in fixedDrives)
        {
            AddTarget(drive.RootDirectory.FullName, 0);

            foreach (var folderName in DuplicateFolderNames)
            {
                AddTarget(Path.Combine(drive.RootDirectory.FullName, folderName), 2);
            }
        }

        return results
            .OrderBy(target => GetDrivePriority(GetDriveName(target.Path)))
            .ThenBy(target => target.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<string> EnumerateFilesWithDepth(string rootPath, int maxDepth, BroadScanBudget budget, bool includeAllExtensions, ScanRunState runState, string phaseName)
    {
        if (!Directory.Exists(rootPath) || budget.ScannedDirectories >= DuplicateMaxDirectoryCount)
        {
            yield break;
        }

        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((rootPath, 0));

        while (pending.Count > 0 && budget.ScannedDirectories < BroadScanMaxDirectories)
        {
            var (currentPath, depth) = pending.Pop();
            if (IsProtectedPath(currentPath) || IsProtectedSystemTreePath(currentPath))
            {
                continue;
            }

            budget.ScannedDirectories++;
            foreach (var file in FileSystemHelper.EnumerateFilesSafe(currentPath, onError: CreateEnumerationWarningSink(runState, phaseName)))
            {
                if (budget.ScannedFiles >= BroadScanMaxFiles)
                {
                    yield break;
                }

                if (!includeAllExtensions)
                {
                    var extension = Path.GetExtension(file);
                    if (!PackageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                yield return file;
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            var directories = FileSystemHelper.EnumerateDirectoriesSafe(currentPath, onError: CreateEnumerationWarningSink(runState, phaseName))
                .Where(path => !IsProtectedPath(path) && !IsProtectedSystemTreePath(path) && !ShouldSilentlySkipBroadScanPath(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = directories.Count - 1; i >= 0; i--)
            {
                pending.Push((directories[i], depth + 1));
            }
        }
    }

    private bool IsProtectedPath(string path)
    {
        var normalized = NormalizePath(path);
        if (_protectedExactPaths.Contains(normalized))
        {
            return true;
        }

        var name = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return RootProtectedNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsProtectedSystemTreePath(string path)
    {
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var systemRoot = NormalizePath(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
        var protectedRoots = new[]
        {
            NormalizePath(Path.Combine(systemRoot, "Windows")),
            NormalizePath(Path.Combine(systemRoot, "Program Files")),
            NormalizePath(Path.Combine(systemRoot, "Program Files (x86)")),
            NormalizePath(Path.Combine(systemRoot, "ProgramData")),
            NormalizePath(Path.Combine(systemRoot, "Recovery"))
        };

        return protectedRoots.Any(root =>
            normalized.Equals(root, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasKeyword(string value, params string[] keywords)
    {
        return keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldSkipFile(FileInfo file)
    {
        if ((file.Attributes & FileAttributes.Hidden) != 0 && file.Extension.Equals(".sys", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = file.Name;
        return name.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase)
            || name.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase)
            || name.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase)
            || name.Equals("DumpStack.log.tmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSilentlySkipBroadScanPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        return IsKnownShellLibraryProxyPath(normalizedPath) || IsVolatileTransientPath(normalizedPath);
    }

    private static bool IsKnownShellLibraryProxyPath(string path)
    {
        var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return false;
        }

        return leaf.Equals("My Music", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("My Pictures", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("My Videos", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("我的音乐", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("我的图片", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("我的视频", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVolatileTransientPath(string path)
    {
        var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf))
        {
            return false;
        }

        return leaf.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || leaf.EndsWith(".temp", StringComparison.OrdinalIgnoreCase)
            || leaf.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
            || leaf.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
            || leaf.StartsWith("~$", StringComparison.OrdinalIgnoreCase)
            || leaf.StartsWith("CR_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyLargeContentDirectory(string path)
    {
        var lastSegment = Path.GetFileName(path);
        return BroadFolderKeywords.Any(keyword => lastSegment.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            || path.Contains(@"\Downloads", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Desktop", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Documents", StringComparison.OrdinalIgnoreCase)
            || path.Contains(@"\Videos", StringComparison.OrdinalIgnoreCase);
    }

    private static bool DirectoryLooksLikeApplicationRoot(string path)
    {
        try
        {
            if (Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }
        }
        catch
        {
        }

        var name = Path.GetFileName(path);
        return name.Contains("steam", StringComparison.OrdinalIgnoreCase)
            || name.Contains("game", StringComparison.OrdinalIgnoreCase)
            || name.Contains("program", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyOrphanApplicationDirectory(string path, InstalledAppSnapshot installedApps)
    {
        if (!Directory.Exists(path) || !DirectoryLooksLikeApplicationRoot(path))
        {
            return false;
        }

        try
        {
            var lastWrite = Directory.GetLastWriteTimeUtc(path);
            if (lastWrite > DateTime.UtcNow.AddDays(-120))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        var sizeBytes = FileSystemHelper.GetPathSizeBytes(path);
        if (sizeBytes < 64L * 1024 * 1024 || sizeBytes > 8L * 1024 * 1024 * 1024)
        {
            return false;
        }

        var normalized = NormalizePath(path);
        if (installedApps.InstallLocations.Any(location => normalized.StartsWith(location, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var name = Path.GetFileName(path);
        return !installedApps.DisplayNames.Any(display => name.Contains(display, StringComparison.OrdinalIgnoreCase));
    }

    private static InstalledAppSnapshot LoadInstalledAppSnapshot()
    {
        var installLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var displayNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applications = new List<InstalledAppRecord>();

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var parent in new[] { @"Software\Microsoft\Windows\CurrentVersion\Uninstall", @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            {
                using var key = hive.OpenSubKey(parent);
                if (key is null)
                {
                    continue;
                }

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey is null)
                    {
                        continue;
                    }

                    var installLocation = subKey.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrWhiteSpace(installLocation))
                    {
                        installLocation = NormalizePath(installLocation);
                        installLocations.Add(installLocation);
                    }

                    var displayName = subKey.GetValue("DisplayName")?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(displayName) && displayName.Length >= 3)
                    {
                        displayNames.Add(displayName);
                    }

                    var publisher = subKey.GetValue("Publisher")?.ToString()?.Trim() ?? string.Empty;
                    var displayIcon = subKey.GetValue("DisplayIcon")?.ToString()?.Trim() ?? string.Empty;
                    var uninstallString = subKey.GetValue("UninstallString")?.ToString()?.Trim() ?? string.Empty;
                    var resolvedInstallLocation = ResolveInstallLocation(installLocation, displayIcon);
                    if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(resolvedInstallLocation))
                    {
                        applications.Add(new InstalledAppRecord(
                            displayName,
                            resolvedInstallLocation,
                            publisher,
                            displayIcon,
                            uninstallString,
                            false,
                            BuildAppIdentityKey(displayName, resolvedInstallLocation),
                            GetExecutableNamesForPath(resolvedInstallLocation, displayIcon)));
                    }
                }
            }
        }

        return new InstalledAppSnapshot(installLocations, displayNames, applications);
    }

    private IReadOnlyList<CDriveOverviewEntry> BuildCDriveOverviewEntries(InstalledAppSnapshot installedApps, bool quickOnly = false)
    {
        var cRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var entries = new Dictionary<string, CDriveOverviewEntry>(StringComparer.OrdinalIgnoreCase);

        if (!quickOnly)
        {
            AddOverviewDirectory(entries, cRoot, "Users", installedApps, quickSize: false);
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(cRoot))
            {
                var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (quickOnly && directoryName.Equals("Users", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddOverviewEntry(entries, directory, installedApps, quickSize: quickOnly);
            }
        }
        catch
        {
        }

        var userDirectories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };

        foreach (var directory in userDirectories.Where(Directory.Exists))
        {
            AddOverviewEntry(entries, directory, installedApps, quickSize: quickOnly);
        }

        foreach (var directory in userDirectories
                     .Where(Directory.Exists)
                     .Where(path => path.EndsWith(@"\Desktop", StringComparison.OrdinalIgnoreCase)
                         || path.EndsWith(@"\Downloads", StringComparison.OrdinalIgnoreCase)))
        {
            AddOverviewChildEntries(entries, directory, installedApps, quickSize: quickOnly);
        }

        if (!quickOnly)
        {
            foreach (var sharedRoot in new[]
            {
                Path.Combine(cRoot, "Program Files"),
                Path.Combine(cRoot, "Program Files (x86)"),
                Path.Combine(cRoot, "ProgramData")
            })
            {
                AddOverviewChildEntries(entries, sharedRoot, installedApps, quickSize: false);
            }
        }

        return entries.Values
            .Where(entry => entry.SizeBytes > 0)
            .OrderBy(GetOverviewCategoryPriority)
            .ThenByDescending(entry => entry.SizeBytes)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddOverviewDirectory(IDictionary<string, CDriveOverviewEntry> entries, string rootPath, string name, InstalledAppSnapshot installedApps, bool quickSize)
    {
        var path = Path.Combine(rootPath, name);
        if (!Directory.Exists(path))
        {
            return;
        }

        AddOverviewEntry(entries, path, installedApps, quickSize);
    }

    private void AddOverviewFile(IDictionary<string, CDriveOverviewEntry> entries, string path, InstalledAppSnapshot installedApps, bool quickSize)
    {
        if (!File.Exists(path) || !ShouldIncludeOverviewEntry(path, installedApps, isFile: true))
        {
            return;
        }

        var key = NormalizePath(path);
        if (entries.ContainsKey(key))
        {
            return;
        }

        entries[key] = CreateOverviewEntry(path, installedApps, isFile: true, quickSize);
    }

    private void AddOverviewChildEntries(IDictionary<string, CDriveOverviewEntry> entries, string rootPath, InstalledAppSnapshot installedApps, bool quickSize)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(rootPath))
            {
                AddOverviewEntry(entries, directory, installedApps, quickSize);
            }
        }
        catch
        {
        }
    }

    private void AddOverviewEntry(IDictionary<string, CDriveOverviewEntry> entries, string path, InstalledAppSnapshot installedApps, bool quickSize)
    {
        if (!Directory.Exists(path) || !ShouldIncludeOverviewEntry(path, installedApps, isFile: false))
        {
            return;
        }

        var normalizedPath = NormalizePath(path);
        if (entries.ContainsKey(normalizedPath))
        {
            return;
        }

        entries[normalizedPath] = CreateOverviewEntry(path, installedApps, isFile: false, quickSize);
    }

    private CDriveOverviewEntry CreateOverviewEntry(string path, InstalledAppSnapshot installedApps, bool isFile, bool quickSize)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = path;
        }

        var sizeBytes = quickSize
            ? FileSystemHelper.GetPathSizeBytesQuick(path)
            : FileSystemHelper.GetPathSizeBytes(path);
        var installedApp = MatchInstalledApp(path, name, installedApps);
        var category = GetOverviewCategory(path, name, installedApp);
        var protectionLevel = GetOverviewProtectionLevel(path, category, installedApp);
        var purposeText = GetOverviewPurposeText(path, category, installedApp, isFile);
        var migrationMode = GetMigrationMode(path, category, installedApps, installedApp, isFile);
        var migrationStrategy = GetMigrationStrategy(path, category, installedApp, migrationMode);
        var adapter = ResolveMigrationAdapter(path, installedApp);
        var companionPaths = ResolveOverviewCompanionPaths(path, installedApp, adapter);
        var validationTargetExe = ResolveOverviewValidationTargetExe(installedApp, adapter);
        var iconSourcePath = ResolveOverviewIconSourcePath(path, installedApp, companionPaths, validationTargetExe);
        var iconInstallRoot = ResolveOverviewIconInstallRoot(path, installedApp, companionPaths);
        var migrationHintText = GetMigrationHintText(path, category, migrationMode, installedApp);
        var recommendationText = GetOverviewRecommendationText(path, category, protectionLevel, installedApp, migrationMode);
        var canRelocate = category is "第三方已安装应用" or "用户数据目录" or "未知大目录";
        var canDelete = category is "未知大目录" or "应用缓存/数据";
        var canAutoMigrate = migrationMode == MigrationMode.AutoSafe;

        return new CDriveOverviewEntry
        {
            Name = name,
            Path = NormalizePath(path),
            SizeBytes = sizeBytes,
            Category = category,
            PurposeText = purposeText,
            ProtectionLevel = protectionLevel,
            RecommendationText = recommendationText,
            IconSourcePath = iconSourcePath,
            IconInstallRoot = iconInstallRoot,
            CanDelete = canDelete,
            CanRelocate = canRelocate,
            CanAutoMigrate = canAutoMigrate,
            MigrationMode = migrationMode,
            MigrationStrategy = migrationStrategy,
            AdapterKey = adapter?.Key ?? string.Empty,
            CompanionPaths = companionPaths,
            ValidationTargetExe = validationTargetExe,
            CanUseGenericJunctionRelocation = migrationStrategy == MigrationStrategy.GenericJunction,
            MigrationHintText = migrationHintText,
            SelectionEnabled = canAutoMigrate,
            InstalledAppName = installedApp?.DisplayName ?? string.Empty
        };
    }

    private static IReadOnlyList<string> ResolveOverviewCompanionPaths(
        string path,
        InstalledAppRecord? installedApp,
        MigrationAdapter? adapter)
    {
        var results = new List<string>();

        if (installedApp is not null)
        {
            results.AddRange(ResolveCompanionPaths(installedApp));
        }

        if (adapter is not null)
        {
            var parent = Path.GetDirectoryName(path) ?? string.Empty;
            results.AddRange(adapter.CompanionDirectoryNames
                .Select(value => NormalizePath(Path.Combine(parent, value))));
        }

        return results
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveOverviewValidationTargetExe(
        InstalledAppRecord? installedApp,
        MigrationAdapter? adapter)
    {
        return adapter?.ValidationTargetExeNames.FirstOrDefault()
            ?? installedApp?.ExecutableNames.FirstOrDefault()
            ?? string.Empty;
    }

    private static string ResolveOverviewIconSourcePath(
        string path,
        InstalledAppRecord? installedApp,
        IReadOnlyCollection<string> companionPaths,
        string validationTargetExe)
    {
        var normalizedPath = NormalizePath(path);
        if (File.Exists(normalizedPath))
        {
            return normalizedPath;
        }

        if (!string.IsNullOrWhiteSpace(installedApp?.DisplayIcon))
        {
            var displayIconPath = NormalizeExecutablePath(installedApp.DisplayIcon);
            if (File.Exists(displayIconPath))
            {
                return displayIconPath;
            }
        }

        foreach (var root in EnumerateIconRoots(normalizedPath, installedApp, companionPaths))
        {
            if (!string.IsNullOrWhiteSpace(validationTargetExe))
            {
                var validationTarget = FindExecutablePath(root, validationTargetExe);
                if (File.Exists(validationTarget))
                {
                    return NormalizePath(validationTarget);
                }
            }

            foreach (var executableName in installedApp?.ExecutableNames ?? [])
            {
                var executablePath = FindExecutablePath(root, executableName);
                if (File.Exists(executablePath))
                {
                    return NormalizePath(executablePath);
                }
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            try
            {
                var firstExecutable = Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (File.Exists(firstExecutable))
                {
                    return NormalizePath(firstExecutable);
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static string ResolveOverviewIconInstallRoot(
        string path,
        InstalledAppRecord? installedApp,
        IReadOnlyCollection<string> companionPaths)
    {
        var normalizedPath = NormalizePath(path);
        if (Directory.Exists(normalizedPath))
        {
            return normalizedPath;
        }

        if (installedApp is not null && Directory.Exists(installedApp.InstallLocation))
        {
            return NormalizePath(installedApp.InstallLocation);
        }

        return companionPaths.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static IEnumerable<string> EnumerateIconRoots(
        string path,
        InstalledAppRecord? installedApp,
        IReadOnlyCollection<string> companionPaths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var normalized = NormalizePath(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                return;
            }
        }

        AddRoot(path);
        if (installedApp is not null)
        {
            AddRoot(installedApp.InstallLocation);
        }

        foreach (var companionPath in companionPaths)
        {
            AddRoot(companionPath);
        }

        return seen;
    }

    private static InstalledAppRecord? MatchInstalledApp(string path, string name, InstalledAppSnapshot installedApps)
    {
        var normalizedPath = NormalizePath(path);

        var installMatch = installedApps.Applications
            .FirstOrDefault(app => !string.IsNullOrWhiteSpace(app.InstallLocation)
                && normalizedPath.StartsWith(app.InstallLocation, StringComparison.OrdinalIgnoreCase));
        if (installMatch is not null)
        {
            return installMatch;
        }

        if (LooksLikeGenericUserFolder(name, normalizedPath))
        {
            return null;
        }

        return installedApps.Applications
            .FirstOrDefault(app => name.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase)
                || app.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetOverviewCategory(string path, string name, InstalledAppRecord? installedApp)
    {
        var normalizedPath = NormalizePath(path);
        if (OverviewProtectedFiles.Contains(name, StringComparer.OrdinalIgnoreCase)
            || name.Equals("Windows", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Recovery", StringComparison.OrdinalIgnoreCase))
        {
            return "系统核心";
        }

        if (name.Equals("Program Files", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ProgramData", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Users", StringComparison.OrdinalIgnoreCase))
        {
            return name.Equals("Users", StringComparison.OrdinalIgnoreCase) ? "用户数据目录" : "系统共享组件";
        }

        if (normalizedPath.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase))
        {
            return "用户数据目录";
        }

        if (installedApp is not null && !IsSharedRuntimeName(name))
        {
            return "第三方已安装应用";
        }

        if (normalizedPath.Contains(@"\ProgramData\", StringComparison.OrdinalIgnoreCase))
        {
            return IsSharedRuntimeName(name) ? "系统共享组件" : "应用缓存/数据";
        }

        if (normalizedPath.Contains(@"\Program Files\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\Program Files (x86)\", StringComparison.OrdinalIgnoreCase))
        {
            return IsSharedRuntimeName(name) ? "系统共享组件" : "第三方已安装应用";
        }

        return "未知大目录";
    }

    private static CDriveProtectionLevel GetOverviewProtectionLevel(string path, string category, InstalledAppRecord? installedApp)
    {
        return category switch
        {
            "系统核心" => CDriveProtectionLevel.SystemProtected,
            "系统共享组件" => CDriveProtectionLevel.SharedComponent,
            "第三方已安装应用" when installedApp is not null => CDriveProtectionLevel.MoveRecommended,
            "用户数据目录" => CDriveProtectionLevel.MoveRecommended,
            "应用缓存/数据" => CDriveProtectionLevel.ReviewBeforeDelete,
            _ => CDriveProtectionLevel.ReviewBeforeDelete
        };
    }

    private static string GetOverviewPurposeText(string path, string category, InstalledAppRecord? installedApp, bool isFile)
    {
        if (isFile)
        {
            return path switch
            {
                var file when file.EndsWith("pagefile.sys", StringComparison.OrdinalIgnoreCase) => "Windows 虚拟内存分页文件，用来稳定系统内存使用。",
                var file when file.EndsWith("hiberfil.sys", StringComparison.OrdinalIgnoreCase) => "Windows 休眠文件，用于休眠和快速启动。",
                var file when file.EndsWith("swapfile.sys", StringComparison.OrdinalIgnoreCase) => "Windows 辅助交换文件，配合系统内存管理使用。",
                _ => "系统根目录下的重要系统文件。"
            };
        }

        return category switch
        {
            "系统核心" => "Windows 核心目录，存放系统文件、恢复环境和关键运行组件。",
            "系统共享组件" => "系统或多个程序共享使用的公共组件、运行库或统一数据目录。",
            "第三方已安装应用" => string.IsNullOrWhiteSpace(installedApp?.Publisher)
                ? "识别到的第三方应用安装目录。"
                : $"识别到的第三方应用安装目录，发布者：{installedApp.Publisher}。",
            "用户数据目录" => "用户自己的资料目录，常放桌面文件、下载、文档和视频。",
            "应用缓存/数据" => "程序生成的缓存、日志或公共数据目录，通常与某个应用相关。",
            _ => "暂未归类的 C 盘目录，建议先打开看看里面是什么再决定。"
        };
    }

    private static string GetOverviewRecommendationText(string path, string category, CDriveProtectionLevel protectionLevel, InstalledAppRecord? installedApp, MigrationMode migrationMode)
    {
        var normalizedPath = NormalizePath(path);
        var displayName = GetFriendlyAppOrFolderName(path, installedApp);
        var userFolderLabel = GetFriendlyUserFolderLabel(normalizedPath);
        var driverBound = IsDriverBoundPath(normalizedPath, installedApp);

        return category switch
        {
            "系统核心" => "这是系统保护内容，只展示用途和大小，不提供删除。",
            "系统共享组件" => driverBound
                ? $"这更像“{displayName}”的共享组件或驱动配套目录。直接删掉后，常见后果是：软件本体打不开、驱动面板/灯效/声卡控制面板异常、开机服务报错，甚至以后重装前还要再清残留。除非你非常确定用途，否则不要直接删。"
                : $"这类目录通常被系统或多个程序共用。现在删掉，电脑一般不会立刻黑屏，但后面某些软件的修复、升级、卸载或运行可能报缺文件。更稳妥的做法，是去处理下面更具体的应用目录，而不是动这一层公共目录。",
            "第三方已安装应用" when migrationMode == MigrationMode.AutoSafe => $"这更像“{displayName}”的软件安装目录，但当前已经通过安全规则，允许直接做兼容迁移。点“迁移”后，程序会搬到 D/E，并在原位置保留联接，所以多数快捷方式、开始菜单入口和原路径引用通常还能继续工作；如果你的目标是彻底卸载，而不是换盘，还是优先用正常卸载更干净。",
            "第三方已安装应用" => driverBound
                ? $"这更像“{displayName}”的程序主体或驱动目录。直接删目录后，软件可能马上打不开，桌面快捷方式会失效，设备驱动/反作弊/后台服务也可能异常。更稳妥：先在 Windows 的“应用和功能”里正常卸载，再决定是否重装到 D/E。"
                : $"这更像“{displayName}”的软件安装目录。直接删目录最常见的结果是：软件打不开、桌面快捷方式点了报错、后续更新或卸载找不到原文件。想腾 C 盘空间，更建议先正常卸载，再重装到 D/E，而不是把整个目录当垃圾直接删。",
            "用户数据目录" when migrationMode == MigrationMode.AutoSafe => $"这是你的{userFolderLabel}或它下面的大目录。点“迁移”会把整个目录搬到 D/E，并在原位置保留兼容联接，常见入口通常还能继续用；点“删除”则是里面的照片、视频、文档、安装包会一起没了。若你只想清一部分，更建议先双击进去再挑大的子目录处理。",
            "用户数据目录" => $"这是你的{userFolderLabel}或资料目录。整目录处理的后果很直接：迁移会整批搬走里面的内容，删除会让里面文件一起消失。除非你就是想整体搬家或整体丢弃，否则更适合先打开目录，只移动里面不常用的大文件、安装包或便携工具。",
            "应用缓存/数据" => BuildAppDataRecommendationText(normalizedPath, displayName, driverBound, protectionLevel),
            _ => normalizedPath.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase)
                ? $"这更像你自己放在 C 盘里的目录。点“迁移”会把整个目录搬到 D/E，并在原位置保留兼容联接；点“删除”则是里面所有内容一起没了。先双击打开看看里面是不是项目、视频、安装包或便携软件，再决定。"
                : "建议先确认用途。"
        };
    }

    private static MigrationMode GetMigrationMode(string path, string category, InstalledAppSnapshot installedApps, InstalledAppRecord? installedApp, bool isFile)
    {
        var normalizedPath = NormalizePath(path);
        if (category is "系统核心" or "系统共享组件")
        {
            return MigrationMode.Protected;
        }

        if (isFile)
        {
            return MigrationMode.GuideOnly;
        }

        if (IsDriverBoundPath(normalizedPath, installedApp))
        {
            return MigrationMode.Protected;
        }

        var adapter = ResolveMigrationAdapter(path, installedApp);
        if (CanUseAdapterBackedMigration(normalizedPath, installedApp, adapter))
        {
            return MigrationMode.AutoSafe;
        }

        if (CanUseGenericInstalledAppMigration(normalizedPath, category, installedApps, installedApp))
        {
            return MigrationMode.AutoSafe;
        }

        if (installedApp is not null
            || normalizedPath.Contains(@"\Program Files\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\Program Files (x86)\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\ProgramData\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase))
        {
            return MigrationMode.GuideOnly;
        }

        if (IsKnownUserRoot(normalizedPath))
        {
            return MigrationMode.GuideOnly;
        }

        if (normalizedPath.StartsWith(@"C:\Users\", StringComparison.OrdinalIgnoreCase)
            || IsCustomRootLevelDirectory(normalizedPath))
        {
            return MigrationMode.AutoSafe;
        }

        return MigrationMode.GuideOnly;
    }

    private static MigrationStrategy GetMigrationStrategy(string path, string category, InstalledAppRecord? installedApp, MigrationMode migrationMode)
    {
        if (migrationMode != MigrationMode.AutoSafe)
        {
            return MigrationStrategy.Protected;
        }

        if (ResolveMigrationAdapter(path, installedApp) is not null)
        {
            return MigrationStrategy.Adapter;
        }

        return MigrationStrategy.GenericJunction;
    }

    private static string GetMigrationHintText(string path, string category, MigrationMode migrationMode, InstalledAppRecord? installedApp)
    {
        var normalizedPath = NormalizePath(path);
        var displayName = GetFriendlyAppOrFolderName(path, installedApp);
        var driverBound = IsDriverBoundPath(normalizedPath, installedApp);
        return migrationMode switch
        {
            MigrationMode.AutoSafe when ResolveMigrationAdapter(path, installedApp) is not null => $"“{displayName}”已做适配。迁移时会优先处理主目录和关键附属目录，再在原位置保留兼容联接，所以大多数情况下还能像以前一样从原快捷方式继续打开。",
            MigrationMode.AutoSafe when category == "第三方已安装应用" => $"“{displayName}”现在支持通用兼容迁移。程序会把安装目录搬到 D/E，再在原位置保留同名联接，所以大多数桌面快捷方式、开始菜单入口和原路径引用通常还能继续用。",
            MigrationMode.AutoSafe => $"这个目录可以直接搬到 D/E。迁移后会在原位置保留兼容联接，旧快捷方式和常见启动方式通常还能继续用；但如果你根本不再需要里面内容，删除会比迁移更省空间。",
            MigrationMode.GuideOnly when category == "第三方已安装应用" => $"“{displayName}”更像标准安装型应用。现在不建议直接整目录搬家，最稳妥的方法是：先正常卸载，再重装到 D/E，这样最不容易留下旧注册表、旧服务或旧快捷方式问题。",
            MigrationMode.GuideOnly when IsKnownUserRoot(normalizedPath) => "这是桌面/下载/文档/视频这类入口目录本身。直接整根搬家容易把你日常还在用的文件一起挪走；更推荐先打开后，只迁里面真正占空间的大目录、安装包或便携软件。",
            MigrationMode.GuideOnly => $"这类目录需要你先看清里面是什么，再决定迁移还是删除。通常的安全做法是：先打开目录，把确认不常用的大内容移到 D/E，而不是闭眼整目录处理。",
            _ => driverBound
                ? $"这类目录常和“{displayName}”的驱动、服务、反作弊或硬件控制面板绑在一起。强行迁移或删错，常见后果是设备功能异常、面板打不开、开机报错或游戏无法启动，所以默认不提供自动迁移。"
                : $"这类目录属于高风险保护项。没有确认用途前，不建议直接迁移或删除；确实要处理，也最好先用正常卸载或官方设置里的迁移方式。"
        };
    }

    private static string BuildAppDataRecommendationText(string normalizedPath, string displayName, bool driverBound, CDriveProtectionLevel protectionLevel)
    {
        if (PathLooksLikePackageCache(normalizedPath))
        {
            return $"这里通常是安装器留下的修复包、升级包或卸载所需文件。现在删掉，电脑一般不会立刻坏，但以后如果“{displayName}”要修复、升级或卸载，可能提示缺少安装源，需要重新下载完整安装包。";
        }

        if (driverBound)
        {
            return $"这更像“{displayName}”的公共数据、缓存或更新目录。删掉后最常见的结果是：软件会重建一部分缓存，但灯效/宏/设备配置、更新包、日志或控制面板数据也可能一起丢，严重时还会让驱动面板、反作弊或配套功能异常。除非你明确知道里面只是缓存，否则先别删。";
        }

        return protectionLevel == CDriveProtectionLevel.ReviewBeforeDelete
            ? $"这里通常放缓存、日志、下载包、离线资源或公共数据。删掉后最常见的结果是：软件第一次启动会重新生成缓存，可能会慢一点；如果里面还有下载内容、离线资源、登录资料或配置，它们也会一起没了。先双击进去看一眼最稳妥。"
            : $"建议先确认里面到底是缓存还是重要数据。缓存删掉一般还能自动重建；但如果里面放的是下载内容、离线包、账号数据或配置，删掉后就需要重新下载、重新登录或重新设置。";
    }

    private static bool PathLooksLikePackageCache(string normalizedPath)
    {
        return normalizedPath.Contains("Package Cache", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\Installer", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\InstallShield", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\Downloader", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\Setup", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanUseAdapterBackedMigration(string normalizedPath, InstalledAppRecord? installedApp, MigrationAdapter? adapter)
    {
        if (adapter is null
            || adapter.DriverBound
            || !adapter.AllowAutomaticMigration
            || !Directory.Exists(normalizedPath)
            || PathLooksLikePackageCache(normalizedPath)
            || LooksLikeBlockedGenericMigrationPath(normalizedPath, installedApp))
        {
            return false;
        }

        if (installedApp is not null && IsProtectedSoftware(installedApp))
        {
            return false;
        }

        return adapter.ValidationTargetExeNames.Count > 0
            || (installedApp?.ExecutableNames.Count ?? 0) > 0;
    }

    private static bool CanUseGenericInstalledAppMigration(
        string normalizedPath,
        string category,
        InstalledAppSnapshot installedApps,
        InstalledAppRecord? installedApp)
    {
        if (category != "第三方已安装应用"
            || installedApp is null
            || !Directory.Exists(normalizedPath)
            || IsProtectedSoftware(installedApp)
            || ResolveMigrationAdapter(normalizedPath, installedApp) is not null
            || LooksLikeBlockedGenericMigrationPath(normalizedPath, installedApp))
        {
            return false;
        }

        var installRoot = NormalizePath(installedApp.InstallLocation);
        if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
        {
            return false;
        }

        var underKnownInstallRoot =
            normalizedPath.Contains(@"\Program Files\", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains(@"\Program Files (x86)\", StringComparison.OrdinalIgnoreCase)
            || IsCustomRootLevelDirectory(normalizedPath);
        if (!underKnownInstallRoot)
        {
            return false;
        }

        if (!IsSameOrParentPath(normalizedPath, installRoot) && !IsSameOrParentPath(installRoot, normalizedPath))
        {
            return false;
        }

        var appsUnderPath = GetInstalledAppsWithinPath(normalizedPath, installedApps);
        if (appsUnderPath.Count == 0)
        {
            appsUnderPath = [installedApp];
        }

        if (appsUnderPath.Any(app =>
                IsProtectedSoftware(app)
                || LooksLikeBlockedGenericMigrationPath(NormalizePath(app.InstallLocation), app)
                || !HasExecutableValidationTarget(app)))
        {
            return false;
        }

        return HasExecutableValidationTarget(installedApp);
    }

    private static List<InstalledAppRecord> GetInstalledAppsWithinPath(string normalizedPath, InstalledAppSnapshot installedApps)
    {
        return installedApps.Applications
            .Where(app => IsSameOrParentPath(normalizedPath, app.InstallLocation))
            .DistinctBy(app => app.AppIdentityKey)
            .ToList();
    }

    private static bool IsSameOrParentPath(string candidateParent, string candidateChild)
    {
        var normalizedParent = NormalizePath(candidateParent);
        var normalizedChild = NormalizePath(candidateChild);
        if (string.IsNullOrWhiteSpace(normalizedParent) || string.IsNullOrWhiteSpace(normalizedChild))
        {
            return false;
        }

        return normalizedChild.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)
            || normalizedChild.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedChild.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExecutableValidationTarget(InstalledAppRecord app)
    {
        if (app.ExecutableNames.Count > 0)
        {
            return true;
        }

        var iconPath = NormalizeExecutablePath(app.DisplayIcon);
        return File.Exists(iconPath);
    }

    private static bool LooksLikeBlockedGenericMigrationPath(string normalizedPath, InstalledAppRecord? installedApp)
    {
        if (PathLooksLikePackageCache(normalizedPath))
        {
            return true;
        }

        var leafName = Path.GetFileName(normalizedPath);
        if (IsSharedRuntimeName(leafName)
            || IsSharedRuntimeName(installedApp?.DisplayName ?? string.Empty)
            || IsSharedRuntimeName(installedApp?.Publisher ?? string.Empty))
        {
            return true;
        }

        var matchSource = string.Join(
            " ",
            new[]
            {
                normalizedPath,
                leafName,
                installedApp?.DisplayName ?? string.Empty,
                installedApp?.Publisher ?? string.Empty
            });

        return GenericMigrationBlockedTokens.Any(token => matchSource.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetFriendlyAppOrFolderName(string path, InstalledAppRecord? installedApp)
    {
        if (!string.IsNullOrWhiteSpace(installedApp?.DisplayName))
        {
            return installedApp.DisplayName;
        }

        var leaf = Path.GetFileName(NormalizePath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(leaf) ? "这个目录" : leaf;
    }

    private static string GetFriendlyUserFolderLabel(string normalizedPath)
    {
        return normalizedPath switch
        {
            var path when path.EndsWith(@"\Desktop", StringComparison.OrdinalIgnoreCase) => "桌面目录",
            var path when path.EndsWith(@"\Downloads", StringComparison.OrdinalIgnoreCase) => "下载目录",
            var path when path.EndsWith(@"\Documents", StringComparison.OrdinalIgnoreCase) => "文档目录",
            var path when path.EndsWith(@"\Videos", StringComparison.OrdinalIgnoreCase) => "视频目录",
            _ => "资料目录"
        };
    }

    private static bool IsKnownUserRoot(string normalizedPath)
    {
        var knownRoots = new[]
        {
            NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            NormalizePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos))
        };

        return knownRoots.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCustomRootLevelDirectory(string normalizedPath)
    {
        var segments = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return normalizedPath.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase)
            && segments.Length == 2
            && !segments[1].Equals("Users", StringComparison.OrdinalIgnoreCase)
            && !RootProtectedNames.Contains(segments[1], StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeGenericUserFolder(string name, string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!normalizedPath.Contains(@"\Users\", StringComparison.OrdinalIgnoreCase)
            && !normalizedPath.Equals(@"C:\Users", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GenericUserFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<MigrationCandidate> BuildMigrationCandidates(IReadOnlyCollection<CDriveOverviewEntry> overviewEntries)
    {
        return overviewEntries
            .Where(entry => entry.ProtectionLevel is not CDriveProtectionLevel.SystemProtected and not CDriveProtectionLevel.SharedComponent)
            .OrderBy(entry => entry.MigrationMode)
            .ThenByDescending(entry => entry.SizeBytes)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new MigrationCandidate
            {
                Id = entry.Id,
                Name = entry.Name,
                SourcePath = entry.Path,
                SizeBytes = entry.SizeBytes,
                Category = entry.Category,
                PurposeText = entry.PurposeText,
                RecommendationText = entry.RecommendationText,
                IconSourcePath = entry.IconSourcePath,
                IconInstallRoot = entry.IconInstallRoot,
                MigrationHintText = entry.MigrationHintText,
                MigrationMode = entry.MigrationMode,
                MigrationStrategy = entry.MigrationStrategy,
                AdapterKey = entry.AdapterKey,
                CompanionPaths = entry.CompanionPaths,
                ValidationTargetExe = entry.ValidationTargetExe,
                CanUseGenericJunctionRelocation = entry.CanUseGenericJunctionRelocation,
                SelectionEnabled = entry.SelectionEnabled,
                CanDelete = entry.CanDelete || entry.MigrationMode == MigrationMode.AutoSafe,
                InstalledAppName = entry.InstalledAppName
            })
            .ToList();
    }

    private bool ShouldIncludeOverviewEntry(string path, InstalledAppSnapshot installedApps, bool isFile)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        if (isFile)
        {
            return false;
        }

        if (IsProtectedPath(normalizedPath))
        {
            return false;
        }

        var name = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = normalizedPath;
        }

        var installedApp = MatchInstalledApp(normalizedPath, name, installedApps);
        var category = GetOverviewCategory(normalizedPath, name, installedApp);
        var protectionLevel = GetOverviewProtectionLevel(normalizedPath, category, installedApp);
        return protectionLevel is not CDriveProtectionLevel.SystemProtected and not CDriveProtectionLevel.SharedComponent;
    }

    private static void ReportSnapshotProgress(
        IProgress<OperationProgress>? progress,
        string jobScope,
        int completedPhases,
        int totalPhases,
        int startPercent,
        int endPercent,
        string phaseName,
        string message)
    {
        if (progress is null)
        {
            return;
        }

        var safeTotalPhases = Math.Max(totalPhases, 1);
        var safeStart = Math.Clamp(startPercent, 0, 100);
        var safeEnd = Math.Clamp(endPercent, safeStart, 100);
        var span = Math.Max(safeEnd - safeStart, 1);
        var progressPercent = safeStart + (int)Math.Round(span * (double)Math.Clamp(completedPhases, 0, safeTotalPhases) / safeTotalPhases);
        progress.Report(new OperationProgress
        {
            Percent = Math.Clamp(progressPercent, 0, 100),
            Phase = phaseName,
            Message = message,
            IsIndeterminate = false,
            JobScope = jobScope
        });
    }

    private IReadOnlyList<InfrequentSoftwareEntry> BuildInfrequentSoftwareEntries(
        AppSettings settings,
        IReadOnlyCollection<DriveInfo> fixedDrives,
        InstalledAppSnapshot installedApps,
        IReadOnlyCollection<string> selectedDriveNames,
        ScanRunState runState)
    {
        var whitelistAppIds = (settings.WhitelistedAppIdentityKeys ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runningProcesses = LoadRunningProcessUsageEntries();
        var userAssistEntries = LoadUserAssistUsageEntries();
        var shortcutEntries = LoadShortcutUsageEntries(runState);
        var scopedInstalledApps = FilterInstalledAppSnapshotByDrives(installedApps, selectedDriveNames);
        var catalog = BuildSoftwareCatalog(fixedDrives, scopedInstalledApps, selectedDriveNames, runState);
        var entries = new Dictionary<string, InfrequentSoftwareEntry>(StringComparer.OrdinalIgnoreCase);
        var utcToday = DateTime.UtcNow.Date;

        foreach (var app in catalog)
        {
            if (IsProtectedSoftware(app))
            {
                continue;
            }

            var usage = DetermineUsage(app, runningProcesses, userAssistEntries, shortcutEntries);
            var unusedDays = usage.LastUsedUtc.HasValue
                ? Math.Max((utcToday - usage.LastUsedUtc.Value.Date).Days, 0)
                : (int?)null;
            var isWhitelisted = whitelistAppIds.Contains(app.AppIdentityKey);
            var recommended = ShouldRecommendInfrequentDeletion(unusedDays, usage.Confidence, isWhitelisted);

            if (!ShouldIncludeInfrequentEntry(app, unusedDays, usage.Confidence, isWhitelisted, usage.IsCurrentlyRunning))
            {
                continue;
            }

            var recommendationText = BuildInfrequentSoftwareRecommendationText(unusedDays, usage, isWhitelisted);
            var deletionImpactText = BuildInfrequentSoftwareDeletionImpactText(app);
            var canDeepDelete = (Directory.Exists(app.InstallLocation) || File.Exists(app.InstallLocation) || !string.IsNullOrWhiteSpace(app.UninstallString))
                && !isWhitelisted;
            var driveName = GetDriveName(app.InstallLocation);
            var iconSourcePath = ResolveInfrequentSoftwareIconSourcePath(app, shortcutEntries);
            var statusKind = isWhitelisted
                ? InfrequentSoftwareStatusKind.Whitelisted
                : usage.IsCurrentlyRunning
                    ? InfrequentSoftwareStatusKind.CurrentRunning
                : recommended
                    ? InfrequentSoftwareStatusKind.RecommendedDeletion
                    : !unusedDays.HasValue
                        ? InfrequentSoftwareStatusKind.NoReliableUsageRecord
                        : unusedDays.Value < 30
                            ? InfrequentSoftwareStatusKind.ActiveRecent
                            : InfrequentSoftwareStatusKind.ReviewOnly;

            entries[app.AppIdentityKey] = new InfrequentSoftwareEntry
            {
                AppIdentityKey = app.AppIdentityKey,
                DisplayName = app.DisplayName,
                PrimaryDrive = driveName,
                InstallRoot = app.InstallLocation,
                IconSourcePath = iconSourcePath,
                CompanionPaths = ResolveCompanionPaths(app),
                SizeBytes = GetSoftwareSizeBytes(app),
                LastUsedUtc = usage.LastUsedUtc,
                UnusedDays = unusedDays,
                UsageConfidence = usage.Confidence,
                UsageSourceText = usage.SourceText,
                UsageEvidenceText = usage.EvidenceText,
                RecommendationText = recommendationText,
                DeletionImpactText = deletionImpactText,
                CanDeepDelete = canDeepDelete,
                IsProtected = false,
                IsWhitelisted = isWhitelisted,
                IsCurrentlyRunning = usage.IsCurrentlyRunning,
                RunningProcessText = usage.RunningProcessSummary,
                RecommendedForDeletion = recommended,
                StatusKind = statusKind
            };
        }

        return entries.Values
            .Where(entry => entry.SizeBytes > 0)
            .OrderBy(entry => GetDrivePriority(entry.PrimaryDrive))
            .ThenBy(entry => entry.RecommendedForDeletion ? 0 : entry.IsWhitelisted ? 2 : 1)
            .ThenByDescending(entry => entry.UnusedDays ?? -1)
            .ThenByDescending(entry => entry.SizeBytes)
            .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildInfrequentSoftwareDeletionImpactText(InstalledAppRecord app)
    {
        var displayName = string.IsNullOrWhiteSpace(app.DisplayName) ? "这个软件" : app.DisplayName;
        if (string.IsNullOrWhiteSpace(app.UninstallString))
        {
            return $"删除后：会直接删除“{displayName}”的程序目录，并继续清理强匹配的注册表、启动项、快捷方式、计划任务和防火墙残留，尽量做到重装不冲突。先别删：你现在还要直接从这个目录启动它，或者里面还有你自己放进去的数据、脚本或插件。";
        }

        return $"删除后：会先尝试用“{displayName}”自己的官方卸载流程，再继续清理强匹配的卸载键、启动项、快捷方式、计划任务和防火墙残留，尽量做到像没装过一样。先别删：你暂时还会用它，或者你还没确认里面有没有需要单独备份的数据。";
    }

    private static bool ShouldRecommendInfrequentDeletion(int? unusedDays, AppUsageConfidence confidence, bool isWhitelisted)
    {
        if (isWhitelisted || !unusedDays.HasValue || unusedDays.Value < 60)
        {
            return false;
        }

        return confidence is AppUsageConfidence.High or AppUsageConfidence.Medium;
    }

    private static string BuildInfrequentSoftwareRecommendationText(int? unusedDays, AppUsageResolution usage, bool isWhitelisted)
    {
        if (isWhitelisted)
        {
            return "已加入白名单，这一类软件暂时不再建议删除。";
        }

        if (usage.IsCurrentlyRunning)
        {
            return string.IsNullOrWhiteSpace(usage.RunningProcessSummary)
                ? "当前检测到这个软件还在运行，先别删；真要删，先关闭主程序和后台常驻进程。"
                : $"当前检测到 {usage.RunningProcessSummary} 仍在运行，先别删；真要删，先关闭主程序和后台常驻进程。";
        }

        if (!unusedDays.HasValue)
        {
            return "暂未拿到可靠使用记录，默认只提示你确认，不自动建议删除。";
        }

        if (unusedDays.Value < 30)
        {
            return $"近 30 天内还有{usage.SourceText}，暂不建议删除。";
        }

        if (ShouldRecommendInfrequentDeletion(unusedDays, usage.Confidence, isWhitelisted: false))
        {
            return $"已超过 {unusedDays.Value} 天没有看到新的{usage.SourceText}，而且可信度为{GetUsageConfidenceDisplayText(usage.Confidence)}，可考虑深度删除。";
        }

        if (usage.Confidence == AppUsageConfidence.Low)
        {
            return $"已超过 {unusedDays.Value} 天没有看到新的{usage.SourceText}，但这只是低可信估算，建议先打开看看是否还会用，再决定删不删。";
        }

        return $"最近一次{usage.SourceText}距今 {unusedDays.Value} 天，建议先确认是否还会使用。";
    }

    private static string GetUsageConfidenceDisplayText(AppUsageConfidence confidence)
    {
        return confidence switch
        {
            AppUsageConfidence.High => "高",
            AppUsageConfidence.Medium => "中",
            AppUsageConfidence.Low => "低",
            _ => "未知"
        };
    }

    private static string ResolveInfrequentSoftwareIconSourcePath(InstalledAppRecord app, IReadOnlyList<ShortcutUsageEntry> shortcutEntries)
    {
        if (File.Exists(app.InstallLocation))
        {
            return NormalizePath(app.InstallLocation);
        }

        foreach (var executableName in app.ExecutableNames)
        {
            var executablePath = FindExecutablePath(app.InstallLocation, executableName);
            if (File.Exists(executablePath))
            {
                return NormalizePath(executablePath);
            }
        }

        if (!string.IsNullOrWhiteSpace(app.DisplayIcon))
        {
            var iconPath = NormalizeExecutablePath(app.DisplayIcon);
            if (File.Exists(iconPath))
            {
                return iconPath;
            }
        }

        var shortcutTarget = shortcutEntries
            .Where(entry => MatchesShortcutUsage(app, entry))
            .Select(entry => NormalizePath(entry.TargetPath))
            .FirstOrDefault(path => File.Exists(path));
        if (!string.IsNullOrWhiteSpace(shortcutTarget))
        {
            return shortcutTarget;
        }

        if (!Directory.Exists(app.InstallLocation))
        {
            return string.Empty;
        }

        try
        {
            return Directory.EnumerateFiles(app.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly)
                .Select(NormalizePath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private IReadOnlyList<InstalledAppRecord> BuildSoftwareCatalog(
        IReadOnlyCollection<DriveInfo> fixedDrives,
        InstalledAppSnapshot installedApps,
        IReadOnlyCollection<string> selectedDriveNames,
        ScanRunState runState)
    {
        var entries = new Dictionary<string, InstalledAppRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in installedApps.Applications
                     .Where(app => !string.IsNullOrWhiteSpace(app.AppIdentityKey))
                     .OrderBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (!entries.ContainsKey(app.AppIdentityKey))
            {
                entries[app.AppIdentityKey] = app;
            }
        }

        foreach (var portable in DiscoverPortableApps(fixedDrives, installedApps, selectedDriveNames, runState))
        {
            if (!entries.ContainsKey(portable.AppIdentityKey))
            {
                entries[portable.AppIdentityKey] = portable;
            }
        }

        return entries.Values.ToList();
    }

    private IReadOnlyList<InstalledAppRecord> DiscoverPortableApps(
        IReadOnlyCollection<DriveInfo> fixedDrives,
        InstalledAppSnapshot installedApps,
        IReadOnlyCollection<string> selectedDriveNames,
        ScanRunState runState)
    {
        var roots = new List<string>();
        roots.AddRange(GetScopedUserFolderRoots(selectedDriveNames));
        foreach (var drive in fixedDrives)
        {
            roots.Add(drive.RootDirectory.FullName);
        }

        var results = new Dictionary<string, InstalledAppRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots
                     .Where(Directory.Exists)
                     .Select(NormalizePath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var directory in FileSystemHelper.EnumerateDirectoriesSafe(root, onError: CreateEnumerationWarningSink(runState, "长期未用软件")))
            {
                var normalized = NormalizePath(directory);
                if (IsProtectedPath(normalized)
                    || LooksLikeDevelopmentWorkspace(normalized)
                    || RootProtectedNames.Contains(Path.GetFileName(normalized), StringComparer.OrdinalIgnoreCase)
                    || installedApps.InstallLocations.Any(path => normalized.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                    || results.Values.Any(existing => existing.InstallLocation.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var executableNames = GetExecutableNamesForPath(normalized, string.Empty);
                if (executableNames.Count == 0)
                {
                    continue;
                }

                var displayName = Path.GetFileName(normalized);
                var appIdentityKey = BuildAppIdentityKey(displayName, normalized);
                if (string.IsNullOrWhiteSpace(appIdentityKey) || results.ContainsKey(appIdentityKey))
                {
                    continue;
                }

                var displayIcon = executableNames
                    .Select(name => FindExecutablePath(normalized, name))
                    .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                    ?? string.Empty;

                results[appIdentityKey] = new InstalledAppRecord(
                    displayName,
                    normalized,
                    string.Empty,
                    displayIcon,
                    string.Empty,
                    true,
                    appIdentityKey,
                    executableNames);
            }
        }

        return results.Values.ToList();
    }

    private AppUsageResolution DetermineUsage(
        InstalledAppRecord app,
        IReadOnlyList<RunningProcessUsageEntry> runningProcesses,
        IReadOnlyList<UserAssistUsageEntry> userAssistEntries,
        IReadOnlyList<ShortcutUsageEntry> shortcutEntries)
    {
        var matchingProcesses = runningProcesses
            .Where(entry => MatchesRunningProcess(app, entry))
            .OrderByDescending(entry => entry.StartedAtUtc ?? DateTime.MinValue)
            .ToList();
        if (matchingProcesses.Count > 0)
        {
            var runningProcessNames = matchingProcesses
                .Select(entry => entry.DisplayName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            var runningSummary = string.Join("、", runningProcessNames);
            var runningEvidence = string.IsNullOrWhiteSpace(runningSummary)
                ? "当前检测到这个软件仍在运行，先别删。"
                : $"当前检测到 {runningSummary} 正在运行，先别删；如果后面真要删，先把它和后台常驻进程都关掉。";
            return new AppUsageResolution(
                DateTime.UtcNow,
                AppUsageConfidence.High,
                "当前正在运行",
                runningEvidence,
                true,
                runningSummary);
        }

        var userAssist = userAssistEntries
            .Where(entry => entry.LastUsedUtc.HasValue && MatchesUsageEntry(app, entry.DecodedName))
            .OrderByDescending(entry => entry.LastUsedUtc)
            .FirstOrDefault();
        if (userAssist is not null && userAssist.LastUsedUtc.HasValue)
        {
            return new AppUsageResolution(
                userAssist.LastUsedUtc,
                AppUsageConfidence.High,
                "启动记录",
                $"UserAssist 记录到最近一次启动痕迹：{userAssist.LastUsedUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}。",
                false,
                string.Empty);
        }

        var shortcut = shortcutEntries
            .Where(entry => MatchesShortcutUsage(app, entry))
            .OrderByDescending(entry => entry.LastActivityUtc)
            .FirstOrDefault();
        if (shortcut is not null && shortcut.LastActivityUtc.HasValue)
        {
            return new AppUsageResolution(
                shortcut.LastActivityUtc,
                AppUsageConfidence.Medium,
                "快捷方式痕迹",
                $"快捷方式最近活动时间：{shortcut.LastActivityUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}。",
                false,
                string.Empty);
        }

        var activityUtc = GetExecutableActivityUtc(app);
        if (activityUtc.HasValue)
        {
            return new AppUsageResolution(
                activityUtc,
                AppUsageConfidence.Low,
                "程序活动估算",
                $"按主程序或安装目录活动时间估算：{activityUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}。",
                false,
                string.Empty);
        }

        return new AppUsageResolution(
            null,
            AppUsageConfidence.Unknown,
            "无可靠记录",
            "未检测到可靠使用记录。默认只展示给你确认，不自动建议删除。",
            false,
            string.Empty);
    }

    private static bool ShouldIncludeInfrequentEntry(
        InstalledAppRecord app,
        int? unusedDays,
        AppUsageConfidence confidence,
        bool isWhitelisted,
        bool isCurrentlyRunning)
    {
        if (isWhitelisted)
        {
            return true;
        }

        if (isCurrentlyRunning)
        {
            return true;
        }

        if (unusedDays.HasValue && unusedDays.Value >= 30)
        {
            return true;
        }

        return confidence == AppUsageConfidence.Unknown || app.IsPortable;
    }

    private static bool MatchesUsageEntry(InstalledAppRecord app, string decodedName)
    {
        if (string.IsNullOrWhiteSpace(decodedName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(app.InstallLocation)
            && decodedName.Contains(app.InstallLocation, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (app.ExecutableNames.Any(exe => decodedName.Contains(exe, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return decodedName.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesShortcutUsage(InstalledAppRecord app, ShortcutUsageEntry shortcut)
    {
        if (!shortcut.LastActivityUtc.HasValue)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(shortcut.TargetPath))
        {
            if (!string.IsNullOrWhiteSpace(app.InstallLocation)
                && shortcut.TargetPath.StartsWith(app.InstallLocation, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (app.ExecutableNames.Any(exe => shortcut.TargetPath.Contains(exe, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return shortcut.ShortcutName.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRunningProcess(InstalledAppRecord app, RunningProcessUsageEntry process)
    {
        if (string.IsNullOrWhiteSpace(process.ProcessName) && string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return false;
        }

        if (File.Exists(app.InstallLocation) && !string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return process.ExecutablePath.Equals(NormalizePath(app.InstallLocation), StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            if (!string.IsNullOrWhiteSpace(app.InstallLocation)
                && process.ExecutablePath.StartsWith(app.InstallLocation, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (app.ExecutableNames.Any(exe => process.ExecutablePath.EndsWith(Path.DirectorySeparatorChar + exe, StringComparison.OrdinalIgnoreCase)
                || process.ExecutablePath.EndsWith(Path.AltDirectorySeparatorChar + exe, StringComparison.OrdinalIgnoreCase)
                || process.ExecutablePath.Contains(exe, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (app.ExecutableNames.Any(exe => process.ProcessName.Equals(Path.GetFileNameWithoutExtension(exe), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return process.DisplayName.Contains(app.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<RunningProcessUsageEntry> LoadRunningProcessUsageEntries()
    {
        var results = new List<RunningProcessUsageEntry>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                using (process)
                {
                    var processName = process.ProcessName?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(processName))
                    {
                        continue;
                    }

                    var executablePath = TryGetProcessExecutablePath(process);
                    var startedAtUtc = TryGetProcessStartTimeUtc(process);
                    var windowTitle = process.MainWindowTitle?.Trim() ?? string.Empty;
                    var displayName = string.IsNullOrWhiteSpace(windowTitle) ? processName : $"{processName}（{windowTitle}）";

                    results.Add(new RunningProcessUsageEntry(
                        processName,
                        executablePath,
                        displayName,
                        startedAtUtc));
                }
            }
            catch
            {
            }
        }

        return results;
    }

    private static IReadOnlyList<UserAssistUsageEntry> LoadUserAssistUsageEntries()
    {
        var results = new List<UserAssistUsageEntry>();
        using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist");
        if (root is null)
        {
            return results;
        }

        foreach (var subKeyName in root.GetSubKeyNames())
        {
            using var countKey = root.OpenSubKey($@"{subKeyName}\Count");
            if (countKey is null)
            {
                continue;
            }

            foreach (var valueName in countKey.GetValueNames())
            {
                if (countKey.GetValue(valueName) is not byte[] bytes || bytes.Length < 68)
                {
                    continue;
                }

                var decodedName = DecodeRot13(valueName);
                var fileTime = bytes.Length >= 68 ? BitConverter.ToInt64(bytes, 60) : 0;
                DateTime? lastUsedUtc = null;
                if (fileTime > 0)
                {
                    try
                    {
                        lastUsedUtc = DateTime.FromFileTimeUtc(fileTime);
                    }
                    catch
                    {
                        lastUsedUtc = null;
                    }
                }

                results.Add(new UserAssistUsageEntry(decodedName, lastUsedUtc));
            }
        }

        return results;
    }

    private IReadOnlyList<ShortcutUsageEntry> LoadShortcutUsageEntries(ScanRunState runState)
    {
        var directories = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        var results = new List<ShortcutUsageEntry>();
        foreach (var directory in directories)
        {
            foreach (var shortcut in FileSystemHelper.EnumerateFilesSafe(
                         directory,
                         "*.lnk",
                         recursive: true,
                         onError: CreateEnumerationWarningSink(runState, "长期未用软件")))
            {
                try
                {
                    var info = new FileInfo(shortcut);
                    var targetPath = ResolveShortcutTarget(shortcut);
                    var lastActivityUtc = info.LastAccessTimeUtc > DateTime.UnixEpoch
                        ? info.LastAccessTimeUtc
                        : info.LastWriteTimeUtc;
                    results.Add(new ShortcutUsageEntry(
                        Path.GetFileNameWithoutExtension(shortcut),
                        shortcut,
                        targetPath,
                        lastActivityUtc > DateTime.UnixEpoch ? lastActivityUtc : null));
                }
                catch
                {
                }
            }
        }

        return results;
    }

    private static bool LooksLikeDevelopmentWorkspace(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            if (DevelopmentWorkspaceDirectoryMarkers.Any(marker => Directory.Exists(Path.Combine(path, marker))))
            {
                return true;
            }

            if (DevelopmentWorkspaceFileMarkers.Any(marker => File.Exists(Path.Combine(path, marker))))
            {
                return true;
            }

            var projectFileHit = Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Any(fileName =>
                    !string.IsNullOrWhiteSpace(fileName)
                    && (fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".vcxproj", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)));
            if (projectFileHit)
            {
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static string TryGetProcessExecutablePath(Process process)
    {
        try
        {
            return NormalizePath(process.MainModule?.FileName ?? string.Empty);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DateTime? TryGetProcessStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveShortcutTarget(string shortcutPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return string.Empty;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            var target = shortcut.TargetPath as string ?? string.Empty;
            return NormalizePath(target);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static DateTime? GetExecutableActivityUtc(InstalledAppRecord app)
    {
        if (!string.IsNullOrWhiteSpace(app.DisplayIcon))
        {
            var iconPath = NormalizeExecutablePath(app.DisplayIcon);
            if (File.Exists(iconPath))
            {
                return File.GetLastWriteTimeUtc(iconPath);
            }
        }

        foreach (var executableName in app.ExecutableNames)
        {
            var executablePath = FindExecutablePath(app.InstallLocation, executableName);
            if (File.Exists(executablePath))
            {
                return File.GetLastWriteTimeUtc(executablePath);
            }
        }

        if (Directory.Exists(app.InstallLocation))
        {
            return Directory.GetLastWriteTimeUtc(app.InstallLocation);
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveCompanionPaths(InstalledAppRecord app)
    {
        var adapter = ResolveMigrationAdapter(app.InstallLocation, app);
        if (adapter is null)
        {
            return [];
        }

        var parent = Path.GetDirectoryName(app.InstallLocation) ?? string.Empty;
        return adapter.CompanionDirectoryNames
            .Select(name => NormalizePath(Path.Combine(parent, name)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long GetSoftwareSizeBytes(InstalledAppRecord app)
    {
        if (Directory.Exists(app.InstallLocation))
        {
            return FileSystemHelper.GetPathSizeBytes(app.InstallLocation);
        }

        if (File.Exists(app.InstallLocation))
        {
            return new FileInfo(app.InstallLocation).Length;
        }

        return 0;
    }

    private static bool IsProtectedSoftware(InstalledAppRecord app)
    {
        var normalized = NormalizePath(app.InstallLocation);
        var leafName = Path.GetFileName(normalized);
        var systemRoot = NormalizePath(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        if (normalized.StartsWith(Path.Combine(systemRoot, "Windows"), StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\Common Files\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\WindowsApps", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\ModifiableWindowsApps", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (leafName.Equals("WSL", StringComparison.OrdinalIgnoreCase)
            || app.DisplayName.Contains("Windows Subsystem for Linux", StringComparison.OrdinalIgnoreCase)
            || app.DisplayName.Equals("WSL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsSharedRuntimeName(leafName)
            || IsSharedRuntimeName(app.DisplayName)
            || IsDriverBoundPath(normalized, app))
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> GetExecutableNamesForPath(string path, string displayIcon)
    {
        var names = new List<string>();
        var iconPath = NormalizeExecutablePath(displayIcon);
        if (File.Exists(iconPath))
        {
            names.Add(Path.GetFileName(iconPath));
        }

        if (!Directory.Exists(path))
        {
            return names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        try
        {
            names.AddRange(Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly)
                .Select(file => Path.GetFileName(file))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(8)!);
        }
        catch
        {
        }

        if (names.Count == 0)
        {
            try
            {
                names.AddRange(Directory.EnumerateFiles(path, "*.exe", SearchOption.AllDirectories)
                    .Select(file => Path.GetFileName(file))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Take(6)!);
            }
            catch
            {
            }
        }

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FindExecutablePath(string rootPath, string executableName)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(executableName) || !Directory.Exists(rootPath))
        {
            return string.Empty;
        }

        try
        {
            return Directory.EnumerateFiles(rootPath, executableName, SearchOption.TopDirectoryOnly).FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeExecutablePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var clean = rawPath.Trim().Trim('"');
        var commaIndex = clean.IndexOf(',');
        if (commaIndex > 0)
        {
            clean = clean[..commaIndex];
        }

        return NormalizePath(clean);
    }

    private static string DecodeRot13(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                >= 'a' and <= 'z' => (char)('a' + ((ch - 'a' + 13) % 26)),
                >= 'A' and <= 'Z' => (char)('A' + ((ch - 'A' + 13) % 26)),
                _ => ch
            });
        }

        return builder.ToString();
    }

    private sealed record AppUsageResolution(
        DateTime? LastUsedUtc,
        AppUsageConfidence Confidence,
        string SourceText,
        string EvidenceText,
        bool IsCurrentlyRunning,
        string RunningProcessSummary);

    private sealed record UserAssistUsageEntry(
        string DecodedName,
        DateTime? LastUsedUtc);

    private sealed record RunningProcessUsageEntry(
        string ProcessName,
        string ExecutablePath,
        string DisplayName,
        DateTime? StartedAtUtc);

    private sealed record ShortcutUsageEntry(
        string ShortcutName,
        string ShortcutPath,
        string TargetPath,
        DateTime? LastActivityUtc);

    private static int GetOverviewCategoryPriority(CDriveOverviewEntry entry)
    {
        return entry.Category switch
        {
            "第三方已安装应用" => 0,
            "用户数据目录" => 1,
            "应用缓存/数据" => 2,
            "未知大目录" => 3,
            "系统共享组件" => 4,
            "系统核心" => 5,
            _ => 9
        };
    }

    private static bool IsSharedRuntimeName(string name)
    {
        return SharedRuntimeNames.Any(value => name.Contains(value, StringComparison.OrdinalIgnoreCase))
            || name.Contains("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Windows", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".NET", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveInstallLocation(string? installLocation, string? displayIcon)
    {
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            var normalizedLocation = NormalizePath(installLocation);
            if (Directory.Exists(normalizedLocation))
            {
                return normalizedLocation;
            }
        }

        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return string.Empty;
        }

        var clean = displayIcon.Trim().Trim('"');
        var commaIndex = clean.IndexOf(',');
        if (commaIndex > 0)
        {
            clean = clean[..commaIndex];
        }

        if (!File.Exists(clean))
        {
            return string.Empty;
        }

        return NormalizePath(Path.GetDirectoryName(clean) ?? string.Empty);
    }

    private static MigrationAdapter? ResolveMigrationAdapter(string path, InstalledAppRecord? installedApp)
    {
        var normalizedPath = NormalizePath(path);
        var matchSource = string.Join(
            " ",
            new[]
            {
                normalizedPath,
                Path.GetFileName(normalizedPath),
                installedApp?.DisplayName ?? string.Empty,
                installedApp?.Publisher ?? string.Empty
            });

        return MigrationAdapters.FirstOrDefault(adapter =>
            adapter.AllowAutomaticMigration
            && !adapter.DriverBound
            && adapter.MatchTokens.Any(token => matchSource.Contains(token, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsDriverBoundPath(string normalizedPath, InstalledAppRecord? installedApp)
    {
        var matchSource = string.Join(
            " ",
            new[]
            {
                normalizedPath,
                Path.GetFileName(normalizedPath),
                installedApp?.DisplayName ?? string.Empty,
                installedApp?.Publisher ?? string.Empty
            });

        return DriverBoundPublisherTokens.Any(token => matchSource.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildAppIdentityKey(string name, string path)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name;
        var normalizedName = new string(baseName
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch))
            .ToArray());

        if (!string.IsNullOrWhiteSpace(normalizedName))
        {
            return normalizedName;
        }

        return Path.GetFileNameWithoutExtension(path).Trim().ToLowerInvariant();
    }

    private static string BuildWhitelistHint(string ruleSource, CleanupCandidateKind candidateKind)
    {
        if (candidateKind == CleanupCandidateKind.DuplicateFile)
        {
            return "这是重复副本。如果你经常手动比对这个文件夹，可以先加入白名单，避免重复提醒。";
        }

        if (IsFrequentlyUsedCache(ruleSource))
        {
            return "这是常用软件缓存。经常使用的软件建议先确认再清，也可以加入白名单暂时不再显示。";
        }

        return string.Empty;
    }

    private static IReadOnlyCollection<string> NormalizeWhitelistPaths(IEnumerable<string>? paths)
    {
        return (paths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsWhitelisted(string path, IReadOnlyCollection<string> whitelistPaths)
    {
        if (whitelistPaths.Count == 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = NormalizePath(path);
        return whitelistPaths.Any(whitelist =>
            normalizedPath.Equals(whitelist, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(whitelist + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(whitelist + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private CleanupItem? CreateDirectoryItem(
        string category,
        string name,
        string typeDescription,
        string path,
        CleanupTargetKind targetKind,
        string note,
        string impactText,
        CleanupImpactSeverity impactSeverity,
        bool safeAuto,
        bool recommended,
        string? driveName = null,
        string ruleSource = "",
        CleanupCandidateKind candidateKind = CleanupCandidateKind.SafeJunk,
        bool isApplicationRelated = false)
    {
        var sizeBytes = FileSystemHelper.GetPathSizeBytes(path);
        if (sizeBytes <= 0 || IsProtectedPath(path))
        {
            return null;
        }

        typeDescription = EnhanceTypeDescription(typeDescription, ruleSource, candidateKind);
        note = EnhanceNote(note, ruleSource, candidateKind);
        impactText = EnhanceImpactText(impactText, name, path, ruleSource, candidateKind, targetKind, impactSeverity, isApplicationRelated);

        return new CleanupItem
        {
            DriveName = driveName ?? GetDriveName(path),
            Category = category,
            Name = name,
            TypeDescription = typeDescription,
            Path = path,
            NormalizedPath = NormalizePath(path),
            RuleSource = string.IsNullOrWhiteSpace(ruleSource) ? category : ruleSource,
            CandidateKind = candidateKind,
            IsApplicationRelated = isApplicationRelated,
            DeepCleanupEligible = isApplicationRelated,
            AppIdentityKey = isApplicationRelated ? BuildAppIdentityKey(name, path) : string.Empty,
            TargetKind = targetKind,
            Note = note,
            ImpactText = impactText,
            ImpactSeverity = impactSeverity,
            SizeBytes = sizeBytes,
            SafeAuto = safeAuto,
            Recommended = recommended,
            WhitelistHintText = BuildWhitelistHint(ruleSource, candidateKind)
        };
    }

    private CleanupItem? CreateFileItem(
        string category,
        string name,
        string typeDescription,
        string path,
        CleanupTargetKind targetKind,
        string note,
        string impactText,
        CleanupImpactSeverity impactSeverity,
        bool safeAuto,
        bool recommended,
        string? driveName = null,
        string ruleSource = "",
        CleanupCandidateKind candidateKind = CleanupCandidateKind.Package,
        bool isApplicationRelated = false)
    {
        if (!File.Exists(path) || IsProtectedPath(path))
        {
            return null;
        }

        var sizeBytes = FileSystemHelper.GetPathSizeBytes(path);
        if (sizeBytes <= 0)
        {
            return null;
        }

        typeDescription = EnhanceTypeDescription(typeDescription, ruleSource, candidateKind);
        note = EnhanceNote(note, ruleSource, candidateKind);
        impactText = EnhanceImpactText(impactText, name, path, ruleSource, candidateKind, targetKind, impactSeverity, isApplicationRelated);

        return new CleanupItem
        {
            DriveName = driveName ?? GetDriveName(path),
            Category = category,
            Name = name,
            TypeDescription = typeDescription,
            Path = path,
            NormalizedPath = NormalizePath(path),
            RuleSource = string.IsNullOrWhiteSpace(ruleSource) ? category : ruleSource,
            CandidateKind = candidateKind,
            IsApplicationRelated = isApplicationRelated,
            DeepCleanupEligible = isApplicationRelated,
            AppIdentityKey = isApplicationRelated ? BuildAppIdentityKey(name, path) : string.Empty,
            TargetKind = targetKind,
            Note = note,
            ImpactText = impactText,
            ImpactSeverity = impactSeverity,
            SizeBytes = sizeBytes,
            SafeAuto = safeAuto,
            Recommended = recommended,
            WhitelistHintText = BuildWhitelistHint(ruleSource, candidateKind)
        };
    }

    private static string EnhanceTypeDescription(string typeDescription, string ruleSource, CleanupCandidateKind candidateKind)
    {
        if (candidateKind == CleanupCandidateKind.DuplicateFile && !typeDescription.Contains("重复", StringComparison.OrdinalIgnoreCase))
        {
            return $"{typeDescription}（重复副本）";
        }

        if (IsFrequentlyUsedCache(ruleSource) && !typeDescription.Contains("常用软件", StringComparison.OrdinalIgnoreCase))
        {
            return $"{typeDescription}（常用软件缓存）";
        }

        return typeDescription;
    }

    private static string EnhanceNote(string note, string ruleSource, CleanupCandidateKind candidateKind)
    {
        if (candidateKind == CleanupCandidateKind.DuplicateFile && !note.Contains("重复", StringComparison.OrdinalIgnoreCase))
        {
            note = "这是重复副本。 " + note;
        }

        if (IsFrequentlyUsedCache(ruleSource) && !note.Contains("常用软件", StringComparison.OrdinalIgnoreCase))
        {
            note = "这是常用软件缓存。删掉后软件本体不会丢，但下次打开时可能要重新缓存或重新下载一部分内容。 " + note;
        }

        return note;
    }

    private static string EnhanceImpactText(
        string impactText,
        string name,
        string path,
        string ruleSource,
        CleanupCandidateKind candidateKind,
        CleanupTargetKind targetKind,
        CleanupImpactSeverity impactSeverity,
        bool isApplicationRelated)
    {
        if (candidateKind == CleanupCandidateKind.DuplicateFile && !impactText.Contains("重复", StringComparison.OrdinalIgnoreCase))
        {
            impactText += " 这是重复副本，不会删掉被保留的主副本。";
        }

        if (IsFrequentlyUsedCache(ruleSource) && !impactText.Contains("常用软件", StringComparison.OrdinalIgnoreCase))
        {
            impactText += " 这是常用软件缓存，清理后软件本体仍能用，但第一次再次打开时可能需要重新缓存。";
        }

        var decisionHint = BuildDecisionHint(name, path, ruleSource, candidateKind, targetKind, impactSeverity, isApplicationRelated);
        if (!string.IsNullOrWhiteSpace(decisionHint))
        {
            impactText += " " + decisionHint;
        }

        return impactText;
    }

    private static string BuildDecisionHint(
        string name,
        string path,
        string ruleSource,
        CleanupCandidateKind candidateKind,
        CleanupTargetKind targetKind,
        CleanupImpactSeverity impactSeverity,
        bool isApplicationRelated)
    {
        var normalizedPath = NormalizePath(path);

        if (string.Equals(ruleSource, "Known:WindowsOld", StringComparison.OrdinalIgnoreCase))
        {
            return "适合现在删：你确定不再需要回退到升级前的 Windows。先别删：你最近刚升级系统，还想保留“回到旧版本”的余地。";
        }

        if (string.Equals(ruleSource, "Aggregate:RecycleBin", StringComparison.OrdinalIgnoreCase)
            || targetKind == CleanupTargetKind.RecycleBin)
        {
            return "适合现在删：你已经检查过回收站，没有要还原的误删文件。先别删：你还没确认里面有没有照片、文档或安装包。";
        }

        if (string.Equals(ruleSource, "Known:MemoryDmp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ruleSource, "Known:Minidump", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ruleSource, "Known:CrashDumps", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ruleSource, "Known:WerQueue", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ruleSource, "Known:WerArchive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ruleSource, "Known:Panther", StringComparison.OrdinalIgnoreCase))
        {
            return "适合现在删：你近期不准备排查蓝屏、闪退、升级失败或系统报错。先别删：你正要把这些记录发给售后、技术支持或自己排障。";
        }

        if (string.Equals(ruleSource, "Known:SoftwareDistribution", StringComparison.OrdinalIgnoreCase))
        {
            return "适合现在删：你只是想腾空间，而且当前 Windows 更新已经装完。先别删：你正在处理更新失败、补丁异常或想保留已下载更新做排查。";
        }

        if (string.Equals(ruleSource, "Known:NvidiaDownloader", StringComparison.OrdinalIgnoreCase))
        {
            return "适合现在删：驱动已经装好，你也不打算靠这份缓存离线重装。先别删：网络不方便，或者你想保留驱动包备用。";
        }

        if (ruleSource.StartsWith("Known:CloudMusic", StringComparison.OrdinalIgnoreCase))
        {
            return ruleSource.EndsWith("Update", StringComparison.OrdinalIgnoreCase)
                ? "适合现在删：升级已经完成，你不需要保留旧安装包。先别删：你还想留这份更新包备用，或者当前网易云正在更新。"
                : "适合现在删：你只想腾空间，不在乎离线歌曲或封面重新缓存。先别删：你经常离线听歌，或者网易云现在还在后台播放。";
        }

        if (ruleSource.StartsWith("Known:QQLive", StringComparison.OrdinalIgnoreCase))
        {
            return ruleSource.EndsWith("Log", StringComparison.OrdinalIgnoreCase)
                ? "适合现在删：你只是清空间，不需要保留腾讯视频故障日志。先别删：你正准备排查播放异常、闪退或下载失败。"
                : "适合现在删：你不需要保留离线视频、封面或网页缓存。先别删：你还想继续离线观看，或者腾讯视频现在还在后台运行。";
        }

        if (ruleSource.StartsWith("Known:Xwechat", StringComparison.OrdinalIgnoreCase))
        {
            return ruleSource.EndsWith("Log", StringComparison.OrdinalIgnoreCase)
                ? "适合现在删：你不打算再看微信旧版日志。先别删：你正要排查登录、闪退或消息异常。"
                : ruleSource.EndsWith("Update", StringComparison.OrdinalIgnoreCase)
                    ? "适合现在删：旧更新包已经没用了。先别删：你还想保留升级包备用，或者微信仍在后台占用。"
                    : "适合现在删：你只是清缓存。先别删：你还在用对应功能，或者微信此刻还在后台运行。";
        }

        if (ruleSource.StartsWith("Known:Code", StringComparison.OrdinalIgnoreCase)
            || ruleSource.StartsWith("Known:Cursor", StringComparison.OrdinalIgnoreCase))
        {
            return "适合现在删：你只是想清编辑器缓存，能接受第一次再次打开稍慢。先别删：你现在正开着编辑器工作，或者想保留当前缓存来减少重建时间。";
        }

        if (string.Equals(ruleSource, "Known:EdgeCache", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ruleSource, "Known:ChromeCache", StringComparison.OrdinalIgnoreCase))
        {
            return "适合现在删：你只是清网页缓存。先别删：你网络不方便，还想保留已经缓存的网页资源、视频片段或图片。";
        }

        return candidateKind switch
        {
            CleanupCandidateKind.Package => "适合现在删：安装已经完成，别处还有备份。先别删：你后面还要靠这份安装包、压缩包或镜像重装软件、装给别人，或做离线备用。",
            CleanupCandidateKind.LargeFile => "适合现在删：你确认这只是旧视频、旧镜像、旧备份、旧素材或不再需要的单个大文件。先别删：它仍是你在用的工程、课程资料、虚拟机、游戏资源或唯一备份。",
            CleanupCandidateKind.LargeDirectory => "适合现在删：你确认整个文件夹都是不要的旧资料、旧安装包、旧项目或便携工具。先别删：里面还混着正在用的文档、照片、代码、素材或程序。",
            CleanupCandidateKind.DuplicateFile => "适合现在删：你只想保留一份主副本。先别删：你是故意把多个版本分开放，或者还没确认它们是不是完全一样。",
            CleanupCandidateKind.AppCache => "适合现在删：你只想腾空间，能接受缓存重新生成。先别删：你还要用离线内容，或者对应软件现在正在运行，删完可能马上又被重建。",
            CleanupCandidateKind.AppResidue when isApplicationRelated => $"适合现在删：你已经确定“{name}”不用了，想把旧目录和强关联残留一起清掉。先别删：你还会直接从这个目录启动它，或者还没确认是否存在正式卸载入口。",
            CleanupCandidateKind.SafeJunk when impactSeverity == CleanupImpactSeverity.High => "这是高风险垃圾项。适合现在删：你明确知道它是什么，而且确认不再需要对应回退或排障能力。先别删：你现在还说不清它的用途。",
            CleanupCandidateKind.SafeJunk when impactSeverity == CleanupImpactSeverity.Medium => "适合现在删：你只是清空间，不需要保留历史日志、历史缓存或排障记录。先别删：你近期要查问题，或者想保留这份历史记录。",
            _ when targetKind == CleanupTargetKind.FilePermanent && normalizedPath.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase) =>
                "这是直接永久删除，不会先进回收站。先确认它不是你之后还要用的安装包、备份、项目文件或资料。",
            _ when isApplicationRelated =>
                "这和某个应用有关。适合现在删：你已经决定不再使用它。先别删：你还希望原来的快捷方式、更新流程或离线数据继续可用。",
            _ when impactSeverity == CleanupImpactSeverity.High =>
                "这是高风险项；如果你现在还不能明确说出它是干什么的，先别删。",
            _ => string.Empty
        };
    }

    private static bool IsFrequentlyUsedCache(string ruleSource)
    {
        if (string.IsNullOrWhiteSpace(ruleSource))
        {
            return false;
        }

        return ruleSource.StartsWith("Known:CloudMusic", StringComparison.OrdinalIgnoreCase)
            || ruleSource.StartsWith("Known:QQLive", StringComparison.OrdinalIgnoreCase)
            || ruleSource.StartsWith("Known:Xwechat", StringComparison.OrdinalIgnoreCase)
            || ruleSource.StartsWith("Known:Code", StringComparison.OrdinalIgnoreCase)
            || ruleSource.StartsWith("Known:Cursor", StringComparison.OrdinalIgnoreCase)
            || ruleSource.StartsWith("Known:EdgeCache", StringComparison.OrdinalIgnoreCase)
            || ruleSource.StartsWith("Known:ChromeCache", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyCollection<DriveInfo> SelectDrivesByScope(IReadOnlyCollection<DriveInfo> allFixedDrives, ScanDriveScope scope)
    {
        var systemDriveName = GetSystemDriveName();
        return scope switch
        {
            ScanDriveScope.SystemDriveOnly => allFixedDrives
                .Where(drive => GetDriveName(drive.Name).Equals(systemDriveName, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            ScanDriveScope.OtherFixedDrives => allFixedDrives
                .Where(drive => !GetDriveName(drive.Name).Equals(systemDriveName, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            _ => allFixedDrives.ToList()
        };
    }

    private static bool IncludesSystemDrive(IReadOnlyCollection<string> driveNames)
    {
        var systemDriveName = GetSystemDriveName();
        return driveNames.Contains(systemDriveName, StringComparer.OrdinalIgnoreCase);
    }

    private static string BuildPhaseLabel(
        ScanDriveScope scope,
        IReadOnlyCollection<string> loadedDrives,
        IReadOnlyCollection<string> pendingDrives,
        SnapshotBuildProfile profile)
    {
        if (profile == SnapshotBuildProfile.QuickCFirst)
        {
            return $"已先显示 {GetSystemDriveName()}盘候选和第一批 C盘建议，正在补全完整 {GetSystemDriveName()}盘总览、长期未用软件和其它盘";
        }

        if (profile == SnapshotBuildProfile.QuickCleanupOnly)
        {
            return $"已先显示 {GetSystemDriveName()}盘候选，正在补全第一批 C盘建议和其它盘";
        }

        return scope switch
        {
            ScanDriveScope.SystemDriveOnly when pendingDrives.Count > 0 => $"已先显示 {GetSystemDriveName()}盘，正在补全其它盘",
            ScanDriveScope.SystemDriveOnly => $"{GetSystemDriveName()}盘扫描完成",
            ScanDriveScope.OtherFixedDrives when loadedDrives.Count > 0 => $"已加载 {string.Join(" / ", loadedDrives)} 盘结果",
            ScanDriveScope.OtherFixedDrives => "未检测到其它固定盘",
            _ when loadedDrives.Count > 0 => "全部固定盘扫描完成",
            _ => "未检测到固定磁盘"
        };
    }

    private static InstalledAppSnapshot FilterInstalledAppSnapshotByDrives(InstalledAppSnapshot installedApps, IReadOnlyCollection<string> selectedDriveNames)
    {
        if (selectedDriveNames.Count == 0)
        {
            return new InstalledAppSnapshot(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                []);
        }

        var selectedLocations = installedApps.InstallLocations
            .Where(path => IsPathOnSelectedDrive(path, selectedDriveNames))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedApplications = installedApps.Applications
            .Where(app => IsPathOnSelectedDrive(app.InstallLocation, selectedDriveNames))
            .ToList();
        var displayNames = selectedApplications
            .Select(app => app.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new InstalledAppSnapshot(selectedLocations, displayNames, selectedApplications);
    }

    private static IReadOnlyList<string> GetScopedUserFolderRoots(IReadOnlyCollection<string> selectedDriveNames)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
        };

        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) && IsPathOnSelectedDrive(path, selectedDriveNames))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPathOnSelectedDrive(string? path, IReadOnlyCollection<string> selectedDriveNames)
    {
        if (selectedDriveNames.Count == 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var driveName = GetDriveName(path);
        return !string.IsNullOrWhiteSpace(driveName)
            && selectedDriveNames.Contains(driveName, StringComparer.OrdinalIgnoreCase);
    }

    private static string GetSystemDriveName()
    {
        return GetDriveName(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
    }

    private static IReadOnlyList<DriveInfo> GetFixedDrives()
    {
        return DriveInfo.GetDrives()
            .Where(IsReadyFixedDrive)
            .OrderBy(drive => GetDrivePriority(GetDriveName(drive.Name)))
            .ThenBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsReadyFixedDrive(DriveInfo drive)
    {
        try
        {
            return drive.IsReady && drive.DriveType == DriveType.Fixed;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDriveName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var root = Path.GetPathRoot(path);
        return string.IsNullOrWhiteSpace(root)
            ? string.Empty
            : root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':').ToUpperInvariant();
    }

    private static int GetDrivePriority(string? driveName)
    {
        if (string.IsNullOrWhiteSpace(driveName))
        {
            return 99;
        }

        if (driveName.Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (driveName.Equals("全部盘", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return char.ToUpperInvariant(driveName[0]) switch
        {
            >= 'A' and <= 'Z' => 10 + driveName[0] - 'A',
            _ => 98
        };
    }

    private static string GetInstallerTypeDescription(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".exe" => "安装程序",
            ".msi" => "安装包",
            ".msix" => "应用安装包",
            ".zip" or ".7z" or ".rar" => "压缩包",
            ".iso" => "镜像文件",
            ".cab" => "驱动/系统包",
            _ => "文件"
        };
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
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private sealed class BroadScanBudget
    {
        public int ScannedDirectories { get; set; }
        public int ScannedFiles { get; set; }
    }

    private sealed class ScanRunState(string logPath)
    {
        public string LogPath { get; } = logPath;
        public List<ScanWarning> Warnings { get; } = [];
        public HashSet<string> WarningKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private enum SnapshotBuildProfile
    {
        Full,
        QuickCleanupOnly,
        QuickCFirst
    }

    private sealed record InstalledAppSnapshot(
        HashSet<string> InstallLocations,
        HashSet<string> DisplayNames,
        List<InstalledAppRecord> Applications);

    private sealed record InstalledAppRecord(
        string DisplayName,
        string InstallLocation,
        string Publisher,
        string DisplayIcon,
        string UninstallString,
        bool IsPortable,
        string AppIdentityKey,
        IReadOnlyList<string> ExecutableNames);
}
