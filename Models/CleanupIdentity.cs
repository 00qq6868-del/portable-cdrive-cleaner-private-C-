namespace PortableCDriveCleaner.Models;

public sealed class CleanupIdentity
{
    public string AppIdentityKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public List<string> DisplayAliases { get; init; } = [];
    public List<string> KnownPaths { get; init; } = [];
    public List<string> Keywords { get; init; } = [];
    public List<string> ExecutableNames { get; init; } = [];
}
