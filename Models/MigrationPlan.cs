using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Models;

public sealed class MigrationPlan
{
    public required IReadOnlyList<MigrationCandidate> Candidates { get; init; }
    public required string TargetRoot { get; init; }
    public required string TargetDriveName { get; init; }

    public long TotalBytes => Candidates.Sum(candidate => candidate.SizeBytes);
    public int TotalCount => Candidates.Count;
    public string TotalBytesText => SizeFormatter.Format(TotalBytes);
}
