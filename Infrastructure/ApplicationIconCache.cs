using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PortableCDriveCleaner.Infrastructure;

public static class ApplicationIconCache
{
    private const int SmallIconSize = 20;
    private const int MinimumVisiblePixelCount = 18;
    private const uint ShgsiIcon = 0x000000100;
    private const uint ShgsiLargeIcon = 0x000000000;
    private const uint ShgsiSmallIcon = 0x000000001;

    private static readonly ConcurrentDictionary<string, Image> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<int, Image> FallbackIcons = new();
    private static readonly Lazy<Icon> AppIcon = new(LoadApplicationIcon);

    public static Image GetSmallIcon(string? iconSourcePath, string? installRoot)
    {
        return GetIcon(iconSourcePath, installRoot, SmallIconSize);
    }

    public static Icon GetAppIcon()
    {
        return (Icon)AppIcon.Value.Clone();
    }

    public static Image GetIcon(string? iconSourcePath, string? installRoot, int size)
    {
        var normalizedSize = NormalizeSize(size);
        var fallbackKind = ResolveFallbackKind(iconSourcePath, installRoot);
        foreach (var candidate in EnumerateCandidates(iconSourcePath, installRoot))
        {
            var normalized = NormalizeIconPath(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || (!File.Exists(normalized) && !Directory.Exists(normalized)))
            {
                continue;
            }

            var cacheKey = $"{normalizedSize}|{normalized}";
            var image = Cache.GetOrAdd(cacheKey, _ => LoadImageFromPath(normalized, normalizedSize, fallbackKind));
            if (!ReferenceEquals(image, GetFallbackIcon(normalizedSize, fallbackKind)))
            {
                return image;
            }
        }

        return GetFallbackIcon(normalizedSize, fallbackKind);
    }

    private static IEnumerable<string> EnumerateCandidates(string? iconSourcePath, string? installRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryAddCandidate(iconSourcePath, seen, out var directSource))
        {
            yield return directSource;
        }

        if (TryAddCandidate(installRoot, seen, out var directInstallRoot))
        {
            yield return directInstallRoot;
        }

        if (!string.IsNullOrWhiteSpace(installRoot) && Directory.Exists(installRoot))
        {
            IEnumerable<string> executables = [];
            try
            {
                executables = Directory.EnumerateFiles(installRoot, "*.exe", SearchOption.TopDirectoryOnly).Take(8).ToList();
            }
            catch
            {
            }

            foreach (var executable in executables)
            {
                if (TryAddCandidate(executable, seen, out var normalizedExecutable))
                {
                    yield return normalizedExecutable;
                }
            }
        }
    }

    private static bool TryAddCandidate(string? candidate, ISet<string> seen, out string normalized)
    {
        normalized = NormalizeIconPath(candidate);
        return !string.IsNullOrWhiteSpace(normalized) && seen.Add(normalized);
    }

    private static Image LoadImageFromPath(string normalizedPath, int size, FallbackIconKind fallbackKind)
    {
        try
        {
            if (Directory.Exists(normalizedPath))
            {
                using var directoryIcon = GetStockIcon(FallbackIconKind.Folder, size);
                return CreateValidatedBitmap(directoryIcon, size, fallbackKind);
            }

            if (normalizedPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var ico = new Icon(normalizedPath, new Size(size, size));
                return CreateValidatedBitmap(ico, size, fallbackKind);
            }

            using var icon = Icon.ExtractAssociatedIcon(normalizedPath);
            if (icon is not null)
            {
                return CreateValidatedBitmap(icon, size, fallbackKind);
            }
        }
        catch
        {
        }

        return GetFallbackIcon(size, fallbackKind);
    }

    private static Image CreateValidatedBitmap(Icon icon, int size, FallbackIconKind fallbackKind)
    {
        var bitmap = CreateBitmap(icon, size);
        if (HasVisiblePixels(bitmap))
        {
            return bitmap;
        }

        bitmap.Dispose();
        return GetFallbackIcon(size, fallbackKind);
    }

    private static Image GetFallbackIcon(int size, FallbackIconKind kind)
    {
        var cacheKey = (size * 10) + (int)kind;
        return FallbackIcons.GetOrAdd(cacheKey, _ => CreateFallbackIcon(size, kind));
    }

    private static Image CreateFallbackIcon(int size, FallbackIconKind kind)
    {
        try
        {
            using var icon = GetStockIcon(kind, size);
            return CreateBitmap(icon, size);
        }
        catch
        {
            using var icon = new Icon(Application.ExecutablePath, new Size(size, size));
            return CreateBitmap(icon, size);
        }
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            return new Icon(Application.ExecutablePath);
        }
        catch
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    private static Bitmap CreateBitmap(Icon icon, int size)
    {
        using var sourceBitmap = icon.ToBitmap();
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(sourceBitmap, new Rectangle(0, 0, size, size));
        return bitmap;
    }

    private static bool HasVisiblePixels(Bitmap bitmap)
    {
        var visiblePixels = 0;
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y).A > 24)
                {
                    visiblePixels++;
                    if (visiblePixels >= MinimumVisiblePixelCount)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static int NormalizeSize(int size)
    {
        return Math.Clamp(size, 16, 256);
    }

    private static FallbackIconKind ResolveFallbackKind(string? iconSourcePath, string? installRoot)
    {
        var candidates = new[] { iconSourcePath, installRoot };
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeIconPath(candidate);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (Directory.Exists(normalized))
            {
                var root = Path.GetPathRoot(normalized);
                if (!string.IsNullOrWhiteSpace(root)
                    && string.Equals(
                        normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return FallbackIconKind.Drive;
                }

                return FallbackIconKind.Folder;
            }

            if (Path.GetExtension(normalized).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return FallbackIconKind.Application;
            }
        }

        return FallbackIconKind.Application;
    }

    private static Icon GetStockIcon(FallbackIconKind kind, int size)
    {
        var info = new SHSTOCKICONINFO
        {
            cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>()
        };
        var flags = ShgsiIcon | (size <= 20 ? ShgsiSmallIcon : ShgsiLargeIcon);
        var result = SHGetStockIconInfo(GetStockIconId(kind), flags, ref info);
        if (result != 0 || info.hIcon == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to load stock icon for {kind}. HRESULT={result}");
        }

        try
        {
            using var icon = Icon.FromHandle(info.hIcon);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static SHSTOCKICONID GetStockIconId(FallbackIconKind kind)
    {
        return kind switch
        {
            FallbackIconKind.Folder => SHSTOCKICONID.Folder,
            FallbackIconKind.Drive => SHSTOCKICONID.DriveFixed,
            _ => SHSTOCKICONID.Application
        };
    }

    private static string NormalizeIconPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        var clean = rawPath.Trim().Trim('"');
        var commaIndex = clean.IndexOf(',');
        if (commaIndex > 0)
        {
            clean = clean[..commaIndex];
        }

        try
        {
            return Path.GetFullPath(clean).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return clean.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private enum FallbackIconKind
    {
        Application = 1,
        Folder = 2,
        Drive = 3
    }

    private enum SHSTOCKICONID : uint
    {
        Application = 2,
        Folder = 3,
        DriveFixed = 8
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public IntPtr hIcon;
        public int iSysImageIndex;
        public int iIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(SHSTOCKICONID stockIconId, uint flags, ref SHSTOCKICONINFO stockIconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
