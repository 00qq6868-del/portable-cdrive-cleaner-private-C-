using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private static readonly int[] SizeBuckets = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    private static readonly ConcurrentDictionary<string, Image> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Image> FallbackIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Icon> AppIcon = new(LoadApplicationIcon);
    private static readonly string[] IconResourceExtensions = [".exe", ".dll", ".ico", ".icl", ".cpl", ".scr", ".mui"];

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
            if (string.IsNullOrWhiteSpace(candidate.NormalizedPath)
                || (!File.Exists(candidate.NormalizedPath) && !Directory.Exists(candidate.NormalizedPath)))
            {
                continue;
            }

            var cacheKey = $"{cachePrefix}|{candidate.CacheKey}";
            var image = Cache.GetOrAdd(cacheKey, _ => LoadImageFromPath(candidate, normalizedSize, request.SemanticKind, fallbackKind));
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

    private static IEnumerable<IconSourceCandidate> EnumerateCandidates(IconLookupRequest request)
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

    private static bool TryAddCandidate(string? candidate, ISet<string> seen, out IconSourceCandidate normalized)
    {
        normalized = ParseIconSourceCandidate(candidate);
        return !string.IsNullOrWhiteSpace(normalized.NormalizedPath) && seen.Add(normalized.CacheKey);
    }

    private static Image LoadImageFromPath(
        IconSourceCandidate source,
        int size,
        IconSemanticKind semanticKind,
        FallbackIconKind fallbackKind)
    {
        try
        {
            if (Directory.Exists(source.NormalizedPath))
            {
                if (TryGetShellFileIcon(source.NormalizedPath, size, isDirectory: true, out var directoryIcon)
                    && directoryIcon is not null)
                {
                    using (directoryIcon)
                    {
                        return CreateValidatedBitmap(directoryIcon, size, fallbackKind);
                    }
                }

                return IsDirectorySemantic(semanticKind)
                    ? CreateBitmapFromStockIcon(fallbackKind, size)
                    : GetFallbackIcon(size, fallbackKind);
            }

            if (source.NormalizedPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var ico = new Icon(source.NormalizedPath, new Size(size, size));
                return CreateValidatedBitmap(ico, size, fallbackKind);
            }

            if (TryExtractShellResourceIcon(source, size, out var extractedIcon)
                && extractedIcon is not null)
            {
                using (extractedIcon)
                {
                    return CreateValidatedBitmap(extractedIcon, size, fallbackKind);
                }
            }

            if (TryGetShellFileIcon(source.NormalizedPath, size, isDirectory: false, out var associatedIcon)
                && associatedIcon is not null)
            {
                using (associatedIcon)
                {
                    return CreateValidatedBitmap(associatedIcon, size, fallbackKind);
                }
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
        if (sourceBitmap.Width == size && sourceBitmap.Height == size)
        {
            return CloneBitmap(sourceBitmap);
        }

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = sourceBitmap.Width > size || sourceBitmap.Height > size
            ? InterpolationMode.HighQualityBicubic
            : InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.None;
        var destination = GetDestinationRectangle(sourceBitmap.Size, size);
        graphics.DrawImage(sourceBitmap, destination, new Rectangle(Point.Empty, sourceBitmap.Size), GraphicsUnit.Pixel);
        return bitmap;
    }

    private static Bitmap CloneBitmap(Bitmap source)
    {
        var clone = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.Clear(Color.Transparent);
        graphics.DrawImageUnscaled(source, Point.Empty);
        return clone;
    }

    private static Rectangle GetDestinationRectangle(Size sourceSize, int targetSize)
    {
        if (sourceSize.Width <= targetSize && sourceSize.Height <= targetSize)
        {
            var offsetX = Math.Max(0, (targetSize - sourceSize.Width) / 2);
            var offsetY = Math.Max(0, (targetSize - sourceSize.Height) / 2);
            return new Rectangle(offsetX, offsetY, sourceSize.Width, sourceSize.Height);
        }

        var scale = Math.Min(targetSize / (double)sourceSize.Width, targetSize / (double)sourceSize.Height);
        var width = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
        var height = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
        var x = Math.Max(0, (targetSize - width) / 2);
        var y = Math.Max(0, (targetSize - height) / 2);
        return new Rectangle(x, y, width, height);
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
            var normalized = ParseIconSourceCandidate(candidate).NormalizedPath;
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

    private static bool TryExtractShellResourceIcon(IconSourceCandidate source, int size, out Icon? icon)
    {
        icon = null;
        if (string.IsNullOrWhiteSpace(source.NormalizedPath) || !File.Exists(source.NormalizedPath))
        {
            return false;
        }

        IntPtr largeIcon = IntPtr.Zero;
        IntPtr smallIcon = IntPtr.Zero;
        try
        {
            var packedSize = (uint)((size & 0xffff) | ((size & 0xffff) << 16));
            var hr = SHDefExtractIcon(source.NormalizedPath, source.ResourceIndex, 0, out largeIcon, out smallIcon, packedSize);
            var handle = largeIcon != IntPtr.Zero ? largeIcon : smallIcon;
            if (hr < 0 || handle == IntPtr.Zero)
            {
                return false;
            }

            using var extracted = Icon.FromHandle(handle);
            icon = (Icon)extracted.Clone();
            return true;
        }
        catch
        {
            icon = null;
            return false;
        }
        finally
        {
            if (largeIcon != IntPtr.Zero)
            {
                DestroyIcon(largeIcon);
            }

            if (smallIcon != IntPtr.Zero && smallIcon != largeIcon)
            {
                DestroyIcon(smallIcon);
            }
        }
    }

    private static bool TryGetShellFileIcon(string path, int size, bool isDirectory, out Icon? icon)
    {
        icon = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var info = new SHFILEINFO();
        var flags = ShgsiIcon
            | (size <= SmallIconSize ? ShgsiSmallIcon : ShgsiLargeIcon)
            | (isDirectory ? ShgfiUseFileAttributes : 0u);
        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;

        try
        {
            var result = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return false;
            }

            using var shellIcon = Icon.FromHandle(info.hIcon);
            icon = (Icon)shellIcon.Clone();
            return true;
        }
        catch
        {
            icon = null;
            return false;
        }
        finally
        {
            if (info.hIcon != IntPtr.Zero)
            {
                DestroyIcon(info.hIcon);
            }
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

    private static IconSourceCandidate ParseIconSourceCandidate(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return default;
        }

        var clean = rawPath.Trim();
        var resourceIndex = 0;
        if (TrySplitIconResourceIndex(clean, out var pathWithoutIndex, out var parsedIndex))
        {
            clean = pathWithoutIndex;
            resourceIndex = parsedIndex;
        }

        clean = Environment.ExpandEnvironmentVariables(clean.Trim().Trim('"'));
        var normalized = NormalizePath(clean);
        return string.IsNullOrWhiteSpace(normalized)
            ? default
            : new IconSourceCandidate(normalized, resourceIndex, $"{normalized}|{resourceIndex}");
    }

    private static bool TrySplitIconResourceIndex(string rawPath, out string pathWithoutIndex, out int resourceIndex)
    {
        pathWithoutIndex = rawPath.Trim();
        resourceIndex = 0;

        var commaIndex = rawPath.LastIndexOf(',');
        if (commaIndex <= 0 || commaIndex >= rawPath.Length - 1)
        {
            return false;
        }

        var suffix = rawPath[(commaIndex + 1)..].Trim();
        if (!int.TryParse(suffix, out resourceIndex))
        {
            resourceIndex = 0;
            return false;
        }

        var candidatePath = rawPath[..commaIndex].Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidatePath) || !LooksLikeIconResourcePath(candidatePath))
        {
            resourceIndex = 0;
            return false;
        }

        pathWithoutIndex = candidatePath;
        return true;
    }

    private static bool LooksLikeIconResourcePath(string candidatePath)
    {
        if (File.Exists(candidatePath) || Directory.Exists(candidatePath) || Path.IsPathRooted(candidatePath))
        {
            return true;
        }

        var extension = Path.GetExtension(candidatePath);
        return !string.IsNullOrWhiteSpace(extension)
            && IconResourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string rawPath)
    {
        try
        {
            return Path.GetFullPath(rawPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return rawPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private readonly record struct IconSourceCandidate(
        string NormalizedPath,
        int ResourceIndex,
        string CacheKey);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(SHSTOCKICONID stockIconId, uint flags, ref SHSTOCKICONINFO stockIconInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHDefExtractIcon(
        string pszIconFile,
        int iIndex,
        uint uFlags,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        uint nIconSize);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
