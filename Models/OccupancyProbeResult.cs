namespace PortableCDriveCleaner.Models;

public sealed class OccupancyProbeResult
{
    public required string ProbedPath { get; init; }
    public IReadOnlyList<OccupancyProcessInfo> BlockingProcesses { get; init; } = [];
    public bool HasBlockingProcesses => BlockingProcesses.Count > 0;
}

public sealed class OccupancyProcessInfo
{
    public required int ProcessId { get; init; }
    public required string ProcessName { get; init; }
    public required string DisplayName { get; init; }
    public required string MainWindowTitle { get; init; }
    public required bool CanGracefullyClose { get; init; }
}

public sealed class OccupancyCloseResult
{
    public IReadOnlyList<OccupancyProcessInfo> AttemptedProcesses { get; init; } = [];
    public IReadOnlyList<OccupancyProcessInfo> ClosedProcesses { get; init; } = [];
    public IReadOnlyList<OccupancyProcessInfo> RemainingProcesses { get; init; } = [];
}
