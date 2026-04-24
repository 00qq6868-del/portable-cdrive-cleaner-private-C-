using System.ComponentModel;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Forms;

public sealed class RegressionAuditDialog : Form
{
    private readonly ScrollFriendlyDataGridView _grid = new();
    private readonly ScrollFriendlyRichTextBox _detailBox = new();
    private readonly Label _summaryLabel = new();
    private readonly FlowLayoutPanel _summaryChipFlow = new();
    private readonly Label _openItemsLabel = new();
    private readonly Label _runtimeLabel = new();
    private readonly Label _detailTitleLabel = new();
    private readonly Button _closeButton = new();
    private readonly BindingList<RegressionAuditEntry> _rows;

    public RegressionAuditDialog(IReadOnlyCollection<RegressionAuditEntry> entries, string runtimeInfoText)
    {
        _rows = new BindingList<RegressionAuditEntry>(entries
            .OrderBy(entry => entry.Order)
            .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
            .ToList());

        Text = "问题结案清单";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(980, 720);
        ClientSize = new Size(1180, 820);
        BackColor = Color.FromArgb(243, 246, 248);

        BuildUi(runtimeInfoText);
        Resize += (_, _) => UpdateResponsiveLayout();
        Shown += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) => BeginInvoke(new Action(() =>
        {
            UiScaleHelper.RefreshRegisteredButtonSizing(this);
            UpdateResponsiveLayout();
        }));
        PopulateGrid();
        UpdateSelectionDetails();
    }

    private void BuildUi(string runtimeInfoText)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 14)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        var headerPanel = CreateSurfacePanel();
        headerPanel.Padding = new Padding(18, 14, 18, 14);
        root.Controls.Add(headerPanel, 0, 0);

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = Padding.Empty
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerPanel.Controls.Add(headerLayout);

        headerLayout.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 12.8f, FontStyle.Bold),
            ForeColor = Color.FromArgb(27, 42, 56),
            Margin = new Padding(0, 0, 0, 8),
            Text = "历史问题结案清单"
        }, 0, 0);

        _summaryLabel.AutoSize = true;
        _summaryLabel.ForeColor = Color.FromArgb(41, 77, 96);
        _summaryLabel.Font = new Font("Microsoft YaHei UI", 9.1f, FontStyle.Bold);
        _summaryLabel.Margin = new Padding(0, 0, 0, 6);
        _summaryLabel.Text = BuildSummaryText();
        headerLayout.Controls.Add(_summaryLabel, 0, 1);

        _summaryChipFlow.AutoSize = true;
        _summaryChipFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _summaryChipFlow.WrapContents = true;
        _summaryChipFlow.Margin = new Padding(0, 0, 0, 8);
        _summaryChipFlow.Padding = Padding.Empty;
        PopulateSummaryChips();
        headerLayout.Controls.Add(_summaryChipFlow, 0, 2);

        _openItemsLabel.AutoSize = true;
        _openItemsLabel.MaximumSize = new Size(1080, 0);
        _openItemsLabel.ForeColor = Color.FromArgb(143, 94, 24);
        _openItemsLabel.Margin = new Padding(0, 0, 0, 6);
        _openItemsLabel.Text = BuildOpenItemsText();
        headerLayout.Controls.Add(_openItemsLabel, 0, 3);

        _runtimeLabel.AutoSize = true;
        _runtimeLabel.MaximumSize = new Size(1080, 0);
        _runtimeLabel.ForeColor = Color.FromArgb(89, 101, 113);
        _runtimeLabel.Margin = Padding.Empty;
        _runtimeLabel.Text = string.IsNullOrWhiteSpace(runtimeInfoText)
            ? $"当前核对版本：{Application.ExecutablePath}"
            : $"当前核对版本：{runtimeInfoText}\r\n实际 EXE：{Application.ExecutablePath}";
        headerLayout.Controls.Add(_runtimeLabel, 0, 4);

        var gridPanel = CreateSurfacePanel();
        gridPanel.Padding = new Padding(12);
        gridPanel.Margin = new Padding(0, 12, 0, 12);
        root.Controls.Add(gridPanel, 0, 1);

        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
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
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(229, 242, 255);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(24, 37, 48);
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 250);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(56, 70, 82);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        _grid.ColumnHeadersHeight = UiScaleHelper.MeasureGridHeaderHeight(new Font("Microsoft YaHei UI", 9, FontStyle.Bold), minHeight: 42, verticalPadding: 18);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.RowTemplate.Height = UiScaleHelper.MeasureGridRowHeight(_grid.Font, minHeight: 34, verticalPadding: 16);
        _grid.EnableHeadersVisualStyles = false;
        _grid.GridColor = Color.FromArgb(231, 236, 241);
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
        _detailTitleLabel.ForeColor = Color.FromArgb(39, 59, 76);
        _detailTitleLabel.Margin = new Padding(0, 0, 0, 8);
        _detailTitleLabel.Text = "当前项详情";
        detailLayout.Controls.Add(_detailTitleLabel, 0, 0);

        _detailBox.Dock = DockStyle.Fill;
        _detailBox.BackColor = Color.FromArgb(250, 252, 253);
        _detailBox.Font = new Font("Microsoft YaHei UI", 9);
        _detailBox.Margin = Padding.Empty;
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

        _closeButton.AutoSize = false;
        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.FlatAppearance.BorderColor = Color.FromArgb(214, 221, 228);
        _closeButton.FlatAppearance.BorderSize = 1;
        _closeButton.BackColor = Color.White;
        _closeButton.ForeColor = Color.FromArgb(37, 54, 68);
        _closeButton.Margin = Padding.Empty;
        _closeButton.Text = "关闭";
        _closeButton.Click += (_, _) => Close();
        UiScaleHelper.RegisterButtonSizing(_closeButton, "关闭", 124, 40, minHeight: 40, verticalPadding: 16);
        footerPanel.Controls.Add(_closeButton);
    }

    private void PopulateGrid()
    {
        _grid.Columns.Clear();
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RegressionAuditEntry.StatusText),
            HeaderText = "状态",
            Width = Math.Max(118, UiScaleHelper.MeasureGridColumnWidth("状态", 118, 28)),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RegressionAuditEntry.Category),
            HeaderText = "分组",
            Width = Math.Max(128, UiScaleHelper.MeasureGridColumnWidth("分组", 128, 28)),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RegressionAuditEntry.Title),
            HeaderText = "问题",
            Width = Math.Max(236, UiScaleHelper.MeasureGridColumnWidth("问题", 236, 28)),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(RegressionAuditEntry.ConclusionText),
            HeaderText = "当前结论",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 420,
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
        var total = _rows.Count;
        var closed = _rows.Count(entry => entry.CountsAsClosed);
        var partial = _rows.Count(entry => entry.Status == RegressionAuditStatus.Partial);
        var readiness = total == 0
            ? 100
            : (int)Math.Round(closed * 100d / total, MidpointRounding.AwayFromZero);

        return partial == 0
            ? $"核心问题已全部收口 | 当前完成度 {readiness}%"
            : $"核心问题已收口 {closed}/{total} 项 | 当前完成度 {readiness}% | 仍有 {partial} 项继续推进";
    }

    private string BuildOpenItemsText()
    {
        var partialTitles = _rows
            .Where(entry => entry.IsCoreOpen)
            .Select(entry => entry.Title)
            .ToList();

        return partialTitles.Count == 0
            ? "当前没有仍在推进的核心问题，剩余内容都属于保护边界或后续优化。"
            : $"仍在推进的核心问题：{string.Join("；", partialTitles)}";
    }

    private void PopulateSummaryChips()
    {
        _summaryChipFlow.Controls.Clear();
        _summaryChipFlow.Controls.Add(CreateSummaryChip(
            $"已修复 {_rows.Count(entry => entry.Status == RegressionAuditStatus.Resolved)} 项",
            Color.FromArgb(237, 248, 244),
            Color.FromArgb(28, 111, 83)));
        _summaryChipFlow.Controls.Add(CreateSummaryChip(
            $"仍在推进 {_rows.Count(entry => entry.Status == RegressionAuditStatus.Partial)} 项",
            Color.FromArgb(255, 247, 232),
            Color.FromArgb(156, 96, 26)));
        _summaryChipFlow.Controls.Add(CreateSummaryChip(
            $"主体完成 {_rows.Count(entry => entry.Status == RegressionAuditStatus.FollowUp)} 项",
            Color.FromArgb(235, 245, 255),
            Color.FromArgb(33, 91, 148)));
        _summaryChipFlow.Controls.Add(CreateSummaryChip(
            $"保护规则 {_rows.Count(entry => entry.Status == RegressionAuditStatus.ProtectedRule)} 项",
            Color.FromArgb(245, 238, 252),
            Color.FromArgb(100, 52, 143)));
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

    private void UpdateSelectionDetails()
    {
        var entry = GetCurrentEntry();
        if (entry is null)
        {
            _detailTitleLabel.Text = "当前项详情";
            _detailBox.Text = "当前没有可显示的条目。";
            return;
        }

        _detailTitleLabel.Text = $"当前项详情：{entry.Title}";
        _detailBox.Text =
            $"状态：{entry.StatusText}\r\n" +
            $"分组：{entry.Category}\r\n\r\n" +
            $"当前结论：\r\n{entry.ConclusionText}\r\n\r\n" +
            $"下一步：\r\n{entry.NextActionText}\r\n\r\n" +
            $"补充说明：\r\n{entry.NotesText}";
    }

    private RegressionAuditEntry? GetCurrentEntry()
    {
        return _grid.CurrentRow?.DataBoundItem as RegressionAuditEntry
            ?? _grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => row.DataBoundItem as RegressionAuditEntry)
                .FirstOrDefault(entry => entry is not null);
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
            nameof(RegressionAuditEntry.StatusText) => entry.StatusText,
            nameof(RegressionAuditEntry.Category) => entry.Category,
            nameof(RegressionAuditEntry.Title) => entry.Title,
            nameof(RegressionAuditEntry.ConclusionText) => $"{entry.ConclusionText}\r\n\r\n下一步：{entry.NextActionText}",
            _ => string.Empty
        };
    }

    private void GridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _rows.Count || e.ColumnIndex < 0 || e.ColumnIndex >= _grid.Columns.Count)
        {
            return;
        }

        if (_grid.Columns[e.ColumnIndex].DataPropertyName != nameof(RegressionAuditEntry.StatusText))
        {
            return;
        }

        var entry = _rows[e.RowIndex];
        e.CellStyle.ForeColor = entry.Status switch
        {
            RegressionAuditStatus.Resolved => Color.FromArgb(28, 111, 83),
            RegressionAuditStatus.Partial => Color.FromArgb(156, 96, 26),
            RegressionAuditStatus.ProtectedRule => Color.FromArgb(100, 52, 143),
            RegressionAuditStatus.FollowUp => Color.FromArgb(33, 91, 148),
            _ => Color.FromArgb(56, 70, 82)
        };
        e.CellStyle.BackColor = entry.Status switch
        {
            RegressionAuditStatus.Resolved => Color.FromArgb(237, 248, 244),
            RegressionAuditStatus.Partial => Color.FromArgb(255, 247, 232),
            RegressionAuditStatus.ProtectedRule => Color.FromArgb(245, 238, 252),
            RegressionAuditStatus.FollowUp => Color.FromArgb(235, 245, 255),
            _ => Color.White
        };
        e.CellStyle.SelectionBackColor = e.CellStyle.BackColor;
        e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
    }

    private static Panel CreateSurfacePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = Padding.Empty
        };

        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(226, 232, 237));
            var bounds = panel.ClientRectangle;
            bounds.Width -= 1;
            bounds.Height -= 1;
            e.Graphics.DrawRectangle(pen, bounds);
        };

        return panel;
    }

    private void UpdateResponsiveLayout()
    {
        _runtimeLabel.MaximumSize = new Size(UiScaleHelper.MeasureWrapWidth(ClientSize.Width, 100, minWidth: 520), 0);
    }
}
