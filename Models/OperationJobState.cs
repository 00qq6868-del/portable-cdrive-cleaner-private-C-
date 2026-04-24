namespace PortableCDriveCleaner.Models;

public enum OperationJobState
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Canceled = 4
}
