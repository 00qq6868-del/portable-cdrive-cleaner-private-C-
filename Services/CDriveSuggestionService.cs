using Microsoft.Win32;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class CDriveSuggestionService
{
    public IReadOnlyList<CDriveSuggestion> BuildSuggestions(IReadOnlyCollection<CleanupItem> items)
    {
        var results = new Dictionary<string, CDriveSuggestion>(StringComparer.OrdinalIgnoreCase);

        AddCleanupItemSuggestions(results, items);
        AddInstalledApplicationSuggestions(results);

        return results.Values
            .OrderBy(GetSourcePriority)
            .ThenByDescending(item => item.SizeBytes)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
    }

    private static void AddCleanupItemSuggestions(IDictionary<string, CDriveSuggestion> results, IReadOnlyCollection<CleanupItem> items)
    {
        foreach (var item in items.Where(item => item.DriveName.Equals("C", StringComparison.OrdinalIgnoreCase)))
        {
            CDriveSuggestion? suggestion = item.CandidateKind switch
            {
                CleanupCandidateKind.Package => new CDriveSuggestion
                {
                    SourceType = "安装包/压缩包",
                    Name = item.Name,
                    SizeBytes = item.SizeBytes,
                    CurrentPath = item.Path,
                    ActionText = "适合搬到 D / E；不用时可直接删",
                    ReasonText = "这类安装包通常只是安装介质，不影响已经装好的程序运行。"
                },
                CleanupCandidateKind.LargeFile => new CDriveSuggestion
                {
                    SourceType = "大文件",
                    Name = item.Name,
                    SizeBytes = item.SizeBytes,
                    CurrentPath = item.Path,
                    ActionText = "优先移动到 D / E，再决定是否删除",
                    ReasonText = "大文件放在 C 盘会持续占主系统盘空间，移走通常比直接删更稳妥。"
                },
                CleanupCandidateKind.LargeDirectory => new CDriveSuggestion
                {
                    SourceType = "大目录",
                    Name = item.Name,
                    SizeBytes = item.SizeBytes,
                    CurrentPath = item.Path,
                    ActionText = "如果是资料或安装包目录，建议整体迁到 D / E",
                    ReasonText = "这类大目录多数是用户内容，不属于系统运行必需项。"
                },
                CleanupCandidateKind.AppCache => new CDriveSuggestion
                {
                    SourceType = "应用缓存",
                    Name = item.Name,
                    SizeBytes = item.SizeBytes,
                    CurrentPath = item.Path,
                    ActionText = "可直接清；常用软件建议把缓存/下载位置改到 D / E",
                    ReasonText = "缓存删掉后一般会自动重建，不需要重新安装软件本体。"
                },
                CleanupCandidateKind.AppResidue => new CDriveSuggestion
                {
                    SourceType = "应用残留",
                    Name = item.Name,
                    SizeBytes = item.SizeBytes,
                    CurrentPath = item.Path,
                    ActionText = "确认不用后可删；下次安装建议直接装到 D / E",
                    ReasonText = "这是旧应用残留或便携目录候选，继续留在 C 盘意义通常不大。"
                },
                _ => null
            };

            if (suggestion is null)
            {
                continue;
            }

            var key = NormalizePath(item.Path) + "|" + suggestion.SourceType;
            results.TryAdd(key, suggestion);
        }
    }

    private static void AddInstalledApplicationSuggestions(IDictionary<string, CDriveSuggestion> results)
    {
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

                    var displayName = subKey.GetValue("DisplayName")?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(displayName) || ShouldSkipAppName(displayName))
                    {
                        continue;
                    }

                    var installLocation = subKey.GetValue("InstallLocation")?.ToString();
                    var displayIcon = subKey.GetValue("DisplayIcon")?.ToString();
                    var installPath = ResolveInstallPath(installLocation, displayIcon);
                    if (string.IsNullOrWhiteSpace(installPath) || !installPath.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var estimatedSizeKb = 0L;
                    try
                    {
                        estimatedSizeKb = Convert.ToInt64(subKey.GetValue("EstimatedSize") ?? 0);
                    }
                    catch
                    {
                    }

                    var publisher = subKey.GetValue("Publisher")?.ToString()?.Trim();
                    var suggestion = new CDriveSuggestion
                    {
                        SourceType = "已安装应用",
                        Name = displayName,
                        SizeBytes = Math.Max(estimatedSizeKb, 0) * 1024L,
                        CurrentPath = installPath,
                        ActionText = "如体积较大，建议卸载后重装到 D / E",
                        ReasonText = string.IsNullOrWhiteSpace(publisher)
                            ? "这是当前装在 C 盘的应用，迁到其他盘更容易给系统盘腾空间。"
                            : $"发布者：{publisher}。如果它不是系统组件，可考虑卸载后改装到 D / E。"
                    };

                    var keyText = NormalizePath(installPath) + "|installed";
                    results.TryAdd(keyText, suggestion);
                }
            }
        }
    }

    private static bool ShouldSkipAppName(string displayName)
    {
        return displayName.Contains("Microsoft Visual C++", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains(".NET", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Windows", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Update", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Security Update", StringComparison.OrdinalIgnoreCase)
            || displayName.Contains("Hotfix", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveInstallPath(string? installLocation, string? displayIcon)
    {
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            var normalized = NormalizePath(installLocation);
            if (!normalized.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
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

        if (File.Exists(clean))
        {
            var directory = Path.GetDirectoryName(clean) ?? string.Empty;
            var normalized = NormalizePath(directory);
            if (!normalized.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static int GetSourcePriority(CDriveSuggestion suggestion)
    {
        return suggestion.SourceType switch
        {
            "已安装应用" => 0,
            "大目录" => 1,
            "大文件" => 2,
            "安装包/压缩包" => 3,
            "应用缓存" => 4,
            "应用残留" => 5,
            _ => 9
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
}
