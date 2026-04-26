using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PortableCDriveCleaner.Infrastructure;

public static class ApplicationIconCache
{
    private const int SmallIconSize = 20;
    private const int MinimumVisiblePixelCount = 18;
    private const uint ShgsiIcon = 0x000000100;
    private const uint ShgsiLargeIcon = 0x000000000;
    private const uint ShgsiSmallIcon = 0x000000001;
    private static readonly int[] SizeBuckets = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    private static readonly ConcurrentDictionary<string, Image> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Image> FallbackIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Icon> AppIcon = new(LoadApplicationIcon);

    public static Image GetSmallIcon(string? iconSourcePath, string? installRoot)
    {
        return GetSmallIcon(new IconLookupRequest(iconSourcePath, installRoot, IconSemanticKind.Application));
    }

    public static Image GetSmallIcon(IconLookupRequest request)
    {
        return GetIcon(request, SmallIconSize);
    }

    public static Image GetIcon(IconLookupRequest request, int size)
    {
        var normalizedSize = GetRecommendedIconSize(size);
        var fallbackKind = ResolveFallbackKind(request);
        var fallbackIcon = GetFallbackIcon(normalizedSize, fallbackKind);
        var cachePrefix = $"dark|{normalizedSize}|{request.BuildCacheKey()}";

        foreach (var candidate in EnumerateCandidates(request))
        {
            var normalized = NormalizeIconPath(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || (!File.Exists(normalized) && !Directory.Exists(normalized)))
            {
                continue;
            }

            var cacheKey = $"{cachePrefix}|{normalized}";
            var image = Cache.GetOrAdd(cacheKey, _ => LoadImageFromPath(normalized, normalizedSize, request.SemanticKind, fallbackKind));
            if (!ReferenceEquals(image, fallbackIcon))
            {
                return image;
            }
        }

        return fallbackIcon;
    }

    public static Icon GetAppIcon()
    {
        return (Icon)AppIcon.Value.Clone();
    }

    public static int GetRecommendedIconSize(int desiredSize)
    {
        var normalized = Math.Max(16, desiredSize);
        foreach (var bucket in SizeBuckets)
        {
            if (normalized <= bucket)
            {
                return bucket;
            }
        }

        return SizeBuckets[^1];
    }

    public static int GetRecommendedIconSizeForDpi(int deviceDpi, int logicalSize = SmallIconSize)
    {
        var scaledSize = (int)Math.Round(logicalSize * Math.Max(deviceDpi, 96) / 96d);
        return GetRecommendedIconSize(scaledSize);
    }

    private static IEnumerable<string> EnumerateCandidates(IconLookupRequest request)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryAddCandidate(request.IconSourcePath, seen, out var directSource))
        {
            yield return directSource;
        }

        if (TryAddCandidate(request.InstallRoot, seen, out var directInstallRoot))
        {
            yield return directInstallRoot;
        }

        if (!string.IsNullOrWhiteSpace(request.InstallRoot) && Directory.Exists(request.InstallRoot))
        {
            IEnumerable<string> executables = [];
            try
            {
                executables = Directory.EnumerateFiles(request.InstallRoot, "*.exe", SearchOption.TopDirectoryOnly).Take(8).ToList();
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

    private static Image LoadImageFromPath(
        string normalizedPath,
        int size,
        IconSemanticKind semanticKind,
        FallbackIconKind fallbackKind)
    {
        try
        {
            if (Directory.Exists(normalizedPath))
            {
                return IsDirectorySemantic(semanticKind)
                    ? CreateBitmapFromStockIcon(fallbackKind, size)
                    : GetFallbackIcon(size, fallbackKind);
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

    private static bool IsDirectorySemantic(IconSemanticKind semanticKind)
    {
        return semanticKind is IconSemanticKind.Directory or IconSemanticKind.Drive or IconSemanticKind.UserData;
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
        var cacheKey = $"dark|{size}|{kind}";
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
            try
            {
                using var icon = new Icon(Application.ExecutablePath, new Size(size, size));
                return CreateBitmap(icon, size);
            }
            catch
            {
                using var icon = (Icon)SystemIcons.Application.Clone();
                return CreateBitmap(icon, size);
            }
        }
    }

    private static Image CreateBitmapFromStockIcon(FallbackIconKind kind, int size)
    {
        using var icon = GetStockIcon(kind, size);
        return CreateValidatedBitmap(icon, size, kind);
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

    private static FallbackIconKind ResolveFallbackKind(IconLookupRequest request)
    {
        return request.SemanticKind switch
        {
            IconSemanticKind.Drive => FallbackIconKind.Drive,
            IconSemanticKind.Directory => ResolveDirectoryFallbackKind(request.IconSourcePath, request.InstallRoot),
            IconSemanticKind.UserData => FallbackIconKind.UserData,
            IconSemanticKind.Cache => FallbackIconKind.Cache,
            IconSemanticKind.Logs => FallbackIconKind.Logs,
            IconSemanticKind.Package => FallbackIconKind.Package,
            IconSemanticKind.Duplicate => FallbackIconKind.Duplicate,
            IconSemanticKind.Cleanup => FallbackIconKind.Cleanup,
            IconSemanticKind.System => FallbackIconKind.System,
            IconSemanticKind.Document => FallbackIconKind.Document,
            _ => FallbackIconKind.Application
        };
    }

    private static FallbackIconKind ResolveDirectoryFallbackKind(string? iconSourcePath, string? installRoot)
    {
        foreach (var candidate in new[] { iconSourcePath, installRoot })
        {
            var normalized = NormalizeIconPath(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || !Directory.Exists(normalized))
            {
                continue;
            }

            var root = Path.GetPathRoot(normalized);
            if (!string.IsNullOrWhiteSpace(root)
                && string.Equals(
                    normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return FallbackIconKind.Drive;
            }
        }

        return FallbackIconKind.Folder;
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
            FallbackIconKind.UserData => SHSTOCKICONID.Users,
            FallbackIconKind.Cache => SHSTOCKICONID.Stack,
            FallbackIconKind.Logs => SHSTOCKICONID.DocumentNoAssociation,
            FallbackIconKind.Package => SHSTOCKICONID.ZipFile,
            FallbackIconKind.Duplicate => SHSTOCKICONID.MixedFiles,
            FallbackIconKind.Cleanup => SHSTOCKICONID.Delete,
            FallbackIconKind.System => SHSTOCKICONID.Settings,
            FallbackIconKind.Document => SHSTOCKICONID.DocumentNoAssociation,
            _ => SHSTOCKICONID.Software
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
        Drive = 3,
        UserData = 4,
        Cache = 5,
        Logs = 6,
        Package = 7,
        Duplicate = 8,
        Cleanup = 9,
        System = 10,
        Document = 11
    }

    private enum SHSTOCKICONID : uint
    {
        DocumentNoAssociation = 0,
        Application = 2,
        Folder = 3,
        DriveFixed = 8,
        Delete = 84,
        Software = 82,
        Stack = 55,
        MixedFiles = 74,
        Users = 96,
        Settings = 106,
        ZipFile = 105
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
