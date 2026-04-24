using System.ComponentModel;

namespace PortableCDriveCleaner.Models;

public sealed class CleanupSelectionRow : INotifyPropertyChanged
{
    private bool _selected;
    private long? _sizeBytesOverride;
    private string _lastRunStatus = string.Empty;
    private string _lastRunMessage = string.Empty;
    private IReadOnlyList<string> _blockingProcessNames = [];
    private bool _retrySuggested;

    public CleanupSelectionRow(CleanupItem item, SelectionTier selectionTier)
    {
        Item = item;
        SelectionTier = selectionTier;
        _selected = selectionTier == SelectionTier.AutoSafe;
    }

    public CleanupItem Item { get; }
    public SelectionTier SelectionTier { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    public string DriveName => Item.DriveName;
    public string Category => Item.Category;
    public CleanupCandidateKind CandidateKind => Item.CandidateKind;
    public string Name => Item.Name;
    public long EffectiveSizeBytes => _sizeBytesOverride ?? Item.SizeBytes;
    public string LastRunStatus => _lastRunStatus;
    public string LastRunMessage => _lastRunMessage;
    public IReadOnlyList<string> BlockingProcessNames => _blockingProcessNames;
    public bool RetrySuggested => _retrySuggested;
    public bool HasRecentRunResult => !string.IsNullOrWhiteSpace(_lastRunStatus);
    public string RecommendationText => _lastRunStatus switch
    {
        "队列中" => "后台处理中",
        "部分成功" => "已清一部分",
        "失败" when _retrySuggested => "需关闭占用后重试",
        "失败" => "需手动处理",
        _ => Item.RecommendedActionText
    };
    public string CandidateKindText => Item.CandidateKind switch
    {
        CleanupCandidateKind.SafeJunk => "明确垃圾",
        CleanupCandidateKind.AppCache => "应用缓存",
        CleanupCandidateKind.DuplicateFile => "重复文件",
        CleanupCandidateKind.Package => "下载包/压缩包",
        CleanupCandidateKind.LargeFile => "大文件",
        CleanupCandidateKind.LargeDirectory => "大目录",
        CleanupCandidateKind.AppResidue => "应用残留",
        CleanupCandidateKind.Aggregate => "汇总项",
        _ => Item.Category
    };
    public string TypeDescription => Item.TypeDescription;
    public string SizeText => Infrastructure.SizeFormatter.Format(EffectiveSizeBytes);
    public string Path => Item.Path;
    public string LocationSummary => Infrastructure.PathSummaryFormatter.Summarize(Item.Path);
    public string Note => Item.Note;
    public string ImpactText => Item.ImpactText;
    public string WhitelistHintText => Item.WhitelistHintText;
    public string ImpactSeverityText => Item.ImpactSeverity switch
    {
        CleanupImpactSeverity.Low => "低",
        CleanupImpactSeverity.Medium => "中",
        CleanupImpactSeverity.High => "高",
        _ => "低"
    };
    public string SelectionTierText => SelectionTier switch
    {
        SelectionTier.AutoSafe => "已自动勾选",
        SelectionTier.ReviewOnly => "等待确认",
        _ => "手动选择"
    };

    public void ApplyRunResult(CleanupResult result)
    {
        _sizeBytesOverride = result.RemainingBytes >= 0 ? result.RemainingBytes : EffectiveSizeBytes;
        _lastRunStatus = result.Status;
        _lastRunMessage = result.Message ?? string.Empty;
        _blockingProcessNames = result.BlockingProcessNames;
        _retrySuggested = result.RetrySuggested;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastRunStatus)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastRunMessage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlockingProcessNames)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RetrySuggested)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRecentRunResult)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecommendationText)));
    }

    public void MarkQueued(string queueMessage)
    {
        _lastRunStatus = "队列中";
        _lastRunMessage = string.IsNullOrWhiteSpace(queueMessage) ? "已加入后台任务队列" : queueMessage;
        _retrySuggested = false;
        _blockingProcessNames = [];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastRunStatus)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastRunMessage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BlockingProcessNames)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RetrySuggested)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRecentRunResult)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RecommendationText)));
    }

    public void ClearSelection()
    {
        Selected = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
