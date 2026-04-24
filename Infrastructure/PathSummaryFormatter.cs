namespace PortableCDriveCleaner.Infrastructure;

public static class PathSummaryFormatter
{
    public static string Summarize(string path, int maxLength = 54)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var fileName = Path.GetFileName(normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrWhiteSpace(fileName) && fileName.Length < maxLength - 4)
        {
            var prefixLength = Math.Max(10, maxLength - fileName.Length - 4);
            return $"{normalized[..prefixLength]}...\\{fileName}";
        }

        var head = Math.Max(12, maxLength / 2 - 2);
        var tail = Math.Max(12, maxLength - head - 3);
        return $"{normalized[..head]}...{normalized[^tail..]}";
    }
}
