using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class CleanupService
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string commandLine, out int argc);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    private static readonly string[] ElevatedRootFolders = ["Windows", "ProgramData", "$SysReset", "$Recycle.Bin", "PerfLogs"];
    private static readonly string[] GenericIdentityTokens =
    [
        "setup", "install", "installer", "update", "updates", "cache", "temp", "tmp", "log", "logs",
        "download", "downloads", "package", "packages", "portable", "windows", "x64", "x86", "arm64"
    ];

    private readonly PortableContext _context;
    private readonly OccupancyProbeService _occupancyProbeService;

    public CleanupService(PortableContext context, OccupancyProbeService occupancyProbeService)
    {
        _context = context;
        _occupancyProbeService = occupancyProbeService;
    }

    public bool RequiresElevation(IEnumerable<CleanupItem> items, bool includeResidueCleanup = true)
    {
        var selection = items.DistinctBy(item => item.Id).ToList();
        var systemDriveRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        var currentUser = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var item in selection)
        {
            if (item.TargetKind == CleanupTargetKind.RecycleBin)
            {
                return true;
            }

            if (IsPrivilegedPath(item.Path, systemDriveRoot, currentUser))
            {
                return true;
            }
        }

        return includeResidueCleanup && selection.Any(item => item.IsApplicationRelated);
    }

    public IReadOnlyList<ResidueAction> PlanResidueActions(IReadOnlyCollection<CleanupItem> items)
    {
        var resolvedProfiles = ResolveInstalledAppProfiles(items);
        return PlanResidueActions(items, resolvedProfiles);
    }

    private IReadOnlyList<ResidueAction> PlanResidueActions(
        IReadOnlyCollection<CleanupItem> items,
        IReadOnlyDictionary<Guid, InstalledAppProfile?> resolvedProfiles)
    {
        var actions = new Dictionary<string, ResidueAction>(StringComparer.OrdinalIgnoreCase);
        var cache = new ResidueProbeCache();

        foreach (var item in items.DistinctBy(item => item.Id))
        {
            if (!item.IsApplicationRelated)
            {
                continue;
            }

            resolvedProfiles.TryGetValue(item.Id, out var installedProfile);
            var identity = BuildIdentity(item, installedProfile);
            if (string.IsNullOrWhiteSpace(identity.DisplayName) || string.IsNullOrWhiteSpace(identity.RootPath))
            {
                continue;
            }

            AddRegistryResidueActions(actions, identity);
            AddShortcutResidueActions(actions, identity, cache);
            AddScheduledTaskResidueActions(actions, identity, cache);
            AddServiceResidueActions(actions, identity, cache);
            AddFirewallResidueActions(actions, identity, cache);
            AddKnownDataResidueActions(actions, identity);
        }

        return actions.Values
            .OrderBy(action => action.Identity.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(action => action.KindText, StringComparer.OrdinalIgnoreCase)
            .ThenBy(action => action.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyDictionary<Guid, InstalledAppProfile?> ResolveInstalledAppProfiles(IReadOnlyCollection<CleanupItem> items)
    {
        var profiles = new Dictionary<Guid, InstalledAppProfile?>();
        var cache = new Dictionary<string, InstalledAppProfile?>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items.DistinctBy(item => item.Id))
        {
            if (!item.DeepCleanupEligible)
            {
                profiles[item.Id] = null;
                continue;
            }

            var cacheKey = string.IsNullOrWhiteSpace(item.AppIdentityKey)
                ? NormalizePath(item.Path)
                : $"{NormalizeIdentityKey(item.AppIdentityKey)}|{NormalizePath(item.Path)}";
            if (!cache.TryGetValue(cacheKey, out var profile))
            {
                profile = TryResolveInstalledAppProfile(item);
                cache[cacheKey] = profile;
            }

            profiles[item.Id] = profile;
        }

        return profiles;
    }

    public CleanupRunResult Run(
        IReadOnlyCollection<CleanupItem> items,
        bool includeResidueCleanup = true,
        IProgress<OperationProgress>? progress = null,
        Guid jobId = default)
    {
        var selection = items.DistinctBy(item => item.Id).ToList();
        var results = new List<CleanupResult>();
        var executedResidueActions = new List<ResidueAction>();
        long freedBytes = 0;
        var deletedItemCount = 0;
        var skippedLockedCount = 0;
        var invokedOfficialUninstaller = false;
        var officialUninstallAttempted = false;
        var officialUninstallSucceededCount = 0;
        var officialUninstallFailedCount = 0;
        var officialUninstallAttempts = new List<OfficialUninstallAttemptInfo>();
        var blockingProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolvedProfiles = new Dictionary<Guid, InstalledAppProfile?>();
        var totalCount = selection.Count;
        var totalBytes = selection.Sum(item => Math.Max(item.SizeBytes, 0));
        long processedBytes = 0;
        var index = 0;

        foreach (var item in selection)
        {
            index++;
            progress?.Report(new OperationProgress
            {
                Percent = totalCount == 0 ? 0 : (index - 1) * 100 / Math.Max(totalCount, 1),
                Phase = "识别和准备",
                Message = $"正在准备处理 {item.Name}",
                IsIndeterminate = false,
                ProcessedBytes = processedBytes,
                TotalBytes = totalBytes,
                JobScope = item.Name
            });

            var installedProfile = includeResidueCleanup && item.DeepCleanupEligible
                ? TryResolveInstalledAppProfile(item)
                : null;
            resolvedProfiles[item.Id] = installedProfile;
            var before = CaptureSnapshot(item);
            try
            {
                if (item.IsApplicationRelated)
                {
                    var preOccupancy = _occupancyProbeService.Probe(item.Path);
                    if (preOccupancy.BlockingProcesses.Count > 0)
                    {
                        progress?.Report(new OperationProgress
                        {
                            Percent = totalCount == 0 ? 0 : ((index - 1) * 100 + 10) / Math.Max(totalCount, 1),
                            Phase = "关闭占用",
                            Message = $"正在尝试关闭占用 {item.Name} 的程序",
                            IsIndeterminate = true,
                            ProcessedBytes = processedBytes,
                            TotalBytes = totalBytes,
                            JobScope = item.Name
                        });
                        var closeResult = _occupancyProbeService.TryCloseProcesses(preOccupancy.BlockingProcesses, TimeSpan.FromSeconds(6));
                        foreach (var processName in closeResult.RemainingProcesses.Select(process => process.DisplayName))
                        {
                            if (!string.IsNullOrWhiteSpace(processName))
                            {
                                blockingProcesses.Add(processName);
                            }
                        }
                    }
                }

                var officialUninstallResult = installedProfile is not null
                    ? TryInvokeOfficialUninstaller(item, installedProfile, progress)
                    : OfficialUninstallExecutionResult.Empty;
                if (officialUninstallResult.Attempts.Count > 0)
                {
                    officialUninstallAttempted = true;
                    officialUninstallAttempts.AddRange(officialUninstallResult.Attempts);
                    if (officialUninstallResult.Succeeded)
                    {
                        officialUninstallSucceededCount++;
                    }
                    else
                    {
                        officialUninstallFailedCount++;
                    }
                }

                if (officialUninstallResult.Succeeded)
                {
                    invokedOfficialUninstaller = true;
                    WaitForOfficialUninstallSettlement(item, installedProfile!, progress, processedBytes, totalBytes, index, totalCount, blockingProcesses);
                }

                progress?.Report(new OperationProgress
                {
                    Percent = totalCount == 0 ? 0 : ((index - 1) * 100 + 35) / Math.Max(totalCount, 1),
                    Phase = "删除文件",
                    Message = $"正在删除 {item.Name}",
                    IsIndeterminate = before.SizeBytes > 256L * 1024 * 1024,
                    ProcessedBytes = processedBytes,
                    TotalBytes = totalBytes,
                    JobScope = item.Name
                });

                ExecuteCleanup(item);
                WaitForPathState(item);

                var after = CaptureSnapshot(item);
                var occupancy = ProbeOccupancy(item, after);
                foreach (var processName in occupancy.BlockingProcesses.Select(process => process.DisplayName))
                {
                    if (!string.IsNullOrWhiteSpace(processName))
                    {
                        blockingProcesses.Add(processName);
                    }
                }
                var result = BuildCleanupResult(item, before, after, null, occupancy);
                results.Add(result);
                freedBytes += Math.Max(before.SizeBytes - after.SizeBytes, 0);
                processedBytes += Math.Max(before.SizeBytes, 0);

                if (result.Status == "成功")
                {
                    deletedItemCount++;
                }
                else
                {
                    skippedLockedCount++;
                }
            }
            catch (Exception ex)
            {
                var after = CaptureSnapshot(item);
                var occupancy = ProbeOccupancy(item, after);
                foreach (var processName in occupancy.BlockingProcesses.Select(process => process.DisplayName))
                {
                    if (!string.IsNullOrWhiteSpace(processName))
                    {
                        blockingProcesses.Add(processName);
                    }
                }
                results.Add(BuildCleanupResult(item, before, after, ex.Message, occupancy));
                skippedLockedCount++;
                processedBytes += Math.Max(before.SizeBytes, 0);
            }
        }

        if (includeResidueCleanup)
        {
            progress?.Report(new OperationProgress
            {
                Percent = 92,
                Phase = "深度残留清理",
                Message = "正在清理注册表、快捷方式、计划任务和服务残留",
                IsIndeterminate = true,
                ProcessedBytes = processedBytes,
                TotalBytes = totalBytes,
                JobScope = "残留清理"
            });
            foreach (var action in PlanResidueActions(selection, resolvedProfiles))
            {
                executedResidueActions.Add(ExecuteResidueAction(action));
            }
        }

        var existsAfter = results.Any(result => result.ExistsAfter) || executedResidueActions.Any(action => action.ExistsAfter);
        var residueVerification = includeResidueCleanup ? VerifyResidues(selection, resolvedProfiles) : new ResidueVerificationResult
        {
            VerifiedClean = !existsAfter,
            RemainingResidueCount = 0,
            RemainingResidues = [],
            RemainingActions = []
        };
        var followUpRecommendations = BuildFollowUpRecommendations(
            results,
            residueVerification,
            officialUninstallAttempts,
            blockingProcesses);
        var logPath = WriteLog(results, executedResidueActions, officialUninstallAttempts, freedBytes);
        var removedResidues = executedResidueActions.Where(action => !action.ExistsAfter).ToList();
        progress?.Report(new OperationProgress
        {
            Percent = 100,
            Phase = "完成",
            Message = "清理完成，正在更新界面",
            IsIndeterminate = false,
            ProcessedBytes = totalBytes,
            TotalBytes = totalBytes,
            JobScope = "完成"
        });

        return new CleanupRunResult
        {
            JobId = jobId,
            Phase = includeResidueCleanup ? "DeepCleanupPipeline" : "CleanupOnly",
            Results = results,
            FreedBytes = freedBytes,
            LogPath = logPath,
            DeletedItemCount = deletedItemCount,
            SkippedLockedCount = skippedLockedCount,
            ExistsAfter = existsAfter,
            VerifiedClean = !existsAfter && residueVerification.VerifiedClean,
            InvokedOfficialUninstaller = invokedOfficialUninstaller,
            OfficialUninstallAttempted = officialUninstallAttempted,
            OfficialUninstallSucceededCount = officialUninstallSucceededCount,
            OfficialUninstallFailedCount = officialUninstallFailedCount,
            DeepVerificationPassed = residueVerification.VerifiedClean && !existsAfter,
            RegistryResidueRemovedCount = removedResidues.Count(action => action.Kind == ResidueActionKind.RegistryKey || action.Kind == ResidueActionKind.RegistryValue || action.Kind == ResidueActionKind.StartupValue),
            ShortcutResidueRemovedCount = removedResidues.Count(action => action.Kind == ResidueActionKind.ShortcutFile),
            TaskResidueRemovedCount = removedResidues.Count(action => action.Kind == ResidueActionKind.ScheduledTask),
            ServiceResidueRemovedCount = removedResidues.Count(action => action.Kind == ResidueActionKind.Service),
            FirewallResidueRemovedCount = removedResidues.Count(action => action.Kind == ResidueActionKind.FirewallRule),
            ResidualWarnings = residueVerification.RemainingResidues,
            RemainingResidueActions = residueVerification.RemainingActions,
            RemainingRegistryResidueCount = residueVerification.RemainingRegistryResidueCount,
            RemainingShortcutResidueCount = residueVerification.RemainingShortcutResidueCount,
            RemainingTaskResidueCount = residueVerification.RemainingTaskResidueCount,
            RemainingServiceResidueCount = residueVerification.RemainingServiceResidueCount,
            RemainingFirewallResidueCount = residueVerification.RemainingFirewallResidueCount,
            RemainingDirectoryResidueCount = residueVerification.RemainingDirectoryResidueCount,
            RemainingFileResidueCount = residueVerification.RemainingFileResidueCount,
            OfficialUninstallAttempts = officialUninstallAttempts,
            FollowUpRecommendations = followUpRecommendations,
            BlockingProcesses = blockingProcesses.ToList(),
            ResidueActions = executedResidueActions
        };
    }

    private InstalledAppProfile? TryResolveInstalledAppProfile(CleanupItem item)
    {
        var itemPath = NormalizePath(item.Path);
        var itemName = GetBaseName(item);
        var itemIdentityKey = NormalizeIdentityKey(item.AppIdentityKey);
        InstalledAppProfile? bestMatch = null;
        var bestScore = 0;

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var keyPath in new[]
                     {
                         @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
                         @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                     })
            {
                using var parent = hive.OpenSubKey(keyPath);
                if (parent is null)
                {
                    continue;
                }

                foreach (var subKeyName in parent.GetSubKeyNames())
                {
                    using var subKey = parent.OpenSubKey(subKeyName);
                    if (subKey is null)
                    {
                        continue;
                    }

                    var displayName = subKey.GetValue("DisplayName")?.ToString()?.Trim() ?? string.Empty;
                    var installLocation = NormalizePath(subKey.GetValue("InstallLocation")?.ToString() ?? string.Empty);
                    var displayIcon = subKey.GetValue("DisplayIcon")?.ToString()?.Trim() ?? string.Empty;
                    var publisher = subKey.GetValue("Publisher")?.ToString()?.Trim() ?? string.Empty;
                    var displayVersion = subKey.GetValue("DisplayVersion")?.ToString()?.Trim() ?? string.Empty;
                    var installSource = NormalizePath(subKey.GetValue("InstallSource")?.ToString() ?? string.Empty);
                    var uninstallString = subKey.GetValue("UninstallString")?.ToString()?.Trim() ?? string.Empty;
                    var quietUninstallString = subKey.GetValue("QuietUninstallString")?.ToString()?.Trim() ?? string.Empty;
                    var modifyPath = subKey.GetValue("ModifyPath")?.ToString()?.Trim() ?? string.Empty;
                    var quietCommandCandidates = BuildOfficialUninstallCommands(quietUninstallString, uninstallString, modifyPath);
                    var resolvedLocation = ResolveInstallLocation(installLocation, displayIcon, quietUninstallString, uninstallString, modifyPath, installSource);
                    var displayIconPath = NormalizeExecutablePath(displayIcon);
                    var uninstallDirectory = ResolveCommandDirectory(uninstallString);
                    var quietUninstallDirectory = ResolveCommandDirectory(quietUninstallString);
                    var modifyDirectory = ResolveCommandDirectory(modifyPath);
                    var companionPaths = GetCompanionPaths(
                        resolvedLocation,
                        displayName,
                        publisher,
                        displayIconPath,
                        uninstallDirectory,
                        quietUninstallDirectory,
                        modifyDirectory,
                        installSource);
                    var score = GetInstalledAppMatchScore(
                        item,
                        itemPath,
                        itemName,
                        itemIdentityKey,
                        displayName,
                        resolvedLocation,
                        displayIconPath,
                        quietUninstallString,
                        uninstallString,
                        uninstallDirectory,
                        quietUninstallDirectory,
                        modifyPath,
                        modifyDirectory,
                        publisher,
                        installSource,
                        companionPaths);

                    if (score < 45 || score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestMatch = new InstalledAppProfile
                    {
                        AppIdentityKey = BuildProfileIdentityKey(displayName, resolvedLocation, uninstallString),
                        DisplayName = displayName,
                        Publisher = publisher,
                        DisplayVersion = displayVersion,
                        InstallLocation = resolvedLocation,
                        InstallSource = installSource,
                        DisplayIconPath = displayIconPath,
                        UninstallString = uninstallString,
                        QuietUninstallString = quietUninstallString,
                        ModifyPath = modifyPath,
                        QuietUninstallCommand = quietCommandCandidates.FirstOrDefault() ?? string.Empty,
                        OfficialUninstallCommands = quietCommandCandidates,
                        IsDriverBound = IsDriverBoundProfile(displayName, publisher, resolvedLocation),
                        ExecutableNames = BuildExecutableNames(resolvedLocation, displayIconPath, uninstallDirectory),
                        CompanionPaths = companionPaths
                    };
                }
            }
        }

        return bestMatch;
    }

    private OfficialUninstallExecutionResult TryInvokeOfficialUninstaller(
        CleanupItem item,
        InstalledAppProfile profile,
        IProgress<OperationProgress>? progress)
    {
        if (profile.IsDriverBound || profile.OfficialUninstallCommands.Count == 0)
        {
            return OfficialUninstallExecutionResult.Empty;
        }

        var attempts = new List<OfficialUninstallAttemptInfo>();
        foreach (var command in profile.OfficialUninstallCommands)
        {
            var (fileName, arguments, useShellExecute) = ParseCommandLine(command);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                attempts.Add(new OfficialUninstallAttemptInfo
                {
                    ItemId = item.Id,
                    ItemName = item.Name,
                    AppDisplayName = profile.DisplayName,
                    Command = command,
                    FileName = string.Empty,
                    Arguments = string.Empty,
                    Succeeded = false,
                    FailureReason = "无法解析官方卸载命令。"
                });
                continue;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                progress?.Report(new OperationProgress
                {
                    Percent = 10,
                    Phase = "官方卸载",
                    Message = $"正在调用 {profile.DisplayName} 的官方卸载器",
                    IsIndeterminate = true
                });

                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = useShellExecute,
                        CreateNoWindow = !useShellExecute,
                        WorkingDirectory = string.IsNullOrWhiteSpace(profile.InstallLocation)
                            ? Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory
                            : profile.InstallLocation
                    }
                };

                process.Start();
                process.WaitForExit();
                stopwatch.Stop();
                var succeeded = process.ExitCode == 0
                    || process.ExitCode == 1605
                    || process.ExitCode == 1641
                    || process.ExitCode == 3010;
                attempts.Add(new OfficialUninstallAttemptInfo
                {
                    ItemId = item.Id,
                    ItemName = item.Name,
                    AppDisplayName = profile.DisplayName,
                    Command = command,
                    FileName = fileName,
                    Arguments = arguments,
                    ExitCode = process.ExitCode,
                    Succeeded = succeeded,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    FailureReason = succeeded ? string.Empty : $"退出码 {process.ExitCode}"
                });
                if (succeeded)
                {
                    return new OfficialUninstallExecutionResult(attempts, true);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                attempts.Add(new OfficialUninstallAttemptInfo
                {
                    ItemId = item.Id,
                    ItemName = item.Name,
                    AppDisplayName = profile.DisplayName,
                    Command = command,
                    FileName = fileName,
                    Arguments = arguments,
                    Succeeded = false,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    FailureReason = ex.Message
                });
            }
        }

        return new OfficialUninstallExecutionResult(attempts, false);
    }

    private void WaitForOfficialUninstallSettlement(
        CleanupItem item,
        InstalledAppProfile profile,
        IProgress<OperationProgress>? progress,
        long processedBytes,
        long totalBytes,
        int index,
        int totalCount,
        HashSet<string> blockingProcesses)
    {
        var probePaths = GetOfficialUninstallSettlementPaths(item, profile);
        if (probePaths.Count == 0)
        {
            Thread.Sleep(250);
            return;
        }

        progress?.Report(new OperationProgress
        {
            Percent = totalCount == 0 ? 0 : ((index - 1) * 100 + 26) / Math.Max(totalCount, 1),
            Phase = "等待卸载器收尾",
            Message = $"正在等待 {item.Name} 的官方卸载器完成收尾",
            IsIndeterminate = true,
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes,
            JobScope = item.Name
        });

        var closeAttempted = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var blockers = probePaths
                .SelectMany(path => _occupancyProbeService.Probe(path).BlockingProcesses)
                .DistinctBy(process => process.ProcessId)
                .ToList();
            if (blockers.Count == 0)
            {
                Thread.Sleep(250);
                return;
            }

            if (!closeAttempted)
            {
                closeAttempted = true;
                var closeResult = _occupancyProbeService.TryCloseProcesses(blockers, TimeSpan.FromSeconds(2));
                foreach (var processName in closeResult.RemainingProcesses.Select(process => process.DisplayName))
                {
                    if (!string.IsNullOrWhiteSpace(processName))
                    {
                        blockingProcesses.Add(processName);
                    }
                }
            }

            Thread.Sleep(350);
        }
    }

    private static IReadOnlyList<string> GetOfficialUninstallSettlementPaths(CleanupItem item, InstalledAppProfile profile)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(paths, NormalizePath(item.Path));
        AddIfPresent(paths, NormalizePath(profile.InstallLocation));

        foreach (var companionPath in profile.CompanionPaths.Take(8))
        {
            AddIfPresent(paths, NormalizePath(companionPath));
        }

        return paths.ToList();
    }

    private void ExecuteCleanup(CleanupItem item)
    {
        WithOptionalServiceStop(item, () =>
        {
            switch (item.TargetKind)
            {
                case CleanupTargetKind.DirectoryContents:
                    foreach (var entry in FileSystemHelper.GetTopLevelEntries(item.Path))
                    {
                        try
                        {
                            FileSystemHelper.DeletePathPermanent(entry);
                        }
                        catch
                        {
                        }
                    }
                    break;
                case CleanupTargetKind.DirectoryTree:
                    FileSystemHelper.DeletePathPermanent(item.Path);
                    break;
                case CleanupTargetKind.FilePermanent:
                    FileSystemHelper.DeleteFilePermanent(item.Path);
                    break;
                case CleanupTargetKind.FileRecycle:
                    FileSystemHelper.DeleteToRecycleBin(item.Path);
                    break;
                case CleanupTargetKind.RecycleBin:
                    RecycleBinHelper.EmptyRecycleBin();
                    break;
            }
        });
    }

    private static void WithOptionalServiceStop(CleanupItem item, Action action)
    {
        var restartServices = new List<string>();
        if (item.Path.EndsWith(@"\Windows\SoftwareDistribution\Download", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var service in new[] { "wuauserv", "bits", "dosvc" })
            {
                try
                {
                    ShellHelper.RunProcessFireAndForget("sc.exe", ["stop", service]);
                    restartServices.Add(service);
                }
                catch
                {
                }
            }
        }

        try
        {
            action();
        }
        finally
        {
            foreach (var service in restartServices)
            {
                try
                {
                    ShellHelper.RunProcessFireAndForget("sc.exe", ["start", service]);
                }
                catch
                {
                }
            }
        }
    }

    private static void WaitForPathState(CleanupItem item)
    {
        if (item.TargetKind == CleanupTargetKind.DirectoryContents || item.TargetKind == CleanupTargetKind.RecycleBin)
        {
            Thread.Sleep(200);
            return;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!FileSystemHelper.ExistsPath(item.Path))
            {
                return;
            }

            Thread.Sleep(120);
        }
    }

    private CleanupResult BuildCleanupResult(
        CleanupItem item,
        PathSnapshot before,
        PathSnapshot after,
        string? errorMessage,
        OccupancyProbeResult occupancy)
    {
        var status = DetermineStatus(item, before, after, errorMessage, out var message);
        var deletedEntries = Math.Max(before.TopLevelEntryCount - after.TopLevelEntryCount, 0);
        var blockingProcesses = occupancy.BlockingProcesses
            .Select(process => process.DisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var retrySuggested = status != "成功" && blockingProcesses.Count > 0;

        if (retrySuggested)
        {
            var processList = string.Join("、", blockingProcesses.Take(3));
            message = status == "部分成功"
                ? $"已清掉一部分，但当前仍被 {processList} 占用，关闭后可以继续重试。"
                : $"当前正被 {processList} 使用，需关闭后重试。";
        }

        return new CleanupResult
        {
            ItemId = item.Id,
            Status = status,
            Name = item.Name,
            NormalizedPath = item.NormalizedPath,
            TargetKind = item.TargetKind,
            OriginalBytes = before.SizeBytes,
            FreedBytes = Math.Max(before.SizeBytes - after.SizeBytes, 0),
            RemainingBytes = after.SizeBytes,
            SizeText = SizeFormatter.Format(before.SizeBytes),
            FreedText = SizeFormatter.Format(Math.Max(before.SizeBytes - after.SizeBytes, 0)),
            RemainingText = SizeFormatter.Format(after.SizeBytes),
            Path = item.Path,
            ExistsAfter = after.Exists,
            DeletedEntryCount = deletedEntries,
            RemainingEntryCount = after.TopLevelEntryCount,
            BlockingProcessNames = blockingProcesses,
            RetrySuggested = retrySuggested,
            Message = message
        };
    }

    private OccupancyProbeResult ProbeOccupancy(CleanupItem item, PathSnapshot after)
    {
        if (item.TargetKind == CleanupTargetKind.RecycleBin)
        {
            return new OccupancyProbeResult
            {
                ProbedPath = item.Path,
                BlockingProcesses = []
            };
        }

        if (!after.Exists && after.TopLevelEntryCount == 0)
        {
            return new OccupancyProbeResult
            {
                ProbedPath = item.Path,
                BlockingProcesses = []
            };
        }

        return _occupancyProbeService.Probe(item.Path);
    }

    private static string DetermineStatus(
        CleanupItem item,
        PathSnapshot before,
        PathSnapshot after,
        string? errorMessage,
        out string? message)
    {
        message = errorMessage;
        var freedBytes = Math.Max(before.SizeBytes - after.SizeBytes, 0);
        var deletedEntries = Math.Max(before.TopLevelEntryCount - after.TopLevelEntryCount, 0);

        switch (item.TargetKind)
        {
            case CleanupTargetKind.FilePermanent:
            case CleanupTargetKind.FileRecycle:
            case CleanupTargetKind.DirectoryTree:
                if (!after.Exists)
                {
                    if (item.IsApplicationRelated)
                    {
                        message ??= "主目标已经删除完成，后续会继续复查并清理强关联残留。";
                    }
                    return "成功";
                }

                if (freedBytes > 0 || deletedEntries > 0)
                {
                    message ??= $"已删除一部分内容，但主路径仍存在，当前还剩 {SizeFormatter.Format(after.SizeBytes)}。通常是目录里还有被占用文件、残留子目录，或程序刚把一部分内容重新写回来了。";
                    return "部分成功";
                }

                message ??= "未能删除该项，通常是因为文件正在占用、路径被系统锁定，或权限不足。";
                return "失败";

            case CleanupTargetKind.DirectoryContents:
                if (!after.Exists || (after.TopLevelEntryCount == 0 && after.SizeBytes == 0))
                {
                    message ??= "目录内容已清空完成；这个缓存目录本身会保留，后续程序需要时会重新往里写内容。";
                    return "成功";
                }

                if (freedBytes > 0 || deletedEntries > 0)
                {
                    message ??= $"目录中的一部分内容已清掉，目前还剩 {after.TopLevelEntryCount} 项 / {SizeFormatter.Format(after.SizeBytes)}。通常是文件仍被占用，或者缓存刚被系统/应用立即重建。";
                    return "部分成功";
                }

                message ??= $"目录内容没有明显减少，目前仍有 {after.TopLevelEntryCount} 项 / {SizeFormatter.Format(after.SizeBytes)}。通常是程序仍在占用，或该缓存已被立即重建。";
                return "失败";

            case CleanupTargetKind.RecycleBin:
                if (after.SizeBytes == 0)
                {
                    message ??= "回收站已经清空完成。";
                    return "成功";
                }

                if (after.SizeBytes < before.SizeBytes)
                {
                    message ??= "回收站已清掉一部分内容，但仍有剩余文件没有被系统释放。";
                    return "部分成功";
                }

                message ??= "回收站内容没有减少，通常是系统尚未释放、权限不足，或存在锁定项目。";
                return "失败";

            default:
                return after.Exists ? "失败" : "成功";
        }
    }

    private static PathSnapshot CaptureSnapshot(CleanupItem item)
    {
        if (item.TargetKind == CleanupTargetKind.RecycleBin)
        {
            var recycleBytes = RecycleBinHelper.GetRecycleBinSizeBytes();
            return new PathSnapshot(item.Path, recycleBytes > 0, recycleBytes, 0);
        }

        if (File.Exists(item.Path))
        {
            return new PathSnapshot(item.Path, true, FileSystemHelper.GetPathSizeBytes(item.Path), 1);
        }

        if (Directory.Exists(item.Path))
        {
            var entries = FileSystemHelper.GetTopLevelEntries(item.Path);
            return new PathSnapshot(item.Path, true, FileSystemHelper.GetPathSizeBytes(item.Path), entries.Count);
        }

        return new PathSnapshot(item.Path, false, 0, 0);
    }

    private CleanupIdentity BuildIdentity(CleanupItem item, InstalledAppProfile? profile)
    {
        var itemRootPath = item.TargetKind switch
        {
            CleanupTargetKind.FilePermanent or CleanupTargetKind.FileRecycle => Path.GetDirectoryName(item.Path) ?? item.Path,
            _ => item.Path
        };

        var baseName = GetBaseName(item);
        var normalizedItemRoot = NormalizePath(itemRootPath);
        var normalizedProfileRoot = NormalizePath(profile?.InstallLocation ?? string.Empty);
        var rootPath = ChooseIdentityRoot(normalizedItemRoot, normalizedProfileRoot);
        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(knownPaths, NormalizePath(item.Path));
        AddIfPresent(knownPaths, normalizedItemRoot);
        AddIfPresent(knownPaths, rootPath);
        AddIfPresent(knownPaths, normalizedProfileRoot);
        foreach (var companionPath in item.CompanionPaths)
        {
            AddIfPresent(knownPaths, NormalizePath(companionPath));
        }

        foreach (var companionPath in profile?.CompanionPaths ?? [])
        {
            AddIfPresent(knownPaths, NormalizePath(companionPath));
        }

        var displayAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(displayAliases, baseName);
        AddIfPresent(displayAliases, item.Name);
        AddIfPresent(displayAliases, item.InstalledAppId);
        AddIfPresent(displayAliases, profile?.DisplayName);
        AddIfPresent(displayAliases, Path.GetFileName(rootPath));
        foreach (var alias in knownPaths.Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))))
        {
            AddIfPresent(displayAliases, alias);
        }

        var exeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(item.Path) && item.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            AddIfPresent(exeNames, Path.GetFileName(item.Path));
        }

        foreach (var executableName in profile?.ExecutableNames ?? [])
        {
            AddIfPresent(exeNames, executableName);
        }

        if (Directory.Exists(rootPath))
        {
            try
            {
                foreach (var name in Directory.EnumerateFiles(rootPath, "*.exe", SearchOption.TopDirectoryOnly)
                             .Select(path => Path.GetFileName(path) ?? string.Empty)
                             .Where(name => !string.IsNullOrWhiteSpace(name))
                             .Take(8))
                {
                    AddIfPresent(exeNames, name);
                }
            }
            catch
            {
            }
        }

        foreach (var executableName in exeNames)
        {
            AddIfPresent(displayAliases, Path.GetFileNameWithoutExtension(executableName));
            AddIfPresent(displayAliases, executableName);
        }

        foreach (var alias in GetKnownProductAliases(profile?.DisplayName ?? item.Name, profile?.Publisher ?? string.Empty, rootPath))
        {
            AddIfPresent(displayAliases, alias);
        }

        var keywords = displayAliases
            .SelectMany(SplitKeywords)
            .Concat(knownPaths.SelectMany(GetPathIdentitySegments))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CleanupIdentity
        {
            AppIdentityKey = NormalizeIdentityKey(profile?.AppIdentityKey ?? item.AppIdentityKey),
            DisplayName = string.IsNullOrWhiteSpace(profile?.DisplayName) ? baseName : profile.DisplayName,
            Publisher = profile?.Publisher ?? string.Empty,
            RootPath = rootPath,
            DisplayAliases = displayAliases
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList(),
            KnownPaths = knownPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList(),
            Keywords = keywords,
            ExecutableNames = exeNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList()
        };
    }

    private static string ChooseIdentityRoot(string itemRootPath, string profileRootPath)
    {
        if (!string.IsNullOrWhiteSpace(profileRootPath))
        {
            return profileRootPath;
        }

        return itemRootPath;
    }

    private static void AddIfPresent(ISet<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static IEnumerable<string> GetPathIdentitySegments(string path)
    {
        return NormalizePath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length >= 2)
            .Where(segment => !IsIgnoredIdentityPathSegment(segment))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredIdentityPathSegment(string segment)
    {
        return segment.Equals("Users", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("AppData", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Roaming", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Local", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("LocalLow", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Program Files", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("ProgramData", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Current", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Application", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Applications", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Resources", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Desktop", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Downloads", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Documents", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Videos", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("Windows", StringComparison.OrdinalIgnoreCase)
            || segment.EndsWith(":", StringComparison.OrdinalIgnoreCase);
    }

    private void AddRegistryResidueActions(Dictionary<string, ResidueAction> actions, CleanupIdentity identity)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            AddRegistryKeyCandidates(actions, hive, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", identity);
            AddRegistryKeyCandidates(actions, hive, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", identity);
            AddAppPathCandidates(actions, hive, @"Software\Microsoft\Windows\CurrentVersion\App Paths", identity);
            AddStartupValueCandidates(actions, hive, @"Software\Microsoft\Windows\CurrentVersion\Run", identity);
            AddStartupValueCandidates(actions, hive, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", identity);
            AddSoftwareHiveResidueActions(actions, hive, @"Software", identity);
            AddSoftwareHiveResidueActions(actions, hive, @"Software\WOW6432Node", identity);
        }
    }

    private void AddRegistryKeyCandidates(Dictionary<string, ResidueAction> actions, RegistryKey hive, string parentKeyPath, CleanupIdentity identity)
    {
        using var parent = hive.OpenSubKey(parentKeyPath);
        if (parent is null)
        {
            return;
        }

        foreach (var subKeyName in parent.GetSubKeyNames())
        {
            using var subKey = parent.OpenSubKey(subKeyName);
            if (subKey is null)
            {
                continue;
            }

            var displayName = subKey.GetValue("DisplayName")?.ToString() ?? string.Empty;
            var installLocation = subKey.GetValue("InstallLocation")?.ToString() ?? string.Empty;
            var displayIcon = subKey.GetValue("DisplayIcon")?.ToString() ?? string.Empty;
            var uninstallString = subKey.GetValue("UninstallString")?.ToString() ?? string.Empty;

            if (!MatchesIdentity(identity, displayName, installLocation, displayIcon, uninstallString))
            {
                continue;
            }

            AddResidueAction(
                actions,
                new ResidueAction
                {
                    Identity = identity,
                    Kind = ResidueActionKind.RegistryKey,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? subKeyName : displayName,
                    Target = $@"{GetHiveName(hive)}\{parentKeyPath}\{subKeyName}",
                    Description = "卸载信息和安装残留注册表键。",
                    RegistryHive = GetHiveName(hive),
                    RegistryKeyPath = $@"{parentKeyPath}\{subKeyName}",
                    MatchReason = "卸载信息指向将被删除的应用路径。"
                });
        }
    }

    private void AddAppPathCandidates(Dictionary<string, ResidueAction> actions, RegistryKey hive, string parentKeyPath, CleanupIdentity identity)
    {
        using var parent = hive.OpenSubKey(parentKeyPath);
        if (parent is null)
        {
            return;
        }

        foreach (var subKeyName in parent.GetSubKeyNames())
        {
            using var subKey = parent.OpenSubKey(subKeyName);
            var defaultValue = subKey?.GetValue(string.Empty)?.ToString() ?? string.Empty;
            var pathValue = subKey?.GetValue("Path")?.ToString() ?? string.Empty;
            if (!MatchesIdentity(identity, subKeyName, defaultValue, pathValue))
            {
                continue;
            }

            AddResidueAction(
                actions,
                new ResidueAction
                {
                    Identity = identity,
                    Kind = ResidueActionKind.RegistryKey,
                    DisplayName = subKeyName,
                    Target = $@"{GetHiveName(hive)}\{parentKeyPath}\{subKeyName}",
                    Description = "App Paths 启动路径残留。",
                    RegistryHive = GetHiveName(hive),
                    RegistryKeyPath = $@"{parentKeyPath}\{subKeyName}",
                    MatchReason = "启动路径仍指向将被清理的程序目录。"
                });
        }
    }

    private void AddStartupValueCandidates(Dictionary<string, ResidueAction> actions, RegistryKey hive, string keyPath, CleanupIdentity identity)
    {
        using var key = hive.OpenSubKey(keyPath);
        if (key is null)
        {
            return;
        }

        foreach (var valueName in key.GetValueNames())
        {
            var command = key.GetValue(valueName)?.ToString() ?? string.Empty;
            if (!MatchesIdentity(identity, valueName, command))
            {
                continue;
            }

            AddResidueAction(
                actions,
                new ResidueAction
                {
                    Identity = identity,
                    Kind = ResidueActionKind.StartupValue,
                    DisplayName = valueName,
                    Target = $@"{GetHiveName(hive)}\{keyPath} -> {valueName}",
                    Description = "开机自启动残留。",
                    RegistryHive = GetHiveName(hive),
                    RegistryKeyPath = keyPath,
                    RegistryValueName = valueName,
                    Command = command,
                    MatchReason = "启动命令仍指向将被清理的目录或程序。"
                });
        }
    }

    private void AddSoftwareHiveResidueActions(Dictionary<string, ResidueAction> actions, RegistryKey hive, string parentKeyPath, CleanupIdentity identity)
    {
        using var parent = hive.OpenSubKey(parentKeyPath);
        if (parent is null)
        {
            return;
        }

        var productNames = GetRegistryProductAliases(identity)
            .Take(32)
            .ToList();
        var vendorNames = GetRegistryVendorAliases(identity)
            .Take(16)
            .ToList();

        foreach (var productName in productNames)
        {
            TryAddSoftwareRegistryResidueAction(actions, hive, parentKeyPath, productName, identity);
        }

        foreach (var vendorName in vendorNames)
        {
            using var vendorKey = parent.OpenSubKey(vendorName);
            if (vendorKey is null)
            {
                continue;
            }

            foreach (var productName in productNames)
            {
                TryAddSoftwareRegistryResidueAction(actions, hive, $@"{parentKeyPath}\{vendorName}", productName, identity);
            }
        }
    }

    private void TryAddSoftwareRegistryResidueAction(
        Dictionary<string, ResidueAction> actions,
        RegistryKey hive,
        string parentKeyPath,
        string subKeyName,
        CleanupIdentity identity)
    {
        using var candidateKey = hive.OpenSubKey($@"{parentKeyPath}\{subKeyName}");
        if (candidateKey is null || !RegistryKeyStrongMatch(identity, candidateKey, subKeyName))
        {
            return;
        }

        AddResidueAction(
            actions,
            new ResidueAction
            {
                Identity = identity,
                Kind = ResidueActionKind.RegistryKey,
                DisplayName = subKeyName,
                Target = $@"{GetHiveName(hive)}\{parentKeyPath}\{subKeyName}",
                Description = "应用设置和产品级注册表残留。",
                RegistryHive = GetHiveName(hive),
                RegistryKeyPath = $@"{parentKeyPath}\{subKeyName}",
                MatchReason = "产品级注册表键与应用身份强匹配。"
            });
    }

    private void AddShortcutResidueActions(Dictionary<string, ResidueAction> actions, CleanupIdentity identity, ResidueProbeCache cache)
    {
        foreach (var shortcut in GetShortcutEntries(cache))
        {
            if (!MatchesIdentity(identity, shortcut.TargetPath, shortcut.ShortcutPath, shortcut.DisplayName))
            {
                continue;
            }

            AddResidueAction(
                actions,
                new ResidueAction
                {
                    Identity = identity,
                    Kind = ResidueActionKind.ShortcutFile,
                    DisplayName = shortcut.DisplayName,
                    Target = shortcut.ShortcutPath,
                    Description = "桌面或开始菜单中仍指向旧程序路径的快捷方式。",
                    MatchReason = "快捷方式目标仍指向将被清理的路径。"
                });
        }
    }

    private void AddScheduledTaskResidueActions(Dictionary<string, ResidueAction> actions, CleanupIdentity identity, ResidueProbeCache cache)
    {
        foreach (var task in GetScheduledTaskEntries(cache))
        {
            if (!MatchesArtifactIdentity(identity, task.Execute, task.Arguments, task.DisplayName))
            {
                continue;
            }

            AddResidueAction(
                actions,
                new ResidueAction
                {
                    Identity = identity,
                    Kind = ResidueActionKind.ScheduledTask,
                    DisplayName = task.DisplayName,
                    Target = task.DisplayName,
                    Description = "计划任务仍指向旧程序路径。",
                    Command = task.Execute,
                    MatchReason = "任务动作执行路径仍指向将被清理的程序。"
                });
        }
    }

    private void AddServiceResidueActions(Dictionary<string, ResidueAction> actions, CleanupIdentity identity, ResidueProbeCache cache)
    {
        foreach (var service in GetServiceEntries(cache))
        {
            if (!MatchesArtifactIdentity(identity, service.PathName, service.DisplayName, service.Name))
            {
                continue;
            }

            AddResidueAction(
                actions,
                new ResidueAction
                {
                    Identity = identity,
                    Kind = ResidueActionKind.Service,
                    DisplayName = string.IsNullOrWhiteSpace(service.DisplayName) ? service.Name : service.DisplayName,
                    Target = service.Name,
                    Description = "Windows 服务仍指向旧程序路径。",
                    Command = service.PathName,
                    MatchReason = "服务二进制路径仍指向将被清理的程序。"
                });
        }
    }

    private void AddFirewallResidueActions(Dictionary<string, ResidueAction> actions, CleanupIdentity identity, ResidueProbeCache cache)
    {
        foreach (var rule in GetFirewallEntries(cache))
        {
            if (!MatchesArtifactIdentity(identity, rule.Program, rule.DisplayName, rule.Name))
            {
                continue;
            }

            AddResidueAction(
                actions,
                new ResidueAction
                {
                    Identity = identity,
                    Kind = ResidueActionKind.FirewallRule,
                    DisplayName = string.IsNullOrWhiteSpace(rule.DisplayName) ? rule.Name : rule.DisplayName,
                    Target = rule.Name,
                    Description = "防火墙规则仍绑定旧程序路径。",
                    Command = rule.Program,
                    MatchReason = "规则程序路径仍指向将被清理的可执行文件。"
                });
        }
    }

    private void AddKnownDataResidueActions(Dictionary<string, ResidueAction> actions, CleanupIdentity identity)
    {
        foreach (var path in GetKnownResidueDirectories(identity))
        {
            if (!Directory.Exists(path))
            {
                continue;
            }

            AddResidueAction(
                actions,
                new ResidueAction
                {
                    Identity = identity,
                    Kind = ResidueActionKind.Directory,
                    DisplayName = Path.GetFileName(path),
                    Target = path,
                    Description = "常见的应用数据残留目录。",
                    MatchReason = "目录名与应用身份强匹配。"
                });
        }
    }

    private ResidueAction ExecuteResidueAction(ResidueAction action)
    {
        try
        {
            switch (action.Kind)
            {
                case ResidueActionKind.RegistryKey:
                    DeleteRegistryKey(action.RegistryHive, action.RegistryKeyPath);
                    break;
                case ResidueActionKind.RegistryValue:
                case ResidueActionKind.StartupValue:
                    DeleteRegistryValue(action.RegistryHive, action.RegistryKeyPath, action.RegistryValueName);
                    break;
                case ResidueActionKind.ShortcutFile:
                case ResidueActionKind.File:
                    FileSystemHelper.DeletePathPermanent(action.Target);
                    break;
                case ResidueActionKind.Directory:
                    FileSystemHelper.DeletePathPermanent(action.Target);
                    break;
                case ResidueActionKind.ScheduledTask:
                    ShellHelper.RunProcessCapture("schtasks.exe", ["/Delete", "/TN", action.Target, "/F"]);
                    break;
                case ResidueActionKind.Service:
                    TryStopService(action.Target);
                    ShellHelper.RunProcessCapture("sc.exe", ["delete", action.Target], ignoreErrors: true);
                    break;
                case ResidueActionKind.FirewallRule:
                    ShellHelper.RunProcessCapture(
                        "powershell.exe",
                        ["-NoProfile", "-Command", $"Remove-NetFirewallRule -Name '{EscapePowerShellSingleQuoted(action.Target)}' -ErrorAction SilentlyContinue"],
                        ignoreErrors: true);
                    break;
            }
        }
        catch
        {
        }

        return CloneResidueAction(action, CheckResidueStillExists(action));
    }

    private ResidueVerificationResult VerifyResidues(
        IReadOnlyCollection<CleanupItem> items,
        IReadOnlyDictionary<Guid, InstalledAppProfile?> resolvedProfiles)
    {
        var remainingActions = PlanResidueActions(items, resolvedProfiles)
            .Where(CheckResidueStillExists)
            .OrderBy(action => action.KindText, StringComparer.OrdinalIgnoreCase)
            .ThenBy(action => action.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(action => action.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var remaining = remainingActions
            .Select(FormatResidualWarning)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ResidueVerificationResult
        {
            VerifiedClean = remainingActions.Count == 0,
            RemainingResidueCount = remainingActions.Count,
            RemainingResidues = remaining,
            RemainingActions = remainingActions,
            RemainingRegistryResidueCount = CountResiduesOfKinds(remainingActions, ResidueActionKind.RegistryKey, ResidueActionKind.RegistryValue, ResidueActionKind.StartupValue),
            RemainingShortcutResidueCount = CountResiduesOfKinds(remainingActions, ResidueActionKind.ShortcutFile),
            RemainingTaskResidueCount = CountResiduesOfKinds(remainingActions, ResidueActionKind.ScheduledTask),
            RemainingServiceResidueCount = CountResiduesOfKinds(remainingActions, ResidueActionKind.Service),
            RemainingFirewallResidueCount = CountResiduesOfKinds(remainingActions, ResidueActionKind.FirewallRule),
            RemainingDirectoryResidueCount = CountResiduesOfKinds(remainingActions, ResidueActionKind.Directory),
            RemainingFileResidueCount = CountResiduesOfKinds(remainingActions, ResidueActionKind.File)
        };
    }

    private static int CountResiduesOfKinds(IEnumerable<ResidueAction> actions, params ResidueActionKind[] kinds)
    {
        var kindSet = kinds.ToHashSet();
        return actions.Count(action => kindSet.Contains(action.Kind));
    }

    private static IReadOnlyList<string> BuildFollowUpRecommendations(
        IReadOnlyCollection<CleanupResult> results,
        ResidueVerificationResult residueVerification,
        IReadOnlyCollection<OfficialUninstallAttemptInfo> officialUninstallAttempts,
        IReadOnlyCollection<string> blockingProcesses)
    {
        var recommendations = new List<string>();

        if (blockingProcesses.Count > 0 || results.Any(result => result.RetrySuggested))
        {
            var processNames = blockingProcesses
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList();
            recommendations.Add(processNames.Count > 0
                ? $"仍有文件被占用。先彻底关闭这些程序和后台进程：{string.Join("、", processNames)}，然后再点一次“关闭占用并重试”。"
                : "仍有文件被占用。先把对应软件和后台进程彻底关掉，再点一次“关闭占用并重试”。");
        }

        var failedOfficialUninstalls = officialUninstallAttempts
            .Where(attempt => !attempt.Succeeded)
            .Select(attempt => string.IsNullOrWhiteSpace(attempt.AppDisplayName) ? attempt.ItemName : attempt.AppDisplayName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        if (failedOfficialUninstalls.Count > 0)
        {
            recommendations.Add($"有 {failedOfficialUninstalls.Count} 项官方卸载没有成功返回，例如：{string.Join("、", failedOfficialUninstalls)}。如果这类软件还留着服务、启动项或卸载记录，建议先用软件自己的卸载入口或控制面板正常卸载一次，再回来做残留清理。");
        }

        if (residueVerification.RemainingServiceResidueCount > 0)
        {
            recommendations.Add($"仍有 {residueVerification.RemainingServiceResidueCount} 项服务残留。常见原因是服务还没完全停掉或仍被系统持有；建议先重试一次，仍在的话重启电脑后再清。");
        }

        if (residueVerification.RemainingTaskResidueCount > 0)
        {
            recommendations.Add($"仍有 {residueVerification.RemainingTaskResidueCount} 项计划任务残留。通常可以直接再次深度清理一轮；如果刚刚的软件还在运行，它会立刻把任务重新写回来。");
        }

        if (residueVerification.RemainingFirewallResidueCount > 0)
        {
            recommendations.Add($"仍有 {residueVerification.RemainingFirewallResidueCount} 项防火墙规则残留。若对应程序目录已经没了，一般再执行一次深度清理就能带走；若程序还在后台运行，先完全退出它。");
        }

        if (residueVerification.RemainingRegistryResidueCount > 0)
        {
            recommendations.Add($"仍有 {residueVerification.RemainingRegistryResidueCount} 项注册表残留。优先看日志里的具体键值位置，确认它们确实指向已经删掉的程序目录后，再重试一次深度清理。");
        }

        if (residueVerification.RemainingDirectoryResidueCount > 0 || residueVerification.RemainingFileResidueCount > 0)
        {
            recommendations.Add($"仍有 {residueVerification.RemainingDirectoryResidueCount + residueVerification.RemainingFileResidueCount} 项文件/目录残留。若是缓存或日志目录，常见原因是程序刚刚又写回来了；先关程序，再清一次会更干净。");
        }

        if (recommendations.Count == 0 && results.Any(result => string.Equals(result.Status, "部分成功", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("本轮有部分成功项目，但没有检测到明确阻塞项。建议先查看日志里的剩余路径，确认是不是程序刚刚自动重建了缓存，再决定是否立即重试。");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("当前这一轮已经没有明显的后续处理建议。若你准备重装同名软件，可以先直接安装试试；若仍报冲突，再把本轮日志发出来继续补。");
        }

        return recommendations;
    }

    private static string FormatResidualWarning(ResidueAction action)
    {
        var target = string.IsNullOrWhiteSpace(action.Target) ? "未提供位置" : action.Target;
        var reason = string.IsNullOrWhiteSpace(action.MatchReason) ? action.Description : action.MatchReason;
        return $"{action.KindText}：{action.DisplayName} | {target} | 原因：{reason}";
    }

    private static void TryStopService(string serviceName)
    {
        try
        {
            ShellHelper.RunProcessCapture("sc.exe", ["stop", serviceName], ignoreErrors: true);
        }
        catch
        {
        }
    }

    private static void DeleteRegistryKey(string hiveName, string keyPath)
    {
        var hive = OpenHive(hiveName);
        hive.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
    }

    private static void DeleteRegistryValue(string hiveName, string keyPath, string valueName)
    {
        using var key = OpenHive(hiveName).OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static bool CheckResidueStillExists(ResidueAction action)
    {
        try
        {
            return action.Kind switch
            {
                ResidueActionKind.RegistryKey => OpenHive(action.RegistryHive).OpenSubKey(action.RegistryKeyPath) is not null,
                ResidueActionKind.RegistryValue or ResidueActionKind.StartupValue => OpenHive(action.RegistryHive).OpenSubKey(action.RegistryKeyPath)?.GetValue(action.RegistryValueName) is not null,
                ResidueActionKind.ShortcutFile or ResidueActionKind.File => File.Exists(action.Target),
                ResidueActionKind.Directory => Directory.Exists(action.Target),
                ResidueActionKind.ScheduledTask => ScheduledTaskExists(action.Target),
                ResidueActionKind.Service => ServiceExists(action.Target),
                ResidueActionKind.FirewallRule => FirewallRuleExists(action.Target),
                _ => false
            };
        }
        catch
        {
            return true;
        }
    }

    private static bool ScheduledTaskExists(string taskName)
    {
        var output = ShellHelper.RunProcessCapture("schtasks.exe", ["/Query", "/TN", taskName], ignoreErrors: true);
        return !output.Contains("ERROR:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ServiceExists(string serviceName)
    {
        var output = ShellHelper.RunProcessCapture("sc.exe", ["query", serviceName], ignoreErrors: true);
        return !output.Contains("指定的服务未安装", StringComparison.OrdinalIgnoreCase)
            && !output.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool FirewallRuleExists(string ruleName)
    {
        var output = ShellHelper.RunProcessCapture(
            "powershell.exe",
            ["-NoProfile", "-Command", $"$rule = Get-NetFirewallRule -Name '{EscapePowerShellSingleQuoted(ruleName)}' -ErrorAction SilentlyContinue; if ($null -eq $rule) {{ 'missing' }} else {{ 'present' }}"],
            ignoreErrors: true);
        return output.Contains("present", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveInstallLocation(
        string installLocation,
        string displayIcon,
        string quietUninstallString,
        string uninstallString,
        string modifyPath,
        string installSource)
    {
        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            return NormalizePath(installLocation);
        }

        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return string.Empty;
        }

        var iconPath = displayIcon.Trim().Trim('"');
        var commaIndex = iconPath.IndexOf(',');
        if (commaIndex >= 0)
        {
            iconPath = iconPath[..commaIndex];
        }

        if (File.Exists(iconPath))
        {
            return NormalizePath(Path.GetDirectoryName(iconPath) ?? string.Empty);
        }

        foreach (var fallbackDirectory in new[]
                 {
                     ResolveCommandDirectory(quietUninstallString),
                     ResolveCommandDirectory(uninstallString),
                     ResolveCommandDirectory(modifyPath),
                     ResolveExistingDirectoryPath(installSource)
                 })
        {
            if (!string.IsNullOrWhiteSpace(fallbackDirectory))
            {
                return fallbackDirectory;
            }
        }

        return string.Empty;
    }

    private static string NormalizeExecutablePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var clean = value.Trim().Trim('"');
        var commaIndex = clean.IndexOf(',');
        if (commaIndex >= 0)
        {
            clean = clean[..commaIndex];
        }

        return File.Exists(clean) ? NormalizePath(clean) : string.Empty;
    }

    private static string ResolveCommandDirectory(string commandLine)
    {
        var (fileName, _, _) = ParseCommandLine(commandLine);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return File.Exists(fileName)
            ? NormalizePath(Path.GetDirectoryName(fileName) ?? string.Empty)
            : string.Empty;
    }

    private static string BuildProfileIdentityKey(string displayName, string installLocation, string uninstallString)
    {
        var source = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : !string.IsNullOrWhiteSpace(installLocation) ? installLocation : uninstallString;
        return NormalizeIdentityKey(source);
    }

    private static string NormalizeIdentityKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch))
            .ToArray());
    }

    private static string ResolveExistingDirectoryPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizePath(value.Trim().Trim('"'));
        if (Directory.Exists(normalized))
        {
            return normalized;
        }

        if (File.Exists(normalized))
        {
            return NormalizePath(Path.GetDirectoryName(normalized) ?? string.Empty);
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> BuildOfficialUninstallCommands(
        string quietUninstallString,
        string uninstallString,
        string modifyPath)
    {
        var commands = new List<string>();
        AddOfficialUninstallCommand(commands, quietUninstallString, trustExistingCommand: true);
        AddOfficialUninstallCommand(commands, BuildQuietUninstallCommand(quietUninstallString));
        AddOfficialUninstallCommand(commands, BuildQuietUninstallCommand(uninstallString));
        AddOfficialUninstallCommand(commands, BuildQuietUninstallCommand(modifyPath));
        return commands;
    }

    private static void AddOfficialUninstallCommand(List<string> commands, string command, bool trustExistingCommand = false)
    {
        var normalized = trustExistingCommand
            ? NormalizeTrustedUninstallCommand(command)
            : command.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || commands.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var (fileName, _, _) = ParseCommandLine(normalized);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        commands.Add(normalized);
    }

    private static string NormalizeTrustedUninstallCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var trimmed = command.Trim();
        var (fileName, arguments, _) = ParseCommandLine(trimmed);
        return string.IsNullOrWhiteSpace(fileName)
            ? string.Empty
            : ComposeCommandLine(fileName, arguments);
    }

    private static string BuildQuietUninstallCommand(string uninstallString)
    {
        if (string.IsNullOrWhiteSpace(uninstallString))
        {
            return string.Empty;
        }

        var cleaned = uninstallString.Trim();
        if (ContainsQuietUninstallSwitch(cleaned))
        {
            return cleaned;
        }

        var (fileName, arguments, _) = ParseCommandLine(cleaned);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        if (cleaned.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = cleaned
                .Replace("/I", "/X", StringComparison.OrdinalIgnoreCase)
                .Replace("/i", "/X", StringComparison.OrdinalIgnoreCase);
            if (!normalized.Contains("/X", StringComparison.OrdinalIgnoreCase))
            {
                normalized += " /X";
            }

            if (!normalized.Contains("/qn", StringComparison.OrdinalIgnoreCase))
            {
                normalized += " /qn";
            }

            if (!normalized.Contains("/norestart", StringComparison.OrdinalIgnoreCase))
            {
                normalized += " /norestart";
            }

            return normalized;
        }

        var executableName = Path.GetFileName(fileName) ?? string.Empty;
        if (LooksLikeInnoUninstaller(executableName))
        {
            return ComposeCommandLine(fileName, AppendUninstallArguments(arguments, "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-"));
        }

        if (LooksLikeSquirrelUninstaller(executableName, arguments))
        {
            return ComposeCommandLine(fileName, AppendUninstallArguments(arguments, "-s"));
        }

        if (LooksLikeNsisUninstaller(executableName))
        {
            return ComposeCommandLine(fileName, AppendUninstallArguments(arguments, "/S"));
        }

        if (LooksLikeInstallShieldUninstaller(executableName, arguments))
        {
            return ComposeCommandLine(fileName, AppendUninstallArguments(arguments, "/s", "/v\"/qn REBOOT=ReallySuppress\""));
        }

        if (LooksLikeBurnBundleUninstaller(executableName, arguments))
        {
            return ComposeCommandLine(fileName, AppendUninstallArguments(arguments, "/quiet", "/norestart"));
        }

        return string.Empty;
    }

    private static bool ContainsQuietUninstallSwitch(string value)
    {
        var normalized = value.Replace('=', ' ');
        var tokens = normalized
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Any(token =>
            token.Equals("/quiet", StringComparison.OrdinalIgnoreCase)
            || token.Equals("-quiet", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/qn", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/qb", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/qb!", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/silent", StringComparison.OrdinalIgnoreCase)
            || token.Equals("-silent", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/verysilent", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/s", StringComparison.OrdinalIgnoreCase)
            || token.Equals("-s", StringComparison.OrdinalIgnoreCase)
            || token.Equals("/S", StringComparison.Ordinal)
            || token.Equals("--silent", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeInnoUninstaller(string executableName)
    {
        return executableName.StartsWith("unins", StringComparison.OrdinalIgnoreCase)
            && executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSquirrelUninstaller(string executableName, string arguments)
    {
        return executableName.Equals("Update.exe", StringComparison.OrdinalIgnoreCase)
            && arguments.Contains("--uninstall", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeNsisUninstaller(string executableName)
    {
        if (!executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return executableName.Equals("uninstall.exe", StringComparison.OrdinalIgnoreCase)
            || executableName.Equals("uninstaller.exe", StringComparison.OrdinalIgnoreCase)
            || executableName.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)
            || executableName.StartsWith("uninst", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeInstallShieldUninstaller(string executableName, string arguments)
    {
        if (!executableName.Equals("setup.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return arguments.Contains("removeonly", StringComparison.OrdinalIgnoreCase)
            || arguments.Contains("uninstall", StringComparison.OrdinalIgnoreCase)
            || arguments.Contains("maintenancemode", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeBurnBundleUninstaller(string executableName, string arguments)
    {
        if (!executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return arguments.Contains("/uninstall", StringComparison.OrdinalIgnoreCase)
            || arguments.Contains("-uninstall", StringComparison.OrdinalIgnoreCase)
            || arguments.Contains("--uninstall", StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendUninstallArguments(string existingArguments, params string[] argumentsToAdd)
    {
        var builder = new StringBuilder(existingArguments?.Trim() ?? string.Empty);
        foreach (var argument in argumentsToAdd.Where(argument => !string.IsNullOrWhiteSpace(argument)))
        {
            if (builder.ToString().Contains(argument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(argument);
        }

        return builder.ToString().Trim();
    }

    private static string ComposeCommandLine(string fileName, string arguments)
    {
        var quotedFileName = fileName.Contains(' ') && !fileName.StartsWith('"')
            ? $"\"{fileName}\""
            : fileName;
        return string.IsNullOrWhiteSpace(arguments)
            ? quotedFileName
            : $"{quotedFileName} {arguments.Trim()}";
    }

    private static bool IsDriverBoundProfile(string displayName, string publisher, string installLocation)
    {
        var combined = string.Join(" ", new[] { displayName, publisher, installLocation });
        return combined.Contains("nvidia", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("intel", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("oem", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("driver", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("anticheat", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("huorong", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> BuildExecutableNames(string installLocation, string displayIconPath, string uninstallDirectory)
    {
        var executableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(displayIconPath))
        {
            AddIfPresent(executableNames, Path.GetFileName(displayIconPath));
        }

        if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).Take(10))
                {
                    AddIfPresent(executableNames, Path.GetFileName(path));
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(uninstallDirectory) && Directory.Exists(uninstallDirectory))
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(uninstallDirectory, "*.exe", SearchOption.TopDirectoryOnly).Take(4))
                {
                    AddIfPresent(executableNames, Path.GetFileName(path));
                }
            }
            catch
            {
            }
        }

        return executableNames.ToList();
    }

    private static IReadOnlyList<string> GetCompanionPaths(
        string installLocation,
        string displayName,
        string publisher,
        string displayIconPath,
        string uninstallDirectory,
        string quietUninstallDirectory,
        string modifyDirectory,
        string installSource)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(candidates, installLocation);
        AddIfPresent(candidates, uninstallDirectory);
        AddIfPresent(candidates, quietUninstallDirectory);
        AddIfPresent(candidates, modifyDirectory);
        AddIfPresent(candidates, ResolveExistingDirectoryPath(installSource));
        if (!string.IsNullOrWhiteSpace(displayIconPath))
        {
            AddIfPresent(candidates, NormalizePath(Path.GetDirectoryName(displayIconPath) ?? string.Empty));
        }

        var parent = Path.GetDirectoryName(installLocation);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            foreach (var folderName in new[] { "Update", "Updates", "Updater" })
            {
                var updateFolder = Path.Combine(parent, folderName);
                if (Directory.Exists(updateFolder))
                {
                    AddIfPresent(candidates, NormalizePath(updateFolder));
                }
            }
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow")
                 }.Where(Directory.Exists))
        {
            foreach (var name in BuildResidueNameVariants(displayName, publisher, installLocation))
            {
                var direct = Path.Combine(root, name);
                if (Directory.Exists(direct))
                {
                    AddIfPresent(candidates, NormalizePath(direct));
                }
            }
        }

        return candidates.ToList();
    }

    private static IEnumerable<string> BuildResidueNameVariants(string displayName, string publisher, string installLocation)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[] { displayName, publisher, Path.GetFileName(installLocation) })
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            values.Add(raw.Trim());
            values.Add(raw.Replace(" ", string.Empty));
            values.Add(raw.Replace("-", string.Empty));
            foreach (var token in SplitKeywords(raw))
            {
                values.Add(token);
            }
        }

        foreach (var alias in GetKnownProductAliases(displayName, publisher, installLocation))
        {
            values.Add(alias);
            values.Add(alias.Replace(" ", string.Empty));
            values.Add(alias.Replace("-", string.Empty));
            foreach (var token in SplitKeywords(alias))
            {
                values.Add(token);
            }
        }

        var pathSegments = GetPathIdentitySegments(installLocation).ToList();
        foreach (var segment in pathSegments)
        {
            values.Add(segment);
        }

        if (pathSegments.Count >= 2)
        {
            values.Add(Path.Combine(pathSegments[^2], pathSegments[^1]));
        }

        try
        {
            if (Directory.Exists(installLocation))
            {
                foreach (var executablePath in Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).Take(8))
                {
                    var executableName = Path.GetFileNameWithoutExtension(executablePath);
                    if (string.IsNullOrWhiteSpace(executableName))
                    {
                        continue;
                    }

                    values.Add(executableName);
                    foreach (var token in SplitKeywords(executableName))
                    {
                        values.Add(token);
                    }
                }
            }
        }
        catch
        {
        }

        return values.Where(value => !string.IsNullOrWhiteSpace(value));
    }

    private static IEnumerable<string> GetKnownProductAliases(string displayName, string publisher, string installLocation)
    {
        var rawValues = new[] { displayName, publisher, Path.GetFileName(installLocation), installLocation };
        var normalizedValues = rawValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeIdentityKey)
            .ToList();
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAliases(params string[] aliases)
        {
            foreach (var alias in aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                results.Add(alias);
            }
        }

        if (normalizedValues.Any(value => value.Contains("wechat", StringComparison.OrdinalIgnoreCase)
            || value.Contains("weixin", StringComparison.OrdinalIgnoreCase)
            || value.Contains("微信", StringComparison.OrdinalIgnoreCase)
            || value.Contains("xwechat", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("WeChat", "Weixin", "xwechat", "微信");
        }

        if (normalizedValues.Any(value => value.Contains("qqmusic", StringComparison.OrdinalIgnoreCase) || value.Contains("qq音乐", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("QQMusic", "QQ音乐");
        }

        if (normalizedValues.Any(value => value.Contains("qqlive", StringComparison.OrdinalIgnoreCase) || value.Contains("腾讯视频", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("QQLive", "腾讯视频");
        }

        if (normalizedValues.Any(value => value.Contains("wetype", StringComparison.OrdinalIgnoreCase) || value.Contains("腾讯输入法", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("WeType", "腾讯输入法");
        }

        if (normalizedValues.Any(value => value.Equals("qq", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("qq", StringComparison.OrdinalIgnoreCase)
            || value.Contains("腾讯qq", StringComparison.OrdinalIgnoreCase)
            || value.Contains("qqnt", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("QQ", "QQNT", "腾讯QQ");
        }

        if (normalizedValues.Any(value => value.Contains("qqguild", StringComparison.OrdinalIgnoreCase)
            || value.Contains("qq_guild", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("QQGuild", "QQ Guild", "qq_guild");
        }

        if (normalizedValues.Any(value => value.Contains("yuanbao", StringComparison.OrdinalIgnoreCase)
            || value.Contains("元宝", StringComparison.OrdinalIgnoreCase)
            || value.Contains("comtencentyuanbao", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Yuanbao", "元宝", "com.tencent.yuanbao");
        }

        if (normalizedValues.Any(value => value.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("neteasecloudmusic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("网易云音乐", StringComparison.OrdinalIgnoreCase)
            || value.Contains("网易云", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("CloudMusic", "NeteaseCloudMusic", "网易云音乐");
        }

        if (normalizedValues.Any(value => value.Contains("bilibili", StringComparison.OrdinalIgnoreCase) || value.Contains("哔哩哔哩", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("bilibili", "哔哩哔哩");
        }

        if (normalizedValues.Any(value => value.Contains("pycharm", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("PyCharm", "pycharm64");
        }

        if (normalizedValues.Any(value => value.Contains("intellijidea", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ideaic", StringComparison.OrdinalIgnoreCase)
            || value.Equals("idea", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("IntelliJ IDEA", "IntelliJIdea", "idea64");
        }

        if (normalizedValues.Any(value => value.Contains("webstorm", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("WebStorm", "webstorm64");
        }

        if (normalizedValues.Any(value => value.Contains("rider", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Rider", "rider64");
        }

        if (normalizedValues.Any(value => value.Contains("clion", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("CLion", "clion64");
        }

        if (normalizedValues.Any(value => value.Contains("goland", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("GoLand", "goland64");
        }

        if (normalizedValues.Any(value => value.Contains("datagrip", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("DataGrip", "datagrip64");
        }

        if (normalizedValues.Any(value => value.Contains("visualstudiocode", StringComparison.OrdinalIgnoreCase)
            || value.Contains("vscode", StringComparison.OrdinalIgnoreCase)
            || value.Equals("code", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Code", "VSCode", "Visual Studio Code", "Code - Insiders");
        }

        if (normalizedValues.Any(value => value.Contains("cursor", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Cursor", ".cursor");
        }

        if (normalizedValues.Any(value => value.Contains("githubdesktop", StringComparison.OrdinalIgnoreCase)
            || value.Contains("github desktop", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("GitHub Desktop", "GitHubDesktop");
        }

        if (normalizedValues.Any(value => value.Contains("clashverge", StringComparison.OrdinalIgnoreCase)
            || value.Contains("clashforwindows", StringComparison.OrdinalIgnoreCase)
            || value.Equals("clash", StringComparison.OrdinalIgnoreCase)
            || value.Contains("clash", StringComparison.OrdinalIgnoreCase)
            || value.Contains("flclash", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Clash", "Clash Verge", "ClashVerge", "Clash for Windows", "clash_win", "FlClash", "io.github.clash-verge-rev.clash-verge-rev");
        }

        if (normalizedValues.Any(value => value.Equals("git", StringComparison.OrdinalIgnoreCase)
            || value.Contains("\\git\\", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("git", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Git");
        }

        if (normalizedValues.Any(value => value.Contains("steam", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Steam", "steamwebhelper");
        }

        if (normalizedValues.Any(value => value.Contains("sunlogin", StringComparison.OrdinalIgnoreCase)
            || value.Contains("向日葵", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Sunlogin", "向日葵");
        }

        if (normalizedValues.Any(value => value.Contains("douyin", StringComparison.OrdinalIgnoreCase)
            || value.Contains("抖音", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Douyin", "抖音");
        }

        if (normalizedValues.Any(value => value.Equals("python", StringComparison.OrdinalIgnoreCase)
            || value.Contains("python", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Python", "python", "pythonw", "py");
        }

        return results;
    }

    private static IEnumerable<string> GetKnownVendorAliases(string displayName, string publisher, string installLocation)
    {
        var rawValues = new[] { displayName, publisher, Path.GetFileName(installLocation), installLocation };
        var normalizedValues = rawValues
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeIdentityKey)
            .ToList();
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAliases(params string[] aliases)
        {
            foreach (var alias in aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)))
            {
                results.Add(alias);
            }
        }

        if (normalizedValues.Any(value => value.Contains("wechat", StringComparison.OrdinalIgnoreCase)
            || value.Contains("qqmusic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("qqlive", StringComparison.OrdinalIgnoreCase)
            || value.Equals("qq", StringComparison.OrdinalIgnoreCase)
            || value.Contains("wetype", StringComparison.OrdinalIgnoreCase)
            || value.Contains("tencent", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Tencent", "腾讯");
        }

        if (normalizedValues.Any(value => value.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase)
            || value.Contains("netease", StringComparison.OrdinalIgnoreCase)
            || value.Contains("网易", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("NetEase", "网易");
        }

        if (normalizedValues.Any(value => value.Contains("jetbrains", StringComparison.OrdinalIgnoreCase)
            || value.Contains("pycharm", StringComparison.OrdinalIgnoreCase)
            || value.Contains("intellij", StringComparison.OrdinalIgnoreCase)
            || value.Contains("webstorm", StringComparison.OrdinalIgnoreCase)
            || value.Contains("goland", StringComparison.OrdinalIgnoreCase)
            || value.Contains("datagrip", StringComparison.OrdinalIgnoreCase)
            || value.Contains("rider", StringComparison.OrdinalIgnoreCase)
            || value.Contains("clion", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("JetBrains");
        }

        if (normalizedValues.Any(value => value.Contains("visualstudiocode", StringComparison.OrdinalIgnoreCase)
            || value.Contains("vscode", StringComparison.OrdinalIgnoreCase)
            || value.Equals("code", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Microsoft");
        }

        if (normalizedValues.Any(value => value.Contains("githubdesktop", StringComparison.OrdinalIgnoreCase)
            || value.Contains("github", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("GitHub");
        }

        if (normalizedValues.Any(value => value.Contains("clash", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Clash");
        }

        if (normalizedValues.Any(value => value.Contains("cursor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("anysphere", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Anysphere");
        }

        if (normalizedValues.Any(value => value.Contains("bilibili", StringComparison.OrdinalIgnoreCase)
            || value.Contains("哔哩哔哩", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("bilibili");
        }

        if (normalizedValues.Any(value => value.Equals("git", StringComparison.OrdinalIgnoreCase)
            || value.Contains("\\git\\", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("git", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Git");
        }

        if (normalizedValues.Any(value => value.Contains("steam", StringComparison.OrdinalIgnoreCase)
            || value.Contains("valve", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Valve");
        }

        if (normalizedValues.Any(value => value.Contains("sunlogin", StringComparison.OrdinalIgnoreCase)
            || value.Contains("向日葵", StringComparison.OrdinalIgnoreCase)
            || value.Contains("oray", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Oray", "Sunlogin", "向日葵");
        }

        if (normalizedValues.Any(value => value.Contains("douyin", StringComparison.OrdinalIgnoreCase)
            || value.Contains("抖音", StringComparison.OrdinalIgnoreCase)
            || value.Contains("bytedance", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("ByteDance", "字节跳动", "Douyin", "抖音");
        }

        if (normalizedValues.Any(value => value.Contains("python", StringComparison.OrdinalIgnoreCase)))
        {
            AddAliases("Python Software Foundation", "Python");
        }

        return results;
    }

    private static int GetInstalledAppMatchScore(
        CleanupItem item,
        string itemPath,
        string itemName,
        string itemIdentityKey,
        string displayName,
        string installLocation,
        string displayIconPath,
        string quietUninstallString,
        string uninstallString,
        string uninstallDirectory,
        string quietUninstallDirectory,
        string modifyPath,
        string modifyDirectory,
        string publisher,
        string installSource,
        IReadOnlyCollection<string> companionPaths)
    {
        var score = 0;
        if (IsSamePathFamily(itemPath, installLocation))
        {
            score += 140;
        }

        if (companionPaths.Any(path => IsSamePathFamily(itemPath, path)))
        {
            score += 90;
        }

        if (IsSamePathFamily(itemPath, uninstallDirectory))
        {
            score += 80;
        }

        if (IsSamePathFamily(itemPath, quietUninstallDirectory))
        {
            score += 70;
        }

        if (IsSamePathFamily(itemPath, modifyDirectory))
        {
            score += 55;
        }

        if (IsSamePathFamily(itemPath, ResolveExistingDirectoryPath(installSource)))
        {
            score += 45;
        }

        if (!string.IsNullOrWhiteSpace(displayIconPath)
            && itemPath.Contains(Path.GetFileNameWithoutExtension(displayIconPath), StringComparison.OrdinalIgnoreCase))
        {
            score += 35;
        }

        var profileIdentityKey = BuildProfileIdentityKey(displayName, installLocation, uninstallString);
        if (!string.IsNullOrWhiteSpace(itemIdentityKey) && itemIdentityKey.Equals(profileIdentityKey, StringComparison.OrdinalIgnoreCase))
        {
            score += 55;
        }

        if (!string.IsNullOrWhiteSpace(displayName)
            && (displayName.Contains(itemName, StringComparison.OrdinalIgnoreCase)
                || itemName.Contains(displayName, StringComparison.OrdinalIgnoreCase)))
        {
            score += 35;
        }

        var itemKeywords = SplitKeywords(string.Join(" ", new[] { itemName, item.Name, item.InstalledAppId, Path.GetFileName(itemPath) }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateKeywords = SplitKeywords(string.Join(" ", new[]
            {
                displayName,
                publisher,
                Path.GetFileName(installLocation),
                Path.GetFileName(uninstallDirectory),
                Path.GetFileName(quietUninstallDirectory),
                Path.GetFileName(modifyDirectory),
                Path.GetFileName(ResolveExistingDirectoryPath(installSource)),
                quietUninstallString,
                uninstallString,
                modifyPath
            }))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sharedKeywords = itemKeywords.Intersect(candidateKeywords, StringComparer.OrdinalIgnoreCase).Count();
        if (sharedKeywords >= 2)
        {
            score += 30;
        }
        else if (sharedKeywords == 1)
        {
            score += 12;
        }

        return score;
    }

    private static bool IsSamePathFamily(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase)
            || right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    private static (string FileName, string Arguments, bool UseShellExecute) ParseCommandLine(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return (string.Empty, string.Empty, true);
        }

        var trimmed = commandLine.Trim();
        var argv = TrySplitCommandLineArguments(trimmed);
        if (argv.Count > 0)
        {
            var firstToken = argv[0];
            var remainingArguments = string.Join(" ", argv.Skip(1));
            if (LooksLikeCommandExecutable(firstToken))
            {
                return (firstToken, remainingArguments, false);
            }
        }

        if (trimmed.StartsWith('"'))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 0)
            {
                return (trimmed[1..closingQuote], trimmed[(closingQuote + 1)..].Trim(), false);
            }
        }

        if (TryResolveUnquotedExecutablePath(trimmed, out var executablePath, out var arguments))
        {
            return (executablePath, arguments, false);
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace < 0
            ? (trimmed, string.Empty, false)
            : (trimmed[..firstSpace], trimmed[(firstSpace + 1)..], false);
    }

    private static IReadOnlyList<string> TrySplitCommandLineArguments(string commandLine)
    {
        var results = new List<string>();
        nint argvPtr = nint.Zero;
        try
        {
            argvPtr = CommandLineToArgvW(commandLine, out var argc);
            if (argvPtr == nint.Zero || argc <= 0)
            {
                return results;
            }

            for (var i = 0; i < argc; i++)
            {
                var argumentPtr = Marshal.ReadIntPtr(argvPtr, i * IntPtr.Size);
                var argument = Marshal.PtrToStringUni(argumentPtr) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(argument))
                {
                    results.Add(argument.Trim());
                }
            }

            return results;
        }
        catch
        {
            return results;
        }
        finally
        {
            if (argvPtr != nint.Zero)
            {
                LocalFree(argvPtr);
            }
        }
    }

    private static bool TryResolveUnquotedExecutablePath(string commandLine, out string executablePath, out string arguments)
    {
        executablePath = string.Empty;
        arguments = string.Empty;

        foreach (var extension in new[] { ".exe", ".msi", ".cmd", ".bat", ".com" })
        {
            var index = commandLine.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var candidate = commandLine[..(index + extension.Length)].Trim().Trim('"');
                if (LooksLikeCommandExecutable(candidate))
                {
                    executablePath = candidate;
                    arguments = commandLine[(index + extension.Length)..].Trim();
                    return true;
                }

                index = commandLine.IndexOf(extension, index + extension.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static bool LooksLikeCommandExecutable(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().Trim('"');
        if (File.Exists(candidate))
        {
            return true;
        }

        return candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || candidate.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
            || candidate.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || candidate.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            || candidate.EndsWith(".com", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("msiexec", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("rundll32", StringComparison.OrdinalIgnoreCase)
            || candidate.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase);
    }

    private string WriteLog(
        IReadOnlyCollection<CleanupResult> results,
        IReadOnlyCollection<ResidueAction> residueActions,
        IReadOnlyCollection<OfficialUninstallAttemptInfo> officialUninstallAttempts,
        long freedBytes)
    {
        Directory.CreateDirectory(_context.LogsRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var jsonPath = Path.Combine(_context.LogsRoot, $"cleanup-{stamp}.json");
        var txtPath = Path.Combine(_context.LogsRoot, $"cleanup-{stamp}.txt");

        var payload = new
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            FreedBytes = freedBytes,
            FreedText = SizeFormatter.Format(freedBytes),
            Results = results,
            OfficialUninstallAttempts = officialUninstallAttempts,
            ResidueActions = residueActions
        };

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

        var sb = new StringBuilder();
        sb.AppendLine("便携式磁盘清理器清理记录");
        sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"实际释放: {SizeFormatter.Format(freedBytes)}");
        sb.AppendLine();

        if (officialUninstallAttempts.Count > 0)
        {
            sb.AppendLine("官方卸载器尝试：");
            foreach (var attempt in officialUninstallAttempts)
            {
                sb.AppendLine($"[{(attempt.Succeeded ? "成功" : "失败")}] {attempt.ItemName} | {attempt.AppDisplayName}");
                sb.AppendLine($"命令: {attempt.Command}");
                if (attempt.ExitCode.HasValue)
                {
                    sb.AppendLine($"退出码: {attempt.ExitCode.Value}");
                }

                sb.AppendLine($"耗时: {attempt.DurationMs} ms");
                if (!string.IsNullOrWhiteSpace(attempt.FailureReason))
                {
                    sb.AppendLine($"说明: {attempt.FailureReason}");
                }

                sb.AppendLine();
            }
        }

        foreach (var result in results)
        {
            sb.AppendLine($"[{result.Status}] {result.Name} | 原大小 {result.SizeText} | 实际释放 {result.FreedText} | {result.Path}");
            sb.AppendLine($"是否仍存在: {(result.ExistsAfter ? "是" : "否")} | 已删条目: {result.DeletedEntryCount} | 剩余条目: {result.RemainingEntryCount}");
            if (!string.IsNullOrWhiteSpace(result.RemainingText))
            {
                sb.AppendLine($"剩余未清理: {result.RemainingText}");
            }

            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                sb.AppendLine($"说明: {result.Message}");
            }

            sb.AppendLine();
        }

        if (residueActions.Count > 0)
        {
            sb.AppendLine("深度残留清理：");
            foreach (var action in residueActions)
            {
                sb.AppendLine($"[{action.KindText}] {action.DisplayName} | {action.Target} | 仍存在: {(action.ExistsAfter ? "是" : "否")}");
                if (!string.IsNullOrWhiteSpace(action.MatchReason))
                {
                    sb.AppendLine($"匹配原因: {action.MatchReason}");
                }
            }
        }

        var remainingResidues = residueActions
            .Where(action => action.ExistsAfter)
            .ToList();
        if (remainingResidues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"仍存在的残留汇总: {BuildResidueBreakdownSummary(remainingResidues)}");
        }

        var remainingResidueLines = remainingResidues
            .Select(action => $"{action.KindText}：{action.DisplayName} | {action.Target}")
            .ToList();
        if (remainingResidueLines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("仍存在的残留：");
            foreach (var residue in remainingResidueLines)
            {
                sb.AppendLine(residue);
            }
        }

        var followUpRecommendations = BuildFollowUpRecommendations(
            results,
            new ResidueVerificationResult
            {
                VerifiedClean = remainingResidues.Count == 0,
                RemainingResidueCount = remainingResidues.Count,
                RemainingResidues = remainingResidueLines,
                RemainingActions = remainingResidues,
                RemainingRegistryResidueCount = CountResiduesOfKinds(remainingResidues, ResidueActionKind.RegistryKey, ResidueActionKind.RegistryValue, ResidueActionKind.StartupValue),
                RemainingShortcutResidueCount = CountResiduesOfKinds(remainingResidues, ResidueActionKind.ShortcutFile),
                RemainingTaskResidueCount = CountResiduesOfKinds(remainingResidues, ResidueActionKind.ScheduledTask),
                RemainingServiceResidueCount = CountResiduesOfKinds(remainingResidues, ResidueActionKind.Service),
                RemainingFirewallResidueCount = CountResiduesOfKinds(remainingResidues, ResidueActionKind.FirewallRule),
                RemainingDirectoryResidueCount = CountResiduesOfKinds(remainingResidues, ResidueActionKind.Directory),
                RemainingFileResidueCount = CountResiduesOfKinds(remainingResidues, ResidueActionKind.File)
            },
            officialUninstallAttempts,
            results.SelectMany(result => result.BlockingProcessNames).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        if (followUpRecommendations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("建议下一步：");
            foreach (var recommendation in followUpRecommendations)
            {
                sb.AppendLine($"- {recommendation}");
            }
        }

        File.WriteAllText(txtPath, sb.ToString(), Encoding.UTF8);
        return txtPath;
    }

    private static bool IsPrivilegedPath(string path, string systemDriveRoot, string currentUser)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return false;
        }

        foreach (var rootFolder in ElevatedRootFolders)
        {
            var privilegedRoot = Path.Combine(Path.GetPathRoot(fullPath) ?? string.Empty, rootFolder);
            if (fullPath.StartsWith(privilegedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!fullPath.StartsWith(systemDriveRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fullPath.StartsWith(currentUser, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relativePath = fullPath[systemDriveRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relativePath.Length > 0 && !relativePath.Contains(Path.DirectorySeparatorChar);
    }

    private static void AddResidueAction(Dictionary<string, ResidueAction> actions, ResidueAction action)
    {
        var key = $"{action.Kind}|{action.Target}".ToUpperInvariant();
        actions.TryAdd(key, action);
    }

    private static string BuildResidueBreakdownSummary(IReadOnlyCollection<ResidueAction> actions)
    {
        var parts = new List<string>();

        void AddPart(string label, params ResidueActionKind[] kinds)
        {
            var count = CountResiduesOfKinds(actions, kinds);
            if (count > 0)
            {
                parts.Add($"{label} {count} 项");
            }
        }

        AddPart("注册表", ResidueActionKind.RegistryKey, ResidueActionKind.RegistryValue, ResidueActionKind.StartupValue);
        AddPart("快捷方式", ResidueActionKind.ShortcutFile);
        AddPart("计划任务", ResidueActionKind.ScheduledTask);
        AddPart("服务", ResidueActionKind.Service);
        AddPart("防火墙规则", ResidueActionKind.FirewallRule);
        AddPart("残留目录", ResidueActionKind.Directory);
        AddPart("残留文件", ResidueActionKind.File);

        return parts.Count == 0 ? "没有剩余残留" : string.Join("，", parts);
    }

    private static string GetBaseName(CleanupItem item)
    {
        var rawName = item.Name;
        if (File.Exists(item.Path))
        {
            rawName = Path.GetFileNameWithoutExtension(item.Path);
        }
        else if (!string.IsNullOrWhiteSpace(item.Path))
        {
            rawName = Path.GetFileName(item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        rawName = rawName.Replace("(x64)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(x86)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Portable", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        var duplicateIndex = rawName.LastIndexOf('(');
        if (duplicateIndex > 0 && rawName.EndsWith(')'))
        {
            var duplicateToken = rawName[(duplicateIndex + 1)..^1];
            if (int.TryParse(duplicateToken, out _))
            {
                rawName = rawName[..duplicateIndex].TrimEnd();
            }
        }

        return string.IsNullOrWhiteSpace(rawName) ? item.Name.Trim() : rawName;
    }

    private static List<string> SplitKeywords(string value)
    {
        return value
            .Split([' ', '-', '_', '.', '(', ')', '[', ']', '【', '】'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length >= 2)
            .Where(token => !GenericIdentityTokens.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool MatchesIdentity(CleanupIdentity identity, params string[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalized = NormalizePathLikeText(value);
            if (identity.KnownPaths.Any(path => normalized.Contains(path, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (identity.ExecutableNames.Any(exe => normalized.Contains(exe, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(identity.AppIdentityKey)
                && NormalizeIdentityKey(normalized).Contains(identity.AppIdentityKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (identity.DisplayAliases.Any(alias => normalized.Contains(alias, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var keywordHits = identity.Keywords.Count(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (keywordHits >= Math.Min(2, Math.Max(1, identity.Keywords.Count)))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(identity.DisplayName)
                && normalized.Contains(identity.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesArtifactIdentity(CleanupIdentity identity, params string[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (MatchesIdentityByKnownPath(identity, value)
                || MatchesIdentityByExecutable(identity, value))
            {
                return true;
            }
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (MatchesIdentityByStrongAlias(identity, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesIdentityByKnownPath(CleanupIdentity identity, string value)
    {
        foreach (var candidate in ExpandArtifactTexts(value))
        {
            if (identity.KnownPaths.Any(path => candidate.Contains(path, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesIdentityByExecutable(CleanupIdentity identity, string value)
    {
        var executableNames = identity.ExecutableNames
            .Select(executable => Path.GetFileName(executable) ?? executable)
            .Where(executable => !string.IsNullOrWhiteSpace(executable))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (executableNames.Count == 0)
        {
            return false;
        }

        foreach (var candidate in ExpandArtifactTexts(value))
        {
            if (executableNames.Any(executable => candidate.Contains(executable, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesIdentityByStrongAlias(CleanupIdentity identity, string value)
    {
        var candidates = ExpandArtifactTexts(value)
            .ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        var normalizedCandidates = candidates
            .Select(NormalizeIdentityKey)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var candidateTokens = candidates
            .SelectMany(SplitArtifactTokens)
            .Select(NormalizeIdentityKey)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(identity.AppIdentityKey)
            && normalizedCandidates.Any(candidate => candidate.Contains(identity.AppIdentityKey, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (var alias in GetHighConfidenceResidueAliases(identity))
        {
            var normalizedAlias = NormalizeIdentityKey(alias);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                continue;
            }

            if (normalizedAlias.Length <= 4)
            {
                if (candidateTokens.Contains(normalizedAlias))
                {
                    return true;
                }

                continue;
            }

            if (normalizedCandidates.Any(candidate =>
                    candidate.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase)
                    || candidate.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        var strongKeywordHits = identity.Keywords
            .Select(NormalizeIdentityKey)
            .Where(keyword => keyword.Length >= 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(keyword => normalizedCandidates.Any(candidate => candidate.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                || candidateTokens.Contains(keyword));
        return strongKeywordHits >= 2;
    }

    private static IEnumerable<string> ExpandArtifactTexts(string value)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddCandidate(string? candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                results.Add(NormalizePathLikeText(candidate));
            }
        }

        AddCandidate(value);
        var expanded = Environment.ExpandEnvironmentVariables(value);
        AddCandidate(expanded);

        foreach (var raw in new[] { value, expanded })
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var (fileName, arguments, _) = ParseCommandLine(raw);
            AddCandidate(fileName);
            AddCandidate(arguments);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                AddCandidate(Path.GetFileName(fileName));
                AddCandidate(Path.GetFileNameWithoutExtension(fileName));
                AddCandidate(Path.GetDirectoryName(fileName));
            }
        }

        return results;
    }

    private static IEnumerable<string> SplitArtifactTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var prepared = new StringBuilder(value.Length * 2);
        var previous = '\0';
        foreach (var current in value)
        {
            if (prepared.Length > 0
                && char.IsLetterOrDigit(current)
                && char.IsLetterOrDigit(previous)
                && ((char.IsLower(previous) && char.IsUpper(current))
                    || (char.IsLetter(previous) && char.IsDigit(current))
                    || (char.IsDigit(previous) && char.IsLetter(current))))
            {
                prepared.Append(' ');
            }

            prepared.Append(current);
            previous = current;
        }

        return prepared.ToString()
            .Split([' ', '-', '_', '.', '\\', '/', '(', ')', '[', ']', '{', '}', ':', ';', ',', '+', '='], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePathLikeText(string value)
    {
        var normalized = value.Replace("\"", string.Empty).Trim();
        if (normalized.StartsWith(@"\\?\"))
        {
            normalized = normalized[4..];
        }

        return normalized;
    }

    private static IEnumerable<string> GetKnownResidueDirectories(CleanupIdentity identity)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directNames = identity.DisplayAliases
            .Append(identity.DisplayName)
            .Concat(GetKnownProductAliases(identity.DisplayName, identity.Publisher, identity.RootPath))
            .Concat(GetFamilySpecificDirectResidueNames(identity))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .SelectMany(name => new[]
            {
                name,
                name.Replace(" ", string.Empty),
                name.Replace("-", string.Empty)
            })
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var pathSegments = identity.KnownPaths
            .SelectMany(GetPathIdentitySegments)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var vendorSegments = GetRegistryVendorAliases(identity)
            .Concat(pathSegments)
            .Where(segment => !identity.DisplayAliases.Contains(segment, StringComparer.OrdinalIgnoreCase))
            .Where(segment => segment.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var baseRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow")
        };

        foreach (var root in baseRoots.Where(Directory.Exists))
        {
            foreach (var name in directNames)
            {
                var direct = Path.Combine(root, name);
                if (Directory.Exists(direct))
                {
                    candidates.Add(direct);
                }
            }

            foreach (var vendor in vendorSegments)
            {
                foreach (var product in directNames.Where(name => !name.Equals(vendor, StringComparison.OrdinalIgnoreCase)))
                {
                    var nested = Path.Combine(root, vendor, product);
                    if (Directory.Exists(nested))
                    {
                        candidates.Add(nested);
                    }
                }
            }

            AddFamilyResidueDirectoryCandidates(candidates, identity, root);
        }

        return candidates;
    }

    private static void AddFamilyResidueDirectoryCandidates(HashSet<string> candidates, CleanupIdentity identity, string root)
    {
        foreach (var vendorRootName in GetFamilyVendorRoots(identity))
        {
            var vendorRoot = Path.Combine(root, vendorRootName);
            if (!Directory.Exists(vendorRoot))
            {
                continue;
            }

            try
            {
                foreach (var child in Directory.EnumerateDirectories(vendorRoot).Take(96))
                {
                    var childName = Path.GetFileName(child);
                    if (string.IsNullOrWhiteSpace(childName) || !IsStrongResidueDirectoryNameMatch(identity, childName))
                    {
                        continue;
                    }

                    candidates.Add(NormalizePath(child));
                }
            }
            catch
            {
            }
        }

        foreach (var explicitPath in GetFamilySpecificAbsoluteResiduePaths(identity))
        {
            if (Directory.Exists(explicitPath))
            {
                candidates.Add(NormalizePath(explicitPath));
            }
        }
    }

    private static IEnumerable<string> GetFamilyVendorRoots(CleanupIdentity identity)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IdentityBelongsToFamily(identity, "Tencent", "腾讯", "WeChat", "Weixin", "QQMusic", "QQLive", "WeType", "QQ", "QQNT", "Yuanbao", "QQGuild"))
        {
            roots.Add("Tencent");
            roots.Add("TENCENT");
        }

        if (IdentityBelongsToFamily(identity, "NetEase", "网易", "CloudMusic", "NeteaseCloudMusic"))
        {
            roots.Add("NetEase");
            roots.Add("Netease");
            roots.Add("网易");
            roots.Add("NeteaseWinDev");
        }

        if (IdentityBelongsToFamily(identity, "JetBrains", "PyCharm", "IntelliJ", "WebStorm", "GoLand", "Rider", "CLion", "DataGrip"))
        {
            roots.Add("JetBrains");
        }

        return roots;
    }

    private static IEnumerable<string> GetFamilySpecificDirectResidueNames(CleanupIdentity identity)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (IdentityBelongsToFamily(identity, "Visual Studio Code", "VSCode", "Microsoft VS Code"))
        {
            names.Add("Code");
            names.Add("Code - Insiders");
        }

        if (IdentityBelongsToFamily(identity, "Cursor"))
        {
            names.Add("Cursor");
        }

        if (IdentityBelongsToFamily(identity, "GitHub Desktop", "GitHubDesktop"))
        {
            names.Add("GitHub Desktop");
            names.Add("GitHubDesktop");
        }

        if (IdentityBelongsToFamily(identity, "Clash", "Clash Verge", "Clash for Windows", "FlClash"))
        {
            names.Add("clash_win");
            names.Add("Clash Verge");
            names.Add("Clash for Windows");
            names.Add("FlClash");
            names.Add("io.github.clash-verge-rev.clash-verge-rev");
        }

        if (IdentityBelongsToFamily(identity, "Yuanbao", "元宝"))
        {
            names.Add("com.tencent.yuanbao");
            names.Add("Yuanbao");
            names.Add("元宝");
        }

        if (IdentityBelongsToFamily(identity, "QQGuild", "QQ Guild", "qq_guild"))
        {
            names.Add("QQGuild");
            names.Add("qq_guild");
        }

        return names;
    }

    private static IEnumerable<string> GetFamilySpecificAbsoluteResiduePaths(CleanupIdentity identity)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            yield break;
        }

        if (IdentityBelongsToFamily(identity, "Visual Studio Code", "VSCode", "Microsoft VS Code"))
        {
            yield return Path.Combine(userProfile, ".vscode");
        }

        if (IdentityBelongsToFamily(identity, "Cursor"))
        {
            yield return Path.Combine(userProfile, ".cursor");
        }
    }

    private static bool IsStrongResidueDirectoryNameMatch(CleanupIdentity identity, string directoryName)
    {
        var normalizedDirectory = NormalizeIdentityKey(directoryName);
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(identity.AppIdentityKey)
            && normalizedDirectory.Contains(identity.AppIdentityKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var alias in GetHighConfidenceResidueAliases(identity))
        {
            var normalizedAlias = NormalizeIdentityKey(alias);
            if (string.IsNullOrWhiteSpace(normalizedAlias))
            {
                continue;
            }

            if (normalizedAlias.Length <= 2)
            {
                if (normalizedDirectory.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            if (normalizedDirectory.Equals(normalizedAlias, StringComparison.OrdinalIgnoreCase)
                || normalizedDirectory.StartsWith(normalizedAlias, StringComparison.OrdinalIgnoreCase)
                || normalizedDirectory.Contains(normalizedAlias, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var keywordHits = identity.Keywords
            .Select(NormalizeIdentityKey)
            .Where(keyword => keyword.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(keyword => normalizedDirectory.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        return keywordHits >= 2;
    }

    private static IEnumerable<string> GetHighConfidenceResidueAliases(CleanupIdentity identity)
    {
        return identity.DisplayAliases
            .Append(identity.DisplayName)
            .Concat(identity.ExecutableNames.Select(executable => Path.GetFileNameWithoutExtension(executable) ?? executable))
            .Concat(GetKnownProductAliases(identity.DisplayName, identity.Publisher, identity.RootPath))
            .Concat(GetFamilySpecificDirectResidueNames(identity))
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IdentityBelongsToFamily(CleanupIdentity identity, params string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return false;
        }

        var evidence = identity.DisplayAliases
            .Append(identity.DisplayName)
            .Append(identity.Publisher)
            .Append(identity.RootPath)
            .Concat(identity.KnownPaths.Select(path => Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty))
            .Concat(identity.ExecutableNames.Select(executable => Path.GetFileNameWithoutExtension(executable) ?? executable))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeIdentityKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var token in tokens)
        {
            var normalizedToken = NormalizeIdentityKey(token);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                continue;
            }

            if (evidence.Any(value => value.Contains(normalizedToken, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetRegistryProductAliases(CleanupIdentity identity)
    {
        return identity.DisplayAliases
            .Append(identity.DisplayName)
            .Concat(GetKnownProductAliases(identity.DisplayName, identity.Publisher, identity.RootPath))
            .SelectMany(alias => new[]
            {
                alias,
                alias.Replace(" ", string.Empty),
                alias.Replace("-", string.Empty)
            })
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Where(alias => !alias.Contains('\\') && !alias.Contains('/'))
            .Where(alias => alias.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetRegistryVendorAliases(CleanupIdentity identity)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(aliases, identity.Publisher);
        foreach (var alias in GetKnownVendorAliases(identity.DisplayName, identity.Publisher, identity.RootPath))
        {
            AddIfPresent(aliases, alias);
        }

        foreach (var segment in identity.KnownPaths.SelectMany(GetPathIdentitySegments))
        {
            if (segment.Length < 2 || identity.DisplayAliases.Contains(segment, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            AddIfPresent(aliases, segment);
        }

        return aliases
            .Where(alias => !alias.Contains('\\') && !alias.Contains('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool RegistryKeyStrongMatch(CleanupIdentity identity, RegistryKey key, string keyName)
    {
        var evidence = new List<string> { keyName, key.Name };

        try
        {
            evidence.AddRange(
                key.GetValueNames()
                    .Take(24)
                    .SelectMany(valueName => new[]
                    {
                        valueName,
                        key.GetValue(valueName)?.ToString() ?? string.Empty
                    }));
        }
        catch
        {
        }

        try
        {
            evidence.AddRange(key.GetSubKeyNames().Take(16));
        }
        catch
        {
        }

        if (MatchesIdentity(identity, evidence.ToArray()))
        {
            return true;
        }

        return identity.DisplayAliases.Any(alias => keyName.Equals(alias, StringComparison.OrdinalIgnoreCase))
            || keyName.Equals(identity.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHiveName(RegistryKey hive)
    {
        return hive.Name switch
        {
            "HKEY_CURRENT_USER" => "HKCU",
            "HKEY_LOCAL_MACHINE" => "HKLM",
            _ => hive.Name
        };
    }

    private static RegistryKey OpenHive(string hiveName)
    {
        return hiveName.ToUpperInvariant() switch
        {
            "HKCU" => Registry.CurrentUser,
            "HKLM" => Registry.LocalMachine,
            _ => throw new InvalidOperationException($"不支持的注册表根键: {hiveName}")
        };
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return value.Replace("'", "''");
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

    private static ResidueAction CloneResidueAction(ResidueAction action, bool existsAfter)
    {
        return new ResidueAction
        {
            Id = action.Id,
            Identity = action.Identity,
            Kind = action.Kind,
            DisplayName = action.DisplayName,
            Target = action.Target,
            Description = action.Description,
            RegistryHive = action.RegistryHive,
            RegistryKeyPath = action.RegistryKeyPath,
            RegistryValueName = action.RegistryValueName,
            Command = action.Command,
            MatchReason = action.MatchReason,
            ExistsAfter = existsAfter
        };
    }

    private static IReadOnlyList<ShortcutEntry> GetShortcutEntries(ResidueProbeCache cache)
    {
        return cache.Shortcuts ??= LoadShortcuts();
    }

    private static IReadOnlyList<ScheduledTaskEntry> GetScheduledTaskEntries(ResidueProbeCache cache)
    {
        return cache.ScheduledTasks ??= LoadShellJsonList<ScheduledTaskEntry>(
            "$ErrorActionPreference='SilentlyContinue'; Get-ScheduledTask | ForEach-Object { $first = $_.Actions | Select-Object -First 1; [pscustomobject]@{ DisplayName = ($_.TaskPath + $_.TaskName); Execute = $first.Execute; Arguments = $first.Arguments } } | ConvertTo-Json -Compress");
    }

    private static IReadOnlyList<ServiceEntry> GetServiceEntries(ResidueProbeCache cache)
    {
        return cache.Services ??= LoadShellJsonList<ServiceEntry>(
            "$ErrorActionPreference='SilentlyContinue'; Get-CimInstance Win32_Service | Select-Object Name, DisplayName, PathName | ConvertTo-Json -Compress");
    }

    private static IReadOnlyList<FirewallEntry> GetFirewallEntries(ResidueProbeCache cache)
    {
        return cache.FirewallRules ??= LoadShellJsonList<FirewallEntry>(
            "$ErrorActionPreference='SilentlyContinue'; Get-NetFirewallRule -PolicyStore ActiveStore | ForEach-Object { $rule = $_; $filters = Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $rule -ErrorAction SilentlyContinue; foreach ($filter in $filters) { [pscustomobject]@{ Name = $rule.Name; DisplayName = $rule.DisplayName; Program = $filter.Program } } } | ConvertTo-Json -Compress");
    }

    private static IReadOnlyList<T> LoadShellJsonList<T>(string script)
    {
        try
        {
            var json = ShellHelper.RunProcessCapture("powershell.exe", ["-NoProfile", "-Command", script], ignoreErrors: true);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => JsonSerializer.Deserialize<List<T>>(json) ?? [],
                JsonValueKind.Object => JsonSerializer.Deserialize<T>(json) is T item ? [item] : [],
                _ => []
            };
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<ShortcutEntry> LoadShortcuts()
    {
        var shortcutPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        }
        .Where(Directory.Exists)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        var results = new List<ShortcutEntry>();
        foreach (var root in shortcutPaths)
        {
            foreach (var file in FileSystemHelper.EnumerateFilesSafe(root, "*.lnk", recursive: true))
            {
                var target = ResolveShortcutTarget(file);
                if (string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }

                results.Add(new ShortcutEntry
                {
                    DisplayName = Path.GetFileNameWithoutExtension(file),
                    ShortcutPath = file,
                    TargetPath = target
                });
            }
        }

        return results;
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
            return (string)(shortcut.TargetPath ?? string.Empty);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record PathSnapshot(string Path, bool Exists, long SizeBytes, int TopLevelEntryCount);

    private sealed class ResidueProbeCache
    {
        public IReadOnlyList<ShortcutEntry>? Shortcuts { get; set; }
        public IReadOnlyList<ScheduledTaskEntry>? ScheduledTasks { get; set; }
        public IReadOnlyList<ServiceEntry>? Services { get; set; }
        public IReadOnlyList<FirewallEntry>? FirewallRules { get; set; }
    }

    private sealed class OfficialUninstallExecutionResult
    {
        public static OfficialUninstallExecutionResult Empty { get; } = new([], false);

        public OfficialUninstallExecutionResult(IReadOnlyList<OfficialUninstallAttemptInfo> attempts, bool succeeded)
        {
            Attempts = attempts;
            Succeeded = succeeded;
        }

        public IReadOnlyList<OfficialUninstallAttemptInfo> Attempts { get; }
        public bool Succeeded { get; }
    }

    private sealed class ShortcutEntry
    {
        public string DisplayName { get; init; } = string.Empty;
        public string ShortcutPath { get; init; } = string.Empty;
        public string TargetPath { get; init; } = string.Empty;
    }

    private sealed class ScheduledTaskEntry
    {
        public string DisplayName { get; init; } = string.Empty;
        public string Execute { get; init; } = string.Empty;
        public string Arguments { get; init; } = string.Empty;
    }

    private sealed class ServiceEntry
    {
        public string Name { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string PathName { get; init; } = string.Empty;
    }

    private sealed class FirewallEntry
    {
        public string Name { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string Program { get; init; } = string.Empty;
    }
}
