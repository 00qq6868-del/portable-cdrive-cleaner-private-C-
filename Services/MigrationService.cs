using System.Text;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class MigrationService
{
    private const string MigrationFolderName = "应用迁移";
    private const string CompanionFolderName = "_companions";
    private readonly PortableContext _context;

    public MigrationService(PortableContext context)
    {
        _context = context;
    }

    public MigrationPlan BuildPlan(IReadOnlyCollection<MigrationCandidate> candidates)
    {
        var selection = candidates
            .Where(candidate => candidate.MigrationMode == MigrationMode.AutoSafe)
            .DistinctBy(candidate => candidate.SourcePath)
            .ToList();

        if (selection.Count == 0)
        {
            throw new InvalidOperationException("当前没有可自动迁移的目录。");
        }

        var drive = GetPreferredTargetDrive()
            ?? throw new InvalidOperationException("没有找到可用的非 C 固定盘，暂时无法自动迁移。");

        var targetRoot = Path.Combine(drive.RootDirectory.FullName, MigrationFolderName);
        Directory.CreateDirectory(targetRoot);

        return new MigrationPlan
        {
            Candidates = selection,
            TargetRoot = targetRoot,
            TargetDriveName = drive.Name.TrimEnd(Path.DirectorySeparatorChar).TrimEnd(':')
        };
    }

    public MigrationRunResult Run(IReadOnlyCollection<MigrationCandidate> candidates, IProgress<OperationProgress>? progress = null)
    {
        var plan = BuildPlan(candidates);
        var results = new List<MigrationItemResult>();
        var ordered = plan.Candidates.OrderByDescending(item => item.SizeBytes).ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            var candidate = ordered[index];
            progress?.Report(new OperationProgress
            {
                Percent = ordered.Count == 0 ? 0 : index * 100 / Math.Max(ordered.Count, 1),
                Phase = "分析迁移计划",
                Message = $"正在分析 {candidate.Name}",
                IsIndeterminate = false
            });
            results.Add(MigrateCandidate(candidate, plan.TargetRoot, progress, index, ordered.Count));
        }

        var logPath = WriteLog(plan, results);
        progress?.Report(new OperationProgress
        {
            Percent = 100,
            Phase = "完成",
            Message = "迁移完成，正在更新结果",
            IsIndeterminate = false
        });
        return new MigrationRunResult
        {
            Results = results,
            TargetRoot = plan.TargetRoot,
            LogPath = logPath
        };
    }

    private MigrationItemResult MigrateCandidate(MigrationCandidate candidate, string targetRoot, IProgress<OperationProgress>? progress, int index, int total)
    {
        var sourcePath = NormalizePath(candidate.SourcePath);
        if (!Directory.Exists(sourcePath))
        {
            return BuildFailure(candidate, sourcePath, string.Empty, "原目录已经不存在，无法迁移。");
        }

        if (candidate.MigrationMode != MigrationMode.AutoSafe || !candidate.SelectionEnabled || candidate.MigrationStrategy == MigrationStrategy.Protected)
        {
            return BuildFailure(candidate, sourcePath, string.Empty, "这个目录当前只支持引导迁移，不能自动搬家。");
        }

        var targetPath = BuildUniqueTargetPath(targetRoot, candidate.Name);
        var operations = BuildPathOperations(candidate, sourcePath, targetPath);
        if (operations.Count == 0)
        {
            return BuildFailure(candidate, sourcePath, targetPath, "没有找到可执行迁移的目录。");
        }

        if (operations.Any(operation => !Directory.Exists(operation.SourcePath)))
        {
            return BuildFailure(candidate, sourcePath, targetPath, "有附属目录已经不存在，已停止这次迁移。");
        }

        var shortcutCount = 0;

        try
        {
            for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                var operation = operations[operationIndex];
                progress?.Report(new OperationProgress
                {
                    Percent = CalculateScopedPercent(index, total, 5, 45, operationIndex, operations.Count),
                    Phase = "复制目录",
                    Message = $"正在复制 {operation.DisplayName}",
                    IsIndeterminate = true
                });
                CopyDirectory(operation.SourcePath, operation.TargetPath);
                ValidateCopiedPath(operation.SourcePath, operation.TargetPath);
            }

            for (var operationIndex = 0; operationIndex < operations.Count; operationIndex++)
            {
                var operation = operations[operationIndex];
                progress?.Report(new OperationProgress
                {
                    Percent = CalculateScopedPercent(index, total, 45, 72, operationIndex, operations.Count),
                    Phase = "切换路径",
                    Message = $"正在切换 {operation.DisplayName} 到新位置",
                    IsIndeterminate = false
                });
                SwitchToJunction(operation);
            }

            progress?.Report(new OperationProgress
            {
                Percent = CalculatePercent(index, total, 78),
                Phase = "修正快捷方式",
                Message = $"正在更新 {candidate.Name} 的桌面和开始菜单入口",
                IsIndeterminate = false
            });
            foreach (var operation in operations)
            {
                shortcutCount += ShellHelper.UpdateShortcutsForRelocatedDirectory(operation.SourcePath, operation.TargetPath);
            }

            progress?.Report(new OperationProgress
            {
                Percent = CalculatePercent(index, total, 90),
                Phase = "验证迁移",
                Message = $"正在验证 {candidate.Name} 的迁移结果",
                IsIndeterminate = false
            });
            ValidateMigration(candidate, operations);
            CleanupBackups(operations);

            return new MigrationItemResult
            {
                CandidateId = candidate.Id,
                Name = candidate.Name,
                SourcePath = sourcePath,
                TargetPath = targetPath,
                SizeBytes = candidate.SizeBytes,
                Success = true,
                MigratedPathCount = operations.Count,
                UpdatedShortcutCount = shortcutCount,
                RolledBack = false,
                Message = BuildSuccessMessage(targetPath, operations.Count, shortcutCount)
            };
        }
        catch (Exception ex)
        {
            var rolledBack = TryRollback(operations, shortcutCount > 0);
            var message = rolledBack
                ? $"{ex.Message} 已自动回滚到原位置。"
                : $"{ex.Message} 自动回滚未完全成功，请先看日志再确认原目录和快捷方式状态。";
            return BuildFailure(candidate, sourcePath, targetPath, message, rolledBack);
        }
    }

    private static int CalculatePercent(int index, int total, int localPercent)
    {
        if (total <= 0)
        {
            return localPercent;
        }

        var basePercent = index * 100d / total;
        var slice = 100d / total;
        return (int)Math.Clamp(Math.Round(basePercent + slice * (localPercent / 100d)), 0, 100);
    }

    private static int CalculateScopedPercent(int index, int total, int startPercent, int endPercent, int itemIndex, int itemCount)
    {
        if (itemCount <= 0)
        {
            return CalculatePercent(index, total, endPercent);
        }

        var localPercent = startPercent + ((endPercent - startPercent) * (itemIndex + 1) / itemCount);
        return CalculatePercent(index, total, localPercent);
    }

    private static IReadOnlyList<MigrationPathOperation> BuildPathOperations(MigrationCandidate candidate, string sourcePath, string targetPath)
    {
        var reservedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        reservedTargets.Add(NormalizePath(targetPath));

        var operations = new List<MigrationPathOperation>
        {
            new(
                sourcePath,
                targetPath,
                BuildBackupPath(sourcePath),
                candidate.Name,
                true)
        };

        var companionRoot = Path.Combine(targetPath, CompanionFolderName);
        foreach (var companionPath in GetEligibleCompanionPaths(candidate, sourcePath))
        {
            var companionLabel = BuildCompanionLabel(companionPath);
            var companionTarget = BuildUniqueTargetPath(companionRoot, companionLabel, reservedTargets);
            operations.Add(new MigrationPathOperation(
                companionPath,
                companionTarget,
                BuildBackupPath(companionPath),
                $"{candidate.Name} 的附属目录 {Path.GetFileName(companionPath)}",
                false));
        }

        return operations;
    }

    private static IReadOnlyList<string> GetEligibleCompanionPaths(MigrationCandidate candidate, string sourcePath)
    {
        var normalizedSource = NormalizePath(sourcePath);
        var accepted = new List<string>();

        foreach (var path in candidate.CompanionPaths
                     .Select(NormalizePath)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Where(Directory.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path.Length)
                     .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (IsSameOrNestedPath(path, normalizedSource) || IsSameOrNestedPath(normalizedSource, path))
            {
                continue;
            }

            if (accepted.Any(existing => IsSameOrNestedPath(path, existing) || IsSameOrNestedPath(existing, path)))
            {
                continue;
            }

            accepted.Add(path);
        }

        return accepted;
    }

    private static string BuildCompanionLabel(string path)
    {
        var normalized = NormalizePath(path);
        var segments = normalized
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length >= 2)
        {
            return $"{segments[^2]}-{segments[^1]}";
        }

        return Path.GetFileName(normalized);
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        FileSystemHelper.CopyDirectoryContents(sourcePath, targetPath);
    }

    private static void ValidateCopiedPath(string sourcePath, string targetPath)
    {
        var sourceSize = FileSystemHelper.GetPathSizeBytes(sourcePath);
        var targetSize = FileSystemHelper.GetPathSizeBytes(targetPath);
        if (sourceSize > 0 && targetSize == 0)
        {
            throw new IOException($"复制“{Path.GetFileName(sourcePath)}”后目标目录为空，已停止迁移。");
        }
    }

    private static void SwitchToJunction(MigrationPathOperation operation)
    {
        Directory.Move(operation.SourcePath, operation.BackupPath);
        try
        {
            FileSystemHelper.CreateDirectoryJunction(operation.SourcePath, operation.TargetPath);
        }
        catch
        {
            Directory.Move(operation.BackupPath, operation.SourcePath);
            throw;
        }
    }

    private static void ValidateMigration(MigrationCandidate candidate, IReadOnlyCollection<MigrationPathOperation> operations)
    {
        foreach (var operation in operations)
        {
            if (!Directory.Exists(operation.SourcePath))
            {
                throw new IOException($"原路径兼容联接没有创建成功：{operation.SourcePath}");
            }

            if (!Directory.Exists(operation.TargetPath))
            {
                throw new IOException($"迁移后的目标目录不存在：{operation.TargetPath}");
            }

            if (!IsJunctionDirectory(operation.SourcePath))
            {
                throw new IOException($"原路径没有切换成兼容联接：{operation.SourcePath}");
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.ValidationTargetExe))
        {
            var hasValidationExe = operations
                .Where(operation => Directory.Exists(operation.TargetPath))
                .SelectMany(operation => Directory.EnumerateFiles(operation.TargetPath, candidate.ValidationTargetExe, SearchOption.AllDirectories))
                .Any();
            if (!hasValidationExe)
            {
                throw new IOException($"迁移后没有找到关键程序文件：{candidate.ValidationTargetExe}");
            }
        }
    }

    private static bool IsJunctionDirectory(string path)
    {
        try
        {
            return Directory.Exists(path)
                && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupBackups(IEnumerable<MigrationPathOperation> operations)
    {
        foreach (var operation in operations)
        {
            try
            {
                if (Directory.Exists(operation.BackupPath))
                {
                    FileSystemHelper.DeletePathPermanent(operation.BackupPath);
                }
            }
            catch
            {
            }
        }
    }

    private static bool TryRollback(IReadOnlyCollection<MigrationPathOperation> operations, bool shortcutsMayNeedRestore)
    {
        var restored = true;
        foreach (var operation in operations.Reverse())
        {
            try
            {
                if (Directory.Exists(operation.SourcePath))
                {
                    Directory.Delete(operation.SourcePath, false);
                }
            }
            catch
            {
                restored = false;
            }

            try
            {
                if (Directory.Exists(operation.BackupPath) && !Directory.Exists(operation.SourcePath))
                {
                    Directory.Move(operation.BackupPath, operation.SourcePath);
                }
            }
            catch
            {
                restored = false;
            }

            try
            {
                if (Directory.Exists(operation.TargetPath)
                    && Directory.Exists(operation.SourcePath)
                    && !Directory.Exists(operation.BackupPath))
                {
                    FileSystemHelper.DeletePathPermanent(operation.TargetPath);
                }
            }
            catch
            {
                restored = false;
            }

            if (!Directory.Exists(operation.SourcePath))
            {
                restored = false;
            }
        }

        if (shortcutsMayNeedRestore)
        {
            foreach (var operation in operations)
            {
                try
                {
                    ShellHelper.UpdateShortcutsForRelocatedDirectory(operation.TargetPath, operation.SourcePath);
                }
                catch
                {
                    restored = false;
                }
            }
        }

        return restored;
    }

    private string WriteLog(MigrationPlan plan, IReadOnlyCollection<MigrationItemResult> results)
    {
        PortableContext.EnsureDirectory(_context.LogsRoot);
        var path = Path.Combine(_context.LogsRoot, $"migration-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var builder = new StringBuilder();
        builder.AppendLine($"迁移目标: {plan.TargetRoot}");
        builder.AppendLine($"迁移项目: {plan.TotalCount} 项，总大小 {plan.TotalBytesText}");
        builder.AppendLine($"成功: {results.Count(result => result.Success)} 项，失败: {results.Count(result => !result.Success)} 项");
        builder.AppendLine($"实际迁移路径: {results.Where(result => result.Success).Sum(result => result.MigratedPathCount)} 处，修正快捷方式: {results.Sum(result => result.UpdatedShortcutCount)} 个");
        builder.AppendLine();

        foreach (var result in results)
        {
            builder.AppendLine($"{(result.Success ? "[成功]" : "[失败]")} {result.Name}");
            builder.AppendLine($"原路径: {result.SourcePath}");
            builder.AppendLine($"目标: {result.TargetPath}");
            builder.AppendLine($"迁移路径数: {result.MigratedPathCount}");
            builder.AppendLine($"快捷方式修正: {result.UpdatedShortcutCount}");
            builder.AppendLine($"是否回滚: {(result.RolledBack ? "是" : "否")}");
            builder.AppendLine($"说明: {result.Message}");
            builder.AppendLine();
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        return path;
    }

    private static MigrationItemResult BuildFailure(MigrationCandidate candidate, string sourcePath, string targetPath, string message, bool rolledBack = false)
    {
        return new MigrationItemResult
        {
            CandidateId = candidate.Id,
            Name = candidate.Name,
            SourcePath = sourcePath,
            TargetPath = targetPath,
            SizeBytes = candidate.SizeBytes,
            Success = false,
            Message = message,
            MigratedPathCount = 0,
            UpdatedShortcutCount = 0,
            RolledBack = rolledBack
        };
    }

    private static string BuildUniqueTargetPath(string targetRoot, string name, ISet<string>? reservedPaths = null)
    {
        var safeName = SanitizeName(name);
        var path = Path.Combine(targetRoot, safeName);
        var index = 2;
        while (Directory.Exists(path)
               || File.Exists(path)
               || (reservedPaths is not null && reservedPaths.Contains(NormalizePath(path))))
        {
            path = Path.Combine(targetRoot, $"{safeName}-{index}");
            index++;
        }

        reservedPaths?.Add(NormalizePath(path));
        return path;
    }

    private static string BuildBackupPath(string sourcePath)
    {
        var backupPath = sourcePath + $".portablecleaner-backup-{DateTime.Now:yyyyMMddHHmmss}";
        var suffix = 1;
        while (Directory.Exists(backupPath) || File.Exists(backupPath))
        {
            backupPath = sourcePath + $".portablecleaner-backup-{DateTime.Now:yyyyMMddHHmmss}-{suffix}";
            suffix++;
        }

        return backupPath;
    }

    private static string SanitizeName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder();
        foreach (var ch in name)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        var result = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? $"迁移目录-{Guid.NewGuid():N}" : result;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static DriveInfo? GetPreferredTargetDrive()
    {
        var systemDrive = (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\").TrimEnd(Path.DirectorySeparatorChar).TrimEnd(':');
        return DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Where(drive => !string.Equals(drive.Name.TrimEnd(Path.DirectorySeparatorChar).TrimEnd(':'), systemDrive, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(drive => drive.AvailableFreeSpace)
            .ThenByDescending(drive => drive.TotalSize)
            .FirstOrDefault();
    }

    private static bool IsSameOrNestedPath(string path, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        if (path.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(rootPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSuccessMessage(string targetPath, int migratedPathCount, int shortcutCount)
    {
        var pathMessage = migratedPathCount > 1
            ? $"本轮一共搬了 {migratedPathCount} 处目录（含附属目录）"
            : "本轮已搬走 1 处目录";
        return $"已迁移到 {targetPath}，并在原位置建立兼容联接。{pathMessage}，快捷方式同步更新 {shortcutCount} 个。";
    }

    private sealed record MigrationPathOperation(
        string SourcePath,
        string TargetPath,
        string BackupPath,
        string DisplayName,
        bool IsPrimary);
}
