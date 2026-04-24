namespace PortableCDriveCleaner.Models;

public sealed class InstalledAppProfile
{
    public string AppIdentityKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string DisplayVersion { get; init; } = string.Empty;
    public string InstallLocation { get; init; } = string.Empty;
    public string InstallSource { get; init; } = string.Empty;
    public string DisplayIconPath { get; init; } = string.Empty;
    public string UninstallString { get; init; } = string.Empty;
    public string QuietUninstallString { get; init; } = string.Empty;
    public string ModifyPath { get; init; } = string.Empty;
    public string QuietUninstallCommand { get; init; } = string.Empty;
    public IReadOnlyList<string> OfficialUninstallCommands { get; init; } = [];
    public bool IsDriverBound { get; init; }
    public IReadOnlyList<string> ExecutableNames { get; init; } = [];
    public IReadOnlyList<string> CompanionPaths { get; init; } = [];
}
