using System.Security.Principal;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class SchedulerService
{
    private readonly PortableContext _context;

    public SchedulerService(PortableContext context)
    {
        _context = context;
    }

    public ScheduleInfo GetInfo(AppSettings settings)
    {
        try
        {
            var output = ShellHelper.RunProcessCapture("schtasks.exe", ["/Query", "/TN", settings.ScheduledTaskName, "/V", "/FO", "LIST"], true);
            if (string.IsNullOrWhiteSpace(output))
            {
                return new ScheduleInfo { Exists = false };
            }

            var map = output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(':', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

            return new ScheduleInfo
            {
                Exists = true,
                Status = map.GetValueOrDefault("Status") ?? "未知",
                NextRunTime = map.GetValueOrDefault("Next Run Time") ?? "-",
                LastResult = map.GetValueOrDefault("Last Result") ?? "-",
                RunAsUser = map.GetValueOrDefault("Run As User") ?? WindowsIdentity.GetCurrent().Name
            };
        }
        catch
        {
            return new ScheduleInfo { Exists = false };
        }
    }

    public ScheduleInfo Sync(PortableContext context, AppSettings settings)
    {
        if (!settings.AutoCleanupEnabled || !settings.ScheduleEnabled)
        {
            return Remove(settings);
        }

        var interval = Math.Clamp(settings.ScheduleIntervalHours, 1, 23);
        var startTime = DateTime.Now.AddMinutes(2).ToString("HH:mm");
        var currentUser = WindowsIdentity.GetCurrent().Name;
        var taskCommand = $"\"{context.EntryExecutablePath}\" --scheduled --no-prompt";

        ShellHelper.RunProcessCapture("schtasks.exe",
        [
            "/Create",
            "/TN", settings.ScheduledTaskName,
            "/TR", taskCommand,
            "/SC", "HOURLY",
            "/MO", interval.ToString(),
            "/ST", startTime,
            "/RL", "HIGHEST",
            "/IT",
            "/RU", currentUser,
            "/F"
        ], true);

        return GetInfo(settings);
    }

    public ScheduleInfo Remove(AppSettings settings)
    {
        try
        {
            ShellHelper.RunProcessCapture("schtasks.exe", ["/Delete", "/TN", settings.ScheduledTaskName, "/F"], true);
        }
        catch
        {
        }

        return GetInfo(settings);
    }

    public string GetStatusText(AppSettings settings)
    {
        var info = GetInfo(settings);
        if (!info.Exists)
        {
            return "未启用后台自动清理";
        }

        return $"状态: {info.Status} | 模式: 每小时静默后台清理 | 下次运行: {info.NextRunTime} | 上次结果: {info.LastResult}";
    }
}
