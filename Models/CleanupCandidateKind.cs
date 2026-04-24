namespace PortableCDriveCleaner.Models;

public enum CleanupCandidateKind
{
    SafeJunk,
    AppCache,
    DuplicateFile,
    Package,
    LargeFile,
    LargeDirectory,
    AppResidue,
    Aggregate
}
