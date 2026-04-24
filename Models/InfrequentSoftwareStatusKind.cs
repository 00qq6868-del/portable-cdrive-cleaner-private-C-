namespace PortableCDriveCleaner.Models;

public enum InfrequentSoftwareStatusKind
{
    RecommendedDeletion = 0,
    ReviewOnly = 1,
    ActiveRecent = 2,
    NoReliableUsageRecord = 3,
    Whitelisted = 4,
    Protected = 5,
    CurrentRunning = 6
}
