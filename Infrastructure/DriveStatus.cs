namespace PortableCDriveCleaner.Infrastructure;

public sealed class DriveStatus
{
    public string Name { get; init; } = "C";
    public long UsedBytes { get; init; }
    public long FreeBytes { get; init; }
    public long TotalBytes { get; init; }
    public string UsedText => SizeFormatter.Format(UsedBytes);
    public string FreeText => SizeFormatter.Format(FreeBytes);
    public string TotalText => SizeFormatter.Format(TotalBytes);
}
