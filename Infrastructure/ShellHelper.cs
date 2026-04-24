using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Infrastructure;

public static class ShellHelper
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static Process StartCurrentExecutable(PortableContext context, IEnumerable<string> args, bool elevated, bool waitForExit)
    {
        return StartExecutable(context.EntryExecutablePath, args, context.AppRoot, elevated, waitForExit);
    }

    public static Process StartExecutable(string executablePath, IEnumerable<string> args, string workingDirectory, bool elevated, bool waitForExit)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = workingDirectory
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (elevated)
        {
            startInfo.Verb = "runas";
        }

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动新进程。");
        if (waitForExit)
        {
            process.WaitForExit();
        }

        return process;
    }

    public static string ExportSelection(PortableContext context, IReadOnlyCollection<CleanupItem> items)
    {
        PortableContext.EnsureDirectory(context.TempRoot);
        var path = context.CreateTempSelectionPath();
        var json = JsonSerializer.Serialize(items);
        File.WriteAllText(path, json);
        return path;
    }

    public static List<CleanupItem> ImportSelection(string selectionFile)
    {
        if (string.IsNullOrWhiteSpace(selectionFile) || !File.Exists(selectionFile))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(selectionFile);
            return JsonSerializer.Deserialize<List<CleanupItem>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void DeleteFileQuietly(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    public static string RunProcessCapture(string fileName, IEnumerable<string> arguments, bool ignoreErrors = false)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (!ignoreErrors && process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    public static void RunProcessFireAndForget(string fileName, IEnumerable<string> arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        process.WaitForExit();
    }

    public static bool TryRunProcess(string fileName, IEnumerable<string> arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var arg in arguments)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            process.Start();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenFolder(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = path,
            UseShellExecute = true
        });
    }

    public static void RevealPath(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
            return;
        }

        if (Directory.Exists(path))
        {
            OpenFolder(path);
        }
    }

    public static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string description,
        string? arguments = null,
        string? iconLocation = null)
    {
        var shortcutDirectory = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrWhiteSpace(shortcutDirectory))
        {
            Directory.CreateDirectory(shortcutDirectory);
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("当前系统不可用 WScript.Shell，无法创建快捷方式。");

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = description;
        shortcut.Arguments = arguments ?? string.Empty;
        shortcut.IconLocation = string.IsNullOrWhiteSpace(iconLocation) ? targetPath : iconLocation;
        shortcut.Save();
    }

    public static bool TryReadShortcut(string shortcutPath, out string targetPath, out string arguments, out string workingDirectory)
    {
        targetPath = string.Empty;
        arguments = string.Empty;
        workingDirectory = string.Empty;

        if (string.IsNullOrWhiteSpace(shortcutPath) || !File.Exists(shortcutPath))
        {
            return false;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            targetPath = shortcut.TargetPath as string ?? string.Empty;
            arguments = shortcut.Arguments as string ?? string.Empty;
            workingDirectory = shortcut.WorkingDirectory as string ?? string.Empty;
            return !string.IsNullOrWhiteSpace(targetPath);
        }
        catch
        {
            targetPath = string.Empty;
            arguments = string.Empty;
            workingDirectory = string.Empty;
            return false;
        }
    }

    public static int UpdateShortcutsForRelocatedDirectory(string sourceRoot, string targetRoot)
    {
        var normalizedSource = NormalizePath(sourceRoot);
        var normalizedTarget = NormalizePath(targetRoot);
        if (string.IsNullOrWhiteSpace(normalizedSource) || string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return 0;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return 0;
        }

        var directories = GetShortcutSearchDirectories();
        var updatedCount = 0;
        dynamic shell = Activator.CreateInstance(shellType)!;

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var shortcutPath in FileSystemHelper.EnumerateFilesSafe(directory, "*.lnk", recursive: true))
            {
                try
                {
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    var currentTarget = NormalizePath(shortcut.TargetPath as string ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(currentTarget)
                        || !currentTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(normalizedSource, currentTarget);
                    var newTarget = NormalizePath(Path.Combine(normalizedTarget, relativePath));
                    shortcut.TargetPath = newTarget;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(newTarget) ?? normalizedTarget;
                    shortcut.IconLocation = newTarget;
                    shortcut.Save();
                    updatedCount++;
                }
                catch
                {
                }
            }
        }

        return updatedCount;
    }

    private static IReadOnlyList<string> GetShortcutSearchDirectories()
    {
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
