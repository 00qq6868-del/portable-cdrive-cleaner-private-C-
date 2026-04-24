namespace PortableCDriveCleaner.Infrastructure;

public static class SizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):N2} TB";
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):N2} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):N2} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):N2} KB";
        return $"{bytes} B";
    }
}
