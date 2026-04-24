using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Services;

public sealed class OccupancyProbeService
{
    private const int ErrorMoreData = 234;
    private const int RmRebootReasonNone = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    private enum RmAppType
    {
        UnknownApp = 0,
        MainWindow = 1,
        OtherWindow = 2,
        Service = 3,
        Explorer = 4,
        Console = 5,
        Critical = 1000
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AppName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string ServiceShortName;

        public RmAppType ApplicationType;
        public uint AppStatus;
        public uint SessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[]? fileNames,
        uint appCount,
        RmUniqueProcess[]? applications,
        uint serviceCount,
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfo,
        [In, Out] RmProcessInfo[]? affectedApps,
        ref uint rebootReasons);

    public OccupancyProbeResult Probe(string path)
    {
        var resources = BuildResourceList(path);
        if (resources.Count == 0)
        {
            return new OccupancyProbeResult
            {
                ProbedPath = path,
                BlockingProcesses = []
            };
        }

        uint sessionHandle = 0;
        var sessionKey = new StringBuilder(32);

        try
        {
            var startResult = RmStartSession(out sessionHandle, 0, sessionKey);
            if (startResult != 0)
            {
                return EmptyResult(path);
            }

            var registerResult = RmRegisterResources(sessionHandle, (uint)resources.Count, resources.ToArray(), 0, null, 0, null);
            if (registerResult != 0)
            {
                return EmptyResult(path);
            }

            uint processInfoNeeded;
            uint processInfo = 0;
            uint rebootReasons = RmRebootReasonNone;
            var result = RmGetList(sessionHandle, out processInfoNeeded, ref processInfo, null, ref rebootReasons);
            if (result == ErrorMoreData)
            {
                var processes = new RmProcessInfo[processInfoNeeded];
                processInfo = processInfoNeeded;
                result = RmGetList(sessionHandle, out processInfoNeeded, ref processInfo, processes, ref rebootReasons);
                if (result == 0)
                {
                    return new OccupancyProbeResult
                    {
                        ProbedPath = path,
                        BlockingProcesses = processes
                            .Take((int)processInfo)
                            .Select(ToProcessInfo)
                            .Where(info => info is not null)
                            .Cast<OccupancyProcessInfo>()
                            .DistinctBy(info => info.ProcessId)
                            .OrderBy(info => info.ProcessName, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    };
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (sessionHandle != 0)
            {
                try
                {
                    RmEndSession(sessionHandle);
                }
                catch
                {
                }
            }
        }

        return EmptyResult(path);
    }

    public OccupancyCloseResult TryCloseProcesses(IEnumerable<OccupancyProcessInfo> processes, TimeSpan timeout)
    {
        var attempted = processes
            .Where(process => process.ProcessId > 0)
            .DistinctBy(process => process.ProcessId)
            .ToList();

        var closed = new List<OccupancyProcessInfo>();
        var remaining = new List<OccupancyProcessInfo>();
        var started = new List<(OccupancyProcessInfo Info, Process Process)>();

        foreach (var processInfo in attempted)
        {
            try
            {
                var process = Process.GetProcessById(processInfo.ProcessId);
                if (process.HasExited)
                {
                    closed.Add(processInfo);
                    continue;
                }

                if (!processInfo.CanGracefullyClose || process.MainWindowHandle == IntPtr.Zero || !process.CloseMainWindow())
                {
                    remaining.Add(processInfo);
                    process.Dispose();
                    continue;
                }

                started.Add((processInfo, process));
            }
            catch
            {
                closed.Add(processInfo);
            }
        }

        var deadline = DateTime.UtcNow + timeout;
        foreach (var startedProcess in started)
        {
            try
            {
                var waitMs = Math.Max((int)(deadline - DateTime.UtcNow).TotalMilliseconds, 0);
                if (waitMs > 0)
                {
                    startedProcess.Process.WaitForExit(waitMs);
                }

                if (startedProcess.Process.HasExited)
                {
                    closed.Add(startedProcess.Info);
                }
                else
                {
                    remaining.Add(startedProcess.Info);
                }
            }
            catch
            {
                remaining.Add(startedProcess.Info);
            }
            finally
            {
                startedProcess.Process.Dispose();
            }
        }

        return new OccupancyCloseResult
        {
            AttemptedProcesses = attempted,
            ClosedProcesses = closed,
            RemainingProcesses = remaining
        };
    }

    private static OccupancyProbeResult EmptyResult(string path)
    {
        return new OccupancyProbeResult
        {
            ProbedPath = path,
            BlockingProcesses = []
        };
    }

    private static OccupancyProcessInfo? ToProcessInfo(RmProcessInfo processInfo)
    {
        try
        {
            using var process = Process.GetProcessById(processInfo.Process.ProcessId);
            var processName = SafeGetProcessName(process);
            var title = SafeGetMainWindowTitle(process);
            var displayName = string.IsNullOrWhiteSpace(processInfo.AppName)
                ? processName
                : processInfo.AppName;

            return new OccupancyProcessInfo
            {
                ProcessId = processInfo.Process.ProcessId,
                ProcessName = processName,
                DisplayName = displayName,
                MainWindowTitle = title,
                CanGracefullyClose = process.MainWindowHandle != IntPtr.Zero
            };
        }
        catch
        {
            return null;
        }
    }

    private static string SafeGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return "未知进程";
        }
    }

    private static string SafeGetMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<string> BuildResourceList(string path)
    {
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            resources.Add(Path.GetFullPath(path));
        }

        if (Directory.Exists(path))
        {
            resources.Add(Path.GetFullPath(path));
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path).Take(32))
                {
                    resources.Add(Path.GetFullPath(entry));
                }
            }
            catch
            {
            }
        }

        return resources.ToList();
    }
}
