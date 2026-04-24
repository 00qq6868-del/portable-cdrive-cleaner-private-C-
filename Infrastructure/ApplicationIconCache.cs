using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PortableCDriveCleaner.Infrastructure;

public static class ApplicationIconCache
{
    private const int SmallIconSize = 18;

    private static readonly ConcurrentDictionary<string, Image> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<Image> FallbackIcon = new(() => CreateBitmap(SystemIcons.Application));

    public static Image GetSmallIcon(string? iconSourcePath, string? installRoot)
    {
        foreach (var candidate in EnumerateCandidates(iconSourcePath, installRoot))
        {
            var normalized = NormalizeIconPath(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || !File.Exists(normalized))
            {
                continue;
            }

            var image = Cache.GetOrAdd(normalized, LoadImageFromPath);
            if (!ReferenceEquals(image, FallbackIcon.Value))
            {
                return image;
            }
        }

        return FallbackIcon.Value;
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

    private static Image LoadImageFromPath(string normalizedPath)
    {
        try
        {
            if (normalizedPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var ico = new Icon(normalizedPath, new Size(SmallIconSize, SmallIconSize));
                return CreateBitmap(ico);
            }

            using var icon = Icon.ExtractAssociatedIcon(normalizedPath);
            if (icon is not null)
            {
                return CreateBitmap(icon);
            }
        }
        catch
        {
        }

        return FallbackIcon.Value;
    }

    private static Bitmap CreateBitmap(Icon icon)
    {
        using var sourceBitmap = icon.ToBitmap();
        var bitmap = new Bitmap(SmallIconSize, SmallIconSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(sourceBitmap, new Rectangle(0, 0, SmallIconSize, SmallIconSize));
        return bitmap;
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
