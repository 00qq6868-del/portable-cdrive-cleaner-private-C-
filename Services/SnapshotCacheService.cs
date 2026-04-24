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
            var envelope = JsonSerializer.Deserialize<SnapshotCacheEnvelope>(File.ReadAllText(_context.SnapshotCachePath), JsonOptions);
            if (envelope is null || envelope.Version != AppSettings.CurrentStartupCacheVersion)
            {
                return null;
            }

            settings.LastSnapshotUtc = envelope.SavedAtUtc;
            settings.LastSnapshotPath = _context.SnapshotCachePath;
            return envelope.Snapshot;
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
        var envelope = new SnapshotCacheEnvelope
        {
            Version = AppSettings.CurrentStartupCacheVersion,
            SavedAtUtc = DateTime.UtcNow,
            Snapshot = snapshot
        };

        File.WriteAllText(_context.SnapshotCachePath, JsonSerializer.Serialize(envelope, JsonOptions));
        settings.LastSnapshotUtc = envelope.SavedAtUtc;
        settings.LastSnapshotPath = _context.SnapshotCachePath;
    }

    private sealed class SnapshotCacheEnvelope
    {
        public int Version { get; init; }
        public DateTime SavedAtUtc { get; init; }
        public required ScanSnapshot Snapshot { get; init; }
    }
}
