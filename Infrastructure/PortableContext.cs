namespace PortableCDriveCleaner.Infrastructure;

public sealed class PortableContext
{
    private const string LocalDataFolderName = "磁盘清理器";

    private PortableContext(string appRoot, string executablePath, string dataRoot, bool usingPortableDataRoot)
    {
        AppRoot = appRoot;
        EntryExecutablePath = executablePath;
        DataRoot = EnsureDirectory(dataRoot);
        UsingPortableDataRoot = usingPortableDataRoot;
        CacheRoot = EnsureDirectory(Path.Combine(DataRoot, "cache"));
        LogsRoot = EnsureDirectory(Path.Combine(DataRoot, "logs"));
        TempRoot = EnsureDirectory(Path.Combine(DataRoot, "temp"));
        SettingsPath = Path.Combine(DataRoot, "settings.json");
        SnapshotCachePath = Path.Combine(CacheRoot, "scan-snapshot.json");
    }

    public string AppRoot { get; }
    public string DataRoot { get; }
    public string CacheRoot { get; }
    public string LogsRoot { get; }
    public string TempRoot { get; }
    public string SettingsPath { get; }
    public string SnapshotCachePath { get; }
    public string EntryExecutablePath { get; }
    public bool UsingPortableDataRoot { get; }

    public static PortableContext Create()
    {
        var exePath = Application.ExecutablePath;
        var appRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var portableDataRoot = Path.Combine(appRoot, "data");
        if (CanWriteToDirectory(appRoot))
        {
            return new PortableContext(appRoot, exePath, portableDataRoot, usingPortableDataRoot: true);
        }

        var localRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LocalDataFolderName,
            "data");
        return new PortableContext(appRoot, exePath, localRoot, usingPortableDataRoot: false);
    }

    public string CreateTempSelectionPath()
    {
        var fileName = $"selection-{Guid.NewGuid():N}.json";
        return Path.Combine(TempRoot, fileName);
    }

    public static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool CanWriteToDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            var probe = Path.Combine(directoryPath, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
