namespace PortableCDriveCleaner.Models;

public sealed class ScheduleInfo
{
    public bool Exists { get; init; }
    public string Status { get; init; } = "未启用";
    public string NextRunTime { get; init; } = "-";
    public string LastResult { get; init; } = "-";
    public string RunAsUser { get; init; } = Environment.UserName;
}
