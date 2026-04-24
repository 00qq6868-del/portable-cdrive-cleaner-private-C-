namespace PortableCDriveCleaner.Models;

public enum ResidueActionKind
{
    RegistryKey,
    RegistryValue,
    StartupValue,
    ShortcutFile,
    ScheduledTask,
    Service,
    FirewallRule,
    Directory,
    File
}
