using System.ComponentModel;
using System.Diagnostics;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Forms;

public sealed class OptimizationAuditDialog : Form
{
    private readonly OptimizationAuditSnapshot _snapshot;
    private readonly BindingList<OptimizationAuditEntry> _rows;
    private readonly ScrollFriendlyDataGridView _grid = new();
    private readonly ScrollFriendlyRichTextBox _detailBox = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _guardrailLabel = new();
    private readonly FlowLayoutPanel _summaryChipFlow = new();
    private readonly Label _detailTitleLabel = new();
    private readonly Button _openStartupSettingsButton = new ThemedButton();
    private readonly Button _copyDetailsButton = new ThemedButton();
    private readonly Button _closeButton = new ThemedButton();

    public OptimizationAuditDialog(OptimizationAuditSnapshot snapshot)
    {
        _snapshot = snapshot;
        _rows = new BindingList<OptimizationAuditEntry>(snapshot.Entries
            .OrderByDescending(entry => entry.ImpactLevel)
            .ThenByDescending(entry => entry.RecommendationLevel)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());

        Text = "安全体检 / 启动项";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = true;
        ShowInTaskbar = true;
        MinimumSize = new Size(980, 700);
        ClientSize = new Size(1180, 800);
        UiThemePalette.ApplyFormChrome(this);

        BuildUi();
        ApplyThemeColors();
        PopulateGrid();
        UpdateSelectionDetails();

        Resize += (_, _) => UpdateResponsiveLayout();
        Shown += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) => BeginInvoke(new Action(() =>
        {
            UiScaleHelper.RefreshRegisteredButtonSizing(this);
            UpdateResponsiveLayout();
        }));
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 14)
        };
        root.BackColor = UiThemePalette.WindowBackground;
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var headerPanel = CreateSurfacePanel();
        headerPanel.Padding = new Padding(18, 14, 18, 14);
        root.Controls.Add(headerPanel, 0, 0);

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerPanel.Controls.Add(headerLayout);

        headerLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 13.2f, FontStyle.Bold),
            ForeColor = UiThemePalette.TextPrimary,
            Margin = new Padding(0, 0, 0, 8),
            Text = "安全体检 / 启动项"
        }, 0, 0);

        _summaryLabel.AutoSize = true;
        _summaryLabel.ForeColor = UiThemePalette.TextSecondary;
        _summaryLabel.Font = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Bold);
        _summaryLabel.Margin = new Padding(0, 0, 0, 8);
        _summaryLabel.Text = BuildSummaryText();
        headerLayout.Controls.Add(_summaryLabel, 0, 1);

        _summaryChipFlow.AutoSize = true;
        _summaryChipFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _summaryChipFlow.WrapContents = true;
        _summaryChipFlow.Margin = new Padding(0, 0, 0, 8);
        _summaryChipFlow.Padding = Padding.Empty;
        PopulateSummaryChips();
        headerLayout.Controls.Add(_summaryChipFlow, 0, 2);

        _guardrailLabel.AutoSize = true;
        _guardrailLabel.MaximumSize = new Size(1080, 0);
        _guardrailLabel.ForeColor = UiThemePalette.Warning;
        _guardrailLabel.Margin = Padding.Empty;
        _guardrailLabel.Text = "只读体检：这里不会修改启动项、注册表、服务或计划任务。需要处理时，请优先进入 Windows 启动应用设置或软件自己的设置页。";
        headerLayout.Controls.Add(_guardrailLabel, 0, 3);

        var gridPanel = CreateSurfacePanel();
        gridPanel.Padding = new Padding(12);
        gridPanel.Margin = new Padding(0, 12, 0, 12);
        root.Controls.Add(gridPanel, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = false;
        _grid.MultiSelect = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9.1f, FontStyle.Bold);
        _grid.ColumnHeadersHeight = UiScaleHelper.MeasureGridHeaderHeight(_grid.ColumnHeadersDefaultCellStyle.Font, minHeight: 42, verticalPadding: 18);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.RowTemplate.Height = UiScaleHelper.MeasureGridRowHeight(_grid.Font, minHeight: 36, verticalPadding: 16);
        _grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;
        _grid.SelectionChanged += (_, _) => UpdateSelectionDetails();
        _grid.CellFormatting += GridCellFormatting;
        gridPanel.Controls.Add(_grid);

        var detailPanel = CreateSurfacePanel();
        detailPanel.Padding = new Padding(16, 12, 16, 12);
        root.Controls.Add(detailPanel, 0, 2);

        var detailLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailPanel.Controls.Add(detailLayout);

        _detailTitleLabel.AutoSize = true;
        _detailTitleLabel.Font = new Font("Microsoft YaHei UI", 9.6f, FontStyle.Bold);
        _detailTitleLabel.ForeColor = UiThemePalette.TextPrimary;
        _detailTitleLabel.Margin = new Padding(0, 0, 0, 8);
        _detailTitleLabel.Text = "当前项详情";
        detailLayout.Controls.Add(_detailTitleLabel, 0, 0);

        _detailBox.Dock = DockStyle.Fill;
        _detailBox.BackColor = UiThemePalette.SurfaceMuted;
        _detailBox.BorderStyle = BorderStyle.None;
        _detailBox.Font = new Font("Microsoft YaHei UI", 9.1f);
        _detailBox.Margin = Padding.Empty;
        _detailBox.ReadOnly = true;
        detailLayout.Controls.Add(_detailBox, 0, 1);

        var footerPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty
        };
        root.Controls.Add(footerPanel, 0, 3);

        ConfigureFooterButton(_closeButton, "关闭", 70, primary: false);
        _closeButton.Click += (_, _) => Close();
        footerPanel.Controls.Add(_closeButton);

        ConfigureFooterButton(_copyDetailsButton, "复制详情", 92, primary: false);
        _copyDetailsButton.Click += (_, _) => CopyCurrentEntryDetails();
        footerPanel.Controls.Add(_copyDetailsButton);

        ConfigureFooterButton(_openStartupSettingsButton, "启动应用设置", 120, primary: true);
        _openStartupSettingsButton.Click += (_, _) => OpenStartupSettings();
        footerPanel.Controls.Add(_openStartupSettingsButton);
    }

    private void PopulateGrid()
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OptimizationAuditEntry.CategoryText),
            HeaderText = "类型",
            Width = Math.Max(92, UiScaleHelper.MeasureGridColumnWidth("类型", 92, 26)),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OptimizationAuditEntry.ImpactText),
            HeaderText = "影响",
            Width = Math.Max(96, UiScaleHelper.MeasureGridColumnWidth("影响", 96, 26)),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OptimizationAuditEntry.RecommendationText),
            HeaderText = "建议",
            Width = Math.Max(112, UiScaleHelper.MeasureGridColumnWidth("建议", 112, 28)),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OptimizationAuditEntry.Name),
            HeaderText = "名称",
            Width = Math.Max(180, UiScaleHelper.MeasureGridColumnWidth("名称", 180, 28)),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OptimizationAuditEntry.Source),
            HeaderText = "来源",
            Width = Math.Max(260, UiScaleHelper.MeasureGridColumnWidth("来源", 260, 28)),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(OptimizationAuditEntry.ReasonText),
            HeaderText = "原因",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 320,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });

        _grid.DataSource = _rows;
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
        }
    }

    private string BuildSummaryText()
    {
        return _rows.Count == 0
            ? "没有发现传统启动项入口。后续版本会继续扩展计划任务和服务的只读体检。"
            : $"发现 {_snapshot.StartupItemCount} 个启动项 | 高影响 {_snapshot.HighImpactCount} 个 | 建议确认 {_snapshot.ReviewCount} 个 | 建议保留 {_snapshot.SafeCount} 个";
    }

    private void PopulateSummaryChips()
    {
        _summaryChipFlow.Controls.Clear();
        _summaryChipFlow.Controls.Add(CreateSummaryChip(
            $"启动项 {_snapshot.StartupItemCount}",
            UiThemePalette.InfoSurface,
            UiThemePalette.Info));
        _summaryChipFlow.Controls.Add(CreateSummaryChip(
            $"高影响 {_snapshot.HighImpactCount}",
            _snapshot.HighImpactCount > 0 ? UiThemePalette.WarningSurface : UiThemePalette.AccentSurface,
            _snapshot.HighImpactCount > 0 ? UiThemePalette.Warning : UiThemePalette.AccentStrong));
        _summaryChipFlow.Controls.Add(CreateSummaryChip(
            $"建议确认 {_snapshot.ReviewCount}",
            UiThemePalette.SurfaceMuted,
            UiThemePalette.TextSecondary));
        _summaryChipFlow.Controls.Add(CreateSummaryChip(
            $"只读不修改",
            UiThemePalette.AccentSurface,
            UiThemePalette.AccentStrong));
    }

    private static Control CreateSummaryChip(string text, Color backColor, Color foreColor)
    {
        return new Label
        {
            AutoSize = true,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Microsoft YaHei UI", 8.7f, FontStyle.Bold),
            Margin = new Padding(0, 0, 8, 6),
            Padding = new Padding(12, 6, 12, 6),
            Text = text
        };
    }

    private OptimizationAuditEntry? GetCurrentEntry()
    {
        return _grid.CurrentRow?.DataBoundItem as OptimizationAuditEntry
            ?? _grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => row.DataBoundItem as OptimizationAuditEntry)
                .FirstOrDefault(entry => entry is not null);
    }

    private void UpdateSelectionDetails()
    {
        var entry = GetCurrentEntry();
        if (entry is null)
        {
            _detailTitleLabel.Text = "当前项详情";
            _detailBox.Text = "当前没有可显示的启动项。";
            _copyDetailsButton.Enabled = false;
            return;
        }

        _copyDetailsButton.Enabled = true;
        _detailTitleLabel.Text = $"当前项详情：{entry.Name}";
        _detailBox.Text = BuildDetails(entry);
    }

    private static string BuildDetails(OptimizationAuditEntry entry)
    {
        return
            $"类型：{entry.CategoryText}\r\n" +
            $"影响：{entry.ImpactText}\r\n" +
            $"建议：{entry.RecommendationText}\r\n" +
            $"厂商提示：{entry.PublisherHint}\r\n\r\n" +
            $"来源：\r\n{entry.Source}\r\n\r\n" +
            $"目标路径：\r\n{entry.TargetPath}\r\n\r\n" +
            $"命令行：\r\n{entry.CommandLine}\r\n\r\n" +
            $"判断原因：\r\n{entry.ReasonText}\r\n\r\n" +
            $"安全处理方式：\r\n{entry.SafeActionText}";
    }

    private void CopyCurrentEntryDetails()
    {
        var entry = GetCurrentEntry();
        if (entry is null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(BuildDetails(entry));
        }
        catch
        {
            MessageBox.Show("复制失败，请手动选中详情文本。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OpenStartupSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:startupapps",
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show("无法直接打开 Windows 启动应用设置。你也可以按 Ctrl+Shift+Esc 打开任务管理器，再进入“启动应用”。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void GridCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count)
        {
            return;
        }

        var entry = _rows[e.RowIndex];
        var column = _grid.Columns[e.ColumnIndex].DataPropertyName;
        e.ToolTipText = column switch
        {
            nameof(OptimizationAuditEntry.CategoryText) => entry.CategoryText,
            nameof(OptimizationAuditEntry.ImpactText) => entry.ImpactText,
            nameof(OptimizationAuditEntry.RecommendationText) => entry.RecommendationText,
            nameof(OptimizationAuditEntry.Name) => entry.Name,
            nameof(OptimizationAuditEntry.Source) => entry.Source,
            nameof(OptimizationAuditEntry.ReasonText) => $"{entry.ReasonText}\r\n\r\n安全处理方式：{entry.SafeActionText}",
            _ => string.Empty
        };
    }

    private void GridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count)
        {
            return;
        }

        var entry = _rows[e.RowIndex];
        var propertyName = _grid.Columns[e.ColumnIndex].DataPropertyName;
        if (propertyName == nameof(OptimizationAuditEntry.ImpactText))
        {
            e.CellStyle.ForeColor = entry.ImpactLevel switch
            {
                OptimizationAuditImpact.High => UiThemePalette.Warning,
                OptimizationAuditImpact.Low => UiThemePalette.AccentStrong,
                _ => UiThemePalette.Info
            };
            e.CellStyle.BackColor = entry.ImpactLevel switch
            {
                OptimizationAuditImpact.High => UiThemePalette.WarningSurface,
                OptimizationAuditImpact.Low => UiThemePalette.AccentSurface,
                _ => UiThemePalette.InfoSurface
            };
            e.CellStyle.SelectionBackColor = e.CellStyle.BackColor;
            e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
        }

        if (propertyName == nameof(OptimizationAuditEntry.RecommendationText))
        {
            e.CellStyle.ForeColor = entry.RecommendationLevel switch
            {
                OptimizationAuditRecommendation.Safe => UiThemePalette.AccentStrong,
                OptimizationAuditRecommendation.Caution => UiThemePalette.Warning,
                _ => UiThemePalette.TextPrimary
            };
        }
    }

    private void ConfigureFooterButton(Button button, string text, int minimumWidth, bool primary)
    {
        UiThemePalette.ApplyButtonStyle(button, primary);
        button.Margin = new Padding(8, 0, 0, 0);
        UiScaleHelper.RegisterButtonSizing(button, text, minimumWidth, 22, minHeight: 38, verticalPadding: 14);
    }

    private static Panel CreateSurfacePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiThemePalette.SurfaceRaised,
            Margin = Padding.Empty
        };
        UiThemePalette.AttachBorderPainter(panel);
        return panel;
    }

    private void UpdateResponsiveLayout()
    {
        var wrapWidth = UiScaleHelper.MeasureWrapWidth(ClientSize.Width, 120, minWidth: 520);
        _guardrailLabel.MaximumSize = new Size(wrapWidth, 0);
    }

    private void ApplyThemeColors()
    {
        UiThemePalette.ApplyTreeTheme(this);
        UiThemePalette.ApplyDataGridTheme(_grid);
        _summaryLabel.ForeColor = UiThemePalette.TextSecondary;
        _guardrailLabel.ForeColor = UiThemePalette.Warning;
        _detailTitleLabel.ForeColor = UiThemePalette.TextPrimary;
        _detailBox.ForeColor = UiThemePalette.TextSecondary;
        _detailBox.BackColor = UiThemePalette.SurfaceMuted;
    }
}
