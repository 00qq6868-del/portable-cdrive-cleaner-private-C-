namespace PortableCDriveCleaner.Models;

public sealed class ResidueAction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public CleanupIdentity Identity { get; init; } = new();
    public ResidueActionKind Kind { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string RegistryHive { get; init; } = string.Empty;
    public string RegistryKeyPath { get; init; } = string.Empty;
    public string RegistryValueName { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string MatchReason { get; init; } = string.Empty;
    public bool ExistsAfter { get; init; }

    public string KindText => Kind switch
    {
        ResidueActionKind.RegistryKey => "注册表键",
        ResidueActionKind.RegistryValue => "注册表值",
        ResidueActionKind.StartupValue => "启动项",
        ResidueActionKind.ShortcutFile => "快捷方式",
        ResidueActionKind.ScheduledTask => "计划任务",
        ResidueActionKind.Service => "服务",
        ResidueActionKind.FirewallRule => "防火墙规则",
        ResidueActionKind.Directory => "残留目录",
        ResidueActionKind.File => "残留文件",
        _ => "残留项"
    };
}
