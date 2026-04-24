using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Forms;

public sealed class OperationProgressDialog : Form
{
    private readonly Label _headingLabel = new();
    private readonly Label _messageLabel = new();
    private readonly Label _detailLabel = new();
    private readonly ThemedProgressBar _progressBar = new();
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private readonly System.Windows.Forms.Timer _detailRefreshTimer = new();
    private int _currentPercent;
    private bool _currentIsIndeterminate;
    private string _currentDetailText = string.Empty;
    private long? _currentProcessedBytes;
    private long? _currentTotalBytes;

    public OperationProgressDialog(string title, string heading, string initialMessage)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = true;
        MaximizeBox = false;
        ShowInTaskbar = true;
        ControlBox = true;
        MinimumSize = new Size(700, 250);
        ClientSize = new Size(760, 260);
        UiThemePalette.ApplyFormChrome(this);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20, 18, 20, 18),
            BackColor = UiThemePalette.WindowBackground
        };
        UiThemePalette.EnableDoubleBuffering(root);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        _headingLabel.AutoSize = true;
        _headingLabel.Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
        _headingLabel.ForeColor = UiThemePalette.TextPrimary;
        _headingLabel.Margin = new Padding(0, 0, 0, 10);
        _headingLabel.Text = heading;
        root.Controls.Add(_headingLabel, 0, 0);

        _messageLabel.AutoSize = true;
        _messageLabel.ForeColor = UiThemePalette.TextSecondary;
        _messageLabel.Margin = new Padding(0, 0, 0, 14);
        _messageLabel.Text = initialMessage;
        root.Controls.Add(_messageLabel, 0, 1);

        _detailLabel.AutoSize = true;
        _detailLabel.ForeColor = UiThemePalette.AccentStrong;
        _detailLabel.Font = new Font("Microsoft YaHei UI", 8.6f, FontStyle.Bold);
        _detailLabel.Margin = new Padding(0, 0, 0, 10);
        _detailLabel.Text = "0% · 正在估算剩余时间";
        root.Controls.Add(_detailLabel, 0, 2);

        _progressBar.Dock = DockStyle.Top;
        _progressBar.Height = Math.Max(16, UiScaleHelper.MeasureButtonHeight(16, 2, _progressBar.Font, "处理中"));
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        root.Controls.Add(_progressBar, 0, 3);

        _detailRefreshTimer.Interval = 1000;
        _detailRefreshTimer.Tick += (_, _) => UpdateDetailText();
        _detailRefreshTimer.Start();

        Resize += (_, _) => UpdateResponsiveLayout();
        Shown += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) => BeginInvoke(new Action(UpdateResponsiveLayout));
        FormClosed += (_, _) => _detailRefreshTimer.Stop();
        UiThemePalette.ApplyTreeTheme(this);
    }

    public void Apply(DeploymentProgressUpdate update)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        _currentPercent = Math.Clamp(update.Percent, 0, 100);
        _currentIsIndeterminate = update.IsIndeterminate;
        _currentDetailText = update.DetailText ?? string.Empty;
        _currentProcessedBytes = update.ProcessedBytes;
        _currentTotalBytes = update.TotalBytes;
        if (!string.IsNullOrWhiteSpace(update.Message) && !string.Equals(_messageLabel.Text, update.Message, StringComparison.Ordinal))
        {
            _messageLabel.Text = update.Message;
        }

        var displayPercent = GetDisplayPercent();
        if (_progressBar.Value != displayPercent)
        {
            _progressBar.Value = displayPercent;
        }
        _progressBar.IsIndeterminate = _currentIsIndeterminate && _currentPercent <= 0;

        UpdateDetailText();
    }

    private void UpdateDetailText()
    {
        _detailLabel.Text = BuildProgressDetail();
    }

    private string BuildProgressDetail()
    {
        var elapsed = DateTime.UtcNow - _startedAtUtc;
        if (_currentPercent <= 0)
        {
            var zeroPercentText = _currentIsIndeterminate
                ? "0% · 正在处理当前阶段"
                : "0% · 正在估算剩余时间";
            return AppendProgressDetail(zeroPercentText);
        }

        if (_currentPercent >= 100)
        {
            return AppendProgressDetail($"100% · 总耗时 {FormatDuration(elapsed)}");
        }

        var progressText = $"{_currentPercent}% · 已用 {FormatDuration(elapsed)}";
        var remaining = EstimateRemaining(elapsed);
        if (remaining.HasValue)
        {
            progressText += $" · 预计还要 {FormatDuration(remaining.Value)}";
        }
        else if (_currentIsIndeterminate)
        {
            progressText += " · 正在估算剩余时间";
        }

        return AppendProgressDetail(progressText);
    }

    private TimeSpan? EstimateRemaining(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
        {
            return null;
        }

        double progressRatio;
        if (_currentTotalBytes.HasValue && _currentTotalBytes.Value > 0 && _currentProcessedBytes.HasValue && _currentProcessedBytes.Value > 0)
        {
            progressRatio = Math.Clamp((double)_currentProcessedBytes.Value / _currentTotalBytes.Value, 0d, 1d);
        }
        else
        {
            progressRatio = Math.Clamp(_currentPercent / 100d, 0d, 1d);
        }

        if (progressRatio <= 0d || progressRatio >= 1d)
        {
            return null;
        }

        var estimatedTotalSeconds = elapsed.TotalSeconds / progressRatio;
        var remainingSeconds = Math.Max(estimatedTotalSeconds - elapsed.TotalSeconds, 0d);
        if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds))
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Min(remainingSeconds, 12 * 60 * 60));
    }

    private string AppendProgressDetail(string summary)
    {
        var extraLines = new List<string>();
        if (_currentTotalBytes.HasValue && _currentTotalBytes.Value > 0)
        {
            extraLines.Add($"已处理 {SizeFormatter.Format(_currentProcessedBytes ?? 0)} / {SizeFormatter.Format(_currentTotalBytes.Value)}");
        }
        else if (_currentProcessedBytes.HasValue && _currentProcessedBytes.Value > 0)
        {
            extraLines.Add($"已处理 {SizeFormatter.Format(_currentProcessedBytes.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(_currentDetailText))
        {
            extraLines.Add(_currentDetailText.Trim());
        }

        return extraLines.Count == 0
            ? summary
            : $"{summary}\r\n{string.Join("\r\n", extraLines)}";
    }

    private int GetDisplayPercent()
    {
        if (_currentPercent > 0)
        {
            return _currentPercent;
        }

        return _currentIsIndeterminate ? 8 : 0;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1)
        {
            return "1秒内";
        }

        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Max(1, (int)Math.Round(duration.TotalSeconds))}秒";
        }

        if (duration.TotalHours < 1)
        {
            return $"{(int)duration.TotalMinutes}分{duration.Seconds:00}秒";
        }

        return $"{(int)duration.TotalHours}小时{duration.Minutes:00}分";
    }

    private void UpdateResponsiveLayout()
    {
        var wrapWidth = UiScaleHelper.MeasureWrapWidth(ClientSize.Width, 60, minWidth: 420);
        _headingLabel.MaximumSize = new Size(wrapWidth, 0);
        _messageLabel.MaximumSize = new Size(wrapWidth, 0);
        _detailLabel.MaximumSize = new Size(wrapWidth, 0);
    }
}
