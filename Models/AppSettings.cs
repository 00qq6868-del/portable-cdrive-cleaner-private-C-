namespace PortableCDriveCleaner.Models;

public sealed class AppSettings
{
    public const int CurrentStartupCacheVersion = 5;
    public const int CurrentAdapterCatalogVersion = 1;

    public string AppName { get; set; } = "便携式磁盘清理器";
    public string Version { get; set; } = "1.0.0";
    public string LastViewMode { get; set; } = "CleanupCandidates";
    public bool AutoCleanupEnabled { get; set; } = true;
    public bool ScheduleEnabled { get; set; } = true;
    public bool PromptOnScheduledRun { get; set; } = false;
    public bool SafeItemsOnlyWhenScheduled { get; set; } = true;
    public int ScheduleIntervalHours { get; set; } = 1;
    public int MinimumPromptSizeMB { get; set; } = 256;
    public bool ScanDownloadsForDuplicates { get; set; } = true;
    public string ScheduledTaskName { get; set; } = "便携式磁盘清理器-每小时静默清理";
    public int MaxMemoryMB { get; set; } = 2048;
    public bool CreateDesktopShortcutOnInstall { get; set; } = true;
    public string PreferredInstallPath { get; set; } = string.Empty;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public bool DetailsPaneCollapsed { get; set; } = true;
    public bool StartupElevationPromptShown { get; set; }
    public int StartupCacheVersion { get; set; } = CurrentStartupCacheVersion;
    public DateTime? LastSnapshotUtc { get; set; }
    public string LastSnapshotPath { get; set; } = string.Empty;
    public bool WarmScanEnabled { get; set; } = true;
    public string ElevationMode { get; set; } = "StartupPrompt";
    public bool ElevatedLauncherRegistered { get; set; }
    public int MaxConcurrentJobs { get; set; } = 2;
    public int AdapterCatalogVersion { get; set; } = CurrentAdapterCatalogVersion;
    public List<string> WhitelistedPaths { get; set; } = [];
    public List<string> WhitelistedAppIdentityKeys { get; set; } = [];
    public string InfrequentAppsViewLastFilter { get; set; } = "Recommended";
    public string ElevatedLauncherTaskName { get; set; } = DeploymentDefaults.ElevatedLauncherTaskName;

    public void Normalize()
    {
        ScheduleIntervalHours = Math.Clamp(ScheduleIntervalHours, 1, 23);
        MinimumPromptSizeMB = Math.Clamp(MinimumPromptSizeMB, 32, 4096);
        MaxMemoryMB = Math.Clamp(MaxMemoryMB, 256, 2048);
        MaxConcurrentJobs = Math.Clamp(MaxConcurrentJobs, 1, 2);
        PromptOnScheduledRun = false;
        AppName = string.IsNullOrWhiteSpace(AppName) ? "便携式磁盘清理器" : AppName.Trim();
        if (AppName.Equals("便携式 C 盘清理器", StringComparison.OrdinalIgnoreCase)
            || AppName.Equals("便携式C盘清理器", StringComparison.OrdinalIgnoreCase))
        {
            AppName = "便携式磁盘清理器";
        }
        Version = string.IsNullOrWhiteSpace(Version) ? "1.0.0" : Version.Trim();
        LastViewMode = string.IsNullOrWhiteSpace(LastViewMode) ? "CleanupCandidates" : LastViewMode.Trim();
        ScheduledTaskName = string.IsNullOrWhiteSpace(ScheduledTaskName)
            ? "便携式磁盘清理器-每小时静默清理"
            : ScheduledTaskName.Trim();
        PreferredInstallPath = PreferredInstallPath?.Trim() ?? string.Empty;
        LastSnapshotPath = LastSnapshotPath?.Trim() ?? string.Empty;
        ElevationMode = string.IsNullOrWhiteSpace(ElevationMode) ? "StartupPrompt" : ElevationMode.Trim();
        ElevatedLauncherTaskName = string.IsNullOrWhiteSpace(ElevatedLauncherTaskName)
            ? DeploymentDefaults.ElevatedLauncherTaskName
            : ElevatedLauncherTaskName.Trim();
        StartupCacheVersion = CurrentStartupCacheVersion;
        AdapterCatalogVersion = CurrentAdapterCatalogVersion;
        WhitelistedPaths = (WhitelistedPaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        WhitelistedAppIdentityKeys = (WhitelistedAppIdentityKeys ?? [])
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        InfrequentAppsViewLastFilter = string.IsNullOrWhiteSpace(InfrequentAppsViewLastFilter)
            ? "Recommended"
            : InfrequentAppsViewLastFilter.Trim();
    }
}
