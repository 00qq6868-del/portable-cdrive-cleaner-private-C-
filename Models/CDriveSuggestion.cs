using PortableCDriveCleaner.Infrastructure;

namespace PortableCDriveCleaner.Models;

public sealed class CDriveSuggestion
{
    public string SourceType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string CurrentPath { get; init; } = string.Empty;
    public string ActionText { get; init; } = string.Empty;
    public string ReasonText { get; init; } = string.Empty;

    public string SizeText => SizeFormatter.Format(SizeBytes);
}
