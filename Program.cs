using PortableCDriveCleaner.Forms;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;
using PortableCDriveCleaner.Services;
using System.Text.Json;

namespace PortableCDriveCleaner;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.AddMessageFilter(new ScrollInputMessageFilter());

        var options = CommandLineOptions.Parse(args);
        var context = PortableContext.Create();
        var settingsService = new SettingsService(context);
        var scanService = new ScanService(context);
        var occupancyProbeService = new OccupancyProbeService();
        var cleanupService = new CleanupService(context, occupancyProbeService);
        var migrationService = new MigrationService(context);
        var schedulerService = new SchedulerService(context);
        var deploymentService = new DeploymentService();
        var snapshotCacheService = new SnapshotCacheService(context);
        var optimizationAuditService = new OptimizationAuditService();
        var settings = settingsService.Load();
        var operationManager = new OperationManager(settings.MaxConcurrentJobs);
        var initialSnapshot = snapshotCacheService.Load(settings);
        var normalizedTaskName = deploymentService.NormalizeElevatedLauncherTaskName(settings.ElevatedLauncherTaskName);
        if (!string.Equals(normalizedTaskName, settings.ElevatedLauncherTaskName, StringComparison.Ordinal))
        {
            settings.ElevatedLauncherTaskName = normalizedTaskName;
            settingsService.Save(settings);
        }

        if (IsInstallerLaunch(context, options))
        {
            RunInstallerFlow(context, settingsService, deploymentService, settings, options);
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.SelectionFile))
        {
            var items = ShellHelper.ImportSelection(options.SelectionFile!);
            RunCleanupHeadless(context, cleanupService, items, settings.AppName, options.NoPrompt, options.SelectionFile);
            return;
        }

        if (options.AutoSafeOnly)
        {
            var items = scanService.Scan(settings).Where(item => item.SafeAuto).ToList();
            RunCleanupHeadless(context, cleanupService, items, settings.AppName, options.NoPrompt, includeResidueCleanup: false);
            return;
        }

        if (options.Scheduled)
        {
            RunScheduledFlow(context, settings, settingsService, scanService, cleanupService, occupancyProbeService, migrationService, schedulerService, options.NoPrompt);
            return;
        }

        var isAdministrator = ShellHelper.IsAdministrator();
        var deploymentRepair = deploymentService.SelfRepairLaunchArtifacts(
            context,
            settings.ElevatedLauncherTaskName,
            settings.CreateDesktopShortcutOnInstall,
            canRepairElevatedTask: isAdministrator);
        var elevatedLauncherAvailable = deploymentRepair.ElevatedTaskAvailableAfterRepair;
        var elevatedLauncherUsable = deploymentRepair.ElevatedTaskAlignedAfterRepair;

        if (settings.ElevatedLauncherRegistered != elevatedLauncherAvailable)
        {
            settings.ElevatedLauncherRegistered = elevatedLauncherAvailable;
            settingsService.Save(settings);
        }

        var readOnlyMode = options.ReadOnly;
        var readOnlyReason = string.Empty;
        if (!isAdministrator
            && !readOnlyMode
            && !options.ElevatedLaunch
            && elevatedLauncherUsable
            && deploymentService.TryRunElevatedLauncher(settings.ElevatedLauncherTaskName))
        {
            return;
        }

        if ((options.ElevatedLaunch || isAdministrator) && !settings.StartupElevationPromptShown)
        {
            settings.StartupElevationPromptShown = true;
            settingsService.Save(settings);
        }

        if (!isAdministrator && !readOnlyMode && !settings.StartupElevationPromptShown)
        {
            var choice = ShowElevationPrompt(
                title: settings.AppName,
                heading: "建议先授予管理员权限",
                message: "为了完整扫描 C 盘和其它固定盘中的缓存、日志、更新残留、回收站和受保护目录垃圾，并且避免删到一半又被权限弹窗打断，这个清理器建议在启动时一次拿到管理员权限。\r\n\r\n如果你现在不给权限，程序仍然可以打开，但会进入“只读扫描模式”：你可以看结果、看 C 盘总览、看哪些目录是系统保护、哪些是第三方应用，但删除、迁移、安装、深度残留清理和后台任务设置都会被禁用。",
                secondaryButtonText: "继续只读浏览",
                tertiaryButtonText: "先退出");

            settings.StartupElevationPromptShown = true;
            settingsService.Save(settings);

            if (choice == DialogResult.Yes)
            {
                RelaunchCurrentExecutable(context, ["--elevated"]);
                return;
            }

            if (choice == DialogResult.Cancel)
            {
                return;
            }

            readOnlyMode = true;
            readOnlyReason = "你这次选择了“继续只读浏览”，所以本次不会执行删除、迁移、安装或深度残留清理。";
        }
        else if (!isAdministrator && !readOnlyMode && !elevatedLauncherUsable)
        {
            readOnlyMode = true;
            readOnlyReason = elevatedLauncherAvailable
                ? "已检测到高权限启动任务，但它还没有对齐当前版本；为避免把你带回旧版本或继续弹权限，本次先降级为只读模式。下次以管理员权限启动后会自动修复。"
                : "当前还没有可用的高权限启动入口；为避免后面删除到一半再被权限拦住，本次先进入只读模式。";
        }

        if (isAdministrator && !options.SkipMigrationPrompt && deploymentService.ShouldSuggestMigration(context))
        {
            if (RunMigrationFlow(context, settings, deploymentService))
            {
                return;
            }
        }

        if (readOnlyMode && string.IsNullOrWhiteSpace(readOnlyReason))
        {
            readOnlyReason = "当前没有拿到管理员权限，所以本次只开放浏览和分析，不执行真正的删除或迁移。";
        }

        var launchDiagnostics = deploymentService.GetLaunchDiagnostics(
            context,
            settings.ElevatedLauncherTaskName,
            options.ElevatedLaunch,
            options.Scheduled,
            deploymentRepair,
            readOnlyReason);
        Application.Run(new MainForm(
            context,
            settingsService,
            scanService,
            cleanupService,
            occupancyProbeService,
            migrationService,
            schedulerService,
            snapshotCacheService,
            operationManager,
            optimizationAuditService,
            settings,
            initialSnapshot,
            readOnlyMode,
            launchDiagnostics.DisplayText,
            launchDiagnostics.ToolTipText,
            startupViewOverride: options.QaViewMode,
            preserveStartupView: options.QaViewMode.HasValue,
            persistWindowState: !options.QaViewMode.HasValue,
            disableStartupRefresh: options.QaViewMode.HasValue && !options.QaAllowStartupRefresh,
            qaStateFilePath: options.QaStateFile));
    }

    private static bool IsInstallerLaunch(PortableContext context, CommandLineOptions options)
    {
        if (options.Installer)
        {
            return true;
        }

        var fileName = Path.GetFileName(context.EntryExecutablePath);
        return fileName.Contains("安装器", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunInstallerFlow(
        PortableContext context,
        SettingsService settingsService,
        DeploymentService deploymentService,
        AppSettings settings,
        CommandLineOptions options)
    {
        if (!ShellHelper.IsAdministrator())
        {
            var choice = ShowElevationPrompt(
                title: "磁盘清理器安装器",
                heading: "安装前建议先授予管理员权限",
                message: "安装器会把程序默认放到非 C 盘，并创建桌面入口。授予管理员权限后，安装时不容易因为权限问题失败，也更方便后面完整扫描和清理受保护目录中的垃圾缓存。\r\n\r\n继续后会重新以管理员身份启动安装器。",
                secondaryButtonText: "先取消安装",
                tertiaryButtonText: null);

            if (choice != DialogResult.Yes)
            {
                return;
            }

            RelaunchCurrentExecutable(context, ["--installer", "--elevated"]);
            return;
        }

        var targets = deploymentService.GetDeploymentTargets();
        var recommendedTarget = targets.FirstOrDefault(target => target.IsRecommended) ?? targets.FirstOrDefault();
        using var dialog = new DeploymentDialog(
            title: "磁盘清理器安装器",
            heading: "把磁盘清理器安装到非 C 盘",
            introText: "默认会优先选择容量更充足的非 C 固定盘，并把程序放到容易找到的“磁盘清理器”目录。装好后你可以直接把这个安装器发给别人，他们也能一键装到合适的位置。",
            targets: targets,
            initialPath: string.IsNullOrWhiteSpace(settings.PreferredInstallPath) ? recommendedTarget?.TargetPath : settings.PreferredInstallPath,
            defaultCreateShortcut: settings.CreateDesktopShortcutOnInstall,
            allowDataMigration: false);

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var targetPath = dialog.SelectedPath;
        var installedExecutablePath = DeployWithProgress(
            context,
            settings.AppName,
            "正在安装磁盘清理器",
            "正在把程序安装到非 C 盘，请稍候。",
            progressTitle: "正在安装到推荐目录...",
            deploy: progress => deploymentService.DeploySelf(context, targetPath, dialog.CreateDesktopShortcut, migrateData: false, progress));

        settings.PreferredInstallPath = targetPath;
        settings.CreateDesktopShortcutOnInstall = dialog.CreateDesktopShortcut;
        settings.StartupElevationPromptShown = true;
        settings.ElevatedLauncherRegistered = true;
        settingsService.Save(settings);
        deploymentService.EnsureElevatedLauncher(installedExecutablePath, targetPath, dialog.CreateDesktopShortcut, settings.ElevatedLauncherTaskName);

        ShellHelper.StartExecutable(
            installedExecutablePath,
            ["--elevated", "--skip-migration-prompt"],
            Path.GetDirectoryName(installedExecutablePath) ?? targetPath,
            elevated: false,
            waitForExit: false);
    }

    private static bool RunMigrationFlow(PortableContext context, AppSettings settings, DeploymentService deploymentService)
    {
        var targets = deploymentService.GetDeploymentTargets();
        var recommendedTarget = targets.FirstOrDefault(target => target.IsRecommended) ?? targets.FirstOrDefault();
        if (recommendedTarget is null)
        {
            return false;
        }

        using var dialog = new DeploymentDialog(
            title: settings.AppName,
            heading: "检测到程序当前位于 C 盘或受保护目录",
            introText: "为了避免继续挤占系统盘，也方便以后给别人拷走直接用，建议现在就把清理器迁到非 C 固定盘。迁过去后会自动更新桌面快捷方式，并保留当前 data 目录里的设置和日志。",
            targets: targets,
            initialPath: string.IsNullOrWhiteSpace(settings.PreferredInstallPath) ? recommendedTarget.TargetPath : settings.PreferredInstallPath,
            defaultCreateShortcut: settings.CreateDesktopShortcutOnInstall,
            allowDataMigration: true);

        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        var targetPath = dialog.SelectedPath;
        var installedExecutablePath = DeployWithProgress(
            context,
            settings.AppName,
            "正在迁移磁盘清理器",
            "正在把当前版本迁到非 C 盘，请稍候。",
            progressTitle: "正在迁移程序和数据...",
            deploy: progress => deploymentService.DeploySelf(context, targetPath, dialog.CreateDesktopShortcut, dialog.MigrateData, progress));
        deploymentService.EnsureElevatedLauncher(installedExecutablePath, targetPath, dialog.CreateDesktopShortcut, settings.ElevatedLauncherTaskName);
        ShellHelper.StartExecutable(
            installedExecutablePath,
            ["--elevated", "--skip-migration-prompt"],
            Path.GetDirectoryName(installedExecutablePath) ?? targetPath,
            elevated: false,
            waitForExit: false);

        return true;
    }

    private static DialogResult ShowElevationPrompt(
        string title,
        string heading,
        string message,
        string secondaryButtonText,
        string? tertiaryButtonText)
    {
        using var dialog = new ElevationPromptDialog(
            title,
            heading,
            message,
            primaryButtonText: "授予管理员权限",
            secondaryButtonText: secondaryButtonText,
            tertiaryButtonText: tertiaryButtonText);
        return dialog.ShowDialog();
    }

    private static void RelaunchCurrentExecutable(PortableContext context, IEnumerable<string> args)
    {
        try
        {
            ShellHelper.StartCurrentExecutable(context, args, elevated: true, waitForExit: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"未能重新以管理员身份启动程序：{ex.Message}", "权限申请失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string DeployWithProgress(
        PortableContext context,
        string title,
        string heading,
        string introText,
        string progressTitle,
        Func<IProgress<DeploymentProgressUpdate>, string> deploy)
    {
        using var progressDialog = new OperationProgressDialog(title, heading, introText);
        progressDialog.Show();
        progressDialog.Apply(new DeploymentProgressUpdate
        {
            Percent = 2,
            Message = progressTitle
        });
        Application.DoEvents();

        var progress = new ImmediateProgress<DeploymentProgressUpdate>(update =>
        {
            progressDialog.Apply(update);
            Application.DoEvents();
        });

        try
        {
            return deploy(progress);
        }
        finally
        {
            progressDialog.Close();
        }
    }

    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public ImmediateProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value)
        {
            _report(value);
        }
    }

    private static void RunScheduledFlow(
        PortableContext context,
        AppSettings settings,
        SettingsService settingsService,
        ScanService scanService,
        CleanupService cleanupService,
        OccupancyProbeService occupancyProbeService,
        MigrationService migrationService,
        SchedulerService schedulerService,
        bool forceSilent)
    {
        if (!settings.AutoCleanupEnabled)
        {
            return;
        }

        var snapshot = scanService.ScanSnapshot(settings);
        var items = snapshot.CleanupItems;
        var totalBytes = items.Sum(item => item.SizeBytes);
        if (totalBytes < settings.MinimumPromptSizeMB * 1024L * 1024L)
        {
            return;
        }

        if (forceSilent || !settings.PromptOnScheduledRun)
        {
            var selection = settings.SafeItemsOnlyWhenScheduled
                ? items.Where(item => item.SafeAuto).ToList()
                : items.Where(item => item.Recommended).ToList();
            if (selection.Count == 0)
            {
                return;
            }

            RunCleanupHeadless(context, cleanupService, selection, settings.AppName, noPrompt: true, includeResidueCleanup: false);
            return;
        }

        var safeBytes = items.Where(item => item.SafeAuto).Sum(item => item.SizeBytes);
        var reviewBytes = items.Where(item => !item.SafeAuto).Sum(item => item.SizeBytes);
        var choice = MessageBox.Show(
            $"检测到固定磁盘可释放空间（C 盘优先）:\r\n\r\n1. 明确垃圾: {SizeFormatter.Format(safeBytes)}\r\n2. 需确认的缓存/重复文件: {SizeFormatter.Format(reviewBytes)}\r\n3. 合计: {SizeFormatter.Format(totalBytes)}\r\n\r\n按钮说明:\r\n是 = 打开完整界面自己勾选确认\r\n否 = 直接清理明确垃圾\r\n取消 = 这次先不处理",
            settings.AppName,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        switch (choice)
        {
            case DialogResult.Yes:
                var deploymentService = new DeploymentService();
                var launchDiagnostics = deploymentService.GetLaunchDiagnostics(context, settings.ElevatedLauncherTaskName, launchedFromElevatedTask: false, launchedFromScheduledTask: true);
                Application.Run(new MainForm(
                    context,
                    settingsService,
                    scanService,
                    cleanupService,
                    occupancyProbeService,
                    migrationService,
                    schedulerService,
                    new SnapshotCacheService(context),
                    new OperationManager(settings.MaxConcurrentJobs),
                    new OptimizationAuditService(),
                    settings,
                    snapshot,
                    runtimeInfoText: launchDiagnostics.DisplayText,
                    runtimeInfoToolTip: launchDiagnostics.ToolTipText));
                break;
            case DialogResult.No:
                RunCleanupHeadless(context, cleanupService, items.Where(item => item.SafeAuto).ToList(), settings.AppName, noPrompt: false, includeResidueCleanup: false);
                break;
        }
    }

    private static void RunCleanupHeadless(
        PortableContext context,
        CleanupService cleanupService,
        IReadOnlyCollection<CleanupItem> items,
        string appName,
        bool noPrompt,
        string? existingSelectionFile = null,
        bool includeResidueCleanup = true)
    {
        if (items.Count == 0)
        {
            if (!noPrompt)
            {
                MessageBox.Show("这次没有发现可处理的项目。", appName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        if (cleanupService.RequiresElevation(items, includeResidueCleanup) && !ShellHelper.IsAdministrator())
        {
            var selectionFile = existingSelectionFile ?? ShellHelper.ExportSelection(context, items);
            try
            {
                var childArgs = new List<string> { "--selection", selectionFile };
                if (noPrompt)
                {
                    childArgs.Add("--no-prompt");
                }

                ShellHelper.StartCurrentExecutable(
                    context,
                    childArgs,
                    elevated: true,
                    waitForExit: true);
            }
            catch (Exception ex)
            {
                if (!noPrompt)
                {
                    MessageBox.Show($"未能获取管理员权限: {ex.Message}", appName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (existingSelectionFile is null)
                {
                    ShellHelper.DeleteFileQuietly(selectionFile);
                }
            }

            return;
        }

        var runResult = cleanupService.Run(items, includeResidueCleanup);
        if (!string.IsNullOrWhiteSpace(existingSelectionFile))
        {
            WriteRunResultFile(existingSelectionFile, runResult);
        }

        if (!noPrompt)
        {
            var successCount = runResult.Results.Count(result => result.Status == "成功");
            var partialCount = runResult.Results.Count(result => result.Status == "部分成功");
            var failCount = runResult.Results.Count(result => result.Status == "失败");
            MessageBox.Show(
                $"本次清理完成。\r\n\r\n成功项目: {successCount}\r\n部分成功: {partialCount}\r\n失败项目: {failCount}\r\n实际释放空间: {SizeFormatter.Format(runResult.FreedBytes)}\r\n\r\n清理日志:\r\n{runResult.LogPath}",
                appName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        if (!string.IsNullOrWhiteSpace(existingSelectionFile))
        {
            ShellHelper.DeleteFileQuietly(existingSelectionFile);
        }
    }

    private static void WriteRunResultFile(string selectionFile, CleanupRunResult runResult)
    {
        try
        {
            var resultPath = GetRunResultPath(selectionFile);
            var json = JsonSerializer.Serialize(runResult);
            File.WriteAllText(resultPath, json);
        }
        catch
        {
        }
    }

    internal static string GetRunResultPath(string selectionFile)
    {
        return selectionFile + ".result.json";
    }
}
