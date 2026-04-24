using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Models;

public sealed class MigrationRunResult
{
    public required IReadOnlyList<MigrationItemResult> Results { get; init; }
    public required string TargetRoot { get; init; }
    public required string LogPath { get; init; }

    public int SuccessCount => Results.Count(result => result.Success);
    public int FailedCount => Results.Count(result => !result.Success);
    public long MigratedBytes => Results.Where(result => result.Success).Sum(result => result.SizeBytes);
    public string MigratedBytesText => SizeFormatter.Format(MigratedBytes);
    public int MigratedPathCount => Results.Where(result => result.Success).Sum(result => result.MigratedPathCount);
    public int UpdatedShortcutCount => Results.Sum(result => result.UpdatedShortcutCount);
    public int RolledBackCount => Results.Count(result => result.RolledBack);
}

public sealed class MigrationItemResult
{
    public required Guid CandidateId { get; init; }
    public required string Name { get; init; }
    public required string SourcePath { get; init; }
    public required string TargetPath { get; init; }
    public required long SizeBytes { get; init; }
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public int MigratedPathCount { get; init; }
    public int UpdatedShortcutCount { get; init; }
    public bool RolledBack { get; init; }
}
