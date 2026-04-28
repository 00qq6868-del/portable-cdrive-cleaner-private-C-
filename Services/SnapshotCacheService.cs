using System.Text.Json;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class SnapshotCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly PortableContext _context;

    public SnapshotCacheService(PortableContext context)
    {
        _context = context;
    }

    public ScanSnapshot? Load(AppSettings settings)
    {
        if (!settings.WarmScanEnabled || string.IsNullOrWhiteSpace(_context.SnapshotCachePath) || !File.Exists(_context.SnapshotCachePath))
        {
            return null;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<SnapshotCacheReadEnvelope>(File.ReadAllText(_context.SnapshotCachePath), JsonOptions);
            if (envelope is null || envelope.Version != AppSettings.CurrentStartupCacheVersion || envelope.Snapshot is null)
            {
                return null;
            }

            settings.LastSnapshotUtc = envelope.SavedAtUtc;
            settings.LastSnapshotPath = _context.SnapshotCachePath;
            return envelope.Snapshot.ToScanSnapshot();
        }
        catch
        {
            return null;
        }
    }

    public void Save(AppSettings settings, ScanSnapshot snapshot)
    {
        if (!settings.WarmScanEnabled || snapshot.IsPartialResult)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_context.SnapshotCachePath)!);
        var envelope = new SnapshotCacheWriteEnvelope
        {
            Version = AppSettings.CurrentStartupCacheVersion,
            SavedAtUtc = DateTime.UtcNow,
            Snapshot = snapshot
        };

        File.WriteAllText(_context.SnapshotCachePath, JsonSerializer.Serialize(envelope, JsonOptions));
        settings.LastSnapshotUtc = envelope.SavedAtUtc;
        settings.LastSnapshotPath = _context.SnapshotCachePath;
    }

    private sealed class SnapshotCacheReadEnvelope
    {
        public int Version { get; init; }
        public DateTime SavedAtUtc { get; init; }
        public CachedScanSnapshot? Snapshot { get; init; }
    }

    private sealed class SnapshotCacheWriteEnvelope
    {
        public int Version { get; init; }
        public DateTime SavedAtUtc { get; init; }
        public required ScanSnapshot Snapshot { get; init; }
    }

    private sealed class CachedScanSnapshot
    {
        public List<CleanupItem> CleanupItems { get; init; } = [];
        public List<CDriveOverviewEntry> CDriveOverviewEntries { get; init; } = [];
        public List<InfrequentSoftwareEntry> InfrequentSoftwareEntries { get; init; } = [];
        public List<MigrationCandidate> MigrationCandidates { get; init; } = [];
        public List<string> LoadedDrives { get; init; } = [];
        public List<string> PendingDrives { get; init; } = [];
        public bool IsPartialResult { get; init; }
        public string PhaseLabel { get; init; } = string.Empty;
        public List<ScanWarning> Warnings { get; init; } = [];
        public HashSet<Guid> SafeDefaultSelectionIds { get; init; } = [];
        public HashSet<Guid> AdditionalReviewSelectionIds { get; init; } = [];
        public Dictionary<string, string> InstalledAppMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> DuplicateGroups { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> WhitelistHints { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public ScanSnapshot ToScanSnapshot()
        {
            return new ScanSnapshot
            {
                CleanupItems = CleanupItems,
                CDriveOverviewEntries = CDriveOverviewEntries,
                InfrequentSoftwareEntries = InfrequentSoftwareEntries,
                MigrationCandidates = MigrationCandidates,
                LoadedDrives = LoadedDrives,
                PendingDrives = PendingDrives,
                IsPartialResult = IsPartialResult,
                PhaseLabel = PhaseLabel,
                Warnings = Warnings,
                SafeDefaultSelectionIds = SafeDefaultSelectionIds,
                AdditionalReviewSelectionIds = AdditionalReviewSelectionIds,
                InstalledAppMappings = InstalledAppMappings,
                DuplicateGroups = DuplicateGroups.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value,
                    StringComparer.OrdinalIgnoreCase),
                WhitelistHints = WhitelistHints
            };
        }
    }
}
