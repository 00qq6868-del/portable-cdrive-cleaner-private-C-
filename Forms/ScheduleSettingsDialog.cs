using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;
using PortableCDriveCleaner.Services;

namespace PortableCDriveCleaner.Forms;

public sealed class ScheduleSettingsDialog : Form
{
    private readonly PortableContext _context;
    private readonly SettingsService _settingsService;
    private readonly SchedulerService _schedulerService;
    private readonly AppSettings _settings;

    private readonly CheckBox _autoCleanupCheckBox = new();
    private readonly CheckBox _scheduleCheckBox = new();
    private readonly CheckBox _safeOnlyCheckBox = new();
    private readonly CheckBox _duplicateCheckBox = new();
    private readonly NumericUpDown _intervalNumeric = new();
    private readonly NumericUpDown _thresholdNumeric = new();
    private readonly Label _statusLabel = new();
    private readonly Label _introLabel = new();
    private readonly Label _memoryHintLabel = new();
    private readonly Panel _card = new();

    public ScheduleSettingsDialog(
        PortableContext context,
        SettingsService settingsService,
        SchedulerService schedulerService,
        AppSettings settings)
    {
        _context = context;
        _settingsService = settingsService;
        _schedulerService = schedulerService;
        _settings = settings;

        BuildUi();
        Resize += (_, _) => UpdateResponsiveLayout();
        Shown += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) => BeginInvoke(new Action(() =>
        {
            UiScaleHelper.RefreshRegisteredButtonSizing(this);
            UpdateResponsiveLayout();
        }));
        LoadFromSettings();
        UpdateControlStates();
        UpdateStatusText();
    }

    public bool SettingsChanged { get; private set; }

    private void BuildUi()
    {
        Text = "自动清理设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        MinimumSize = new Size(640, 460);
        ClientSize = new Size(700, 480);
        UiThemePalette.ApplyFormChrome(this);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 18, 16),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiThemePalette.WindowBackground
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(rootLayout);

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold),
            ForeColor = Color.FromArgb(27, 43, 58),
            Margin = new Padding(0, 0, 0, 12),
            Text = "后台自动清理"
        };
        rootLayout.Controls.Add(titleLabel, 0, 0);

        var scrollHost = new ScrollFriendlyPanel
        {
            Dock = DockStyle.Fill,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        rootLayout.Controls.Add(scrollHost, 0, 1);

        _card.AutoSize = true;
        _card.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _card.Dock = DockStyle.Top;
        _card.BackColor = UiThemePalette.SurfaceRaised;
        _card.Padding = new Padding(18, 16, 18, 16);
        _card.Margin = Padding.Empty;
        UiThemePalette.AttachBorderPainter(_card);
        scrollHost.Controls.Add(_card);

        var cardLayout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 9,
            Margin = Padding.Empty
        };
        cardLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _card.Controls.Add(cardLayout);

        _introLabel.AutoSize = true;
        _introLabel.ForeColor = Color.FromArgb(83, 95, 106);
        _introLabel.Margin = new Padding(0, 0, 0, 12);
        _introLabel.Text = "打开后只会静默处理明确垃圾和完全重复文件；需要你确认的项目，仍然留在主界面手动决定。";
        cardLayout.Controls.Add(_introLabel, 0, 0);

        ConfigureCheckbox(_autoCleanupCheckBox, "启用自动清理能力");
        ConfigureCheckbox(_scheduleCheckBox, "启用每小时静默清理");
        ConfigureCheckbox(_safeOnlyCheckBox, "后台只自动处理明确垃圾和完全重复文件");
        ConfigureCheckbox(_duplicateCheckBox, "扫描各盘常见位置中的重复安装包 / 压缩包");

        _autoCleanupCheckBox.CheckedChanged += (_, _) => UpdateControlStates();
        _scheduleCheckBox.CheckedChanged += (_, _) => UpdateControlStates();

        cardLayout.Controls.Add(_autoCleanupCheckBox, 0, 1);
        cardLayout.Controls.Add(_scheduleCheckBox, 0, 2);
        cardLayout.Controls.Add(_safeOnlyCheckBox, 0, 3);

        _intervalNumeric.Minimum = 1;
        _intervalNumeric.Maximum = 23;
        _thresholdNumeric.Minimum = 32;
        _thresholdNumeric.Maximum = 4096;
        _thresholdNumeric.Increment = 32;

        cardLayout.Controls.Add(CreateInputRow("后台扫描间隔（小时）", _intervalNumeric), 0, 4);
        cardLayout.Controls.Add(CreateInputRow("小于多少 MB 时本轮跳过", _thresholdNumeric), 0, 5);
        cardLayout.Controls.Add(_duplicateCheckBox, 0, 6);

        _memoryHintLabel.AutoSize = true;
        _memoryHintLabel.ForeColor = Color.FromArgb(93, 103, 113);
        _memoryHintLabel.Margin = new Padding(0, 10, 0, 8);
        _memoryHintLabel.Text = "设计内存上限：2 GB。现在这版通常远低于这个值，适合作为绿色便携版直接拷走使用。";
        cardLayout.Controls.Add(_memoryHintLabel, 0, 7);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Color.FromArgb(73, 88, 101);
        _statusLabel.Margin = Padding.Empty;
        cardLayout.Controls.Add(_statusLabel, 0, 8);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 12, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rootLayout.Controls.Add(footer, 0, 2);

        var leftActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Left,
            WrapContents = false,
            Margin = Padding.Empty
        };
        footer.Controls.Add(leftActions, 0, 0);

        var refreshButton = CreateButton("刷新状态", primary: false);
        refreshButton.Click += (_, _) => UpdateStatusText();
        leftActions.Controls.Add(refreshButton);

        var removeButton = CreateButton("删除后台任务", primary: false);
        removeButton.Click += (_, _) => RemoveSchedule();
        leftActions.Controls.Add(removeButton);

        var rightActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Right,
            WrapContents = false,
            Margin = Padding.Empty
        };
        footer.Controls.Add(rightActions, 3, 0);

        var closeButton = CreateButton("关闭", primary: false);
        closeButton.Click += (_, _) => Close();
        rightActions.Controls.Add(closeButton);

        var saveButton = CreateButton("保存并同步", primary: true);
        saveButton.Click += (_, _) => SaveAndClose();
        rightActions.Controls.Add(saveButton);

        ApplyThemeColors();
    }

    private void LoadFromSettings()
    {
        _autoCleanupCheckBox.Checked = _settings.AutoCleanupEnabled;
        _scheduleCheckBox.Checked = _settings.ScheduleEnabled;
        _safeOnlyCheckBox.Checked = _settings.SafeItemsOnlyWhenScheduled;
        _duplicateCheckBox.Checked = _settings.ScanDownloadsForDuplicates;
        _intervalNumeric.Value = _settings.ScheduleIntervalHours;
        _thresholdNumeric.Value = _settings.MinimumPromptSizeMB;
    }

    private void UpdateControlStates()
    {
        var autoEnabled = _autoCleanupCheckBox.Checked;
        var scheduleEnabled = autoEnabled && _scheduleCheckBox.Checked;

        _scheduleCheckBox.Enabled = autoEnabled;
        _safeOnlyCheckBox.Enabled = scheduleEnabled;
        _intervalNumeric.Enabled = scheduleEnabled;
        _thresholdNumeric.Enabled = scheduleEnabled;
    }

    private void SaveAndClose()
    {
        _settings.AutoCleanupEnabled = _autoCleanupCheckBox.Checked;
        _settings.ScheduleEnabled = _scheduleCheckBox.Checked;
        _settings.PromptOnScheduledRun = false;
        _settings.SafeItemsOnlyWhenScheduled = _safeOnlyCheckBox.Checked;
        _settings.ScanDownloadsForDuplicates = _duplicateCheckBox.Checked;
        _settings.ScheduleIntervalHours = (int)_intervalNumeric.Value;
        _settings.MinimumPromptSizeMB = (int)_thresholdNumeric.Value;

        _settingsService.Save(_settings);
        _schedulerService.Sync(_context, _settings);
        SettingsChanged = true;
        Close();
    }

    private void RemoveSchedule()
    {
        _settings.ScheduleEnabled = false;
        _scheduleCheckBox.Checked = false;
        _settingsService.Save(_settings);
        _schedulerService.Remove(_settings);
        SettingsChanged = true;
        UpdateStatusText();
    }

    private void UpdateStatusText()
    {
        _statusLabel.Text = _schedulerService.GetStatusText(_settings);
    }

    private static void ConfigureCheckbox(CheckBox checkBox, string text)
    {
        checkBox.AutoSize = true;
        checkBox.UseCompatibleTextRendering = false;
        checkBox.Margin = new Padding(0, 0, 0, 8);
        checkBox.Text = text;
    }

    private static TableLayoutPanel CreateInputRow(string text, Control input)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 4, 0, 4)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        row.Controls.Add(new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 16, 0),
            Text = text
        }, 0, 0);

        input.Width = UiScaleHelper.MeasureTextWidth("4096", 120, 52, input.Font);
        input.Anchor = AnchorStyles.Right;
        row.Controls.Add(input, 1, 0);
        return row;
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            AutoSize = false,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(14, 0, 14, 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter
        };
        UiThemePalette.ApplyButtonStyle(button, primary);
        UiScaleHelper.RegisterButtonSizing(button, text, 96, 36, minHeight: 42, verticalPadding: 18);
        return button;
    }

    private void UpdateResponsiveLayout()
    {
        var wrapWidth = UiScaleHelper.MeasureWrapWidth(_card.ClientSize.Width, 36, minWidth: 340);
        _introLabel.MaximumSize = new Size(wrapWidth, 0);
        _memoryHintLabel.MaximumSize = new Size(wrapWidth, 0);
        _statusLabel.MaximumSize = new Size(wrapWidth, 0);
        _autoCleanupCheckBox.MaximumSize = new Size(wrapWidth, 0);
        _scheduleCheckBox.MaximumSize = new Size(wrapWidth, 0);
        _safeOnlyCheckBox.MaximumSize = new Size(wrapWidth, 0);
        _duplicateCheckBox.MaximumSize = new Size(wrapWidth, 0);
    }

    private void ApplyThemeColors()
    {
        UiThemePalette.ApplyTreeTheme(this);
        UiThemePalette.ApplyNumericUpDownStyle(_intervalNumeric);
        UiThemePalette.ApplyNumericUpDownStyle(_thresholdNumeric);
        _introLabel.ForeColor = UiThemePalette.TextSecondary;
        _memoryHintLabel.ForeColor = UiThemePalette.TextMuted;
        _statusLabel.ForeColor = UiThemePalette.TextSecondary;
    }
}
