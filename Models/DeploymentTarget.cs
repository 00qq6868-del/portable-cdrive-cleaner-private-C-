using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Models;

public sealed class DeploymentTarget
{
    public string DriveName { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public bool IsRecommended { get; init; }
    public bool IsOnSystemDrive { get; init; }
    public bool NeedsElevation { get; init; }
    public long FreeBytes { get; init; }
    public long TotalBytes { get; init; }

    public string SummaryText => $"{DriveName} 盘可用 {SizeFormatter.Format(FreeBytes)} / 总 {SizeFormatter.Format(TotalBytes)}";
}
