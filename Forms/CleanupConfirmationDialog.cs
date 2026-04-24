using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;

namespace PortableCDriveCleaner.Forms;

public sealed class CleanupConfirmationDialog : Form
{
    private readonly CleanupExecutionPreview _preview;
    private readonly ScrollFriendlyDataGridView _itemGrid = new();
    private readonly ScrollFriendlyDataGridView _residueGrid = new();
    private readonly CheckBox _riskAcknowledgementCheckBox = new();
    private readonly Label _introLabel = new();
    private readonly Label _residueHintLabel = new();
    private readonly Label _riskHintLabel = new();
    private Button? _confirmButton;

    public CleanupConfirmationDialog(string appName, CleanupExecutionPreview preview)
    {
        _preview = preview;

        Text = preview.Title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(920, 620);
        ClientSize = new Size(1080, preview.HasResidueActions ? 760 : 660);
        UiThemePalette.ApplyFormChrome(this);

        BuildUi(appName);
        Resize += (_, _) => UpdateResponsiveLayout();
        Shown += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) => BeginInvoke(new Action(() =>
        {
            UiScaleHelper.RefreshRegisteredButtonSizing(this);
            UpdateResponsiveLayout();
        }));
        UiThemePalette.ApplyTreeTheme(this);
        UiThemePalette.ApplyDataGridTheme(_itemGrid);
        UiThemePalette.ApplyDataGridTheme(_residueGrid);
        UpdateConfirmState();
    }

    private void BuildUi(string appName)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 16),
            BackColor = UiThemePalette.WindowBackground
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 41, 54),
            Margin = new Padding(0, 0, 0, 8),
            Text = _preview.Title
        }, 0, 0);

        root.Controls.Add(BuildSummaryCard(appName), 0, 1);
        root.Controls.Add(BuildContentArea(), 0, 2);
        root.Controls.Add(BuildFooter(), 0, 3);
    }

    private Control BuildSummaryCard(string appName)
    {
        var card = CreateCard(new Padding(16, 14, 16, 14));

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(36, 53, 67),
            Margin = new Padding(0, 0, 0, 6),
            Text = $"{appName} 将处理 {_preview.TotalCount} 项，预计释放 {_preview.TotalBytesText}"
        }, 0, 0);

        _introLabel.AutoSize = true;
        _introLabel.ForeColor = Color.FromArgb(76, 88, 99);
        _introLabel.Margin = new Padding(0, 0, 0, 6);
        _introLabel.Text = _preview.IntroText;
        layout.Controls.Add(_introLabel, 0, 1);

        if (_preview.HasResidueActions)
        {
            _residueHintLabel.AutoSize = true;
            _residueHintLabel.ForeColor = Color.FromArgb(32, 105, 82);
            _residueHintLabel.Margin = new Padding(0, 0, 0, 6);
            _residueHintLabel.Text = $"本次还会联动清理 {_preview.ResidueActionCount} 个强关联残留项，包括注册表、启动项、快捷方式、计划任务或服务残留。";
            layout.Controls.Add(_residueHintLabel, 0, 2);
        }

        _riskHintLabel.AutoSize = true;
        _riskHintLabel.ForeColor = Color.FromArgb(142, 98, 15);
        _riskHintLabel.Margin = Padding.Empty;
        _riskHintLabel.Text = _preview.RiskHintText;
        layout.Controls.Add(_riskHintLabel, 0, _preview.HasResidueActions ? 3 : 2);

        return card;
    }

    private Control BuildContentArea()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = _preview.HasResidueActions ? 2 : 1,
            Margin = new Padding(0, 12, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, _preview.HasResidueActions ? 62 : 100));
        if (_preview.HasResidueActions)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        }

        var itemCard = CreateCard(new Padding(0));
        itemCard.Controls.Add(BuildSectionHeader("将处理的文件和目录"));
        ConfigureItemGrid();
        _itemGrid.DataSource = _preview.Items.ToList();
        itemCard.Controls.Add(_itemGrid);
        layout.Controls.Add(itemCard, 0, 0);

        if (_preview.HasResidueActions)
        {
            var residueCard = CreateCard(new Padding(0));
            residueCard.Margin = new Padding(0, 12, 0, 0);
            residueCard.Controls.Add(BuildSectionHeader("将联动清理的应用残留"));
            ConfigureResidueGrid();
            _residueGrid.DataSource = _preview.ResidueActions.ToList();
            residueCard.Controls.Add(_residueGrid);
            layout.Controls.Add(residueCard, 0, 1);
        }

        return layout;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (_preview.RequiresSecondaryRiskAcknowledgement)
        {
            _riskAcknowledgementCheckBox.AutoSize = true;
            _riskAcknowledgementCheckBox.MaximumSize = new Size(780, 0);
            _riskAcknowledgementCheckBox.ForeColor = Color.FromArgb(136, 77, 10);
            _riskAcknowledgementCheckBox.Margin = new Padding(0, 0, 0, 10);
            _riskAcknowledgementCheckBox.Text = _preview.SecondaryRiskAcknowledgementText;
            _riskAcknowledgementCheckBox.CheckedChanged += (_, _) => UpdateConfirmState();
            footer.Controls.Add(_riskAcknowledgementCheckBox, 0, 0);
            footer.SetColumnSpan(_riskAcknowledgementCheckBox, 3);
        }

        var cancelButton = CreateFooterButton("取消", primary: false);
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        footer.Controls.Add(cancelButton, 1, 1);

        _confirmButton = CreateFooterButton(_preview.ConfirmButtonText, primary: true);
        _confirmButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        footer.Controls.Add(_confirmButton, 2, 1);

        return footer;
    }

    private void UpdateConfirmState()
    {
        if (_confirmButton is null)
        {
            return;
        }

        _confirmButton.Enabled = !_preview.RequiresSecondaryRiskAcknowledgement || _riskAcknowledgementCheckBox.Checked;
    }

    private void ConfigureItemGrid()
    {
        ConfigureBaseGrid(_itemGrid);
        _itemGrid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (_itemGrid.Columns[e.ColumnIndex].DataPropertyName != nameof(CleanupExecutionPreviewItem.ImpactSeverityText))
            {
                return;
            }

            if (_itemGrid.Rows[e.RowIndex].DataBoundItem is not CleanupExecutionPreviewItem item)
            {
                return;
            }

            e.CellStyle.ForeColor = item.Item.ImpactSeverity switch
            {
                CleanupImpactSeverity.Low => Color.FromArgb(27, 125, 78),
                CleanupImpactSeverity.Medium => Color.FromArgb(176, 103, 12),
                CleanupImpactSeverity.High => Color.FromArgb(179, 52, 52),
                _ => e.CellStyle.ForeColor
            };
            e.CellStyle.Font = new Font(_itemGrid.Font, FontStyle.Bold);
        };

        _itemGrid.Columns.Add(CreateTextColumn(nameof(CleanupExecutionPreviewItem.DriveName), "盘符", 72));
        _itemGrid.Columns.Add(CreateTextColumn(nameof(CleanupExecutionPreviewItem.Category), "分类", 112));
        _itemGrid.Columns.Add(CreateFillColumn(nameof(CleanupExecutionPreviewItem.Name), "名称", 22));
        _itemGrid.Columns.Add(CreateTextColumn(nameof(CleanupExecutionPreviewItem.TypeDescription), "类型", 154));
        _itemGrid.Columns.Add(CreateTextColumn(nameof(CleanupExecutionPreviewItem.SizeText), "大小", 104));
        _itemGrid.Columns.Add(CreateTextColumn(nameof(CleanupExecutionPreviewItem.RecommendationText), "建议", 108));
        _itemGrid.Columns.Add(CreateFillColumn(nameof(CleanupExecutionPreviewItem.ImpactText), "删除后果", 42));
        _itemGrid.Columns.Add(CreateTextColumn(nameof(CleanupExecutionPreviewItem.ImpactSeverityText), "严重度", 82));
    }

    private void ConfigureResidueGrid()
    {
        ConfigureBaseGrid(_residueGrid);
        _residueGrid.Columns.Add(CreateTextColumn(nameof(ResidueAction.KindText), "类型", 96));
        _residueGrid.Columns.Add(CreateTextColumn(nameof(ResidueAction.DisplayName), "名称", 170));
        _residueGrid.Columns.Add(CreateFillColumn(nameof(ResidueAction.Target), "目标", 34));
        _residueGrid.Columns.Add(CreateFillColumn(nameof(ResidueAction.MatchReason), "匹配原因", 28));
    }

    private static void ConfigureBaseGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AllowUserToResizeColumns = true;
        grid.AutoGenerateColumns = false;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.ReadOnly = true;
        grid.BorderStyle = BorderStyle.None;
        grid.BackgroundColor = Color.White;
        grid.GridColor = Color.FromArgb(226, 231, 236);
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.ScrollBars = ScrollBars.Both;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ColumnHeadersHeight = UiScaleHelper.MeasureGridHeaderHeight(new Font("Microsoft YaHei UI", 9, FontStyle.Bold), minHeight: 42, verticalPadding: 18);
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = UiScaleHelper.MeasureGridRowHeight(grid.Font, minHeight: 34, verticalPadding: 16);
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 245, 247);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(40, 55, 68);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Color.FromArgb(43, 56, 66);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 243);
        grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(36, 48, 58);
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
    }

    private static Panel BuildSectionHeader(string text)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 12, 16, 10),
            BackColor = Color.White
        };

        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(36, 53, 67),
            Text = text
        });

        return panel;
    }

    private static Panel CreateCard(Padding padding)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiThemePalette.SurfaceRaised,
            Padding = padding,
            Margin = Padding.Empty
        };
        UiThemePalette.AttachBorderPainter(card);
        return card;
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(string propertyName, string header, int width)
    {
        return new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            Width = Math.Max(width, UiScaleHelper.MeasureGridColumnWidth(header, width, 28)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private static DataGridViewTextBoxColumn CreateFillColumn(string propertyName, string header, float fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = fillWeight,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
    }

    private static Button CreateFooterButton(string text, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Margin = new Padding(10, 0, 0, 0),
            Padding = new Padding(16, 0, 16, 0),
            TextAlign = ContentAlignment.MiddleCenter
        };
        UiThemePalette.ApplyButtonStyle(button, primary);
        UiScaleHelper.RegisterButtonSizing(button, text, 100, 42, minHeight: 42, verticalPadding: 18);
        return button;
    }

    private void UpdateResponsiveLayout()
    {
        var wrapWidth = UiScaleHelper.MeasureWrapWidth(ClientSize.Width, 110, minWidth: 520);
        _introLabel.MaximumSize = new Size(wrapWidth, 0);
        _residueHintLabel.MaximumSize = new Size(wrapWidth, 0);
        _riskHintLabel.MaximumSize = new Size(wrapWidth, 0);
        _riskAcknowledgementCheckBox.MaximumSize = new Size(Math.Max(420, wrapWidth - 120), 0);
    }
}
