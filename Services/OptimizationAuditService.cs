using Microsoft.Win32;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class OptimizationAuditService
{
    private static readonly string[] HighImpactTokens =
    [
        "updater", "update", "launcher", "helper", "tray", "agent", "service", "daemon",
        "sync", "cloud", "meeting", "game", "download", "browser", "electron"
    ];

    private static readonly string[] SafeSystemTokens =
    [
        "windows security", "defender", "microsoft", "onedrive", "realtek", "intel", "nvidia",
        "amd", "synaptics", "logitech", "steelseries", "razer", "driver"
    ];

    private static readonly string[] ReviewTokens =
    [
        "qq", "wechat", "weixin", "tencent", "netease", "cloudmusic", "bilibili", "steam",
        "epic", "discord", "telegram", "wps", "adobe", "baidu", "xunlei"
    ];

    public OptimizationAuditSnapshot BuildSnapshot()
    {
        var entries = new List<OptimizationAuditEntry>();
        AddRegistryStartupEntries(entries);
        AddStartupFolderEntries(entries);

        return new OptimizationAuditSnapshot
        {
            Entries = entries
                .DistinctBy(entry => $"{entry.Category}|{entry.Source}|{entry.Name}|{entry.CommandLine}", StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(entry => entry.ImpactLevel)
                .ThenByDescending(entry => entry.RecommendationLevel)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static void AddRegistryStartupEntries(ICollection<OptimizationAuditEntry> entries)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            var hiveName = hive == Registry.CurrentUser ? "HKCU" : "HKLM";
            foreach (var keyPath in new[]
                     {
                         @"Software\Microsoft\Windows\CurrentVersion\Run",
                         @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                         @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                         @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce"
                     })
            {
                using var key = hive.OpenSubKey(keyPath);
                if (key is null)
                {
                    continue;
                }

                foreach (var valueName in key.GetValueNames())
                {
                    var command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(command))
                    {
                        continue;
                    }

                    entries.Add(BuildStartupEntry(
                        valueName,
                        $"{hiveName}\\{keyPath}",
                        command));
                }
            }
        }
    }

    private static void AddStartupFolderEntries(ICollection<OptimizationAuditEntry> entries)
    {
        var startupFolders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
        };

        foreach (var folder in startupFolders.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
        {
            foreach (var path in EnumerateStartupFiles(folder))
            {
                entries.Add(BuildStartupEntry(
                    Path.GetFileNameWithoutExtension(path),
                    folder,
                    path));
            }
        }
    }

    private static IEnumerable<string> EnumerateStartupFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var extension = Path.GetExtension(path);
                    return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static OptimizationAuditEntry BuildStartupEntry(string name, string source, string commandLine)
    {
        var targetPath = ResolveCommandTargetPath(commandLine);
        var effectiveCommandLine = commandLine;
        if (targetPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
            && ShellHelper.TryReadShortcut(targetPath, out var shortcutTargetPath, out var shortcutArguments, out _))
        {
            targetPath = NormalizeExecutablePath(shortcutTargetPath);
            effectiveCommandLine = string.IsNullOrWhiteSpace(shortcutArguments)
                ? targetPath
                : $"\"{targetPath}\" {shortcutArguments}";
        }

        var heuristicText = $"{name} {effectiveCommandLine} {targetPath}";
        var impact = ResolveImpact(heuristicText);
        var recommendation = ResolveRecommendation(heuristicText, impact);
        return new OptimizationAuditEntry
        {
            Category = OptimizationAuditCategory.StartupItem,
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(targetPath) : name,
            Source = source,
            CommandLine = effectiveCommandLine,
            TargetPath = targetPath,
            PublisherHint = ResolvePublisherHint(heuristicText),
            ImpactLevel = impact,
            RecommendationLevel = recommendation,
            ReasonText = BuildReasonText(heuristicText, impact, recommendation),
            SafeActionText = BuildSafeActionText(recommendation)
        };
    }

    private static OptimizationAuditImpact ResolveImpact(string text)
    {
        if (ContainsAny(text, HighImpactTokens))
        {
            return OptimizationAuditImpact.High;
        }

        if (ContainsAny(text, SafeSystemTokens))
        {
            return OptimizationAuditImpact.Low;
        }

        return OptimizationAuditImpact.Medium;
    }

    private static OptimizationAuditRecommendation ResolveRecommendation(string text, OptimizationAuditImpact impact)
    {
        if (ContainsAny(text, SafeSystemTokens))
        {
            return OptimizationAuditRecommendation.Safe;
        }

        if (impact == OptimizationAuditImpact.High || ContainsAny(text, ReviewTokens))
        {
            return OptimizationAuditRecommendation.Review;
        }

        return OptimizationAuditRecommendation.Caution;
    }

    private static string ResolvePublisherHint(string text)
    {
        if (ContainsAny(text, "microsoft", "windows", "defender", "onedrive"))
        {
            return "Microsoft / Windows 相关";
        }

        if (ContainsAny(text, "intel", "nvidia", "amd", "realtek", "synaptics"))
        {
            return "硬件驱动 / 控制面板相关";
        }

        if (ContainsAny(text, "tencent", "wechat", "weixin", "qq"))
        {
            return "腾讯系软件";
        }

        if (ContainsAny(text, "adobe", "creative cloud"))
        {
            return "Adobe 软件";
        }

        return "第三方或用户自定义";
    }

    private static string BuildReasonText(string text, OptimizationAuditImpact impact, OptimizationAuditRecommendation recommendation)
    {
        if (ContainsAny(text, SafeSystemTokens))
        {
            return "看起来和系统、硬件驱动、安全组件或厂商控制面板有关，当前只读展示，不建议直接禁用。";
        }

        if (ContainsAny(text, "updater", "update"))
        {
            return "这是更新器或升级助手类启动项，可能拖慢开机；如果你不依赖自动更新，可以在软件设置里关闭。";
        }

        if (ContainsAny(text, "tray", "helper", "agent", "launcher"))
        {
            return "这是常驻托盘、助手、启动器或后台代理类启动项，可能影响开机速度和内存占用。";
        }

        if (ContainsAny(text, ReviewTokens))
        {
            return "这是常见第三方软件启动项，是否保留取决于你是否需要它开机自动运行。";
        }

        return impact == OptimizationAuditImpact.High || recommendation != OptimizationAuditRecommendation.Safe
            ? "这是需要人工确认的启动项。先确认来源和用途，再到软件设置或 Windows 启动项里处理。"
            : "当前没有发现明显异常，只做信息展示。";
    }

    private static string BuildSafeActionText(OptimizationAuditRecommendation recommendation)
    {
        return recommendation switch
        {
            OptimizationAuditRecommendation.Safe => "建议保留；如果要处理，也优先去软件官方设置里调整。",
            OptimizationAuditRecommendation.Review => "建议确认用途；需要关闭时优先使用任务管理器启动页或软件自己的开机启动设置。",
            _ => "谨慎处理；不要直接删注册表或启动文件，先备份并确认来源。"
        };
    }

    private static string ResolveCommandTargetPath(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return string.Empty;
        }

        var trimmed = commandLine.Trim();
        if ((trimmed.StartsWith('"') || trimmed.StartsWith('\'')) && trimmed.Length > 1)
        {
            var quote = trimmed[0];
            var close = trimmed.IndexOf(quote, 1);
            if (close > 1)
            {
                return NormalizeExecutablePath(trimmed[1..close]);
            }
        }

        var candidate = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return NormalizeExecutablePath(candidate);
    }

    private static string NormalizeExecutablePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var candidate = path.Trim().Trim('"');
        var commaIndex = candidate.LastIndexOf(',');
        if (commaIndex > 2)
        {
            candidate = candidate[..commaIndex];
        }

        try
        {
            candidate = Environment.ExpandEnvironmentVariables(candidate);
            return Path.GetFullPath(candidate);
        }
        catch
        {
            return candidate;
        }
    }

    private static bool ContainsAny(string text, params string[] tokens)
    {
        return ContainsAny(text, (IReadOnlyCollection<string>)tokens);
    }

    private static bool ContainsAny(string text, IReadOnlyCollection<string> tokens)
    {
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
