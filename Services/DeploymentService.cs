using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class DeploymentService
{
    public const string InstallFolderName = DeploymentDefaults.InstallFolderName;
    public const string InstalledExecutableName = DeploymentDefaults.InstalledExecutableName;
    public const string DesktopShortcutName = DeploymentDefaults.DesktopShortcutName;
    public const string InstallFolderShortcutName = DeploymentDefaults.InstallFolderShortcutName;
    public const string ElevatedLauncherTaskName = DeploymentDefaults.ElevatedLauncherTaskName;

    public string NormalizeElevatedLauncherTaskName(string? taskName)
    {
        return string.IsNullOrWhiteSpace(taskName)
            ? ElevatedLauncherTaskName
            : taskName.Trim();
    }

    public IReadOnlyList<DeploymentTarget> GetDeploymentTargets()
    {
        var systemDrive = (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .TrimEnd(':')
            .ToUpperInvariant();

        return DriveInfo.GetDrives()
            .Where(IsReadyFixedDrive)
            .Select(drive => new DeploymentTarget
            {
                DriveName = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                TargetPath = Path.Combine(drive.RootDirectory.FullName, InstallFolderName),
                IsRecommended = false,
                IsOnSystemDrive = drive.Name.StartsWith(systemDrive, StringComparison.OrdinalIgnoreCase),
                NeedsElevation = IsProtectedPath(Path.Combine(drive.RootDirectory.FullName, InstallFolderName)),
                FreeBytes = drive.AvailableFreeSpace,
                TotalBytes = drive.TotalSize
            })
            .OrderBy(target => target.IsOnSystemDrive ? 1 : 0)
            .ThenByDescending(target => target.FreeBytes)
            .ThenByDescending(target => target.TotalBytes)
            .ThenBy(target => target.DriveName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .MarkRecommended();
    }

    public DeploymentTarget? GetRecommendedTarget()
    {
        return GetDeploymentTargets().FirstOrDefault(target => target.IsRecommended)
            ?? GetDeploymentTargets().FirstOrDefault();
    }

    public bool ShouldSuggestMigration(PortableContext context)
    {
        var root = Path.GetPathRoot(context.AppRoot);
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        return context.AppRoot.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase)
            || IsProtectedPath(context.AppRoot);
    }

    public string DeploySelf(
        PortableContext context,
        string targetDirectory,
        bool createDesktopShortcut,
        bool migrateData,
        IProgress<DeploymentProgressUpdate>? progress = null)
    {
        progress?.Report(new DeploymentProgressUpdate
        {
            Percent = 8,
            Message = "正在准备安装目录..."
        });
        Directory.CreateDirectory(targetDirectory);

        var installedExecutablePath = Path.Combine(targetDirectory, InstalledExecutableName);
        progress?.Report(new DeploymentProgressUpdate
        {
            Percent = 42,
            Message = "正在复制程序文件..."
        });
        File.Copy(context.EntryExecutablePath, installedExecutablePath, overwrite: true);

        if (migrateData && context.UsingPortableDataRoot && Directory.Exists(context.DataRoot))
        {
            var destinationDataRoot = Path.Combine(targetDirectory, "data");
            progress?.Report(new DeploymentProgressUpdate
            {
                Percent = 68,
                Message = "正在迁移设置和日志..."
            });
            FileSystemHelper.CopyDirectoryContents(context.DataRoot, destinationDataRoot);
        }

        if (createDesktopShortcut)
        {
            progress?.Report(new DeploymentProgressUpdate
            {
                Percent = 84,
                Message = "正在创建桌面快捷方式..."
            });
            EnsureDesktopShortcut(installedExecutablePath, targetDirectory, useElevatedTaskShortcut: false);
        }

        progress?.Report(new DeploymentProgressUpdate
        {
            Percent = 93,
            Message = "正在同步安装目录入口..."
        });
        EnsureInstallFolderShortcut(installedExecutablePath, targetDirectory, useElevatedTaskShortcut: false);

        progress?.Report(new DeploymentProgressUpdate
        {
            Percent = 100,
            Message = "安装完成，正在准备启动..."
        });

        return installedExecutablePath;
    }

    public void EnsureElevatedLauncher(string executablePath, string workingDirectory, bool createDesktopShortcut, string? taskName = null)
    {
        var effectiveTaskName = NormalizeElevatedLauncherTaskName(taskName);
        var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        var taskCommand = $"\"{executablePath}\" --elevated --skip-migration-prompt";
        ShellHelper.RunProcessCapture(
            "schtasks.exe",
            [
                "/Create",
                "/TN", effectiveTaskName,
                "/TR", taskCommand,
                "/SC", "ONCE",
                "/ST", "00:00",
                "/RL", "HIGHEST",
                "/RU", currentUser,
                "/F"
            ],
            ignoreErrors: true);

        if (createDesktopShortcut)
        {
            EnsureDesktopShortcut(executablePath, workingDirectory, useElevatedTaskShortcut: true, effectiveTaskName);
        }

        EnsureInstallFolderShortcut(executablePath, workingDirectory, useElevatedTaskShortcut: true, effectiveTaskName);
    }

    public void EnsureDesktopShortcut(string executablePath, string workingDirectory, bool useElevatedTaskShortcut, string? taskName = null)
    {
        var effectiveTaskName = NormalizeElevatedLauncherTaskName(taskName);
        var shortcutPath = GetDesktopShortcutPath();
        if (useElevatedTaskShortcut)
        {
            ShellHelper.CreateShortcut(
                shortcutPath,
                Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                workingDirectory,
                "便携式磁盘清理器（高权限启动）",
                arguments: $"/Run /TN \"{effectiveTaskName}\"",
                iconLocation: executablePath);
            return;
        }

        ShellHelper.CreateShortcut(shortcutPath, executablePath, workingDirectory, "便携式磁盘清理器");
    }

    public void EnsureInstallFolderShortcut(string executablePath, string workingDirectory, bool useElevatedTaskShortcut, string? taskName = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return;
        }

        Directory.CreateDirectory(workingDirectory);
        var effectiveTaskName = NormalizeElevatedLauncherTaskName(taskName);
        var shortcutPath = GetInstallFolderShortcutPath(workingDirectory);
        if (useElevatedTaskShortcut)
        {
            ShellHelper.CreateShortcut(
                shortcutPath,
                Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                workingDirectory,
                "磁盘清理器（安装目录高权限入口）",
                arguments: $"/Run /TN \"{effectiveTaskName}\"",
                iconLocation: executablePath);
            return;
        }

        ShellHelper.CreateShortcut(shortcutPath, executablePath, workingDirectory, "磁盘清理器（安装目录入口）");
    }

    public DeploymentSelfRepairResult SelfRepairLaunchArtifacts(
        PortableContext context,
        string taskName,
        bool createDesktopShortcut,
        bool canRepairElevatedTask)
    {
        var effectiveTaskName = NormalizeElevatedLauncherTaskName(taskName);
        var canManageCurrentExecutable = CanManageLaunchArtifacts(context.EntryExecutablePath);
        var managedExecutableNote = canManageCurrentExecutable
            ? "当前运行的是安装目录内的正式 EXE，可以安全修复桌面入口、安装目录入口和高权限任务。"
            : $"当前运行文件名不是“{InstalledExecutableName}”，为避免把桌面或计划任务误改到临时构建，本次只做检查，不自动改写入口。";

        var desktopShortcutPath = GetDesktopShortcutPath();
        var installFolderShortcutPath = GetInstallFolderShortcutPath(context.AppRoot);

        var elevatedTaskAvailableBeforeRepair = HasElevatedLauncherTask(effectiveTaskName);
        var elevatedTaskAlignedBeforeRepair = EvaluateElevatedTask(effectiveTaskName, context.EntryExecutablePath, out var elevatedTaskDetailBeforeRepair);

        var desktopShortcutAlignedBeforeRepair = !createDesktopShortcut;
        var desktopShortcutDetailBeforeRepair = createDesktopShortcut
            ? string.Empty
            : "当前未要求创建桌面快捷方式。";
        if (createDesktopShortcut)
        {
            desktopShortcutAlignedBeforeRepair = EvaluateShortcut(
                desktopShortcutPath,
                "桌面快捷方式",
                effectiveTaskName,
                context.EntryExecutablePath,
                out desktopShortcutDetailBeforeRepair);
        }

        var installFolderShortcutAlignedBeforeRepair = false;
        var installFolderShortcutDetailBeforeRepair = managedExecutableNote;
        if (canManageCurrentExecutable)
        {
            installFolderShortcutAlignedBeforeRepair = EvaluateShortcut(
                installFolderShortcutPath,
                "安装目录入口",
                effectiveTaskName,
                context.EntryExecutablePath,
                out installFolderShortcutDetailBeforeRepair);
        }

        var elevatedTaskRepaired = false;
        if (canManageCurrentExecutable && canRepairElevatedTask && !elevatedTaskAlignedBeforeRepair)
        {
            try
            {
                EnsureElevatedLauncher(context.EntryExecutablePath, context.AppRoot, createDesktopShortcut, effectiveTaskName);
                elevatedTaskRepaired = true;
            }
            catch (Exception ex)
            {
                elevatedTaskDetailBeforeRepair = $"高权限任务自修复失败：{ex.Message}";
            }
        }

        var elevatedTaskAvailableAfterRepair = HasElevatedLauncherTask(effectiveTaskName);
        var elevatedTaskAlignedAfterRepair = EvaluateElevatedTask(effectiveTaskName, context.EntryExecutablePath, out var elevatedTaskDetailAfterRepair);
        var useElevatedShortcuts = elevatedTaskAlignedAfterRepair;

        var desktopShortcutRepaired = false;
        if (canManageCurrentExecutable && createDesktopShortcut && !desktopShortcutAlignedBeforeRepair)
        {
            try
            {
                EnsureDesktopShortcut(context.EntryExecutablePath, context.AppRoot, useElevatedTaskShortcut: useElevatedShortcuts, effectiveTaskName);
                desktopShortcutRepaired = true;
            }
            catch (Exception ex)
            {
                desktopShortcutDetailBeforeRepair = $"桌面快捷方式自修复失败：{ex.Message}";
            }
        }

        var desktopShortcutAlignedAfterRepair = !createDesktopShortcut;
        var desktopShortcutDetailAfterRepair = createDesktopShortcut
            ? string.Empty
            : "当前未要求创建桌面快捷方式。";
        if (createDesktopShortcut)
        {
            desktopShortcutAlignedAfterRepair = EvaluateShortcut(
                desktopShortcutPath,
                "桌面快捷方式",
                effectiveTaskName,
                context.EntryExecutablePath,
                out desktopShortcutDetailAfterRepair);
        }

        var installFolderShortcutRepaired = false;
        if (canManageCurrentExecutable && !installFolderShortcutAlignedBeforeRepair)
        {
            try
            {
                EnsureInstallFolderShortcut(context.EntryExecutablePath, context.AppRoot, useElevatedTaskShortcut: useElevatedShortcuts, effectiveTaskName);
                installFolderShortcutRepaired = true;
            }
            catch (Exception ex)
            {
                installFolderShortcutDetailBeforeRepair = $"安装目录入口自修复失败：{ex.Message}";
            }
        }

        var installFolderShortcutAlignedAfterRepair = false;
        var installFolderShortcutDetailAfterRepair = managedExecutableNote;
        if (canManageCurrentExecutable)
        {
            installFolderShortcutAlignedAfterRepair = EvaluateShortcut(
                installFolderShortcutPath,
                "安装目录入口",
                effectiveTaskName,
                context.EntryExecutablePath,
                out installFolderShortcutDetailAfterRepair);
        }

        return new DeploymentSelfRepairResult
        {
            TaskName = effectiveTaskName,
            ManagedCurrentExecutable = canManageCurrentExecutable,
            ManagedExecutableNote = managedExecutableNote,
            DesktopShortcutManaged = createDesktopShortcut,
            ElevatedTaskAvailableBeforeRepair = elevatedTaskAvailableBeforeRepair,
            ElevatedTaskAlignedBeforeRepair = elevatedTaskAlignedBeforeRepair,
            ElevatedTaskAvailableAfterRepair = elevatedTaskAvailableAfterRepair,
            ElevatedTaskAlignedAfterRepair = elevatedTaskAlignedAfterRepair,
            ElevatedTaskRepaired = elevatedTaskRepaired,
            DesktopShortcutAlignedBeforeRepair = desktopShortcutAlignedBeforeRepair,
            DesktopShortcutAlignedAfterRepair = desktopShortcutAlignedAfterRepair,
            DesktopShortcutRepaired = desktopShortcutRepaired,
            InstallFolderShortcutAlignedBeforeRepair = installFolderShortcutAlignedBeforeRepair,
            InstallFolderShortcutAlignedAfterRepair = installFolderShortcutAlignedAfterRepair,
            InstallFolderShortcutRepaired = installFolderShortcutRepaired,
            ElevatedTaskDetail = elevatedTaskAlignedAfterRepair || elevatedTaskRepaired
                ? elevatedTaskDetailAfterRepair
                : elevatedTaskDetailBeforeRepair,
            DesktopShortcutDetail = desktopShortcutAlignedAfterRepair || desktopShortcutRepaired
                ? desktopShortcutDetailAfterRepair
                : desktopShortcutDetailBeforeRepair,
            InstallFolderShortcutDetail = installFolderShortcutAlignedAfterRepair || installFolderShortcutRepaired
                ? installFolderShortcutDetailAfterRepair
                : installFolderShortcutDetailBeforeRepair
        };
    }

    public bool HasUsableElevatedLauncher(string taskName, string currentExecutablePath)
    {
        return !string.IsNullOrWhiteSpace(taskName)
            && EvaluateElevatedTask(NormalizeElevatedLauncherTaskName(taskName), currentExecutablePath, out _);
    }

    public bool HasElevatedLauncherTask(string taskName)
    {
        var effectiveTaskName = NormalizeElevatedLauncherTaskName(taskName);
        return !string.IsNullOrWhiteSpace(effectiveTaskName)
            && ShellHelper.TryRunProcess("schtasks.exe", ["/Query", "/TN", effectiveTaskName]);
    }

    public bool TryRunElevatedLauncher(string taskName)
    {
        var effectiveTaskName = NormalizeElevatedLauncherTaskName(taskName);
        if (string.IsNullOrWhiteSpace(effectiveTaskName))
        {
            return false;
        }

        return ShellHelper.TryRunProcess("schtasks.exe", ["/Run", "/TN", effectiveTaskName]);
    }

    public LaunchDiagnosticsInfo GetLaunchDiagnostics(
        PortableContext context,
        string elevatedTaskName,
        bool launchedFromElevatedTask,
        bool launchedFromScheduledTask,
        DeploymentSelfRepairResult? repairResult = null,
        string? readOnlyReason = null)
    {
        var executablePath = context.EntryExecutablePath;
        var timestamp = GetExecutableTimestamp(executablePath);
        var effectiveTaskName = NormalizeElevatedLauncherTaskName(elevatedTaskName);
        var launchSource = launchedFromScheduledTask
            ? "计划任务静默启动"
            : launchedFromElevatedTask ? "高权限启动任务" : "直接或桌面启动";
        var dataMode = context.UsingPortableDataRoot ? "便携数据" : "本地数据";

        var taskAligned = repairResult?.ElevatedTaskAlignedAfterRepair
            ?? EvaluateElevatedTask(effectiveTaskName, executablePath, out _);
        var desktopAligned = repairResult?.DesktopShortcutAlignedAfterRepair
            ?? EvaluateShortcut(GetDesktopShortcutPath(), "桌面快捷方式", effectiveTaskName, executablePath, out _);
        var installFolderAligned = repairResult?.InstallFolderShortcutAlignedAfterRepair
            ?? EvaluateShortcut(GetInstallFolderShortcutPath(context.AppRoot), "安装目录入口", effectiveTaskName, executablePath, out _);

        var desktopDetail = repairResult?.DesktopShortcutDetail ?? "未检测桌面快捷方式。";
        var taskDetail = repairResult?.ElevatedTaskDetail ?? "未检测高权限任务。";
        var installFolderDetail = repairResult?.InstallFolderShortcutDetail ?? "未检测安装目录入口。";

        var chainAligned = repairResult?.LaunchChainAligned ?? (taskAligned && desktopAligned && installFolderAligned);
        var chainState = repairResult is not null && !repairResult.ManagedCurrentExecutable
            ? "临时运行"
            : repairResult is not null && repairResult.AnyRepairApplied
                ? (chainAligned ? "已自修复" : "部分修复")
                : chainAligned ? "一致" : "需检查";
        var modeSuffix = string.IsNullOrWhiteSpace(readOnlyReason) ? string.Empty : " · 当前：只读";
        var displayText = $"运行版本：{timestamp:yyyy-MM-dd HH:mm} · 启动来源：{launchSource} · 数据：{dataMode} · 部署链：{chainState}{modeSuffix}";

        var toolTipLines = new List<string>
        {
            $"当前 EXE：{executablePath}",
            $"版本时间戳：{timestamp:yyyy-MM-dd HH:mm:ss}",
            $"启动来源：{launchSource}",
            $"数据目录：{context.DataRoot}",
            $"数据模式：{dataMode}",
            $"安装目录入口：{installFolderDetail}",
            $"桌面入口：{desktopDetail}",
            $"高权限任务：{taskDetail}"
        };

        if (repairResult is not null)
        {
            toolTipLines.Add($"启动链自检：{repairResult.ManagedExecutableNote}");
            if (repairResult.AnyRepairApplied)
            {
                toolTipLines.Add("本次启动已自动修复："
                    + string.Join("、", BuildRepairActionList(repairResult)));
            }
        }

        if (!string.IsNullOrWhiteSpace(readOnlyReason))
        {
            toolTipLines.Add($"只读原因：{readOnlyReason}");
        }

        return new LaunchDiagnosticsInfo
        {
            DisplayText = displayText,
            ToolTipText = string.Join("\r\n", toolTipLines)
        };
    }

    public static bool IsSystemDrivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        return path.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsProtectedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            if (!fullPath.StartsWith(systemRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var protectedRoots = new[]
            {
                Path.Combine(systemRoot, "Windows"),
                Path.Combine(systemRoot, "Program Files"),
                Path.Combine(systemRoot, "Program Files (x86)"),
                Path.Combine(systemRoot, "ProgramData")
            };

            return protectedRoots.Any(root => fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
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

    private static DateTime GetExecutableTimestamp(string executablePath)
    {
        try
        {
            return File.GetLastWriteTime(executablePath);
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private static bool CanManageLaunchArtifacts(string executablePath)
    {
        return string.Equals(
            Path.GetFileName(executablePath),
            InstalledExecutableName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDesktopShortcutPath()
    {
        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return Path.Combine(desktopDirectory, DesktopShortcutName);
    }

    private static string GetInstallFolderShortcutPath(string workingDirectory)
    {
        return Path.Combine(workingDirectory, InstallFolderShortcutName);
    }

    private static IEnumerable<string> BuildRepairActionList(DeploymentSelfRepairResult repairResult)
    {
        if (repairResult.ElevatedTaskRepaired)
        {
            yield return "高权限任务";
        }

        if (repairResult.DesktopShortcutRepaired)
        {
            yield return "桌面快捷方式";
        }

        if (repairResult.InstallFolderShortcutRepaired)
        {
            yield return "安装目录入口";
        }
    }

    private static bool EvaluateShortcut(string shortcutPath, string shortcutLabel, string elevatedTaskName, string currentExecutablePath, out string detail)
    {
        detail = $"未找到{shortcutLabel}。";
        if (!File.Exists(shortcutPath))
        {
            return false;
        }

        if (!ShellHelper.TryReadShortcut(shortcutPath, out var targetPath, out var arguments, out _))
        {
            detail = $"{shortcutLabel}存在，但读取失败：{shortcutPath}";
            return false;
        }

        if (targetPath.EndsWith("schtasks.exe", StringComparison.OrdinalIgnoreCase)
            && arguments.Contains($"/TN \"{elevatedTaskName}\"", StringComparison.OrdinalIgnoreCase))
        {
            var taskAligned = EvaluateElevatedTask(elevatedTaskName, currentExecutablePath, out var taskDetail);
            detail = taskAligned
                ? $"{shortcutLabel}已对齐到高权限任务：{shortcutPath}"
                : $"{shortcutLabel}指向了高权限任务，但任务本身没有对齐当前版本。{taskDetail}";
            return taskAligned;
        }

        if (PathsEqual(targetPath, currentExecutablePath))
        {
            detail = $"{shortcutLabel}直接指向当前 EXE：{shortcutPath}";
            return true;
        }

        detail = $"{shortcutLabel}未对齐当前版本。目标：{targetPath} {arguments}".Trim();
        return false;
    }

    private static bool EvaluateElevatedTask(string elevatedTaskName, string currentExecutablePath, out string detail)
    {
        detail = "未读取到高权限任务。";
        if (!TryGetScheduledTaskCommand(elevatedTaskName, out var taskCommand))
        {
            return false;
        }

        var taskExecutable = ExtractExecutablePath(taskCommand);
        if (string.IsNullOrWhiteSpace(taskExecutable))
        {
            detail = $"高权限任务存在，但未解析出可执行文件：{taskCommand}";
            return false;
        }

        var aligned = PathsEqual(taskExecutable, currentExecutablePath);
        detail = aligned
            ? $"高权限任务已对齐当前 EXE：{taskExecutable}"
            : $"高权限任务未对齐当前版本。任务目标：{taskExecutable}";
        return aligned;
    }

    private static bool TryGetScheduledTaskCommand(string taskName, out string taskCommand)
    {
        taskCommand = string.Empty;
        try
        {
            var output = ShellHelper.RunProcessCapture("schtasks.exe", ["/Query", "/TN", taskName, "/V", "/FO", "LIST"], ignoreErrors: true);
            foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("Task To Run:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                taskCommand = line["Task To Run:".Length..].Trim();
                return !string.IsNullOrWhiteSpace(taskCommand);
            }
        }
        catch
        {
        }

        return false;
    }

    private static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            return endQuote > 1 ? trimmed[1..endQuote] : string.Empty;
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed[..firstSpace] : trimmed;
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

internal static class DeploymentTargetListExtensions
{
    public static IReadOnlyList<DeploymentTarget> MarkRecommended(this IReadOnlyList<DeploymentTarget> targets)
    {
        var recommendedIndex = targets
            .Select((target, index) => new { target, index })
            .FirstOrDefault(item => !item.target.IsOnSystemDrive)?.index ?? 0;

        var result = new List<DeploymentTarget>(targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            var item = targets[i];
            result.Add(new DeploymentTarget
            {
                DriveName = item.DriveName,
                TargetPath = item.TargetPath,
                IsRecommended = i == recommendedIndex,
                IsOnSystemDrive = item.IsOnSystemDrive,
                NeedsElevation = item.NeedsElevation,
                FreeBytes = item.FreeBytes,
                TotalBytes = item.TotalBytes
            });
        }

        return result;
    }
}
