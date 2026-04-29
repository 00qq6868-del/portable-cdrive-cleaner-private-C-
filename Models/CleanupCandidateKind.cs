namespace PortableCDriveCleaner.Models;

public enum CleanupCandidateKind
{
    SafeJunk,
    AppCache,
    DuplicateFile,
    Package,
    PrivacyTrace,
    BrowserCache,
    ChatCache,
    LargeFile,
    LargeDirectory,
    AppResidue,
    Aggregate
}
