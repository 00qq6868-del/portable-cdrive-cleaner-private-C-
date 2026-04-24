using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;
using PortableCDriveCleaner.Services;

namespace PortableCDriveCleaner.Forms;

public sealed class DeploymentDialog : Form
{
    private const int DefaultDialogWidth = 820;
    private const int DefaultDialogHeight = 560;
    private const int MinimumDialogWidth = 760;
    private const int MinimumDialogHeight = 540;

    private readonly ComboBox _targetComboBox = new();
    private readonly TextBox _pathTextBox = new();
    private readonly Label _warningLabel = new();
    private readonly Label _introLabel = new();
    private readonly Label _hintLabel = new();
    private readonly CheckBox _desktopShortcutCheckBox = new();
    private readonly CheckBox _migrateDataCheckBox = new();
    private readonly ScrollFriendlyPanel _scrollHost = new();
    private readonly IReadOnlyList<DeploymentTarget> _targets;

    public DeploymentDialog(
        string title,
        string heading,
        string introText,
        IReadOnlyList<DeploymentTarget> targets,
        string? initialPath,
        bool defaultCreateShortcut,
        bool allowDataMigration)
    {
        _targets = targets;
        SelectedPath = initialPath ?? targets.FirstOrDefault()?.TargetPath ?? string.Empty;

        Text = title;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = false;
        MinimumSize = new Size(MinimumDialogWidth, MinimumDialogHeight);
        ClientSize = new Size(DefaultDialogWidth, DefaultDialogHeight);
        UiThemePalette.ApplyFormChrome(this);

        BuildUi(heading, introText, defaultCreateShortcut, allowDataMigration);
        Resize += (_, _) => UpdateResponsiveLayout();
        Shown += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) => BeginInvoke(new Action(() =>
        {
            UiScaleHelper.RefreshRegisteredButtonSizing(this);
            UpdateResponsiveLayout();
        }));
        LoadTargets();
        UpdateWarning();
    }

    public string SelectedPath { get; private set; }

    public bool CreateDesktopShortcut => _desktopShortcutCheckBox.Checked;

    public bool MigrateData => _migrateDataCheckBox.Checked;

    private void BuildUi(string heading, string introText, bool defaultCreateShortcut, bool allowDataMigration)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 18, 20, 18),
            BackColor = UiThemePalette.WindowBackground
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14, FontStyle.Bold),
            ForeColor = Color.FromArgb(27, 42, 56),
            Margin = new Padding(0, 0, 0, 10),
            Text = heading
        }, 0, 0);

        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiThemePalette.SurfaceRaised,
            Padding = new Padding(0),
            Margin = Padding.Empty
        };
        UiThemePalette.AttachBorderPainter(card);
        root.Controls.Add(card, 0, 1);

        _scrollHost.Dock = DockStyle.Fill;
        _scrollHost.Padding = new Padding(18, 16, 18, 16);
        _scrollHost.BackColor = UiThemePalette.SurfaceRaised;
        _scrollHost.Margin = Padding.Empty;
        card.Controls.Add(_scrollHost);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 7,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 0; i < layout.RowCount; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        _scrollHost.Controls.Add(layout);

        _introLabel.AutoSize = true;
        _introLabel.ForeColor = Color.FromArgb(76, 90, 101);
        _introLabel.Margin = new Padding(0, 0, 0, 14);
        _introLabel.Text = introText;
        layout.Controls.Add(_introLabel, 0, 0);
        layout.SetColumnSpan(_introLabel, 3);

        layout.Controls.Add(CreateLabel("推荐盘符"), 0, 1);
        _targetComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _targetComboBox.Dock = DockStyle.Fill;
        _targetComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_targetComboBox.SelectedItem is DeploymentTarget target)
            {
                _pathTextBox.Text = target.TargetPath;
            }
        };
        layout.Controls.Add(_targetComboBox, 1, 1);

        layout.Controls.Add(CreateLabel("安装路径"), 0, 2);
        _pathTextBox.Dock = DockStyle.Fill;
        _pathTextBox.TextChanged += (_, _) => UpdateWarning();
        layout.Controls.Add(_pathTextBox, 1, 2);

        var browseButton = CreateButton("浏览...", primary: false);
        browseButton.Click += (_, _) => BrowsePath();
        layout.Controls.Add(browseButton, 2, 2);

        _warningLabel.AutoSize = true;
        _warningLabel.ForeColor = Color.FromArgb(185, 62, 62);
        _warningLabel.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(_warningLabel, 0, 3);
        layout.SetColumnSpan(_warningLabel, 3);

        _desktopShortcutCheckBox.AutoSize = true;
        _desktopShortcutCheckBox.Checked = defaultCreateShortcut;
        _desktopShortcutCheckBox.Margin = new Padding(0, 12, 0, 0);
        _desktopShortcutCheckBox.Text = "创建桌面快捷方式";
        layout.Controls.Add(_desktopShortcutCheckBox, 0, 4);
        layout.SetColumnSpan(_desktopShortcutCheckBox, 3);

        _migrateDataCheckBox.AutoSize = true;
        _migrateDataCheckBox.Checked = allowDataMigration;
        _migrateDataCheckBox.Enabled = allowDataMigration;
        _migrateDataCheckBox.Visible = allowDataMigration;
        _migrateDataCheckBox.Margin = new Padding(0, 6, 0, 0);
        _migrateDataCheckBox.Text = "把当前 data 设置和日志一起迁过去";
        layout.Controls.Add(_migrateDataCheckBox, 0, 5);
        layout.SetColumnSpan(_migrateDataCheckBox, 3);

        _hintLabel.AutoSize = true;
        _hintLabel.ForeColor = Color.FromArgb(73, 88, 101);
        _hintLabel.Margin = new Padding(0, 12, 0, 0);
        _hintLabel.Text = "建议不要放在 C 盘。默认会优先选择容量更充足的非 C 固定盘，并把程序放到“磁盘清理器”目录中，方便自己和别人查找。";
        layout.Controls.Add(_hintLabel, 0, 6);
        layout.SetColumnSpan(_hintLabel, 3);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 14, 0, 0)
        };
        root.Controls.Add(footer, 0, 2);

        var cancelButton = CreateButton("取消", primary: false);
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        footer.Controls.Add(cancelButton);

        var confirmButton = CreateButton("立即安装", primary: true);
        confirmButton.Click += (_, _) => Confirm();
        footer.Controls.Add(confirmButton);
        AcceptButton = confirmButton;
        UiThemePalette.ApplyTreeTheme(this);
    }

    private void LoadTargets()
    {
        _targetComboBox.DisplayMember = nameof(DeploymentTarget.SummaryText);
        _targetComboBox.Items.Clear();

        foreach (var target in _targets)
        {
            _targetComboBox.Items.Add(target);
        }

        if (_targets.Count > 0)
        {
            var selected = _targets.FirstOrDefault(target => string.Equals(target.TargetPath, SelectedPath, StringComparison.OrdinalIgnoreCase))
                ?? _targets.FirstOrDefault(target => target.IsRecommended)
                ?? _targets[0];
            _targetComboBox.SelectedItem = selected;
            _pathTextBox.Text = SelectedPath;
        }
    }

    private void BrowsePath()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择安装目录",
            UseDescriptionForTitle = true,
            InitialDirectory = string.IsNullOrWhiteSpace(_pathTextBox.Text)
                ? _targets.FirstOrDefault()?.TargetPath ?? string.Empty
                : _pathTextBox.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _pathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void Confirm()
    {
        SelectedPath = _pathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(SelectedPath))
        {
            MessageBox.Show("请先选择一个安装路径。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (DeploymentService.IsSystemDrivePath(SelectedPath))
        {
            var choice = MessageBox.Show(
                "你现在选择的是 C 盘。这样会继续挤占系统盘空间，也不符合默认建议。只有在你明确知道后果时才建议这样做。\r\n\r\n确定仍然继续吗？",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (choice != DialogResult.Yes)
            {
                return;
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateWarning()
    {
        var path = _pathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            _warningLabel.Text = "请选择一个非 C 盘目录。";
            return;
        }

        if (DeploymentService.IsSystemDrivePath(path))
        {
            _warningLabel.Text = "当前路径位于 C 盘。继续这样安装会占用系统盘空间，只有在你非常确定时才建议继续。";
            return;
        }

        _warningLabel.Text = string.Empty;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(61, 77, 89),
            Margin = new Padding(0, 6, 10, 0),
            Text = text
        };
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            Margin = new Padding(10, 0, 0, 0),
            Padding = new Padding(16, 0, 16, 0),
            Text = text
        };
        UiThemePalette.ApplyButtonStyle(button, primary);
        UiScaleHelper.RegisterButtonSizing(button, text, 110, 42, minHeight: 42, verticalPadding: 18);
        return button;
    }

    private void UpdateResponsiveLayout()
    {
        var wrapWidth = UiScaleHelper.MeasureWrapWidth(_scrollHost.ClientSize.Width, 36, minWidth: 360);
        _introLabel.MaximumSize = new Size(wrapWidth, 0);
        _warningLabel.MaximumSize = new Size(wrapWidth, 0);
        _desktopShortcutCheckBox.MaximumSize = new Size(wrapWidth, 0);
        _migrateDataCheckBox.MaximumSize = new Size(wrapWidth, 0);
        _hintLabel.MaximumSize = new Size(wrapWidth, 0);
        var targetSummaries = _targets.Select(target => target.SummaryText).ToList();
        _targetComboBox.DropDownWidth = Math.Max(_targetComboBox.Width, UiScaleHelper.MeasureOptionWidth(targetSummaries, _targetComboBox.Width, 52, _targetComboBox.Font));
    }
}
