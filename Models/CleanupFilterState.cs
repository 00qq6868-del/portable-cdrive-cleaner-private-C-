namespace PortableCDriveCleaner.Models;

public sealed class CleanupFilterState
{
    public const string AllValue = "all";
    public const string AutoValue = "auto";
    public const string ReviewValue = "review";
    public const string SortSmart = "smart";
    public const string SortSizeDesc = "size_desc";
    public const string SortNameAsc = "name_asc";
    public const string SortDriveSize = "drive_size";

    public string SearchText { get; set; } = string.Empty;
    public string SelectedDrive { get; set; } = AllValue;
    public string SelectedCategory { get; set; } = AllValue;
    public string SelectedAutoMode { get; set; } = AllValue;
    public string SortMode { get; set; } = SortSmart;

    public void Normalize()
    {
        SearchText = SearchText?.Trim() ?? string.Empty;
        SelectedDrive = string.IsNullOrWhiteSpace(SelectedDrive) ? AllValue : SelectedDrive.Trim();
        SelectedCategory = string.IsNullOrWhiteSpace(SelectedCategory) ? AllValue : SelectedCategory.Trim();
        SelectedAutoMode = string.IsNullOrWhiteSpace(SelectedAutoMode) ? AllValue : SelectedAutoMode.Trim();
        SortMode = string.IsNullOrWhiteSpace(SortMode) ? SortSmart : SortMode.Trim();
    }
}
