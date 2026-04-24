using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PortableCDriveCleaner.Infrastructure;

public static class ApplicationIconCache
{
    private const int SmallIconSize = 20;
    private const int MinimumVisiblePixelCount = 18;

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
        foreach (var candidate in EnumerateCandidates(iconSourcePath, installRoot))
        {
            var normalized = NormalizeIconPath(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || !File.Exists(normalized))
            {
                continue;
            }

            var cacheKey = $"{normalizedSize}|{normalized}";
            var image = Cache.GetOrAdd(cacheKey, _ => LoadImageFromPath(normalized, normalizedSize));
            if (!ReferenceEquals(image, GetFallbackIcon(normalizedSize)))
            {
                return image;
            }
        }

        return GetFallbackIcon(normalizedSize);
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

    private static Image LoadImageFromPath(string normalizedPath, int size)
    {
        try
        {
            if (normalizedPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var ico = new Icon(normalizedPath, new Size(size, size));
                return CreateValidatedBitmap(ico, size);
            }

            using var icon = Icon.ExtractAssociatedIcon(normalizedPath);
            if (icon is not null)
            {
                return CreateValidatedBitmap(icon, size);
            }
        }
        catch
        {
        }

        return GetFallbackIcon(size);
    }

    private static Image CreateValidatedBitmap(Icon icon, int size)
    {
        var bitmap = CreateBitmap(icon, size);
        if (HasVisiblePixels(bitmap))
        {
            return bitmap;
        }

        bitmap.Dispose();
        return GetFallbackIcon(size);
    }

    private static Image GetFallbackIcon(int size)
    {
        return FallbackIcons.GetOrAdd(size, CreateFallbackIcon);
    }

    private static Image CreateFallbackIcon(int size)
    {
        try
        {
            using var icon = new Icon(Application.ExecutablePath, new Size(size, size));
            return CreateBitmap(icon, size);
        }
        catch
        {
            using var icon = SystemIcons.Application;
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
}
