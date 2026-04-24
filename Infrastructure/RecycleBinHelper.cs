using System.IO;
using System.Runtime.InteropServices;

namespace PortableCDriveCleaner.Infrastructure;

public static class RecycleBinHelper
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(nint hwnd, string? pszRootPath, uint dwFlags);

    private const uint NoConfirmation = 0x00000001;
    private const uint NoProgressUi = 0x00000002;
    private const uint NoSound = 0x00000004;

    public static long GetRecycleBinSizeBytes()
    {
        long total = 0;
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                total += FileSystemHelper.GetPathSizeBytes(Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin"));
            }
            catch
            {
            }
        }

        return total;
    }

    public static void EmptyRecycleBin()
    {
        _ = SHEmptyRecycleBin(0, null, NoConfirmation | NoProgressUi | NoSound);
    }
}
