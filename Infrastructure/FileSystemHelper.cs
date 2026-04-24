using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PortableCDriveCleaner.Infrastructure;

public static class FileSystemHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);

    private const uint FileOpDelete = 0x0003;
    private const ushort FoFAllowUndo = 0x0040;
    private const ushort FoFNoConfirmation = 0x0010;
    private const ushort FoFNoErrorUi = 0x0400;
    private const ushort FoFSilent = 0x0004;

    public static long GetPathSizeBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        if (File.Exists(path))
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in EnumerateFilesSafe(path, "*", recursive: true))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch
            {
            }
        }

        return total;
    }

    public static long GetPathSizeBytesQuick(string path, int maxDepth = 1, int maxEntries = 1600)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        if (File.Exists(path))
        {
            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        if (!Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        var processedEntries = 0;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((path, 0));

        while (pending.Count > 0 && processedEntries < maxEntries)
        {
            var (currentPath, depth) = pending.Pop();

            foreach (var file in EnumerateFilesSafe(currentPath, "*", recursive: false))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                }

                processedEntries++;
                if (processedEntries >= maxEntries)
                {
                    break;
                }
            }

            if (processedEntries >= maxEntries || depth >= maxDepth)
            {
                continue;
            }

            foreach (var directory in EnumerateDirectoriesSafe(currentPath, recursive: false))
            {
                pending.Push((directory, depth + 1));
                processedEntries++;
                if (processedEntries >= maxEntries)
                {
                    break;
                }
            }
        }

        return total;
    }

    public static void ClearDirectoryContents(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var entry in GetTopLevelEntries(path))
        {
            try
            {
                DeletePathPermanent(entry);
            }
            catch
            {
            }
        }
    }

    public static void DeleteDirectoryTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        DeletePathPermanent(path);
    }

    public static void DeleteFilePermanent(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    public static void DeleteToRecycleBin(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            return;
        }

        if (Directory.Exists(path))
        {
            NormalizeDirectoryAttributes(path);
        }
        else
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }

        var source = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "\0\0";
        var fileOp = new ShFileOpStruct
        {
            wFunc = FileOpDelete,
            pFrom = source,
            pTo = string.Empty,
            fFlags = FoFAllowUndo | FoFNoConfirmation | FoFNoErrorUi | FoFSilent,
            lpszProgressTitle = string.Empty
        };

        var result = SHFileOperation(ref fileOp);
        if (result != 0 || fileOp.fAnyOperationsAborted)
        {
            throw new IOException($"未能将目标移入回收站，错误代码: {result}。");
        }
    }

    public static void DeletePathPermanent(string path)
    {
        if (Directory.Exists(path))
        {
            NormalizeDirectoryAttributes(path);
            Directory.Delete(path, true);
            return;
        }

        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    private static void NormalizeDirectoryAttributes(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var file in EnumerateFilesSafe(path, "*", recursive: true))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch
                {
                }
            }

            foreach (var directory in EnumerateDirectoriesSafe(path, recursive: true)
                         .OrderByDescending(dir => dir.Length))
            {
                try
                {
                    new DirectoryInfo(directory).Attributes = FileAttributes.Normal;
                }
                catch
                {
                }
            }

            new DirectoryInfo(path).Attributes = FileAttributes.Normal;
        }
        catch
        {
        }
    }

    public static List<string> GetTopLevelEntries(string path)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        try
        {
            return Directory.GetFileSystemEntries(path).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static bool ExistsPath(string path)
    {
        return Directory.Exists(path) || File.Exists(path);
    }

    public static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    public static IEnumerable<string> EnumerateFilesSafe(string rootPath, string searchPattern = "*", bool recursive = false, Action<string, Exception>? onError = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(current, searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                onError?.Invoke(current, ex);
                files = [];
            }

            foreach (var file in files)
            {
                yield return file;
            }

            if (!recursive)
            {
                continue;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                onError?.Invoke(current, ex);
                directories = [];
            }

            foreach (var directory in directories)
            {
                if (IsReparsePoint(directory))
                {
                    continue;
                }

                pending.Push(directory);
            }
        }
    }

    public static IEnumerable<string> EnumerateDirectoriesSafe(string rootPath, bool recursive = false, Action<string, Exception>? onError = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                onError?.Invoke(current, ex);
                directories = [];
            }

            foreach (var directory in directories)
            {
                if (IsReparsePoint(directory))
                {
                    continue;
                }

                yield return directory;
                if (recursive)
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return true;
        }
    }

    public static void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        var parentDirectory = Path.GetDirectoryName(junctionPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("无法创建目录联接。");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || !Directory.Exists(junctionPath))
        {
            throw new IOException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
    }
}
