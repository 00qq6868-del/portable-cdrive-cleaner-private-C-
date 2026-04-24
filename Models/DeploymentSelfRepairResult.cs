namespace PortableCDriveCleaner.Models;

public sealed class DeploymentSelfRepairResult
{
    public string TaskName { get; init; } = string.Empty;
    public bool ManagedCurrentExecutable { get; init; }
    public string ManagedExecutableNote { get; init; } = string.Empty;
    public bool DesktopShortcutManaged { get; init; }
    public bool ElevatedTaskAvailableBeforeRepair { get; init; }
    public bool ElevatedTaskAlignedBeforeRepair { get; init; }
    public bool ElevatedTaskAvailableAfterRepair { get; init; }
    public bool ElevatedTaskAlignedAfterRepair { get; init; }
    public bool ElevatedTaskRepaired { get; init; }
    public bool DesktopShortcutAlignedBeforeRepair { get; init; }
    public bool DesktopShortcutAlignedAfterRepair { get; init; }
    public bool DesktopShortcutRepaired { get; init; }
    public bool InstallFolderShortcutAlignedBeforeRepair { get; init; }
    public bool InstallFolderShortcutAlignedAfterRepair { get; init; }
    public bool InstallFolderShortcutRepaired { get; init; }
    public string ElevatedTaskDetail { get; init; } = string.Empty;
    public string DesktopShortcutDetail { get; init; } = string.Empty;
    public string InstallFolderShortcutDetail { get; init; } = string.Empty;

    public bool AnyRepairApplied =>
        ElevatedTaskRepaired || DesktopShortcutRepaired || InstallFolderShortcutRepaired;

    public bool LaunchChainAligned =>
        ElevatedTaskAlignedAfterRepair
        && InstallFolderShortcutAlignedAfterRepair
        && (!DesktopShortcutManaged || DesktopShortcutAlignedAfterRepair);
}
