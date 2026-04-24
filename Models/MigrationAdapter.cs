namespace PortableCDriveCleaner.Models;

public sealed class MigrationAdapter
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public IReadOnlyList<string> MatchTokens { get; init; } = [];
    public IReadOnlyList<string> CompanionDirectoryNames { get; init; } = [];
    public IReadOnlyList<string> ValidationTargetExeNames { get; init; } = [];
    public bool AllowAutomaticMigration { get; init; }
    public bool DriverBound { get; init; }
}
