using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;
using PortableCDriveCleaner.Services;

namespace PortableCDriveCleaner.Forms;

public sealed class MainForm : Form
{
    private const int DefaultWindowWidth = 1360;
    private const int DefaultWindowHeight = 860;
    private const int MinimumWindowWidth = 1220;
    private const int MinimumWindowHeight = 760;
    private const int WideModeThreshold = 1500;
    private const int CompactLayoutHeightThreshold = 1180;
    private const int UltraCompactLayoutHeightThreshold = 980;
    private const int TeachingSecondaryHideHeightThreshold = 900;
    private const int ResizeRefreshIntervalMilliseconds = 140;
    private const int JobCenterPassiveRefreshIntervalMilliseconds = 1000;

    private const string DriveAllKey = CleanupFilterState.AllValue;
    private const string QuickFilterAll = "all";
    private const string QuickFilterSafeJunk = "safe_junk";
    private const string QuickFilterAppCache = "app_cache";
    private const string QuickFilterPackages = "packages";
    private const string QuickFilterDuplicates = "duplicates";
    private const string QuickFilterResidue = "residue";
    private const string AutoModeAppOnly = "app_only";

    private enum LayoutDensityMode
    {
        Regular,
        Compact,
        UltraCompact
    }

    private readonly PortableContext _context;
    private readonly SettingsService _settingsService;
    private readonly ScanService _scanService;
    private readonly CleanupService _cleanupService;
    private readonly SchedulerService _schedulerService;
    private readonly OccupancyProbeService _occupancyProbeService;
    private readonly MigrationService _migrationService;
    private readonly SnapshotCacheService _snapshotCacheService;
    private readonly OperationManager _operationManager;
    private readonly bool _readOnlyMode;
    private readonly bool _preserveStartupView;
    private readonly bool _persistWindowState;

    private readonly TableLayoutPanel _rootLayout = new();
    private readonly Panel _headerPanel = CreateSurfacePanel();
    private readonly Panel _scanWarningPanel = new();
    private readonly Label _scanWarningLabel = new();
    private readonly LinkLabel _scanWarningLink = new();
    private readonly Button _dismissScanWarningButton = new();
    private readonly Panel _resultBannerPanel = new();
    private readonly Panel _jobCenterPanel = new();
    private readonly Label _jobCenterLabel = new();
    private readonly Label _jobCenterSummaryLabel = new();
    private readonly FlowLayoutPanel _jobListFlow = new();
    private readonly Label _resultBannerLabel = new();
    private readonly LinkLabel _resultBannerLink = new();
    private readonly Button _dismissBannerButton = new();
    private readonly Panel _viewModePanel = CreateSurfacePanel();
    private readonly FlowLayoutPanel _viewModeFlow = new();
    private readonly Button _cleanupViewButton = new ThemedButton();
    private readonly Button _overviewViewButton = new ThemedButton();
    private readonly Button _infrequentViewButton = new ThemedButton();
    private readonly Label _headerTitleLabel = new();
    private readonly Label _headerModeLabel = new();
    private readonly Label _driveSummaryLabel = new();
    private readonly Label _runtimeInfoLabel = new();
    private readonly FlowLayoutPanel _driveTabsFlow = new();
    private readonly Label _viewSummaryLabel = new();
    private readonly Label _selectionSummaryLabel = new();
    private readonly Label _selectionHintLabel = new();
    private readonly Panel _toolbarPanel = CreateSurfacePanel();
    private readonly Panel _filtersPanel = CreateSurfacePanel();
    private readonly Panel _summaryPanel = CreateSurfacePanel();
    private readonly Panel _contentPanel = CreateSurfacePanel();
    private readonly Panel _teachingPanel = new();
    private readonly ScrollFriendlyDataGridView _grid = new();
    private readonly ScrollFriendlyDataGridView _overviewGrid = new();
    private readonly ScrollFriendlyDataGridView _infrequentGrid = new();
    private readonly TextBox _searchTextBox = new();
    private readonly ComboBox _categoryComboBox = new();
    private readonly ComboBox _modeComboBox = new();
    private readonly ComboBox _sortComboBox = new();
    private readonly Label _categoryFilterLabel = new();
    private readonly Label _modeFilterLabel = new();
    private readonly Label _sortFilterLabel = new();
    private readonly FlowLayoutPanel _quickFiltersHost = new();
    private readonly Button _scanButton = new ThemedButton();
    private readonly Button _recommendedButton = new ThemedButton();
    private readonly Button _cDriveAdviceButton = new ThemedButton();
    private readonly Button _scheduleSettingsButton = new ThemedButton();
    private readonly Button _migrateSelectedButton = new ThemedButton();
    private readonly Button _deleteOverviewSelectedButton = new ThemedButton();
    private readonly Button _cleanSelectedButton = new ThemedButton();
    private readonly Button _safeCleanButton = new ThemedButton();
    private readonly Button _whitelistButton = new ThemedButton();
    private readonly Button _moreActionsButton = new ThemedButton();
    private readonly Panel _driveTabsPanel = new();
    private readonly Label _teachingPrimaryLabel = new();
    private readonly Label _teachingSecondaryLabel = new();
    private readonly StatusStrip _statusStrip = new();
    private readonly ToolStripStatusLabel _statusMessageLabel = new();
    private readonly ToolStripStatusLabel _scheduleStatusLabel = new();
    private readonly Dictionary<string, Button> _quickFilterButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _driveButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, JobCardBinding> _jobCards = [];
    private readonly ContextMenuStrip _moreActionsMenu = new();
    private readonly ToolStripMenuItem _openLocationMenuItem = new("在资源管理器中定位");
    private readonly ToolStripMenuItem _clearSelectionMenuItem = new("清空勾选");
    private readonly ToolStripMenuItem _regressionAuditMenuItem = new("问题结案清单");
    private readonly ToolStripMenuItem _openLogsMenuItem = new("日志目录");
    private readonly LinkLabel _retryBlockedLink = new();
    private readonly ToolTip _toolTip = new();
    private CDriveSuggestionDialog? _activeCDriveSuggestionDialog;
    private ScheduleSettingsDialog? _activeScheduleSettingsDialog;
    private RegressionAuditDialog? _activeRegressionAuditDialog;

    private readonly DataGridViewCheckBoxColumn _selectColumn = new();
    private readonly DataGridViewCheckBoxColumn _overviewSelectColumn = new();
    private readonly DataGridViewCheckBoxColumn _infrequentSelectColumn = new();
    private readonly DataGridViewImageColumn _cleanupIconColumn = new();
    private readonly DataGridViewImageColumn _overviewIconColumn = new();
    private readonly DataGridViewImageColumn _infrequentIconColumn = new();
    private readonly DataGridViewTextBoxColumn _driveColumn = new();
    private readonly DataGridViewTextBoxColumn _categoryColumn = new();
    private readonly DataGridViewTextBoxColumn _nameColumn = new();
    private readonly DataGridViewTextBoxColumn _typeColumn = new();
    private readonly DataGridViewTextBoxColumn _sizeColumn = new();
    private readonly DataGridViewTextBoxColumn _recommendationColumn = new();
    private readonly DataGridViewTextBoxColumn _locationColumn = new();
    private readonly DataGridViewTextBoxColumn _impactColumn = new();
    private readonly DataGridViewTextBoxColumn _severityColumn = new();

    private AppSettings _settings;
    private CleanupFilterState _filterState = new();
    private ScanSnapshot? _snapshot;
    private BindingList<CleanupSelectionRow> _visibleRows = [];
    private readonly List<CleanupSelectionRow> _allRows = [];
    private BindingList<CDriveOverviewEntry> _visibleOverviewRows = [];
    private readonly List<CDriveOverviewEntry> _overviewRows = [];
    private BindingList<InfrequentSoftwareEntry> _visibleInfrequentRows = [];
    private readonly List<InfrequentSoftwareEntry> _infrequentRows = [];
    private string _activeQuickFilter = QuickFilterAll;
    private bool _suppressFilterEvents;
    private bool _isBusy;
    private string? _lastResultLogPath;
    private string? _lastScanWarningLogPath;
    private MainViewMode _viewMode;
    private MainViewMode? _deferredStartupViewMode;
    private HashSet<Guid> _lastRetrySuggestedItemIds = [];
    private OperationQueueState _queueState = new();
    private readonly ConcurrentDictionary<string, Image> _cleanupIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Image> _overviewIcons = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _infrequentIconLoadCts;
    private CancellationTokenSource? _cleanupIconLoadCts;
    private CancellationTokenSource? _overviewIconLoadCts;
    private readonly System.Windows.Forms.Timer _jobCenterRefreshTimer = new();
    private readonly System.Windows.Forms.Timer _resizeRefreshTimer = new();
    private readonly object _jobCenterStateSync = new();
    private readonly Dictionary<Control, FrozenAutoSizeState> _resizeFrozenControls = [];
    private OperationQueueState? _pendingJobCenterState;
    private bool _hasPendingJobCenterState;
    private bool _resizeDragInProgress;
    private bool _resizeRefreshPending;
    private bool _pendingResizeForceLayout;
    private bool _pendingJobCardWidthRefresh;
    private bool _pendingCleanupIconColumnRefresh;
    private bool _pendingOverviewIconColumnRefresh;
    private bool _pendingInfrequentIconColumnRefresh;
    private Control? _activeContentControl;
    private LayoutDensityMode _layoutDensityMode;
    private bool IsWideMode => WindowState == FormWindowState.Maximized || ClientSize.Width >= WideModeThreshold;
    private bool IsCompactLayout => _layoutDensityMode != LayoutDensityMode.Regular;
    private bool IsUltraCompactLayout => _layoutDensityMode == LayoutDensityMode.UltraCompact;

    public MainForm(
        PortableContext context,
        SettingsService settingsService,
        ScanService scanService,
        CleanupService cleanupService,
        OccupancyProbeService occupancyProbeService,
        MigrationService migrationService,
        SchedulerService schedulerService,
        SnapshotCacheService snapshotCacheService,
        OperationManager operationManager,
        AppSettings settings,
        ScanSnapshot? initialSnapshot = null,
        bool readOnlyMode = false,
        string runtimeInfoText = "",
        string runtimeInfoToolTip = "",
        MainViewMode? startupViewOverride = null,
        bool preserveStartupView = false,
        bool persistWindowState = true)
    {
        _context = context;
        _settingsService = settingsService;
        _scanService = scanService;
        _cleanupService = cleanupService;
        _occupancyProbeService = occupancyProbeService;
        _migrationService = migrationService;
        _schedulerService = schedulerService;
        _snapshotCacheService = snapshotCacheService;
        _operationManager = operationManager;
        _settings = settings;
        _readOnlyMode = readOnlyMode;
        _preserveStartupView = preserveStartupView;
        _persistWindowState = persistWindowState;
        _viewMode = startupViewOverride ?? (Enum.TryParse<MainViewMode>(_settings.LastViewMode, out var savedViewMode)
            ? savedViewMode
            : MainViewMode.CleanupCandidates);
        _runtimeInfoLabel.Text = runtimeInfoText;
        _runtimeInfoLabel.Tag = runtimeInfoText;
        if (!string.IsNullOrWhiteSpace(runtimeInfoToolTip))
        {
            _toolTip.SetToolTip(_runtimeInfoLabel, runtimeInfoToolTip);
        }

        _jobCenterRefreshTimer.Interval = JobCenterPassiveRefreshIntervalMilliseconds;
        _jobCenterRefreshTimer.Tick += (_, _) => FlushPendingJobCenterState();
        _resizeRefreshTimer.Interval = ResizeRefreshIntervalMilliseconds;
        _resizeRefreshTimer.Tick += (_, _) => FlushDeferredResizeRefresh(forceLayout: _pendingResizeForceLayout);

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

        BuildUi();
        ApplyThemeColors();
        UpdateRegressionAuditMenuText();
        ApplyPersistedWindowBounds();
        InitializeFilterControls();
        ConfigureFilterControlsForMode();
        ReplaceSnapshot(initialSnapshot ?? new ScanSnapshot
        {
            CleanupItems = [],
            CDriveOverviewEntries = [],
            InfrequentSoftwareEntries = [],
            MigrationCandidates = [],
            LoadedDrives = [],
            PendingDrives = [],
            IsPartialResult = false,
            PhaseLabel = string.Empty,
            Warnings = [],
            SafeDefaultSelectionIds = new HashSet<Guid>(),
            AdditionalReviewSelectionIds = new HashSet<Guid>(),
            InstalledAppMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DuplicateGroups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            WhitelistHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        });
        UpdateDriveSummary();
        UpdateDriveTabs();
        UpdateScheduleStatus();
        UpdateGridPresentation();
        ApplyCurrentView();
        RefreshScaledUi(forceLayout: true);
        _operationManager.StateChanged += OperationManagerStateChanged;
        UpdateJobCenter(_operationManager.GetState());
        FormClosing += (_, _) =>
        {
            _infrequentIconLoadCts?.Cancel();
            _cleanupIconLoadCts?.Cancel();
            _overviewIconLoadCts?.Cancel();
            _jobCenterRefreshTimer.Stop();
            _resizeRefreshTimer.Stop();
            if (_persistWindowState)
            {
                PersistWindowState();
            }
        };
        ResizeBegin += (_, _) =>
        {
            _resizeDragInProgress = true;
            _resizeRefreshTimer.Stop();
            SuspendResizeSensitiveLayout();
            SetHeavyRedrawSuspended(suspend: true);
        };
        Resize += (_, _) => HandleDeferredResize();
        ResizeEnd += (_, _) =>
        {
            _resizeDragInProgress = false;
            ResumeResizeSensitiveLayout();
            SetHeavyRedrawSuspended(suspend: false);
            FlushDeferredResizeRefresh(forceLayout: true);
        };
        DpiChanged += (_, _) => BeginInvoke(new Action(HandleDpiChanged));
        Shown += async (_, _) =>
        {
            RefreshScaledUi(forceLayout: true);
            if (initialSnapshot is null || initialSnapshot.CleanupItems.Count == 0)
            {
                QueueScanRefresh(userInitiated: false);
                return;
            }

            SetStatusMessage("已回填上次扫描结果，后台正在刷新最新候选...");
            await Task.Delay(120);
            QueueScanRefresh(userInitiated: false);
        };
    }

    private void BuildUi()
    {
        Text = _settings.AppName;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(MinimumWindowWidth, MinimumWindowHeight);
        UiThemePalette.ApplyFormChrome(this);
        UiThemePalette.EnableDoubleBuffering(_rootLayout);

        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.Padding = new Padding(16, 14, 16, 12);
        _rootLayout.BackColor = UiThemePalette.WindowBackground;
        _rootLayout.ColumnCount = 1;
        _rootLayout.RowCount = 11;
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // The content host must stay as the only percent-sized row.
        // Moving the percent row elsewhere or trying to "fix" it later with manual SetBounds/BringToFront
        // can collapse the grid area and even send the UI thread into a layout loop during startup.
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(_rootLayout);

        BuildHeaderRow();
        BuildScanWarningRow();
        BuildResultBannerRow();
        BuildJobCenterRow();
        BuildViewModeRow();
        BuildToolbarRow();
        BuildDriveTabsRow();
        BuildFiltersRow();
        BuildSummaryRow();
        BuildContentRow();
        BuildStatusRow();
    }

    private void ApplyThemeColors()
    {
        UiThemePalette.ApplyTreeTheme(this);
        UiThemePalette.ApplySurface(_headerPanel);
        UiThemePalette.ApplySurface(_viewModePanel, raised: false);
        UiThemePalette.ApplySurface(_toolbarPanel, raised: false);
        UiThemePalette.ApplySurface(_filtersPanel, raised: false);
        UiThemePalette.ApplySurface(_summaryPanel, raised: false);
        UiThemePalette.ApplySurface(_contentPanel);
        UiThemePalette.ApplySurface(_resultBannerPanel);
        UiThemePalette.ApplySurface(_jobCenterPanel);

        _scanWarningPanel.BackColor = UiThemePalette.WarningSurface;
        _teachingPanel.BackColor = UiThemePalette.AccentSurface;
        UiThemePalette.AttachBorderPainter(_scanWarningPanel, UiThemePalette.Warning);
        UiThemePalette.AttachBorderPainter(_teachingPanel, UiThemePalette.Accent);

        _headerTitleLabel.ForeColor = UiThemePalette.TextPrimary;
        _headerModeLabel.ForeColor = UiThemePalette.TextSecondary;
        _driveSummaryLabel.ForeColor = UiThemePalette.TextSecondary;
        _runtimeInfoLabel.ForeColor = UiThemePalette.TextMuted;
        _scanWarningLabel.ForeColor = UiThemePalette.Warning;
        _resultBannerLabel.ForeColor = UiThemePalette.TextPrimary;
        _jobCenterLabel.ForeColor = UiThemePalette.TextPrimary;
        _jobCenterSummaryLabel.ForeColor = UiThemePalette.TextSecondary;
        _teachingPrimaryLabel.ForeColor = UiThemePalette.AccentStrong;
        _teachingSecondaryLabel.ForeColor = UiThemePalette.TextSecondary;
        _viewSummaryLabel.ForeColor = UiThemePalette.TextPrimary;
        _selectionSummaryLabel.ForeColor = UiThemePalette.AccentStrong;
        _selectionHintLabel.ForeColor = UiThemePalette.TextSecondary;

        UiThemePalette.ApplyLinkStyle(_scanWarningLink, warning: true);
        UiThemePalette.ApplyLinkStyle(_resultBannerLink);
        UiThemePalette.ApplyLinkStyle(_retryBlockedLink);
        UiThemePalette.ApplyStatusStripStyle(_statusStrip);
        UiThemePalette.ApplyDataGridTheme(_grid);
        UiThemePalette.ApplyDataGridTheme(_overviewGrid);
        UiThemePalette.ApplyDataGridTheme(_infrequentGrid);
        UiThemePalette.ApplyTextBoxStyle(_searchTextBox);
        UiThemePalette.ApplyComboBoxStyle(_categoryComboBox);
        UiThemePalette.ApplyComboBoxStyle(_modeComboBox);
        UiThemePalette.ApplyComboBoxStyle(_sortComboBox);

        UiThemePalette.ApplyButtonStyle(_scanButton, primary: false);
        UiThemePalette.ApplyButtonStyle(_recommendedButton, primary: false);
        UiThemePalette.ApplyButtonStyle(_cDriveAdviceButton, primary: false);
        UiThemePalette.ApplyButtonStyle(_cleanSelectedButton, primary: false);
        UiThemePalette.ApplyButtonStyle(_safeCleanButton, primary: true);
        UiThemePalette.ApplyButtonStyle(_migrateSelectedButton, primary: true);
        UiThemePalette.ApplyButtonStyle(_deleteOverviewSelectedButton, primary: false);
        UiThemePalette.ApplyButtonStyle(_scheduleSettingsButton, primary: false);
        UiThemePalette.ApplyButtonStyle(_whitelistButton, primary: false);
        UiThemePalette.ApplyButtonStyle(_moreActionsButton, primary: false);

        _dismissScanWarningButton.BackColor = Color.Transparent;
        _dismissScanWarningButton.ForeColor = UiThemePalette.Warning;
        _dismissScanWarningButton.FlatAppearance.BorderSize = 0;
        _dismissBannerButton.BackColor = Color.Transparent;
        _dismissBannerButton.ForeColor = UiThemePalette.TextSecondary;
        _dismissBannerButton.FlatAppearance.BorderSize = 0;
        UpdateViewModeButtons();
    }

    private void BuildHeaderRow()
    {
        _headerPanel.AutoSize = true;
        _headerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _headerPanel.Padding = new Padding(18, 14, 18, 14);
        _rootLayout.Controls.Add(_headerPanel, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _headerPanel.Controls.Add(layout);

        _headerTitleLabel.AutoSize = true;
        _headerTitleLabel.Anchor = AnchorStyles.Left;
        _headerTitleLabel.Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold);
        _headerTitleLabel.Margin = new Padding(0, 0, 16, 4);
        _headerTitleLabel.ForeColor = Color.FromArgb(25, 40, 54);
        _headerTitleLabel.Text = _settings.AppName;
        layout.Controls.Add(_headerTitleLabel, 0, 0);

        _headerModeLabel.AutoSize = true;
        _headerModeLabel.Anchor = AnchorStyles.Right;
        _headerModeLabel.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        _headerModeLabel.ForeColor = Color.FromArgb(56, 85, 97);
        _headerModeLabel.Text = "C 盘优先 · 全部固定盘候选";
        layout.Controls.Add(_headerModeLabel, 1, 0);

        _driveSummaryLabel.AutoSize = true;
        _driveSummaryLabel.ForeColor = Color.FromArgb(83, 95, 106);
        _driveSummaryLabel.Margin = new Padding(0);
        layout.Controls.Add(_driveSummaryLabel, 0, 1);
        layout.SetColumnSpan(_driveSummaryLabel, 2);

        _runtimeInfoLabel.AutoSize = true;
        _runtimeInfoLabel.MaximumSize = new Size(1180, 0);
        _runtimeInfoLabel.ForeColor = Color.FromArgb(89, 101, 113);
        _runtimeInfoLabel.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_runtimeInfoLabel, 0, 2);
        layout.SetColumnSpan(_runtimeInfoLabel, 2);
    }

    private void BuildScanWarningRow()
    {
        _scanWarningPanel.AutoSize = true;
        _scanWarningPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _scanWarningPanel.Dock = DockStyle.Fill;
        _scanWarningPanel.Padding = new Padding(14, 10, 14, 10);
        _scanWarningPanel.Margin = new Padding(0, 0, 0, 10);
        _scanWarningPanel.Visible = false;
        _rootLayout.Controls.Add(_scanWarningPanel, 0, 1);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _scanWarningPanel.Controls.Add(layout);

        _scanWarningLabel.AutoSize = true;
        _scanWarningLabel.MaximumSize = new Size(1100, 0);
        _scanWarningLabel.ForeColor = Color.FromArgb(112, 82, 16);
        layout.Controls.Add(_scanWarningLabel, 0, 0);

        _scanWarningLink.AutoSize = true;
        _scanWarningLink.Margin = new Padding(14, 0, 10, 0);
        _scanWarningLink.Text = "查看扫描日志";
        _scanWarningLink.Visible = false;
        _scanWarningLink.LinkClicked += (_, _) => OpenLastScanWarningLog();
        layout.Controls.Add(_scanWarningLink, 1, 0);

        _dismissScanWarningButton.FlatStyle = FlatStyle.Flat;
        _dismissScanWarningButton.FlatAppearance.BorderSize = 0;
        _dismissScanWarningButton.Text = "收起";
        _dismissScanWarningButton.AutoSize = true;
        _dismissScanWarningButton.Padding = new Padding(6, 0, 6, 0);
        _dismissScanWarningButton.Click += (_, _) => HideScanWarningBanner();
        layout.Controls.Add(_dismissScanWarningButton, 2, 0);
    }

    private void BuildResultBannerRow()
    {
        _resultBannerPanel.AutoSize = true;
        _resultBannerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _resultBannerPanel.Dock = DockStyle.Fill;
        _resultBannerPanel.Padding = new Padding(14, 10, 14, 10);
        _resultBannerPanel.Margin = new Padding(0, 0, 0, 10);
        _resultBannerPanel.Visible = false;
        _rootLayout.Controls.Add(_resultBannerPanel, 0, 2);

        var bannerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1
        };
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _resultBannerPanel.Controls.Add(bannerLayout);

        _resultBannerLabel.AutoSize = true;
        _resultBannerLabel.ForeColor = Color.FromArgb(37, 52, 64);
        bannerLayout.Controls.Add(_resultBannerLabel, 0, 0);

        _resultBannerLink.AutoSize = true;
        _resultBannerLink.Margin = new Padding(14, 0, 10, 0);
        _resultBannerLink.Text = "查看日志";
        _resultBannerLink.Visible = false;
        _resultBannerLink.LinkClicked += (_, _) => OpenLastRunLog();
        bannerLayout.Controls.Add(_resultBannerLink, 1, 0);

        _retryBlockedLink.AutoSize = true;
        _retryBlockedLink.Margin = new Padding(0, 0, 10, 0);
        _retryBlockedLink.Text = "关闭占用并重试";
        _retryBlockedLink.Visible = false;
        _retryBlockedLink.LinkClicked += async (_, _) => await RetryBlockedRowsAsync();
        bannerLayout.Controls.Add(_retryBlockedLink, 2, 0);

        _dismissBannerButton.FlatStyle = FlatStyle.Flat;
        _dismissBannerButton.FlatAppearance.BorderSize = 0;
        _dismissBannerButton.Text = "收起";
        _dismissBannerButton.AutoSize = true;
        _dismissBannerButton.Padding = new Padding(6, 0, 6, 0);
        _dismissBannerButton.Click += (_, _) => HideResultBanner();
        bannerLayout.Controls.Add(_dismissBannerButton, 3, 0);
    }

    private void BuildJobCenterRow()
    {
        _jobCenterPanel.AutoSize = true;
        _jobCenterPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _jobCenterPanel.Dock = DockStyle.Fill;
        _jobCenterPanel.Padding = new Padding(14, 10, 14, 10);
        _jobCenterPanel.Margin = new Padding(0, 0, 0, 10);
        _jobCenterPanel.BackColor = Color.FromArgb(248, 250, 251);
        _rootLayout.Controls.Add(_jobCenterPanel, 0, 3);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _jobCenterPanel.Controls.Add(layout);

        _jobCenterLabel.AutoSize = true;
        _jobCenterLabel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        _jobCenterLabel.ForeColor = Color.FromArgb(38, 54, 66);
        _jobCenterLabel.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(_jobCenterLabel, 0, 0);

        _jobCenterSummaryLabel.AutoSize = true;
        _jobCenterSummaryLabel.ForeColor = Color.FromArgb(72, 84, 95);
        _jobCenterSummaryLabel.Margin = new Padding(0, 0, 0, 8);
        _jobCenterSummaryLabel.Visible = false;
        layout.Controls.Add(_jobCenterSummaryLabel, 0, 1);

        _jobListFlow.AutoSize = true;
        _jobListFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _jobListFlow.Dock = DockStyle.Top;
        _jobListFlow.FlowDirection = FlowDirection.TopDown;
        _jobListFlow.WrapContents = false;
        _jobListFlow.Margin = Padding.Empty;
        UiThemePalette.EnableDoubleBuffering(_jobListFlow);
        layout.Controls.Add(_jobListFlow, 0, 2);

        _jobCenterPanel.Resize += (_, _) => HandleJobCardWidthRefreshRequest();
        _jobListFlow.Resize += (_, _) => HandleJobCardWidthRefreshRequest();
    }

    private void BuildViewModeRow()
    {
        _viewModePanel.AutoSize = true;
        _viewModePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _viewModePanel.Padding = new Padding(12, 8, 12, 8);
        _viewModePanel.Margin = new Padding(0, 0, 0, 10);
        _rootLayout.Controls.Add(_viewModePanel, 0, 4);

        _viewModeFlow.Dock = DockStyle.Fill;
        _viewModeFlow.AutoSize = true;
        _viewModeFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _viewModeFlow.WrapContents = false;
        _viewModeFlow.Margin = Padding.Empty;
        _viewModePanel.Controls.Add(_viewModeFlow);

        ConfigureViewModeButton(_cleanupViewButton, "清理候选", MainViewMode.CleanupCandidates);
        ConfigureViewModeButton(_overviewViewButton, "C盘总览", MainViewMode.CDriveOverview);
        ConfigureViewModeButton(_infrequentViewButton, "长期未用软件", MainViewMode.InfrequentApps);
        _viewModeFlow.Controls.Add(_cleanupViewButton);
        _viewModeFlow.Controls.Add(_overviewViewButton);
        _viewModeFlow.Controls.Add(_infrequentViewButton);
        UpdateViewModeButtons();
    }

    private void BuildToolbarRow()
    {
        _toolbarPanel.AutoSize = true;
        _toolbarPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _toolbarPanel.Padding = new Padding(12, 10, 12, 10);
        _toolbarPanel.Margin = new Padding(0, 0, 0, 10);
        _rootLayout.Controls.Add(_toolbarPanel, 0, 5);

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _toolbarPanel.Controls.Add(flow);

        ConfigureActionButton(_scanButton, "重新扫描", 112);
        _scanButton.Click += async (_, _) => await RefreshScanAsync();
        flow.Controls.Add(_scanButton);

        ConfigureActionButton(_recommendedButton, "勾选建议", 118);
        _recommendedButton.Click += (_, _) =>
        {
            if (_viewMode == MainViewMode.InfrequentApps)
            {
                ApplyInfrequentSelection(entry => entry.RecommendedForDeletion && entry.CanDeepDelete && !entry.IsWhitelisted);
                return;
            }

            ApplyBulkSelection(row => row.Item.Recommended);
        };
        flow.Controls.Add(_recommendedButton);

        ConfigureActionButton(_cDriveAdviceButton, "C盘建议", 112);
        _cDriveAdviceButton.Click += async (_, _) => await OpenCDriveAdviceAsync();
        flow.Controls.Add(_cDriveAdviceButton);

        ConfigureActionButton(_cleanSelectedButton, "处理勾选", 132);
        _cleanSelectedButton.Click += async (_, _) =>
        {
            var items = _viewMode == MainViewMode.InfrequentApps
                ? CreateCleanupItemsFromInfrequentEntries(_infrequentRows.Where(entry => entry.Selected))
                : _allRows.Where(row => row.Selected).Select(CreateEffectiveCleanupItem).ToList();
            await StartCleanupFlowAsync(items, safeOnly: false);
        };
        flow.Controls.Add(_cleanSelectedButton);

        ConfigureActionButton(_safeCleanButton, "一键安全清理", 154, primary: true);
        _safeCleanButton.Click += async (_, _) =>
        {
            var items = _visibleRows
                .Where(row => row.Item.SafeAuto && row.Item.ImpactSeverity == CleanupImpactSeverity.Low)
                .Select(CreateEffectiveCleanupItem)
                .ToList();
            await StartCleanupFlowAsync(items, safeOnly: true);
        };
        flow.Controls.Add(_safeCleanButton);

        ConfigureActionButton(_migrateSelectedButton, "开始迁移已勾选", 160);
        _migrateSelectedButton.Click += async (_, _) => await StartMigrationFlowAsync(GetSelectedMigrationCandidatesFromOverview());
        flow.Controls.Add(_migrateSelectedButton);

        ConfigureActionButton(_deleteOverviewSelectedButton, "删除已勾选", 136);
        _deleteOverviewSelectedButton.Click += async (_, _) =>
        {
            var cleanupItems = CreateCleanupItemsFromMigrationCandidates(GetSelectedDeletionCandidatesFromOverview());
            await StartCleanupFlowAsync(cleanupItems, safeOnly: false);
        };
        flow.Controls.Add(_deleteOverviewSelectedButton);

        ConfigureActionButton(_scheduleSettingsButton, "自动清理", 118);
        _scheduleSettingsButton.Click += (_, _) => OpenScheduleSettings();
        flow.Controls.Add(_scheduleSettingsButton);

        ConfigureActionButton(_whitelistButton, "加入白名单", 126);
        _whitelistButton.Click += (_, _) => AddCurrentSelectionToWhitelist();
        flow.Controls.Add(_whitelistButton);

        ConfigureActionButton(_moreActionsButton, "更多", 92);
        _moreActionsButton.Click += (_, _) => _moreActionsMenu.Show(_moreActionsButton, new Point(0, _moreActionsButton.Height));
        flow.Controls.Add(_moreActionsButton);

        _openLocationMenuItem.Click += (_, _) => OpenSelectedLocation();
        _clearSelectionMenuItem.Click += (_, _) =>
        {
            if (_viewMode == MainViewMode.InfrequentApps)
            {
                ApplyInfrequentSelection(_ => false);
                return;
            }

            if (_viewMode == MainViewMode.CDriveOverview)
            {
                foreach (var entry in _overviewRows)
                {
                    entry.Selected = false;
                }

                _overviewGrid.Refresh();
                UpdateSelectionSummary();
                UpdateActionStates();
                return;
            }

            ApplyBulkSelection(_ => false);
        };
        _regressionAuditMenuItem.Click += (_, _) => OpenRegressionAudit();
        _openLogsMenuItem.Click += (_, _) => ShellHelper.OpenFolder(_context.LogsRoot);
        _moreActionsMenu.Items.AddRange([_openLocationMenuItem, _clearSelectionMenuItem, new ToolStripSeparator(), _regressionAuditMenuItem, _openLogsMenuItem]);
    }

    private void BuildDriveTabsRow()
    {
        _driveTabsPanel.BackColor = Color.White;
        _driveTabsPanel.AutoSize = true;
        _driveTabsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _driveTabsPanel.Padding = new Padding(12, 10, 12, 8);
        _driveTabsPanel.Margin = new Padding(0, 0, 0, 10);
        _driveTabsPanel.Dock = DockStyle.Top;
        _rootLayout.Controls.Add(_driveTabsPanel, 0, 6);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _driveTabsPanel.Controls.Add(layout);

        _teachingPanel.Dock = DockStyle.Top;
        _teachingPanel.AutoSize = true;
        _teachingPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _teachingPanel.BackColor = Color.FromArgb(239, 248, 245);
        _teachingPanel.Padding = new Padding(12, 8, 12, 8);
        _teachingPanel.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(_teachingPanel, 0, 0);

        var teachingLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        _teachingPanel.Controls.Add(teachingLayout);

        _teachingPrimaryLabel.AutoSize = true;
        _teachingPrimaryLabel.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        _teachingPrimaryLabel.ForeColor = Color.FromArgb(22, 94, 74);
        _teachingPrimaryLabel.Margin = new Padding(0, 0, 0, 3);
        teachingLayout.Controls.Add(_teachingPrimaryLabel, 0, 0);

        _teachingSecondaryLabel.AutoSize = true;
        _teachingSecondaryLabel.ForeColor = Color.FromArgb(57, 83, 76);
        _teachingSecondaryLabel.Margin = Padding.Empty;
        teachingLayout.Controls.Add(_teachingSecondaryLabel, 0, 1);

        _driveTabsFlow.Dock = DockStyle.Fill;
        _driveTabsFlow.AutoSize = true;
        _driveTabsFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _driveTabsFlow.WrapContents = false;
        _driveTabsFlow.Margin = Padding.Empty;
        layout.Controls.Add(_driveTabsFlow, 0, 1);
    }

    private void BuildFiltersRow()
    {
        _filtersPanel.AutoSize = true;
        _filtersPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _filtersPanel.Padding = new Padding(14, 12, 14, 12);
        _filtersPanel.Margin = new Padding(0, 0, 0, 10);
        _rootLayout.Controls.Add(_filtersPanel, 0, 7);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 8,
            RowCount = 1,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _filtersPanel.Controls.Add(layout);

        layout.Controls.Add(CreateFilterLabel("搜索"), 0, 0);

        _searchTextBox.Width = 300;
        _searchTextBox.Margin = new Padding(0, 0, 16, 0);
        _searchTextBox.PlaceholderText = "搜名称、路径、影响说明";
        _searchTextBox.TextChanged += (_, _) =>
        {
            if (_suppressFilterEvents)
            {
                return;
            }

            _filterState.SearchText = _searchTextBox.Text.Trim();
            ApplyCurrentView();
        };
        layout.Controls.Add(_searchTextBox, 1, 0);

        ConfigureFilterLabel(_categoryFilterLabel, "分类");
        layout.Controls.Add(_categoryFilterLabel, 2, 0);
        ConfigureComboBox(_categoryComboBox, 150);
        _categoryComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressFilterEvents)
            {
                return;
            }

            _filterState.SelectedCategory = GetComboBoxValue(_categoryComboBox);
            if (_viewMode == MainViewMode.InfrequentApps)
            {
                _settings.InfrequentAppsViewLastFilter = _filterState.SelectedCategory;
            }
            ApplyCurrentView();
        };
        layout.Controls.Add(_categoryComboBox, 3, 0);

        ConfigureFilterLabel(_modeFilterLabel, "建议");
        layout.Controls.Add(_modeFilterLabel, 4, 0);
        ConfigureComboBox(_modeComboBox, 150);
        _modeComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressFilterEvents)
            {
                return;
            }

            _filterState.SelectedAutoMode = GetComboBoxValue(_modeComboBox);
            ApplyCurrentView();
        };
        layout.Controls.Add(_modeComboBox, 5, 0);

        ConfigureFilterLabel(_sortFilterLabel, "排序");
        layout.Controls.Add(_sortFilterLabel, 6, 0);
        ConfigureComboBox(_sortComboBox, 172);
        _sortComboBox.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressFilterEvents)
            {
                return;
            }

            _filterState.SortMode = GetComboBoxValue(_sortComboBox);
            ApplyCurrentView();
        };
        layout.Controls.Add(_sortComboBox, 7, 0);
    }

    private void BuildSummaryRow()
    {
        _summaryPanel.AutoSize = true;
        _summaryPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _summaryPanel.Padding = new Padding(14, 10, 14, 10);
        _summaryPanel.Margin = new Padding(0, 0, 0, 10);
        _rootLayout.Controls.Add(_summaryPanel, 0, 8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _summaryPanel.Controls.Add(layout);

        _viewSummaryLabel.AutoSize = true;
        _viewSummaryLabel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        _viewSummaryLabel.ForeColor = Color.FromArgb(38, 54, 66);
        _viewSummaryLabel.Margin = new Padding(0, 0, 18, 4);
        layout.Controls.Add(_viewSummaryLabel, 0, 0);

        _selectionSummaryLabel.AutoSize = true;
        _selectionSummaryLabel.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        _selectionSummaryLabel.ForeColor = Color.FromArgb(23, 121, 83);
        _selectionSummaryLabel.Margin = new Padding(0, 0, 14, 4);
        layout.Controls.Add(_selectionSummaryLabel, 1, 0);

        _quickFiltersHost.AutoSize = true;
        _quickFiltersHost.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _quickFiltersHost.Anchor = AnchorStyles.Right;
        _quickFiltersHost.WrapContents = false;
        _quickFiltersHost.Margin = Padding.Empty;
        layout.Controls.Add(_quickFiltersHost, 2, 0);
        BuildQuickFiltersRow(_quickFiltersHost);

        _selectionHintLabel.AutoSize = true;
        _selectionHintLabel.MaximumSize = new Size(1160, 0);
        _selectionHintLabel.ForeColor = Color.FromArgb(86, 96, 107);
        _selectionHintLabel.Margin = new Padding(0, 2, 0, 0);
        layout.Controls.Add(_selectionHintLabel, 0, 1);
        layout.SetColumnSpan(_selectionHintLabel, 3);
    }

    private void BuildQuickFiltersRow(FlowLayoutPanel host)
    {
        AddQuickFilterButton(host, QuickFilterAll, "全部");
        AddQuickFilterButton(host, QuickFilterSafeJunk, "明确垃圾");
        AddQuickFilterButton(host, QuickFilterAppCache, "应用缓存");
        AddQuickFilterButton(host, QuickFilterPackages, "下载包");
        AddQuickFilterButton(host, QuickFilterDuplicates, "重复文件");
        AddQuickFilterButton(host, QuickFilterResidue, "应用残留");
    }

    private void BuildContentRow()
    {
        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Padding = new Padding(0);
        _contentPanel.Margin = Padding.Empty;
        _rootLayout.Controls.Add(_contentPanel, 0, 9);

        ConfigureGrid();
        ConfigureOverviewGrid();
        ConfigureInfrequentGrid();
        SetActiveContent(_grid);
    }

    private void BuildStatusRow()
    {
        _statusStrip.Dock = DockStyle.Fill;
        _statusStrip.SizingGrip = false;
        _statusStrip.Margin = new Padding(0, 10, 0, 0);
        _statusStrip.BackColor = Color.FromArgb(244, 247, 249);
        _statusStrip.ForeColor = Color.FromArgb(70, 82, 92);

        _statusMessageLabel.Spring = true;
        _statusMessageLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusMessageLabel.Text = "准备就绪";
        _statusStrip.Items.Add(_statusMessageLabel);

        _scheduleStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _statusStrip.Items.Add(_scheduleStatusLabel);

        _rootLayout.Controls.Add(_statusStrip, 0, 10);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToResizeColumns = true;
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.BorderStyle = BorderStyle.None;
        _grid.BackgroundColor = Color.White;
        _grid.GridColor = Color.FromArgb(229, 234, 239);
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ColumnHeadersHeight = UiScaleHelper.MeasureGridHeaderHeight(_grid.ColumnHeadersDefaultCellStyle.Font ?? new Font("Microsoft YaHei UI", 9, FontStyle.Bold), minHeight: 42, verticalPadding: 18);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.RowTemplate.Height = UiScaleHelper.MeasureGridRowHeight(_grid.Font, minHeight: 38, verticalPadding: 18);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 249);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(41, 56, 68);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(43, 56, 66);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(234, 244, 239);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(36, 48, 58);
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.StandardTab = false;
        _grid.DataError += (_, _) => { };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _selectColumn.Index)
            {
                return;
            }

            _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            UpdateSelectionSummary();
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not CleanupSelectionRow row)
            {
                return;
            }

            OpenRowLocation(row);
        };
        _grid.SelectionChanged += (_, _) => UpdateSelectionHint();
        _grid.DataBindingComplete += (_, _) =>
        {
            if (_grid.Rows.Count == 0)
            {
                UpdateSelectionHint();
                return;
            }

            if (_grid.CurrentCell is null)
            {
                var row = _grid.Rows[0];
                var targetColumn = _nameColumn.Visible ? _nameColumn.Index : _driveColumn.Index;
                _grid.CurrentCell = row.Cells[targetColumn];
                row.Selected = true;
            }

            UpdateSelectionHint();
        };
        _grid.CellFormatting += GridCellFormatting;
        _grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;

        _selectColumn.DataPropertyName = nameof(CleanupSelectionRow.Selected);
        _selectColumn.HeaderText = "勾选";
        _selectColumn.Width = MeasureGridColumnWidth("勾选", 34);
        _selectColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;

        _cleanupIconColumn.Name = "CleanupIcon";
        _cleanupIconColumn.HeaderText = "图标";
        _cleanupIconColumn.Width = MeasureGridColumnWidth("图标", 18);
        _cleanupIconColumn.ReadOnly = true;
        _cleanupIconColumn.ImageLayout = DataGridViewImageCellLayout.Normal;
        _cleanupIconColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        _cleanupIconColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _cleanupIconColumn.DefaultCellStyle.NullValue = ApplicationIconCache.GetIcon(IconSemanticResolver.DefaultCleanup(), GetCurrentIconSize(_grid, _cleanupIconColumn));

        _driveColumn.DataPropertyName = nameof(CleanupSelectionRow.DriveName);
        _driveColumn.HeaderText = "盘符";
        _driveColumn.Width = Math.Max(66, UiScaleHelper.MeasureGridColumnWidth("盘符", 66, 24));
        _driveColumn.ReadOnly = true;
        _driveColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

        _categoryColumn.DataPropertyName = nameof(CleanupSelectionRow.CandidateKindText);
        _categoryColumn.HeaderText = "分类";
        _categoryColumn.Width = Math.Max(110, UiScaleHelper.MeasureGridColumnWidth("分类", 110, 28));
        _categoryColumn.ReadOnly = true;
        _categoryColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

        _nameColumn.DataPropertyName = nameof(CleanupSelectionRow.Name);
        _nameColumn.HeaderText = "名称";
        _nameColumn.ReadOnly = true;
        _nameColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

        _typeColumn.DataPropertyName = nameof(CleanupSelectionRow.TypeDescription);
        _typeColumn.HeaderText = "类型";
        _typeColumn.Width = Math.Max(210, UiScaleHelper.MeasureGridColumnWidth("类型", 210, 30));
        _typeColumn.ReadOnly = true;
        _typeColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

        _sizeColumn.DataPropertyName = nameof(CleanupSelectionRow.SizeText);
        _sizeColumn.HeaderText = "大小";
        _sizeColumn.Width = Math.Max(108, UiScaleHelper.MeasureGridColumnWidth("大小", 108, 28));
        _sizeColumn.ReadOnly = true;
        _sizeColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        _sizeColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        _recommendationColumn.DataPropertyName = nameof(CleanupSelectionRow.RecommendationText);
        _recommendationColumn.HeaderText = "建议";
        _recommendationColumn.Width = Math.Max(110, UiScaleHelper.MeasureGridColumnWidth("建议", 110, 28));
        _recommendationColumn.ReadOnly = true;
        _recommendationColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

        _locationColumn.DataPropertyName = nameof(CleanupSelectionRow.LocationSummary);
        _locationColumn.HeaderText = "位置摘要";
        _locationColumn.ReadOnly = true;
        _locationColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

        _impactColumn.DataPropertyName = nameof(CleanupSelectionRow.ImpactText);
        _impactColumn.HeaderText = "删除后果";
        _impactColumn.ReadOnly = true;
        _impactColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

        _severityColumn.DataPropertyName = nameof(CleanupSelectionRow.ImpactSeverityText);
        _severityColumn.HeaderText = "严重度";
        _severityColumn.Width = Math.Max(82, UiScaleHelper.MeasureGridColumnWidth("严重度", 82, 28));
        _severityColumn.ReadOnly = true;
        _severityColumn.SortMode = DataGridViewColumnSortMode.NotSortable;

        _grid.Columns.AddRange(
            _selectColumn,
            _cleanupIconColumn,
            _driveColumn,
            _categoryColumn,
            _nameColumn,
            _typeColumn,
            _sizeColumn,
            _recommendationColumn,
            _locationColumn,
            _impactColumn,
            _severityColumn);
    }

    private void ConfigureOverviewGrid()
    {
        _overviewGrid.Dock = DockStyle.Fill;
        _overviewGrid.AllowUserToAddRows = false;
        _overviewGrid.AllowUserToDeleteRows = false;
        _overviewGrid.AllowUserToResizeRows = false;
        _overviewGrid.AllowUserToResizeColumns = true;
        _overviewGrid.AutoGenerateColumns = false;
        _overviewGrid.MultiSelect = false;
        _overviewGrid.RowHeadersVisible = false;
        _overviewGrid.BorderStyle = BorderStyle.None;
        _overviewGrid.BackgroundColor = Color.White;
        _overviewGrid.GridColor = Color.FromArgb(229, 234, 239);
        _overviewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _overviewGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _overviewGrid.ColumnHeadersHeight = UiScaleHelper.MeasureGridHeaderHeight(_overviewGrid.ColumnHeadersDefaultCellStyle.Font ?? new Font("Microsoft YaHei UI", 9, FontStyle.Bold), minHeight: 42, verticalPadding: 18);
        _overviewGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _overviewGrid.RowTemplate.Height = UiScaleHelper.MeasureGridRowHeight(_overviewGrid.Font, minHeight: 38, verticalPadding: 18);
        _overviewGrid.EnableHeadersVisualStyles = false;
        _overviewGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 249);
        _overviewGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(41, 56, 68);
        _overviewGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _overviewGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        _overviewGrid.DefaultCellStyle.BackColor = Color.White;
        _overviewGrid.DefaultCellStyle.ForeColor = Color.FromArgb(43, 56, 66);
        _overviewGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(234, 244, 239);
        _overviewGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(36, 48, 58);
        _overviewGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _overviewGrid.ReadOnly = false;
        _overviewGrid.ScrollBars = ScrollBars.Both;
        _overviewGrid.Visible = false;
        _overviewGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _overviewGrid.DataError += (_, _) => { };
        _overviewGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_overviewGrid.IsCurrentCellDirty)
            {
                _overviewGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _overviewGrid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _overviewSelectColumn.Index)
            {
                return;
            }

            if (_overviewGrid.Rows[e.RowIndex].DataBoundItem is not CDriveOverviewEntry entry || !entry.SelectionEnabled)
            {
                e.Cancel = true;
            }
        };
        _overviewGrid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _overviewSelectColumn.Index)
            {
                return;
            }

            _overviewGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            UpdateSelectionSummary();
            UpdateActionStates();
        };
        _overviewGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex == _overviewSelectColumn.Index || _overviewGrid.Rows[e.RowIndex].DataBoundItem is not CDriveOverviewEntry entry)
            {
                return;
            }

            ShellHelper.RevealPath(entry.Path);
        };
        _overviewGrid.SelectionChanged += (_, _) => UpdateSelectionHint();
        _overviewGrid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewRow gridRow in _overviewGrid.Rows)
            {
                if (gridRow.DataBoundItem is not CDriveOverviewEntry entry)
                {
                    continue;
                }

                gridRow.Cells[_overviewSelectColumn.Index].ReadOnly = !entry.SelectionEnabled;
            }

            if (_overviewGrid.Rows.Count > 0 && _overviewGrid.CurrentCell is null)
            {
                var targetColumn = Math.Min(_overviewGrid.Columns.Count - 1, _overviewIconColumn.Index + 1);
                _overviewGrid.CurrentCell = _overviewGrid.Rows[0].Cells[targetColumn];
            }
        };
        _overviewGrid.CellFormatting += OverviewGridCellFormatting;
        _overviewGrid.CellToolTipTextNeeded += OverviewGridCellToolTipTextNeeded;

        _overviewSelectColumn.DataPropertyName = nameof(CDriveOverviewEntry.Selected);
        _overviewSelectColumn.HeaderText = "勾选";
        _overviewSelectColumn.Width = MeasureGridColumnWidth("勾选", 34);
        _overviewSelectColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _overviewSelectColumn.ThreeState = false;

        _overviewGrid.Columns.Add(_overviewSelectColumn);
        _overviewIconColumn.Name = "OverviewIcon";
        _overviewIconColumn.HeaderText = "图标";
        _overviewIconColumn.Width = MeasureGridColumnWidth("图标", 18);
        _overviewIconColumn.ReadOnly = true;
        _overviewIconColumn.ImageLayout = DataGridViewImageCellLayout.Normal;
        _overviewIconColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        _overviewIconColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _overviewIconColumn.DefaultCellStyle.NullValue = ApplicationIconCache.GetIcon(IconSemanticResolver.DefaultOverview(), GetCurrentIconSize(_overviewGrid, _overviewIconColumn));
        _overviewGrid.Columns.Add(_overviewIconColumn);

        _overviewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CDriveOverviewEntry.Name),
            HeaderText = "名称",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 18,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _overviewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CDriveOverviewEntry.SizeText),
            HeaderText = "占用",
            Width = Math.Max(118, UiScaleHelper.MeasureGridColumnWidth("占用", 118, 28)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _overviewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CDriveOverviewEntry.Category),
            HeaderText = "目录类型",
            Width = Math.Max(128, UiScaleHelper.MeasureGridColumnWidth("目录类型", 128, 30)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _overviewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CDriveOverviewEntry.ProtectionText),
            HeaderText = "系统保护",
            Width = Math.Max(104, UiScaleHelper.MeasureGridColumnWidth("系统保护", 104, 30)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _overviewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CDriveOverviewEntry.OperationHintText),
            HeaderText = "建议动作",
            Width = Math.Max(124, UiScaleHelper.MeasureGridColumnWidth("建议动作", 124, 30)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _overviewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CDriveOverviewEntry.Path),
            HeaderText = "路径",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 26,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _overviewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CDriveOverviewEntry.PurposeText),
            HeaderText = "目录用途",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 20,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _overviewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(CDriveOverviewEntry.RecommendationText),
            HeaderText = "说明",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 22,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void ConfigureInfrequentGrid()
    {
        _infrequentGrid.Dock = DockStyle.Fill;
        _infrequentGrid.AllowUserToAddRows = false;
        _infrequentGrid.AllowUserToDeleteRows = false;
        _infrequentGrid.AllowUserToResizeRows = false;
        _infrequentGrid.AllowUserToResizeColumns = true;
        _infrequentGrid.AutoGenerateColumns = false;
        _infrequentGrid.MultiSelect = false;
        _infrequentGrid.RowHeadersVisible = false;
        _infrequentGrid.BorderStyle = BorderStyle.None;
        _infrequentGrid.BackgroundColor = Color.White;
        _infrequentGrid.GridColor = Color.FromArgb(229, 234, 239);
        _infrequentGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _infrequentGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _infrequentGrid.ColumnHeadersHeight = UiScaleHelper.MeasureGridHeaderHeight(_infrequentGrid.ColumnHeadersDefaultCellStyle.Font ?? new Font("Microsoft YaHei UI", 9, FontStyle.Bold), minHeight: 42, verticalPadding: 18);
        _infrequentGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _infrequentGrid.RowTemplate.Height = UiScaleHelper.MeasureGridRowHeight(_infrequentGrid.Font, minHeight: 38, verticalPadding: 18);
        _infrequentGrid.EnableHeadersVisualStyles = false;
        _infrequentGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(245, 247, 249);
        _infrequentGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(41, 56, 68);
        _infrequentGrid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _infrequentGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        _infrequentGrid.DefaultCellStyle.BackColor = Color.White;
        _infrequentGrid.DefaultCellStyle.ForeColor = Color.FromArgb(43, 56, 66);
        _infrequentGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(234, 244, 239);
        _infrequentGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(36, 48, 58);
        _infrequentGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _infrequentGrid.ReadOnly = false;
        _infrequentGrid.ScrollBars = ScrollBars.Both;
        _infrequentGrid.Visible = false;
        _infrequentGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _infrequentGrid.DataError += (_, _) => { };
        _infrequentGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_infrequentGrid.IsCurrentCellDirty)
            {
                _infrequentGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        _infrequentGrid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _infrequentSelectColumn.Index)
            {
                return;
            }

            if (_infrequentGrid.Rows[e.RowIndex].DataBoundItem is not InfrequentSoftwareEntry entry
                || !entry.CanDeepDelete
                || entry.IsProtected
                || entry.IsWhitelisted)
            {
                e.Cancel = true;
            }
        };
        _infrequentGrid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != _infrequentSelectColumn.Index)
            {
                return;
            }

            _infrequentGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            UpdateSelectionSummary();
            UpdateActionStates();
        };
        _infrequentGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex == _infrequentSelectColumn.Index || _infrequentGrid.Rows[e.RowIndex].DataBoundItem is not InfrequentSoftwareEntry entry)
            {
                return;
            }

            ShellHelper.RevealPath(entry.InstallRoot);
        };
        _infrequentGrid.SelectionChanged += (_, _) => UpdateSelectionHint();
        _infrequentGrid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewRow gridRow in _infrequentGrid.Rows)
            {
                if (gridRow.DataBoundItem is not InfrequentSoftwareEntry entry)
                {
                    continue;
                }

                gridRow.Cells[_infrequentSelectColumn.Index].ReadOnly = entry.IsProtected || entry.IsWhitelisted || !entry.CanDeepDelete;
            }

            if (_infrequentGrid.Rows.Count > 0 && _infrequentGrid.CurrentCell is null)
            {
                _infrequentGrid.CurrentCell = _infrequentGrid.Rows[0].Cells[Math.Min(_infrequentGrid.Columns.Count - 1, 2)];
            }
        };
        _infrequentGrid.CellFormatting += InfrequentGridCellFormatting;
        _infrequentGrid.CellToolTipTextNeeded += InfrequentGridCellToolTipTextNeeded;

        _infrequentSelectColumn.DataPropertyName = nameof(InfrequentSoftwareEntry.Selected);
        _infrequentSelectColumn.HeaderText = "勾选";
        _infrequentSelectColumn.Width = MeasureGridColumnWidth("勾选", 34);
        _infrequentSelectColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        _infrequentSelectColumn.ThreeState = false;
        _infrequentGrid.Columns.Add(_infrequentSelectColumn);

        _infrequentIconColumn.DataPropertyName = nameof(InfrequentSoftwareEntry.IconImage);
        _infrequentIconColumn.HeaderText = "图标";
        _infrequentIconColumn.Width = MeasureGridColumnWidth("图标", 18);
        _infrequentIconColumn.ReadOnly = true;
        _infrequentIconColumn.ImageLayout = DataGridViewImageCellLayout.Normal;
        _infrequentIconColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        _infrequentIconColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _infrequentIconColumn.DefaultCellStyle.NullValue = ApplicationIconCache.GetIcon(IconSemanticResolver.DefaultInfrequentApp(), GetCurrentIconSize(_infrequentGrid, _infrequentIconColumn));
        _infrequentGrid.Columns.Add(_infrequentIconColumn);

        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.PrimaryDrive),
            HeaderText = "盘符",
            Width = Math.Max(66, UiScaleHelper.MeasureGridColumnWidth("盘符", 66, 24)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.DisplayName),
            HeaderText = "软件名",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 18,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.SizeText),
            HeaderText = "占用",
            Width = Math.Max(106, UiScaleHelper.MeasureGridColumnWidth("占用", 106, 28)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.LastUsedText),
            HeaderText = "最近使用",
            Width = Math.Max(116, UiScaleHelper.MeasureGridColumnWidth("最近使用", 116, 28)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.UnusedDaysText),
            HeaderText = "未用天数",
            Width = Math.Max(92, UiScaleHelper.MeasureGridColumnWidth("未用天数", 92, 28)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.UsageConfidenceText),
            HeaderText = "可信度",
            Width = Math.Max(82, UiScaleHelper.MeasureGridColumnWidth("可信度", 82, 28)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.UsageSourceText),
            HeaderText = "依据",
            Width = Math.Max(118, UiScaleHelper.MeasureGridColumnWidth("使用依据", 118, 28)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.StatusText),
            HeaderText = "建议动作",
            Width = Math.Max(110, UiScaleHelper.MeasureGridColumnWidth("建议动作", 110, 30)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.InstallRoot),
            HeaderText = "主目录",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 26,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _infrequentGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(InfrequentSoftwareEntry.RecommendationText),
            HeaderText = "说明",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 24,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void InitializeFilterControls()
    {
        _suppressFilterEvents = true;

        _categoryComboBox.DisplayMember = "Text";
        _categoryComboBox.ValueMember = "Value";
        _categoryComboBox.Items.Clear();
        _categoryComboBox.Items.Add(new FilterOption(CleanupFilterState.AllValue, "全部分类"));
        _categoryComboBox.Items.Add(new FilterOption("明确垃圾", "明确垃圾"));
        _categoryComboBox.Items.Add(new FilterOption("应用缓存", "应用缓存"));
        _categoryComboBox.Items.Add(new FilterOption("重复文件", "重复文件"));
        _categoryComboBox.Items.Add(new FilterOption("下载包/压缩包", "下载包 / 压缩包"));
        _categoryComboBox.Items.Add(new FilterOption("大文件", "大文件"));
        _categoryComboBox.Items.Add(new FilterOption("大目录", "大目录"));
        _categoryComboBox.Items.Add(new FilterOption("应用残留", "应用残留"));
        _categoryComboBox.SelectedIndex = 0;

        _modeComboBox.DisplayMember = "Text";
        _modeComboBox.ValueMember = "Value";
        _modeComboBox.Items.Clear();
        _modeComboBox.Items.Add(new FilterOption(CleanupFilterState.AllValue, "全部建议"));
        _modeComboBox.Items.Add(new FilterOption(CleanupFilterState.AutoValue, "只看可安全删"));
        _modeComboBox.Items.Add(new FilterOption(CleanupFilterState.ReviewValue, "只看需要确认"));
        _modeComboBox.Items.Add(new FilterOption(AutoModeAppOnly, "只看应用相关"));
        _modeComboBox.SelectedIndex = 0;

        _sortComboBox.DisplayMember = "Text";
        _sortComboBox.ValueMember = "Value";
        _sortComboBox.Items.Clear();
        _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortSmart, "智能排序"));
        _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortSizeDesc, "体积从大到小"));
        _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortNameAsc, "名称 A-Z"));
        _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortDriveSize, "按盘符和体积"));
        _sortComboBox.SelectedIndex = 0;

        _filterState = new CleanupFilterState
        {
            SelectedDrive = DriveAllKey,
            SelectedCategory = CleanupFilterState.AllValue,
            SelectedAutoMode = CleanupFilterState.AllValue,
            SortMode = CleanupFilterState.SortSmart
        };

        _suppressFilterEvents = false;
        RefreshFilterControlSizing();
    }

    private void ReplaceSnapshot(ScanSnapshot snapshot)
    {
        _snapshot = snapshot;

        foreach (var row in _allRows)
        {
            row.PropertyChanged -= CleanupSelectionRowChanged;
        }

        _allRows.Clear();
        foreach (var item in snapshot.CleanupItems)
        {
            var selectionTier = snapshot.SafeDefaultSelectionIds.Contains(item.Id)
                ? SelectionTier.AutoSafe
                : snapshot.AdditionalReviewSelectionIds.Contains(item.Id)
                    ? SelectionTier.ReviewOnly
                    : SelectionTier.Manual;
            var row = new CleanupSelectionRow(item, selectionTier);
            row.PropertyChanged += CleanupSelectionRowChanged;
            _allRows.Add(row);
        }

        _overviewRows.Clear();
        _overviewRows.AddRange(snapshot.CDriveOverviewEntries);
        _infrequentRows.Clear();
        _infrequentRows.AddRange(snapshot.InfrequentSoftwareEntries);
        _activeCDriveSuggestionDialog?.ApplySnapshot(snapshot);
        UpdateScanWarningBanner(snapshot.Warnings);
        EnsureActiveDriveSelection();
        ConfigureFilterControlsForMode();
        RefreshPillButtonSizing();
        ApplyCurrentView();
    }

    private void CleanupSelectionRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(CleanupSelectionRow.Selected), StringComparison.Ordinal))
        {
            return;
        }

        UpdateSelectionSummary();
        UpdateActionStates();
    }

    private void RefreshCategoryItems()
    {
        var currentValue = GetComboBoxValue(_categoryComboBox);
        var categories = _allRows
            .Select(row => row.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _suppressFilterEvents = true;
        _categoryComboBox.Items.Clear();
        _categoryComboBox.Items.Add(new FilterOption(CleanupFilterState.AllValue, "全部分类"));
        foreach (var category in categories)
        {
            _categoryComboBox.Items.Add(new FilterOption(category, category));
        }

        SelectComboBoxValue(_categoryComboBox, categories.Contains(currentValue, StringComparer.OrdinalIgnoreCase) ? currentValue : CleanupFilterState.AllValue);
        _filterState.SelectedCategory = GetComboBoxValue(_categoryComboBox);
        _suppressFilterEvents = false;
    }

    private void EnsureActiveDriveSelection()
    {
        var drives = (_viewMode == MainViewMode.InfrequentApps
                ? _infrequentRows.Select(row => row.PrimaryDrive)
                : _allRows.Select(row => row.DriveName))
            .Where(IsSpecificDrive)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetDrivePriority)
            .ThenBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (drives.Count == 0)
        {
            _filterState.SelectedDrive = DriveAllKey;
            return;
        }

        if (string.Equals(_filterState.SelectedDrive, DriveAllKey, StringComparison.OrdinalIgnoreCase))
        {
            _filterState.SelectedDrive = drives.Contains("C", StringComparer.OrdinalIgnoreCase) ? "C" : drives[0];
            return;
        }

        if (!drives.Contains(_filterState.SelectedDrive, StringComparer.OrdinalIgnoreCase))
        {
            _filterState.SelectedDrive = drives.Contains("C", StringComparer.OrdinalIgnoreCase) ? "C" : DriveAllKey;
        }
    }

    private void ApplyCurrentView()
    {
        if (_viewMode == MainViewMode.CDriveOverview)
        {
            ApplyOverviewFilters();
        }
        else if (_viewMode == MainViewMode.InfrequentApps)
        {
            ApplyInfrequentFilters();
        }
        else
        {
            ApplyFilters();
        }
    }

    private void ApplyFilters()
    {
        _filterState.Normalize();

        IEnumerable<CleanupSelectionRow> query = _allRows;

        if (!string.Equals(_filterState.SelectedDrive, DriveAllKey, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(row => string.Equals(row.DriveName, _filterState.SelectedDrive, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(_filterState.SelectedCategory, CleanupFilterState.AllValue, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(row => string.Equals(row.Category, _filterState.SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        query = _filterState.SelectedAutoMode switch
        {
            CleanupFilterState.AutoValue => query.Where(row => row.SelectionTier == SelectionTier.AutoSafe),
            CleanupFilterState.ReviewValue => query.Where(row => row.SelectionTier == SelectionTier.ReviewOnly || row.Item.ImpactSeverity != CleanupImpactSeverity.Low),
            AutoModeAppOnly => query.Where(row => row.Item.IsApplicationRelated),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(_filterState.SearchText))
        {
            var search = _filterState.SearchText.Trim();
            query = query.Where(row => MatchesSearch(row, search));
        }

        query = _activeQuickFilter switch
        {
            QuickFilterSafeJunk => query.Where(row => row.Item.Category.Equals("明确垃圾", StringComparison.OrdinalIgnoreCase)
                || row.Item.CandidateKind is CleanupCandidateKind.SafeJunk or CleanupCandidateKind.Aggregate),
            QuickFilterAppCache => query.Where(row => row.Item.CandidateKind == CleanupCandidateKind.AppCache),
            QuickFilterPackages => query.Where(row => row.Item.CandidateKind == CleanupCandidateKind.Package),
            QuickFilterDuplicates => query.Where(row => row.Item.CandidateKind == CleanupCandidateKind.DuplicateFile),
            QuickFilterResidue => query.Where(row => row.Item.CandidateKind == CleanupCandidateKind.AppResidue || row.Item.IsApplicationRelated),
            _ => query
        };

        query = _filterState.SortMode switch
        {
            CleanupFilterState.SortSizeDesc => query
                .OrderByDescending(row => row.EffectiveSizeBytes)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            CleanupFilterState.SortNameAsc => query
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Path, StringComparer.OrdinalIgnoreCase),
            CleanupFilterState.SortDriveSize => query
                .OrderBy(row => GetDrivePriority(row.DriveName))
                .ThenByDescending(row => row.EffectiveSizeBytes)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase),
            _ => query
                .OrderBy(row => GetDrivePriority(row.DriveName))
                .ThenBy(row => row.SelectionTier)
                .ThenBy(row => row.Item.ImpactSeverity)
                .ThenByDescending(row => row.EffectiveSizeBytes)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        };

        _visibleRows = new BindingList<CleanupSelectionRow>(query.ToList());
        _grid.DataSource = _visibleRows;
        QueueCleanupIconLoad(_visibleRows);
        SetActiveContent(_grid);
        UpdateDriveTabs();
        UpdateSelectionSummary();
        UpdateSelectionHint();
        UpdateViewSummary();
        UpdateActionStates();
        UpdateGridPresentation();
    }

    private void ApplyOverviewFilters()
    {
        _filterState.Normalize();

        IEnumerable<CDriveOverviewEntry> query = _overviewRows;
        if (!string.IsNullOrWhiteSpace(_filterState.SearchText))
        {
            var search = _filterState.SearchText.Trim();
            query = query.Where(entry => MatchesOverviewSearch(entry, search));
        }

        if (!string.Equals(_filterState.SelectedCategory, CleanupFilterState.AllValue, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(entry => string.Equals(entry.Category, _filterState.SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        query = _filterState.SortMode switch
        {
            CleanupFilterState.SortNameAsc => query.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            CleanupFilterState.SortSizeDesc => query.OrderByDescending(entry => entry.SizeBytes).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            _ => query
                .OrderBy(GetOverviewCategoryPriority)
                .ThenByDescending(entry => entry.SizeBytes)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        };

        _visibleOverviewRows = new BindingList<CDriveOverviewEntry>(query.ToList());
        _overviewGrid.DataSource = _visibleOverviewRows;
        QueueOverviewIconLoad(_visibleOverviewRows);
        SetActiveContent(_overviewGrid);
        UpdateDriveTabs();
        UpdateSelectionSummary();
        UpdateSelectionHint();
        UpdateViewSummary();
        UpdateActionStates();
        UpdateGridPresentation();
    }

    private void ApplyInfrequentFilters()
    {
        _filterState.Normalize();

        IEnumerable<InfrequentSoftwareEntry> query = _infrequentRows;

        if (!string.Equals(_filterState.SelectedDrive, DriveAllKey, StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(row => string.Equals(row.PrimaryDrive, _filterState.SelectedDrive, StringComparison.OrdinalIgnoreCase));
        }

        query = _filterState.SelectedCategory switch
        {
            "recommended" => query.Where(entry => entry.StatusKind == InfrequentSoftwareStatusKind.RecommendedDeletion),
            "review" => query.Where(entry => entry.StatusKind == InfrequentSoftwareStatusKind.ReviewOnly),
            "running" => query.Where(entry => entry.StatusKind == InfrequentSoftwareStatusKind.CurrentRunning),
            "active" => query.Where(entry => entry.StatusKind == InfrequentSoftwareStatusKind.ActiveRecent),
            "unknown_record" => query.Where(entry => entry.StatusKind == InfrequentSoftwareStatusKind.NoReliableUsageRecord),
            "protected" => query.Where(entry => entry.StatusKind == InfrequentSoftwareStatusKind.Protected),
            "whitelist" => query.Where(entry => entry.StatusKind == InfrequentSoftwareStatusKind.Whitelisted),
            _ => query
        };

        query = _filterState.SelectedAutoMode switch
        {
            "confidence_high" => query.Where(entry => entry.UsageConfidence == AppUsageConfidence.High),
            "confidence_medium" => query.Where(entry => entry.UsageConfidence == AppUsageConfidence.Medium),
            "confidence_low" => query.Where(entry => entry.UsageConfidence == AppUsageConfidence.Low),
            "confidence_unknown" => query.Where(entry => entry.UsageConfidence == AppUsageConfidence.Unknown),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(_filterState.SearchText))
        {
            var search = _filterState.SearchText.Trim();
            query = query.Where(entry => MatchesInfrequentSearch(entry, search));
        }

        query = _filterState.SortMode switch
        {
            CleanupFilterState.SortSizeDesc => query
                .OrderByDescending(entry => entry.SizeBytes)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase),
            CleanupFilterState.SortNameAsc => query
                .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.InstallRoot, StringComparer.OrdinalIgnoreCase),
            _ => query
                .OrderBy(entry => GetDrivePriority(entry.PrimaryDrive))
                .ThenBy(entry => entry.RecommendedForDeletion ? 0 : entry.IsWhitelisted ? 2 : 1)
                .ThenByDescending(entry => entry.UnusedDays ?? -1)
                .ThenByDescending(entry => entry.SizeBytes)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
        };

        _visibleInfrequentRows = new BindingList<InfrequentSoftwareEntry>(query.ToList());
        _infrequentGrid.DataSource = _visibleInfrequentRows;
        QueueInfrequentIconLoad(_visibleInfrequentRows);
        SetActiveContent(_infrequentGrid);
        UpdateDriveTabs();
        UpdateSelectionSummary();
        UpdateSelectionHint();
        UpdateViewSummary();
        UpdateActionStates();
        UpdateGridPresentation();
    }

    private void QueueInfrequentIconLoad(IReadOnlyCollection<InfrequentSoftwareEntry> entries)
    {
        _infrequentIconLoadCts?.Cancel();

        if (entries.Count == 0)
        {
            _infrequentGrid.Invalidate();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _infrequentIconLoadCts = cancellation;
        _ = Task.Run(() => LoadInfrequentIcons(entries, cancellation.Token), cancellation.Token);
    }

    private void QueueCleanupIconLoad(IReadOnlyCollection<CleanupSelectionRow> rows)
    {
        _cleanupIconLoadCts?.Cancel();

        if (rows.Count == 0)
        {
            _grid.Invalidate();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _cleanupIconLoadCts = cancellation;
        _ = Task.Run(() => LoadCleanupIcons(rows, cancellation.Token), cancellation.Token);
    }

    private void QueueOverviewIconLoad(IReadOnlyCollection<CDriveOverviewEntry> entries)
    {
        _overviewIconLoadCts?.Cancel();

        if (entries.Count == 0)
        {
            _overviewGrid.Invalidate();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _overviewIconLoadCts = cancellation;
        _ = Task.Run(() => LoadOverviewIcons(entries, cancellation.Token), cancellation.Token);
    }

    private void LoadInfrequentIcons(IReadOnlyCollection<InfrequentSoftwareEntry> entries, CancellationToken cancellationToken)
    {
        var refreshed = 0;
        var iconSize = GetCurrentIconSize(_infrequentGrid, _infrequentIconColumn);
        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (entry.IconImage is not null)
            {
                continue;
            }

            entry.IconImage = ApplicationIconCache.GetIcon(
                IconSemanticResolver.ForInfrequentApp(entry),
                iconSize);
            refreshed++;
            if (refreshed % 24 == 0)
            {
                InvalidateInfrequentIconColumn();
            }
        }

        InvalidateInfrequentIconColumn();
    }

    private void LoadCleanupIcons(IReadOnlyCollection<CleanupSelectionRow> rows, CancellationToken cancellationToken)
    {
        var refreshed = 0;
        var iconSize = GetCurrentIconSize(_grid, _cleanupIconColumn);
        foreach (var row in rows)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var iconKey = GetCleanupIconKey(row);
            if (string.IsNullOrWhiteSpace(iconKey) || _cleanupIcons.ContainsKey(iconKey))
            {
                continue;
            }

            _cleanupIcons[iconKey] = ApplicationIconCache.GetIcon(
                ResolveCleanupIconRequest(row),
                iconSize);
            refreshed++;
            if (refreshed % 24 == 0)
            {
                InvalidateCleanupIconColumn();
            }
        }

        InvalidateCleanupIconColumn();
    }

    private void LoadOverviewIcons(IReadOnlyCollection<CDriveOverviewEntry> entries, CancellationToken cancellationToken)
    {
        var refreshed = 0;
        var iconSize = GetCurrentIconSize(_overviewGrid, _overviewIconColumn);
        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var iconKey = GetOverviewIconKey(entry);
            if (string.IsNullOrWhiteSpace(iconKey) || _overviewIcons.ContainsKey(iconKey))
            {
                continue;
            }

            _overviewIcons[iconKey] = ApplicationIconCache.GetIcon(
                ResolveOverviewIconRequest(entry),
                iconSize);
            refreshed++;
            if (refreshed % 24 == 0)
            {
                InvalidateOverviewIconColumn();
            }
        }

        InvalidateOverviewIconColumn();
    }

    private void InvalidateInfrequentIconColumn()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (_resizeDragInProgress)
        {
            _pendingInfrequentIconColumnRefresh = true;
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _infrequentIconColumn.Index < 0)
                {
                    return;
                }

                if (_resizeDragInProgress)
                {
                    _pendingInfrequentIconColumnRefresh = true;
                    return;
                }

                SafeInvalidateIconColumn(_infrequentGrid, _infrequentIconColumn.Index);
            }));
        }
        catch
        {
        }
    }

    private void InvalidateCleanupIconColumn()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (_resizeDragInProgress)
        {
            _pendingCleanupIconColumnRefresh = true;
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _cleanupIconColumn.Index < 0)
                {
                    return;
                }

                if (_resizeDragInProgress)
                {
                    _pendingCleanupIconColumnRefresh = true;
                    return;
                }

                SafeInvalidateIconColumn(_grid, _cleanupIconColumn.Index);
            }));
        }
        catch
        {
        }
    }

    private void InvalidateOverviewIconColumn()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (_resizeDragInProgress)
        {
            _pendingOverviewIconColumnRefresh = true;
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _overviewIconColumn.Index < 0)
                {
                    return;
                }

                if (_resizeDragInProgress)
                {
                    _pendingOverviewIconColumnRefresh = true;
                    return;
                }

                SafeInvalidateIconColumn(_overviewGrid, _overviewIconColumn.Index);
            }));
        }
        catch
        {
        }
    }

    private void UpdateDriveSummary()
    {
        var statuses = _scanService.GetFixedDriveStatuses();
        if (statuses.Count == 0)
        {
            _driveSummaryLabel.Text = "未检测到固定磁盘。";
            _driveSummaryLabel.Tag = _driveSummaryLabel.Text;
            return;
        }

        var parts = statuses
            .Select(status => $"{status.Name} 盘可用 {status.FreeText} / 总 {status.TotalText}")
            .ToList();
        var modeText = _readOnlyMode ? "    当前为只读模式" : ShellHelper.IsAdministrator() ? "    当前已拿到管理员权限" : string.Empty;
        var fullText = string.Join("    ", parts) + modeText;
        _driveSummaryLabel.Tag = fullText;
        _driveSummaryLabel.Text = fullText;
    }

    private void UpdateDriveTabs()
    {
        UpdateTeachingStrip();
        _driveTabsFlow.Visible = _viewMode != MainViewMode.CDriveOverview;
        if (_viewMode == MainViewMode.CDriveOverview)
        {
            ApplyDataFirstVisibility();
            return;
        }

        var statuses = _scanService.GetFixedDriveStatuses();
        var drives = statuses.Select(status => status.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        _driveTabsFlow.SuspendLayout();
        _driveTabsFlow.Controls.Clear();
        _driveButtons.Clear();

        var loadedDrives = (_snapshot?.LoadedDrives ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pendingDrives = (_snapshot?.PendingDrives ?? [])
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var totalCount = _viewMode == MainViewMode.InfrequentApps ? _infrequentRows.Count : _allRows.Count;
        AddDriveTabButton(DriveAllKey, $"全部 ({totalCount})");
        foreach (var drive in drives)
        {
            var isPending = _snapshot?.IsPartialResult == true
                && pendingDrives.Contains(drive)
                && !loadedDrives.Contains(drive);
            var count = _viewMode == MainViewMode.InfrequentApps
                ? _infrequentRows.Count(row => string.Equals(row.PrimaryDrive, drive, StringComparison.OrdinalIgnoreCase))
                : _allRows.Count(row => string.Equals(row.DriveName, drive, StringComparison.OrdinalIgnoreCase));
            AddDriveTabButton(drive, isPending ? $"{drive} (加载中)" : $"{drive} ({count})", enabled: !isPending);
        }

        UpdateDriveButtonStyles();
        RefreshPillButtonSizing();
        _driveTabsFlow.ResumeLayout();
        ApplyDataFirstVisibility();
    }

    private void UpdateTeachingStrip()
    {
        if (_viewMode == MainViewMode.CDriveOverview)
        {
            _teachingPrimaryLabel.Text = IsUltraCompactLayout
                ? "单击勾选 · 双击打开文件夹 · 滚轮上下 · Shift+滚轮左右"
                : "单击勾选 = 选择迁移/删除对象    双击行 = 打开文件夹    鼠标滚轮 = 纵向滚动    Shift + 滚轮 = 左右滚动";
            _teachingSecondaryLabel.Text = IsCompactLayout
                ? "可自动迁移可直接搬走；需卸载重装表示是标准安装型应用；也可先加入白名单。"
                : "“适配后自动迁移/可自动迁移”可直接搬走；“需卸载重装”表示是标准安装型应用；“加入白名单”后当前目录会暂时隐藏。";
            _toolTip.SetToolTip(_teachingPanel, $"{_teachingPrimaryLabel.Text}\r\n{_teachingSecondaryLabel.Text}");
            return;
        }

        if (_viewMode == MainViewMode.InfrequentApps)
        {
            _teachingPrimaryLabel.Text = IsUltraCompactLayout
                ? "单击勾选 · 双击打开安装目录 · 滚轮上下 · Shift+滚轮左右"
                : "单击勾选 = 选择要深度删除的软件    双击行 = 打开安装目录    鼠标滚轮 = 纵向滚动    Shift + 滚轮 = 左右滚动";
            _teachingSecondaryLabel.Text = IsCompactLayout
                ? "60 天以上未用会优先高亮；可信度看运行中进程、UserAssist 和快捷方式记录。"
                : "最近 60 天以上未用的软件会优先高亮；可信度来自运行中进程、UserAssist、快捷方式活动和程序活动时间；已白名单的软件不会再建议删除。";
            _toolTip.SetToolTip(_teachingPanel, $"{_teachingPrimaryLabel.Text}\r\n{_teachingSecondaryLabel.Text}");
            return;
        }

        _teachingPrimaryLabel.Text = IsUltraCompactLayout
            ? "单击勾选 · 双击打开文件夹 · 滚轮上下 · Shift+滚轮左右"
            : "单击勾选 = 选择处理对象    双击行 = 打开文件夹    鼠标滚轮 = 纵向滚动    Shift + 滚轮 = 左右滚动";
        _teachingSecondaryLabel.Text = IsCompactLayout
            ? "建议确认表示删前先看一眼；需关闭占用时可点上方“关闭占用并重试”；常用缓存可加入白名单。"
            : "“建议确认”表示删前先看一眼；出现“需关闭占用后重试”时，可直接点上方结果条里的“关闭占用并重试”，常用缓存也可以加入白名单。";
        _toolTip.SetToolTip(_teachingPanel, $"{_teachingPrimaryLabel.Text}\r\n{_teachingSecondaryLabel.Text}");
    }

    private void AddDriveTabButton(string driveKey, string text, bool enabled = true)
    {
        var button = CreatePillButton(text);
        button.Enabled = enabled;
        button.Click += (_, _) =>
        {
            if (!enabled)
            {
                return;
            }

            _filterState.SelectedDrive = driveKey;
            ApplyCurrentView();
        };
        _driveTabsFlow.Controls.Add(button);
        _driveButtons[driveKey] = button;
    }

    private void UpdateDriveButtonStyles()
    {
        foreach (var pair in _driveButtons)
        {
            var selected = string.Equals(_filterState.SelectedDrive, pair.Key, StringComparison.OrdinalIgnoreCase);
            ApplyPillStyle(pair.Value, selected);
        }
    }

    private void AddQuickFilterButton(Control parent, string key, string text)
    {
        var button = CreatePillButton(text, compact: true);
        button.Click += (_, _) =>
        {
            _activeQuickFilter = key;
            UpdateQuickFilterStyles();
            ApplyCurrentView();
        };
        parent.Controls.Add(button);
        _quickFilterButtons[key] = button;
    }

    private void UpdateQuickFilterStyles()
    {
        foreach (var pair in _quickFilterButtons)
        {
            ApplyPillStyle(pair.Value, string.Equals(pair.Key, _activeQuickFilter, StringComparison.OrdinalIgnoreCase), compact: true);
        }
    }

    private void UpdateViewSummary()
    {
        if (_viewMode == MainViewMode.CDriveOverview)
        {
            var totalBytes = _visibleOverviewRows.Sum(row => row.SizeBytes);
            var autoSafeCount = _visibleOverviewRows.Count(row => row.MigrationMode == MigrationMode.AutoSafe);
            var guideCount = _visibleOverviewRows.Count(row => row.MigrationMode == MigrationMode.GuideOnly);
            _viewSummaryLabel.Text = IsUltraCompactLayout
                ? $"C盘总览 {_visibleOverviewRows.Count} 项 · {SizeFormatter.Format(totalBytes)} · 自动迁移 {autoSafeCount} 项"
                : IsCompactLayout
                ? $"C盘总览 {_visibleOverviewRows.Count} 项，占用 {SizeFormatter.Format(totalBytes)}，可自动迁移 {autoSafeCount} 项"
                : $"C盘总览 {_visibleOverviewRows.Count} 项，总占用 {SizeFormatter.Format(totalBytes)}，其中可自动迁移 {autoSafeCount} 项，需手动处理 {guideCount} 项";
            return;
        }

        if (_viewMode == MainViewMode.InfrequentApps)
        {
            var totalBytes = _visibleInfrequentRows.Sum(row => row.SizeBytes);
            var recommendedCount = _visibleInfrequentRows.Count(row => row.RecommendedForDeletion);
            var runningCount = _visibleInfrequentRows.Count(row => row.IsCurrentlyRunning);
            var lowConfidenceReviewCount = _visibleInfrequentRows.Count(row =>
                !row.RecommendedForDeletion
                && !row.IsCurrentlyRunning
                && row.UsageConfidence == AppUsageConfidence.Low
                && row.UnusedDays.HasValue
                && row.UnusedDays.Value >= 60);
            var unknownCount = _visibleInfrequentRows.Count(row => row.UsageConfidence == AppUsageConfidence.Unknown);
            _viewSummaryLabel.Text = IsUltraCompactLayout
                ? $"长期未用 {_visibleInfrequentRows.Count} 项 · {SizeFormatter.Format(totalBytes)} · 建议删除 {recommendedCount} 项 · 正在用 {runningCount} 项"
                : IsCompactLayout
                ? $"长期未用软件 {_visibleInfrequentRows.Count} 项，占用 {SizeFormatter.Format(totalBytes)}，建议删除 {recommendedCount} 项，正在用 {runningCount} 项"
                : $"长期未用软件 {_visibleInfrequentRows.Count} 项，总占用 {SizeFormatter.Format(totalBytes)}，其中高/中可信 60 天以上建议删除 {recommendedCount} 项，当前正在用 {runningCount} 项，低可信待确认 {lowConfidenceReviewCount + unknownCount} 项";
            return;
        }

        var candidateTotalBytes = _visibleRows.Sum(row => row.EffectiveSizeBytes);
        var driveText = string.Equals(_filterState.SelectedDrive, DriveAllKey, StringComparison.OrdinalIgnoreCase)
            ? "全部盘"
            : $"{_filterState.SelectedDrive} 盘";
        _viewSummaryLabel.Text = $"{driveText} 当前候选 {_visibleRows.Count} 项，可释放 {SizeFormatter.Format(candidateTotalBytes)}";
    }

    private void UpdateSelectionSummary()
    {
        if (_viewMode == MainViewMode.CDriveOverview)
        {
            var autoSafeCount = _visibleOverviewRows.Count(entry => entry.MigrationMode == MigrationMode.AutoSafe);
            var guideCount = _visibleOverviewRows.Count(entry => entry.MigrationMode == MigrationMode.GuideOnly);
            var protectedCount = _visibleOverviewRows.Count(entry => entry.MigrationMode == MigrationMode.Protected);
            var selectedEntries = _overviewRows.Where(entry => entry.Selected && entry.SelectionEnabled).ToList();
            _selectionSummaryLabel.Text = IsUltraCompactLayout
                ? $"自动迁移 {autoSafeCount} 项 · 手动处理 {guideCount} 项 · 保护 {protectedCount} 项"
                : IsCompactLayout
                ? $"可自动迁移 {autoSafeCount} 项 · 手动处理 {guideCount} 项 · 保护 {protectedCount} 项"
                : $"可自动迁移 {autoSafeCount} 项 · 需手动处理 {guideCount} 项 · 系统保护 {protectedCount} 项";
            if (selectedEntries.Count > 0)
            {
                _selectionSummaryLabel.Text += IsUltraCompactLayout
                    ? $" · 已勾选 {selectedEntries.Count} 项 / {SizeFormatter.Format(selectedEntries.Sum(entry => entry.SizeBytes))}"
                    : $"    当前已勾选 {selectedEntries.Count} 项 / {SizeFormatter.Format(selectedEntries.Sum(entry => entry.SizeBytes))}";
            }
            return;
        }

        if (_viewMode == MainViewMode.InfrequentApps)
        {
            var selectedApps = _infrequentRows.Where(row => row.Selected).ToList();
            var recommendedCount = _infrequentRows.Count(row => row.RecommendedForDeletion);
            var runningCount = _infrequentRows.Count(row => row.IsCurrentlyRunning);
            var lowConfidenceReviewCount = _infrequentRows.Count(row =>
                !row.RecommendedForDeletion
                && !row.IsCurrentlyRunning
                && row.UsageConfidence == AppUsageConfidence.Low
                && row.UnusedDays.HasValue
                && row.UnusedDays.Value >= 60);
            var whitelistCount = _infrequentRows.Count(row => row.IsWhitelisted);
            _selectionSummaryLabel.Text = IsUltraCompactLayout
                ? $"建议删除 {recommendedCount} 项 · 正在用 {runningCount} 项 · 待确认 {lowConfidenceReviewCount} 项 · 白名单 {whitelistCount} 项"
                : IsCompactLayout
                ? $"建议删除 {recommendedCount} 项 · 正在用 {runningCount} 项 · 待确认 {lowConfidenceReviewCount} 项 · 白名单 {whitelistCount} 项"
                : $"高/中可信 60天以上建议删除 {recommendedCount} 项 · 当前正在用 {runningCount} 项 · 低可信待确认 {lowConfidenceReviewCount} 项 · 已白名单 {whitelistCount} 项";
            if (selectedApps.Count > 0)
            {
                _selectionSummaryLabel.Text += IsUltraCompactLayout
                    ? $" · 已勾选 {selectedApps.Count} 项 / {SizeFormatter.Format(selectedApps.Sum(row => row.SizeBytes))}"
                    : $"    当前已勾选 {selectedApps.Count} 项 / {SizeFormatter.Format(selectedApps.Sum(row => row.SizeBytes))}";
            }
            return;
        }

        var autoSafeRows = _allRows.Where(row => row.SelectionTier == SelectionTier.AutoSafe).ToList();
        var reviewRows = _allRows.Where(row => row.SelectionTier == SelectionTier.ReviewOnly).ToList();
        var selectedRows = _allRows.Where(row => row.Selected).ToList();
        var selectedBytes = selectedRows.Sum(row => row.EffectiveSizeBytes);

        _selectionSummaryLabel.Text = IsUltraCompactLayout
            ? $"低风险 {autoSafeRows.Count} 项 / {SizeFormatter.Format(autoSafeRows.Sum(row => row.EffectiveSizeBytes))} · 中风险 {reviewRows.Count} 项 / {SizeFormatter.Format(reviewRows.Sum(row => row.EffectiveSizeBytes))}"
            : IsCompactLayout
            ? $"低风险 {autoSafeRows.Count} 项 / {SizeFormatter.Format(autoSafeRows.Sum(row => row.EffectiveSizeBytes))}    需确认 {reviewRows.Count} 项 / {SizeFormatter.Format(reviewRows.Sum(row => row.EffectiveSizeBytes))}"
            : $"已自动勾选低风险 {autoSafeRows.Count} 项 / {SizeFormatter.Format(autoSafeRows.Sum(row => row.EffectiveSizeBytes))}    "
                + $"可再确认中风险 {reviewRows.Count} 项 / {SizeFormatter.Format(reviewRows.Sum(row => row.EffectiveSizeBytes))}";

        if (selectedRows.Count > 0)
        {
            _selectionSummaryLabel.Text += IsUltraCompactLayout
                ? $" · 已勾选 {selectedRows.Count} 项 / {SizeFormatter.Format(selectedBytes)}"
                : $"    当前已勾选 {selectedRows.Count} 项 / {SizeFormatter.Format(selectedBytes)}";
        }
    }

    private void UpdateSelectionHint()
    {
        _selectionHintLabel.Visible = !IsCompactLayout;
        if (_viewMode == MainViewMode.CDriveOverview)
        {
            var entry = _overviewGrid.CurrentRow?.DataBoundItem as CDriveOverviewEntry
                ?? _visibleOverviewRows.FirstOrDefault();
            var overviewFullText = entry is null
                ? "这里会解释 C 盘目录都是什么、是否系统保护、哪些适合迁到 D/E，而不是把系统核心目录直接当垃圾删除。"
                : $"当前查看：{entry.Name} | 迁移方式：{entry.OperationHintText} | 说明：{entry.MigrationHintText}";
            var overviewCompactText = entry is null
                ? overviewFullText
                : $"当前查看：{entry.Name} | {entry.OperationHintText}";
            _selectionHintLabel.Text = IsCompactLayout ? overviewCompactText : overviewFullText;
            _toolTip.SetToolTip(_selectionHintLabel, overviewFullText);
            _toolTip.SetToolTip(_summaryPanel, overviewFullText);
            _toolTip.SetToolTip(_selectionSummaryLabel, overviewFullText);
            return;
        }

        if (_viewMode == MainViewMode.InfrequentApps)
        {
            var entry = _infrequentGrid.CurrentRow?.DataBoundItem as InfrequentSoftwareEntry
                ?? _visibleInfrequentRows.FirstOrDefault();
            var infrequentFullText = entry is null
                ? "这里会按盘符展示长期未用的软件。只有 60 天以上且高/中可信的项才会直接标成“建议删除”，低可信和无记录的项只会提醒你确认。"
                : $"当前查看：{entry.DisplayName} | 最近使用：{entry.LastUsedText} | 依据：{entry.UsageSourceText} | 可信度：{entry.UsageConfidenceText} | 说明：{entry.UsageEvidenceText}";
            var infrequentCompactText = entry is null
                ? infrequentFullText
                : $"当前查看：{entry.DisplayName} | 最近使用：{entry.LastUsedText} | {entry.UsageConfidenceText}";
            _selectionHintLabel.Text = IsCompactLayout ? infrequentCompactText : infrequentFullText;
            _toolTip.SetToolTip(_selectionHintLabel, infrequentFullText);
            _toolTip.SetToolTip(_summaryPanel, infrequentFullText);
            _toolTip.SetToolTip(_selectionSummaryLabel, infrequentFullText);
            return;
        }

        var row = _grid.CurrentRow?.DataBoundItem as CleanupSelectionRow
            ?? _visibleRows.FirstOrDefault();

        if (row is null)
        {
            _selectionHintLabel.Text = "当前列表只展示候选项目，不会把系统关键文件和受保护目录直接放进普通删除列表。";
            _toolTip.SetToolTip(_selectionHintLabel, _selectionHintLabel.Text);
            _toolTip.SetToolTip(_summaryPanel, _selectionHintLabel.Text);
            _toolTip.SetToolTip(_selectionSummaryLabel, _selectionHintLabel.Text);
            return;
        }

        var fullText = row.HasRecentRunResult
            ? $"当前查看：{row.Name} | 上次处理：{row.LastRunStatus} | {row.LastRunMessage}"
            : $"当前查看：{row.Name} | 建议：{row.RecommendationText} | 删除后果：{row.ImpactText}";

        if (row.RetrySuggested && row.BlockingProcessNames.Count > 0)
        {
            fullText += $" | 当前占用：{string.Join("、", row.BlockingProcessNames)}";
        }
        else if (!string.IsNullOrWhiteSpace(row.WhitelistHintText))
        {
            fullText += $" | 白名单提示：{row.WhitelistHintText}";
        }

        var compactText = row.HasRecentRunResult
            ? $"当前查看：{row.Name} | 上次处理：{row.LastRunStatus}"
            : $"当前查看：{row.Name} | {row.RecommendationText}";
        _selectionHintLabel.Text = IsCompactLayout ? compactText : fullText;
        _toolTip.SetToolTip(_selectionHintLabel, fullText);
        _toolTip.SetToolTip(_summaryPanel, fullText);
        _toolTip.SetToolTip(_selectionSummaryLabel, fullText);
    }

    private void UpdateActionStates()
    {
        var selectedCount = _allRows.Count(row => row.Selected);
        var selectedMigrationCount = _overviewRows.Count(entry => entry.Selected && entry.SelectionEnabled);
        var selectedDeletionCount = _overviewRows.Count(entry => entry.Selected && entry.CanDelete);
        var selectedInfrequentCount = _infrequentRows.Count(entry => entry.Selected && entry.CanDeepDelete && !entry.IsProtected && !entry.IsWhitelisted);
        var inCandidateView = _viewMode == MainViewMode.CleanupCandidates;
        var inInfrequentView = _viewMode == MainViewMode.InfrequentApps;
        var hasActiveScan = _operationManager.HasActiveJob(OperationJobKind.Scan);
        var dataFirst = IsUltraCompactLayout;
        _cleanSelectedButton.Enabled = !_isBusy && !_readOnlyMode && ((inCandidateView && selectedCount > 0) || (inInfrequentView && selectedInfrequentCount > 0));
        _recommendedButton.Enabled = !_isBusy && !_readOnlyMode && ((inCandidateView && _allRows.Count > 0) || (inInfrequentView && _infrequentRows.Count > 0));
        _safeCleanButton.Enabled = !_isBusy && !_readOnlyMode && inCandidateView && _visibleRows.Any(row => row.Item.SafeAuto && row.Item.ImpactSeverity == CleanupImpactSeverity.Low);
        _recommendedButton.Visible = inCandidateView || inInfrequentView;
        _cleanSelectedButton.Visible = inCandidateView || inInfrequentView;
        _safeCleanButton.Visible = inCandidateView;
        _migrateSelectedButton.Enabled = !_isBusy && !_readOnlyMode && _viewMode == MainViewMode.CDriveOverview && selectedMigrationCount > 0;
        _migrateSelectedButton.Visible = _viewMode == MainViewMode.CDriveOverview;
        _deleteOverviewSelectedButton.Enabled = !_isBusy && !_readOnlyMode && _viewMode == MainViewMode.CDriveOverview && selectedDeletionCount > 0;
        _deleteOverviewSelectedButton.Visible = _viewMode == MainViewMode.CDriveOverview;
        _scanButton.Enabled = !_isBusy && !hasActiveScan;
        _scheduleSettingsButton.Enabled = !_isBusy && !_readOnlyMode;
        _cDriveAdviceButton.Enabled = true;
        _moreActionsButton.Enabled = true;
        _whitelistButton.Enabled = !_isBusy && GetWhitelistSelectionCount() > 0;
        _scheduleSettingsButton.Visible = !dataFirst;
        _whitelistButton.Visible = !dataFirst;
        _moreActionsButton.Visible = !dataFirst;
        _cleanupViewButton.Enabled = true;
        _overviewViewButton.Enabled = true;
        _infrequentViewButton.Enabled = true;
        _openLocationMenuItem.Enabled = _viewMode switch
        {
            MainViewMode.CleanupCandidates => GetCurrentRow() is not null,
            MainViewMode.CDriveOverview => GetCurrentOverviewEntry() is not null,
            MainViewMode.InfrequentApps => GetCurrentInfrequentEntry() is not null,
            _ => false
        };
        _clearSelectionMenuItem.Enabled = !_isBusy && !_readOnlyMode && (selectedCount > 0 || selectedInfrequentCount > 0 || selectedMigrationCount > 0);
        _quickFiltersHost.Visible = inCandidateView;
        ApplyDataFirstVisibility();
    }

    private void UpdateScheduleStatus()
    {
        _scheduleStatusLabel.Text = _readOnlyMode
            ? "当前为只读模式，自动清理和后台任务设置已禁用"
            : _schedulerService.GetStatusText(_settings);
    }

    private void UpdateGridPresentation(bool refreshActiveContent = true)
    {
        SetActiveContent(GetActiveContentControl());

        var wide = IsWideMode;
        _impactColumn.Visible = wide;
        _severityColumn.Visible = wide;

        _nameColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _nameColumn.FillWeight = wide ? 16 : 18;

        _locationColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _locationColumn.FillWeight = wide ? 22 : 24;

        _impactColumn.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _impactColumn.FillWeight = 34;

        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.ScrollBars = ScrollBars.Both;
        _overviewGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _overviewGrid.ScrollBars = ScrollBars.Both;
        _infrequentGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _infrequentGrid.ScrollBars = ScrollBars.Both;
        if (refreshActiveContent && !_resizeDragInProgress)
        {
            GetActiveContentControl().Invalidate();
        }
    }

    private async Task RefreshScanAsync()
    {
        if (_isBusy)
        {
            return;
        }

        QueueScanRefresh(userInitiated: true);
        await Task.CompletedTask;
    }

    private async Task StartCleanupFlowAsync(IReadOnlyCollection<CleanupItem> items, bool safeOnly)
    {
        if (_isBusy)
        {
            return;
        }

        if (_readOnlyMode)
        {
            MessageBox.Show("当前处于只读模式。请重新以管理员身份启动后再执行删除、迁移或深度清理。", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (items.Count == 0)
        {
            MessageBox.Show("当前没有可处理的项目。", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CleanupExecutionPreview preview;
        try
        {
            SetBusy(true, safeOnly ? "正在生成低风险预览..." : "正在生成处理预览...");
            preview = await Task.Run(() => BuildCleanupPreview(items, safeOnly));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"生成删除预览失败：{ex.Message}", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        finally
        {
            SetBusy(false);
        }

        using var dialog = new CleanupConfirmationDialog(_settings.AppName, preview);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            SetStatusMessage("已取消处理");
            return;
        }

        QueueCleanupExecution(items, includeResidueCleanup: true, safeOnly);
        await Task.CompletedTask;
    }

    private CleanupExecutionPreview BuildCleanupPreview(IReadOnlyCollection<CleanupItem> items, bool safeOnly)
    {
        var orderedItems = items
            .DistinctBy(item => item.Id)
            .OrderBy(item => GetDrivePriority(item.DriveName))
            .ThenBy(item => item.ImpactSeverity)
            .ThenByDescending(item => item.SizeBytes)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var residueActions = _cleanupService.PlanResidueActions(orderedItems);
        var totalBytes = orderedItems.Sum(item => item.SizeBytes);

        var introText = safeOnly
            ? "这次只会处理当前视图中低风险、可安全删的项目。系统会先展示清单，你确认后才真正执行。"
            : "确认后才会真正删除文件或目录。应用相关项会连同强关联残留一起清理，尽量减少重装冲突和旧记录残留。";

        var riskHintText = safeOnly
            ? "低风险并不等于完全没后果，例如缓存、日志和重复安装包删掉后，之后可能需要重新下载或重新生成。"
            : "高风险项目会显式标红；如果你还在用这些目录里的程序、备份或安装包，就不应该删除。";

        if (residueActions.Count > 0)
        {
            riskHintText += $" 本次还会联动清理 {residueActions.Count} 个强关联残留项。";
        }

        var requiresSecondaryRiskAcknowledgement = orderedItems.Any(item => item.ImpactSeverity != CleanupImpactSeverity.Low);
        var acknowledgementText = "我知道本次选择里包含中高风险项目。它们删除后可能需要重新下载缓存、重新生成数据，或者需要先卸载再重装到 D/E。";

        return new CleanupExecutionPreview
        {
            Title = safeOnly ? "低风险批量清理确认" : "确认处理勾选项目",
            ConfirmButtonText = safeOnly ? "确认低风险清理" : "确认处理",
            IntroText = introText,
            RiskHintText = riskHintText,
            RequiresSecondaryRiskAcknowledgement = requiresSecondaryRiskAcknowledgement,
            SecondaryRiskAcknowledgementText = acknowledgementText,
            TotalBytes = totalBytes,
            ResidueActions = residueActions,
            Items = orderedItems
                .Select(item => new CleanupExecutionPreviewItem { Item = item })
                .ToList()
        };
    }

    private CleanupRunResult? ExecuteCleanup(
        IReadOnlyCollection<CleanupItem> items,
        bool includeResidueCleanup,
        IProgress<OperationProgress>? progress,
        Guid jobId)
    {
        if (_cleanupService.RequiresElevation(items, includeResidueCleanup) && !ShellHelper.IsAdministrator())
        {
            return ExecuteCleanupElevated(items);
        }

        return _cleanupService.Run(items, includeResidueCleanup, progress, jobId);
    }

    private CleanupRunResult? ExecuteCleanupElevated(IReadOnlyCollection<CleanupItem> items)
    {
        var selectionFile = ShellHelper.ExportSelection(_context, items);
        var resultPath = Program.GetRunResultPath(selectionFile);

        try
        {
            ShellHelper.StartCurrentExecutable(
                _context,
                ["--selection", selectionFile, "--no-prompt"],
                elevated: true,
                waitForExit: true);

            if (!File.Exists(resultPath))
            {
                return null;
            }

            var json = File.ReadAllText(resultPath);
            return JsonSerializer.Deserialize<CleanupRunResult>(json);
        }
        finally
        {
            ShellHelper.DeleteFileQuietly(selectionFile);
            ShellHelper.DeleteFileQuietly(resultPath);
        }
    }

    private void ApplyRunResultToRows(CleanupRunResult runResult)
    {
        if (runResult.Results.Count == 0)
        {
            return;
        }

        var resultsByItemId = runResult.Results.ToDictionary(result => result.ItemId);
        var successResults = runResult.Results
            .Where(result => string.Equals(result.Status, "成功", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var rowsToRemove = new List<CleanupSelectionRow>();

        foreach (var row in _allRows)
        {
            row.ClearSelection();

            if (TryFindMatchingRunResult(row, resultsByItemId, runResult.Results, out var result))
            {
                if (string.Equals(result.Status, "成功", StringComparison.OrdinalIgnoreCase))
                {
                    rowsToRemove.Add(row);
                    continue;
                }

                row.ApplyRunResult(result);
                continue;
            }

            if (ShouldRemoveCandidateRowAfterSuccess(row, successResults))
            {
                rowsToRemove.Add(row);
                continue;
            }
        }

        foreach (var row in rowsToRemove)
        {
            row.PropertyChanged -= CleanupSelectionRowChanged;
            _allRows.Remove(row);
        }

        var infrequentRowsToRemove = _infrequentRows
            .Where(entry => ShouldRemoveInfrequentEntryAfterSuccess(entry, successResults))
            .ToList();
        foreach (var entry in infrequentRowsToRemove)
        {
            _infrequentRows.Remove(entry);
        }

        var overviewRowsToRemove = _overviewRows
            .Where(entry => ShouldRemoveOverviewEntryAfterSuccess(entry, successResults))
            .ToList();
        foreach (var entry in overviewRowsToRemove)
        {
            _overviewRows.Remove(entry);
        }
    }

    private static bool TryFindMatchingRunResult(
        CleanupSelectionRow row,
        IReadOnlyDictionary<Guid, CleanupResult> resultsByItemId,
        IReadOnlyCollection<CleanupResult> results,
        out CleanupResult result)
    {
        if (resultsByItemId.TryGetValue(row.Item.Id, out result!))
        {
            return true;
        }

        result = results.FirstOrDefault(candidate => PathsMatchForUi(candidate.NormalizedPath, row.Item.NormalizedPath))
            ?? new CleanupResult();
        return result.ItemId != Guid.Empty;
    }

    private static bool ShouldRemoveCandidateRowAfterSuccess(CleanupSelectionRow row, IReadOnlyCollection<CleanupResult> successResults)
    {
        return successResults.Any(result => PathShouldBeRemovedBySuccess(result, row.Item.NormalizedPath));
    }

    private static bool ShouldRemoveInfrequentEntryAfterSuccess(InfrequentSoftwareEntry entry, IReadOnlyCollection<CleanupResult> successResults)
    {
        return successResults.Any(result =>
            PathShouldBeRemovedBySuccess(result, entry.InstallRoot)
            || (!string.IsNullOrWhiteSpace(entry.AppIdentityKey)
                && !string.IsNullOrWhiteSpace(result.NormalizedPath)
                && PathsMatchForUi(result.NormalizedPath, entry.InstallRoot)));
    }

    private static bool ShouldRemoveOverviewEntryAfterSuccess(CDriveOverviewEntry entry, IReadOnlyCollection<CleanupResult> successResults)
    {
        return successResults.Any(result => PathShouldBeRemovedBySuccess(result, entry.Path));
    }

    private static bool ShouldRemoveMigrationCandidateAfterSuccess(MigrationCandidate candidate, IReadOnlyCollection<CleanupResult> successResults)
    {
        return successResults.Any(result => PathShouldBeRemovedBySuccess(result, candidate.SourcePath));
    }

    private static bool ShouldRemoveCleanupItemAfterSuccess(CleanupItem item, IReadOnlyCollection<CleanupResult> successResults)
    {
        return successResults.Any(result => PathShouldBeRemovedBySuccess(result, item.NormalizedPath));
    }

    private static bool PathShouldBeRemovedBySuccess(CleanupResult result, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(result.NormalizedPath))
        {
            return false;
        }

        if (PathsMatchForUi(result.NormalizedPath, candidatePath))
        {
            return true;
        }

        return result.TargetKind == CleanupTargetKind.DirectoryTree
            && IsChildPath(candidatePath, result.NormalizedPath);
    }

    private static bool PathsMatchForUi(string leftPath, string rightPath)
    {
        return string.Equals(NormalizeUiPath(leftPath), NormalizeUiPath(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChildPath(string candidatePath, string parentPath)
    {
        var normalizedCandidate = NormalizeUiPath(candidatePath);
        var normalizedParent = NormalizeUiPath(parentPath);
        if (string.IsNullOrWhiteSpace(normalizedCandidate) || string.IsNullOrWhiteSpace(normalizedParent))
        {
            return false;
        }

        var prefix = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeUiPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private void ShowResultBanner(CleanupRunResult runResult)
    {
        var successCount = runResult.Results.Count(result => result.Status == "成功");
        var partialCount = runResult.Results.Count(result => result.Status == "部分成功");
        var failCount = runResult.Results.Count(result => result.Status == "失败");
        var residueCount = runResult.ResidueActions.Count(action => !action.ExistsAfter);
        var firstResidualWarning = runResult.ResidualWarnings.FirstOrDefault();
        var residualBreakdown = BuildResidualBreakdownText(runResult);
        var firstFollowUpRecommendation = runResult.FollowUpRecommendations.FirstOrDefault();
        _lastRetrySuggestedItemIds = runResult.Results
            .Where(result => result.RetrySuggested)
            .Select(result => result.ItemId)
            .ToHashSet();

        _resultBannerLabel.Text = $"本次处理完成：成功 {successCount} 项，部分成功 {partialCount} 项，失败 {failCount} 项，实际释放 {SizeFormatter.Format(runResult.FreedBytes)}"
            + ((partialCount > 0 || failCount > 0) ? "。未清干净的项目已保留在列表里，并更新为当前剩余状态；通常是文件仍被程序或系统占用。" : string.Empty)
            + (runResult.OfficialUninstallAttempted
                ? $"，官方卸载成功 {runResult.OfficialUninstallSucceededCount} 项"
                  + (runResult.OfficialUninstallFailedCount > 0 ? $"，失败 {runResult.OfficialUninstallFailedCount} 项并已退回文件级深删" : string.Empty)
                : string.Empty)
            + (residueCount > 0 ? $"，残留联动清理 {residueCount} 项" : string.Empty)
            + (runResult.VerifiedClean ? "，当前轮次校验为已清干净。" : string.Empty)
            + (runResult.ResidualWarnings.Count > 0
                ? $"，仍有 {runResult.ResidualWarnings.Count} 个残留需要继续处理"
                  + (string.IsNullOrWhiteSpace(residualBreakdown) ? "。" : $"，主要是 {residualBreakdown}。")
                  + (string.IsNullOrWhiteSpace(firstResidualWarning) ? string.Empty : $" 例如：{BuildResidualWarningPreview(firstResidualWarning)}。")
                : string.Empty)
            + (!string.IsNullOrWhiteSpace(firstFollowUpRecommendation) ? $" 建议下一步：{firstFollowUpRecommendation}" : string.Empty)
            + (runResult.BlockingProcesses.Count > 0 ? $" 当前仍检测到占用程序：{string.Join("、", runResult.BlockingProcesses.Take(4))}。" : string.Empty);
        _lastResultLogPath = runResult.LogPath;
        _resultBannerLink.Visible = !string.IsNullOrWhiteSpace(_lastResultLogPath) && File.Exists(_lastResultLogPath);
        _retryBlockedLink.Visible = _lastRetrySuggestedItemIds.Count > 0;
        _resultBannerPanel.BackColor = failCount > 0
            ? Color.FromArgb(255, 241, 240)
            : partialCount > 0 ? Color.FromArgb(255, 247, 231) : Color.FromArgb(235, 248, 241);
        _resultBannerPanel.Visible = true;
        _toolTip.SetToolTip(_resultBannerLabel, BuildResultBannerToolTip(runResult));
        SetStatusMessage("处理完成");
    }

    private void SyncSnapshotAfterCleanupRun(CleanupRunResult runResult)
    {
        if (_snapshot is null || runResult.Results.Count == 0)
        {
            return;
        }

        var successResults = runResult.Results
            .Where(result => string.Equals(result.Status, "成功", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (successResults.Count == 0)
        {
            return;
        }

        var cleanupItems = _snapshot.CleanupItems
            .Where(item => !ShouldRemoveCleanupItemAfterSuccess(item, successResults))
            .ToList();
        var overviewEntries = _snapshot.CDriveOverviewEntries
            .Where(entry => !ShouldRemoveOverviewEntryAfterSuccess(entry, successResults))
            .ToList();
        var infrequentEntries = _snapshot.InfrequentSoftwareEntries
            .Where(entry => !ShouldRemoveInfrequentEntryAfterSuccess(entry, successResults))
            .ToList();
        var migrationCandidates = _snapshot.MigrationCandidates
            .Where(candidate => !ShouldRemoveMigrationCandidateAfterSuccess(candidate, successResults))
            .ToList();
        var cleanupIds = cleanupItems.Select(item => item.Id).ToHashSet();

        _snapshot = new ScanSnapshot
        {
            CleanupItems = cleanupItems,
            CDriveOverviewEntries = overviewEntries,
            InfrequentSoftwareEntries = infrequentEntries,
            MigrationCandidates = migrationCandidates,
            LoadedDrives = _snapshot.LoadedDrives,
            PendingDrives = _snapshot.PendingDrives,
            IsPartialResult = _snapshot.IsPartialResult,
            PhaseLabel = _snapshot.PhaseLabel,
            Warnings = _snapshot.Warnings,
            SafeDefaultSelectionIds = _snapshot.SafeDefaultSelectionIds.Where(cleanupIds.Contains).ToHashSet(),
            AdditionalReviewSelectionIds = _snapshot.AdditionalReviewSelectionIds.Where(cleanupIds.Contains).ToHashSet(),
            InstalledAppMappings = _snapshot.InstalledAppMappings,
            DuplicateGroups = _snapshot.DuplicateGroups,
            WhitelistHints = _snapshot.WhitelistHints
        };

        _activeCDriveSuggestionDialog?.ApplySnapshot(_snapshot);
    }

    private void UpdateScanWarningBanner(IReadOnlyList<ScanWarning> warnings, bool fatal = false, string? overrideMessage = null)
    {
        var visibleWarnings = GetVisibleScanWarnings(warnings);
        if (visibleWarnings.Count == 0 && string.IsNullOrWhiteSpace(overrideMessage))
        {
            HideScanWarningBanner();
            return;
        }

        _lastScanWarningLogPath = visibleWarnings
            .Select(warning => warning.LogPath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        _scanWarningLabel.Text = string.IsNullOrWhiteSpace(overrideMessage)
            ? BuildScanWarningSummary(visibleWarnings)
            : overrideMessage;
        _scanWarningLink.Visible = !string.IsNullOrWhiteSpace(_lastScanWarningLogPath) && File.Exists(_lastScanWarningLogPath);
        _scanWarningPanel.BackColor = fatal ? Color.FromArgb(255, 241, 240) : Color.FromArgb(255, 248, 219);
        _scanWarningLabel.ForeColor = fatal ? Color.FromArgb(150, 57, 57) : Color.FromArgb(112, 82, 16);
        _scanWarningPanel.Visible = true;

        var toolTip = visibleWarnings.Count > 0
            ? string.Join(
                Environment.NewLine + Environment.NewLine,
                visibleWarnings.Take(6).Select(warning =>
                {
                    var lines = new List<string> { $"阶段：{warning.Phase}", $"说明：{warning.Message}" };
                    if (!string.IsNullOrWhiteSpace(warning.AffectedPath))
                    {
                        lines.Add($"路径：{warning.AffectedPath}");
                    }

                    if (!string.IsNullOrWhiteSpace(warning.LogPath))
                    {
                        lines.Add($"日志：{warning.LogPath}");
                    }

                    return string.Join(Environment.NewLine, lines);
                }))
            : overrideMessage ?? string.Empty;
        _toolTip.SetToolTip(_scanWarningLabel, toolTip);
    }

    private void HideScanWarningBanner()
    {
        _scanWarningPanel.Visible = false;
        _scanWarningLink.Visible = false;
    }

    private void OpenLastScanWarningLog()
    {
        if (string.IsNullOrWhiteSpace(_lastScanWarningLogPath) || !File.Exists(_lastScanWarningLogPath))
        {
            return;
        }

        ShellHelper.RevealPath(_lastScanWarningLogPath);
    }

    private string WriteFatalScanFailureLog(Exception ex)
    {
        try
        {
            PortableContext.EnsureDirectory(_context.LogsRoot);
            var path = Path.Combine(_context.LogsRoot, $"scan-fatal-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 扫描失败{Environment.NewLine}{ex}{Environment.NewLine}";
            File.WriteAllText(path, content, System.Text.Encoding.UTF8);
            _lastScanWarningLogPath = path;
            return path;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildScanWarningSummary(ScanSnapshot snapshot)
    {
        return BuildScanWarningSummary(snapshot.Warnings);
    }

    private static string BuildScanWarningSummary(IReadOnlyList<ScanWarning> warnings)
    {
        var visibleWarnings = GetVisibleScanWarnings(warnings);
        if (visibleWarnings.Count == 0)
        {
            return string.Empty;
        }

        var preview = visibleWarnings
            .Take(2)
            .Select(warning => string.IsNullOrWhiteSpace(warning.AffectedPath)
                ? warning.Phase
                : $"{warning.Phase}：{GetWarningPreviewPath(warning.AffectedPath)}")
            .ToList();
        var previewText = preview.Count == 0 ? string.Empty : $" 已跳过示例：{string.Join("；", preview)}。";
        return $"扫描已完成，但有 {visibleWarnings.Count} 处受限目录或取证阶段已跳过，主列表仍可继续使用。{previewText}";
    }

    private static IReadOnlyList<ScanWarning> GetVisibleScanWarnings(IEnumerable<ScanWarning> warnings)
    {
        return warnings
            .Where(warning => warning.Severity != ScanWarningSeverity.Info)
            .ToList();
    }

    private static string GetWarningPreviewPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(leaf) ? path : leaf;
    }

    private static string BuildResidualWarningPreview(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return string.Empty;
        }

        var compact = warning.Replace("\r", " ").Replace("\n", " ").Trim();
        return compact.Length <= 72 ? compact : $"{compact[..69]}...";
    }

    private static string BuildResidualBreakdownText(CleanupRunResult runResult)
    {
        var parts = new List<string>();

        void AddPart(string label, int count)
        {
            if (count > 0)
            {
                parts.Add($"{label} {count} 项");
            }
        }

        AddPart("注册表", runResult.RemainingRegistryResidueCount);
        AddPart("快捷方式", runResult.RemainingShortcutResidueCount);
        AddPart("计划任务", runResult.RemainingTaskResidueCount);
        AddPart("服务", runResult.RemainingServiceResidueCount);
        AddPart("防火墙规则", runResult.RemainingFirewallResidueCount);
        AddPart("残留目录", runResult.RemainingDirectoryResidueCount);
        AddPart("残留文件", runResult.RemainingFileResidueCount);

        return string.Join("、", parts);
    }

    private static string BuildResultBannerToolTip(CleanupRunResult runResult)
    {
        var lines = new List<string>();
        var residualBreakdown = BuildResidualBreakdownText(runResult);

        lines.Add($"成功 {runResult.Results.Count(result => result.Status == "成功")} 项，部分成功 {runResult.Results.Count(result => result.Status == "部分成功")} 项，失败 {runResult.Results.Count(result => result.Status == "失败")} 项。");
        if (runResult.OfficialUninstallAttempted)
        {
            lines.Add($"官方卸载：成功 {runResult.OfficialUninstallSucceededCount} 项，失败 {runResult.OfficialUninstallFailedCount} 项。");
        }

        if (!string.IsNullOrWhiteSpace(residualBreakdown))
        {
            lines.Add($"剩余残留：{residualBreakdown}。");
        }

        if (runResult.BlockingProcesses.Count > 0)
        {
            lines.Add($"当前占用：{string.Join("、", runResult.BlockingProcesses.Take(6))}。");
        }

        if (runResult.FollowUpRecommendations.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("建议下一步：");
            foreach (var recommendation in runResult.FollowUpRecommendations.Take(6))
            {
                lines.Add($"- {recommendation}");
            }
        }

        return string.Join(Environment.NewLine, lines.Where(line => line is not null));
    }

    private void HideResultBanner()
    {
        _resultBannerPanel.Visible = false;
    }

    private void OpenLastRunLog()
    {
        if (string.IsNullOrWhiteSpace(_lastResultLogPath) || !File.Exists(_lastResultLogPath))
        {
            return;
        }

        ShellHelper.RevealPath(_lastResultLogPath);
    }

    private void ApplyBulkSelection(Func<CleanupSelectionRow, bool> selector)
    {
        foreach (var row in _allRows)
        {
            row.Selected = selector(row);
        }

        _grid.Refresh();
        UpdateSelectionSummary();
        UpdateActionStates();
    }

    private void ApplyInfrequentSelection(Func<InfrequentSoftwareEntry, bool> selector)
    {
        foreach (var row in _infrequentRows)
        {
            row.Selected = selector(row);
        }

        _infrequentGrid.Refresh();
        UpdateSelectionSummary();
        UpdateActionStates();
    }

    private IReadOnlyList<CleanupItem> CreateCleanupItemsFromInfrequentEntries(IEnumerable<InfrequentSoftwareEntry> entries)
    {
        return entries
            .DistinctBy(entry => entry.AppIdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new CleanupItem
            {
                Id = entry.Id,
                DriveName = entry.PrimaryDrive,
                Category = "长期未用软件",
                Name = entry.DisplayName,
                TypeDescription = entry.CanDeepDelete ? "长期未用软件，建议深度删除" : "长期未用软件，仅供查看",
                Path = entry.InstallRoot,
                IconSourcePath = entry.IconSourcePath,
                IconInstallRoot = entry.InstallRoot,
                NormalizedPath = entry.InstallRoot,
                RuleSource = "InfrequentSoftware",
                CandidateKind = CleanupCandidateKind.AppResidue,
                InstalledAppId = entry.DisplayName,
                AppIdentityKey = entry.AppIdentityKey,
                CompanionPaths = entry.CompanionPaths,
                IsApplicationRelated = true,
                DeepCleanupEligible = entry.CanDeepDelete,
                TargetKind = Directory.Exists(entry.InstallRoot) ? CleanupTargetKind.DirectoryTree : CleanupTargetKind.FilePermanent,
                Note = entry.UsageEvidenceText,
                ImpactText = entry.DeletionImpactText,
                ImpactSeverity = CleanupImpactSeverity.High,
                SizeBytes = entry.SizeBytes,
                SafeAuto = false,
                Recommended = entry.RecommendedForDeletion,
                WhitelistHintText = "如果这类软件你只是偶尔用，可以先加入白名单，避免后续持续提醒。"
            })
            .ToList();
    }

    private void OpenSelectedLocation()
    {
        if (_viewMode == MainViewMode.CDriveOverview)
        {
            var overviewEntry = GetCurrentOverviewEntry();
            if (overviewEntry is null)
            {
                return;
            }

            ShellHelper.RevealPath(overviewEntry.Path);
            return;
        }

        if (_viewMode == MainViewMode.InfrequentApps)
        {
            var infrequentEntry = GetCurrentInfrequentEntry();
            if (infrequentEntry is null)
            {
                return;
            }

            ShellHelper.RevealPath(infrequentEntry.InstallRoot);
            return;
        }

        var row = GetCurrentRow();
        if (row is null)
        {
            return;
        }

        OpenRowLocation(row);
    }

    private void OpenRowLocation(CleanupSelectionRow row)
    {
        if (row.Item.TargetKind == CleanupTargetKind.RecycleBin)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "shell:RecycleBinFolder",
                    UseShellExecute = true
                });
            }
            catch
            {
            }

            return;
        }

        ShellHelper.RevealPath(row.Path);
    }

    private CleanupSelectionRow? GetCurrentRow()
    {
        return _grid.CurrentRow?.DataBoundItem as CleanupSelectionRow
            ?? _visibleRows.FirstOrDefault();
    }

    private CDriveOverviewEntry? GetCurrentOverviewEntry()
    {
        return _overviewGrid.CurrentRow?.DataBoundItem as CDriveOverviewEntry
            ?? _visibleOverviewRows.FirstOrDefault();
    }

    private InfrequentSoftwareEntry? GetCurrentInfrequentEntry()
    {
        return _infrequentGrid.CurrentRow?.DataBoundItem as InfrequentSoftwareEntry
            ?? _visibleInfrequentRows.FirstOrDefault();
    }

    private void OpenScheduleSettings()
    {
        if (_readOnlyMode)
        {
            MessageBox.Show("当前为只读模式，后台自动清理设置已禁用。请重新以管理员身份启动后再修改。", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_activeScheduleSettingsDialog is { IsDisposed: false })
        {
            _activeScheduleSettingsDialog.Activate();
            _activeScheduleSettingsDialog.BringToFront();
            return;
        }

        var dialog = new ScheduleSettingsDialog(_context, _settingsService, _schedulerService, _settings);
        _activeScheduleSettingsDialog = dialog;
        dialog.FormClosed += async (_, _) =>
        {
            if (ReferenceEquals(_activeScheduleSettingsDialog, dialog))
            {
                _activeScheduleSettingsDialog = null;
            }

            if (!dialog.SettingsChanged)
            {
                UpdateScheduleStatus();
                return;
            }

            _settings = _settingsService.Load();
            UpdateScheduleStatus();
            await RefreshScanAsync();
        };
        dialog.Show(this);
    }

    private void OpenRegressionAudit()
    {
        if (_activeRegressionAuditDialog is { IsDisposed: false })
        {
            _activeRegressionAuditDialog.Activate();
            _activeRegressionAuditDialog.BringToFront();
            return;
        }

        var dialog = new RegressionAuditDialog(BuildRegressionAuditEntries(), _runtimeInfoLabel.Text);
        _activeRegressionAuditDialog = dialog;
        dialog.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_activeRegressionAuditDialog, dialog))
            {
                _activeRegressionAuditDialog = null;
            }
        };
        dialog.Show(this);
    }

    private void UpdateRegressionAuditMenuText()
    {
        var entries = BuildRegressionAuditEntries();
        var partial = entries.Count(entry => entry.Status == RegressionAuditStatus.Partial);
        var followUp = entries.Count(entry => entry.Status == RegressionAuditStatus.FollowUp);

        _regressionAuditMenuItem.Text = partial switch
        {
            > 0 => $"问题结案清单（仍有 {partial} 项核心继续推进）",
            0 when followUp > 0 => $"问题结案清单（核心已收口，余 {followUp} 项持续优化）",
            _ => "问题结案清单（已全部收口）"
        };
    }

    private IReadOnlyList<RegressionAuditEntry> BuildRegressionAuditEntries()
    {
        return
        [
            new RegressionAuditEntry
            {
                Order = 10,
                Category = "启动与扫描",
                Title = "先显示 C盘，再补 D/E",
                Status = RegressionAuditStatus.Resolved,
                ConclusionText = "当前版本会先给出 C盘首批结果，再后台增量补全其它固定盘；用户不用等全盘扫完才看到主列表。",
                NextActionText = "这个问题按当前版本已结案，后面只继续做更快的冷启动和更多缓存命中。",
                NotesText = "对应的 C盘建议 首批数据也会跟着提前出现，不再必须等到扫描进度很后面。"
            },
            new RegressionAuditEntry
            {
                Order = 20,
                Category = "C盘建议",
                Title = "C盘建议早期开窗空白",
                Status = RegressionAuditStatus.Resolved,
                ConclusionText = "打开 C盘建议 时，会优先吃主快照里的首批建议；如果主扫描还没跑到那一步，窗口也会自己预热一批轻量建议，不再长期空表。",
                NextActionText = "这个问题按当前版本已结案，后面继续优化首批内容出现得更早、更稳定。",
                NotesText = "这一项就是你前面重点盯过的“要同步刷新、别等到 70% 才看得到”，现在主窗口和 C盘建议 都会同步增量刷新。"
            },
            new RegressionAuditEntry
            {
                Order = 30,
                Category = "C盘建议",
                Title = "C盘建议勾选不上、单击就弹文件夹",
                Status = RegressionAuditStatus.Resolved,
                ConclusionText = "当前交互已改成：单击勾选列只切换勾选状态，单击其它列只选中行，双击非勾选列才定位文件夹。",
                NextActionText = "这个问题按当前版本已结案，后续只做回归验证，防止以后改表格时再回退。",
                NotesText = "你前面确认过“可以了”，这一条现在被单独记录进结案清单，后面不会再把它当成没做。"
            },
            new RegressionAuditEntry
            {
                Order = 40,
                Category = "扫描降级",
                Title = "受限目录导致启动报错或整页阻断",
                Status = RegressionAuditStatus.Resolved,
                ConclusionText = "扫描遇到拒绝访问、快捷方式取证失败或部分目录不可读时，默认会走非阻断黄条和日志记录，不再用一个目录失败拖垮整轮主界面。",
                NextActionText = "这个问题按当前版本已结案，后面只继续补日志可读性和更多阶段化提示。",
                NotesText = "如果真的是整轮扫描都不可用，才允许走阻断提示；普通权限问题会写日志并继续显示已拿到的结果。"
            },
            new RegressionAuditEntry
            {
                Order = 50,
                Category = "说明文案",
                Title = "删除后果太抽象，小白看不懂",
                Status = RegressionAuditStatus.Resolved,
                ConclusionText = "当前版本已把高风险、中风险、常见缓存、安装包、大文件、大目录和 C盘建议 删除说明统一改成更直白的中文，并补上“适合现在删 / 先别删”的判断句。",
                NextActionText = "这个问题当前已完成第一轮收口；后面如果你再指出某一类项目解释不够人话，就继续专项补那一类。",
                NotesText = "重点覆盖了 Windows.old、回收站、错误报告、网易云/腾讯视频/微信/VS Code/Cursor/浏览器缓存、安装包、重复文件和大目录。"
            },
            new RegressionAuditEntry
            {
                Order = 60,
                Category = "删除与刷新",
                Title = "删除后仍显示、被占用项处理不干净",
                Status = RegressionAuditStatus.Partial,
                ConclusionText = "成功删除的项现在会先按路径和同路径旧行一起从当前列表移除，并同步从内存快照和 C盘建议 数据源里拿掉，不再非得等整轮重扫后才像是删掉了；被占用、被立即重建或只删掉一部分的项，会保留在列表里并更新为更具体的剩余大小、剩余条目和占用说明。",
                NextActionText = "继续补强高频缓存和复杂应用目录的占用关闭适配，以及少数需要更激进残留复查的家族案例。",
                NotesText = "“关闭占用并重试”现在会在尝试关闭后重新探测真实占用，再自动重试真正已经释放的项目；应用相关项重试时也会继续带上深度残留清理。"
            },
            new RegressionAuditEntry
            {
                Order = 70,
                Category = "长期未用软件",
                Title = "按盘显示长期未用软件、显示未用天数",
                Status = RegressionAuditStatus.FollowUp,
                ConclusionText = "主界面第三视图已经上线，会按应用身份聚合显示最近使用时间、未用天数、可信度、使用依据和建议动作，并默认高亮 60 天以上未用的第三方软件。这一轮又补了两层准确率保护：一是正在运行的软件会单独识别成“当前正在用”，不再和久未使用混在一起；二是带 .git / .sln / package.json / node_modules 这类开发工作区标记的目录，会尽量从便携软件发现里排掉，减少把源码目录误认成软件。",
                NextActionText = "继续做证据准确率和保护边界回归，重点复查少数只剩低可信估算的软件，避免把“很久没更新”误读成“很久没使用”。",
                NotesText = "当前证据来源包含运行中进程、UserAssist、快捷方式访问痕迹和安装目录活动时间；低可信估算和“未检测到可靠记录”的项，都会保留在提醒/确认层，不会再直接混进建议删除。"
            },
            new RegressionAuditEntry
            {
                Order = 80,
                Category = "深度删除",
                Title = "删除后像 Geek 一样尽量重装不冲突",
                Status = RegressionAuditStatus.Partial,
                ConclusionText = "当前深删链路已经支持：优先官方卸载，再做强关联注册表、启动项、快捷方式、计划任务、防火墙规则和已识别应用残留清理。这一轮继续补强了官方卸载命中率：除了原来的 UninstallString，现在也会优先吃 QuietUninstallString，并把 ModifyPath、InstallSource、卸载器所在目录一起纳入匹配和静默卸载候选；同时把家族级残留目录探测再往前推了一步，像 JetBrains 的版本目录、Clash 的历史目录名、GitHub Desktop、VS Code / Cursor 的用户侧目录，以及 Tencent / NetEase 这类厂商根目录下面的产品子目录，都更容易一起复查并带走。任务、服务和防火墙规则这块也从原来的宽松包含，收紧成了“路径优先 + 强别名匹配”，专门避免 `Code` 这类短名误扫到系统项。",
                NextActionText = "继续补家族级残留目录与服务/任务特征验证，尤其是 Tencent、NetEase、JetBrains、Git、VS Code/Cursor、Clash 这些本机常见样本。",
                NotesText = "这块已经不是第一版“只删文件”，但还没到你要求的“所有软件都像没装过一样”那个终局。共享运行库、驱动服务、系统 COM 之类仍保持保护边界；QuietUninstallString、JetBrains 版本目录、Clash 历史目录名、VS Code / Cursor 用户侧目录、厂商级产品子目录，以及任务/服务/防火墙里的短名误判收口，这一轮都已经开始覆盖。结果反馈这边也开始按“注册表 / 快捷方式 / 计划任务 / 服务 / 防火墙 / 目录 / 文件”分类型展示剩余残留，不再只给一串笼统警告；官方卸载器的实际执行结果、失败回退情况，以及每一轮更具体的“建议下一步”，也会写进日志和结果摘要里。"
            },
            new RegressionAuditEntry
            {
                Order = 90,
                Category = "自动迁移",
                Title = "把 C盘软件自动迁到别的盘，还尽量不影响原快捷方式",
                Status = RegressionAuditStatus.Partial,
                ConclusionText = "当前已经有目录迁移 + junction 兼容方案，也有一批家族适配器；这轮继续把一部分满足安全条件的标准安装型软件，从“仅引导迁移”提升到了“可自动迁移”，并同步改了说明文案，明确告诉用户这是“通用兼容迁移”，不是直接裸搬目录。",
                NextActionText = "继续补迁后验证、附属目录识别和家族级适配，把 Google、搜狗、FlashCenter 这类已放开的标准安装型样本先逐个实测打磨稳，再逐步扩到更多非驱动型软件。",
                NotesText = "这轮仍然保留保护边界。像 Windows、Common Files、VC++、.NET、WSL、驱动/服务/OEM/反作弊、安装缓存和明显运行库目录，默认不会放进普通自动迁移。"
            },
            new RegressionAuditEntry
            {
                Order = 100,
                Category = "保护边界",
                Title = "系统文件、共享运行库、驱动组件为什么不放进普通删除/迁移流",
                Status = RegressionAuditStatus.ProtectedRule,
                ConclusionText = "这不是漏扫，而是故意保护。像 Windows、Common Files、VC++、.NET、WSL、pagefile.sys、hiberfil.sys、驱动/服务/OEM/反作弊组件，当前版本默认只展示或直接排除，不提供普通自动深删和自动迁移。",
                NextActionText = "除非后面做了专门适配并单独验证安全性，否则这一条保护规则会继续保留。",
                NotesText = "这部分如果为了“看起来全能”就贸然放开，最容易把电脑搞出系统报错、驱动异常、升级修复失败和重装冲突。"
            },
            new RegressionAuditEntry
            {
                Order = 110,
                Category = "权限与部署",
                Title = "第一次授权后尽量别重复弹 UAC",
                Status = RegressionAuditStatus.FollowUp,
                ConclusionText = "当前版本已经支持：首次授权后注册高权限启动任务；后续启动时会先校验任务是否真的对齐当前 EXE，再决定是否复用，不会再因为“任务存在但其实指到旧版”就误跑旧版本。桌面快捷方式和安装目录入口也会在启动时一起自检、自修复。",
                NextActionText = "继续观察个别机器上的系统策略差异，例如计划任务被安全软件清掉、用户主动关闭桌面快捷方式创建、以及极端权限受限环境下只能保留只读模式时的提示细节。",
                NotesText = "这一步已经把“高权限任务存在但未对齐仍被误调用”“桌面入口和安装文件夹入口不同步”这两类关键问题收掉了；安装文件夹里现在也会保留一个跟当前版本同步的启动入口。"
            },
            new RegressionAuditEntry
            {
                Order = 120,
                Category = "界面细节",
                Title = "字体轻微裁切、全窗口 DPI 收口",
                Status = RegressionAuditStatus.FollowUp,
                ConclusionText = "这一轮把 DPI 收口又往前推了一步：除了额外安全边距和更保守的文本测量，又把后台任务卡片的标题/进度区改成按实际文本动态分配宽度，并补上完整悬浮提示。这样长一点的阶段文案和预计剩余时间，不容易再把标题区挤得轻微裁字。",
                NextActionText = "继续做剩余边角回归，重点盯个别非常长的动态说明文案，以及极端窄窗口下的最后少数换行点。",
                NotesText = "这项已经不是早期那种“按钮少半个字”的状态了；当前归到“主体完成”，后面主要等你在真实 100% / 125% / 150% 缩放下再跑一轮人工验收。"
            },
            new RegressionAuditEntry
            {
                Order = 130,
                Category = "滚动与性能",
                Title = "横向滚动、后台任务不卡界面",
                Status = RegressionAuditStatus.FollowUp,
                ConclusionText = "这一轮继续把横向滚动和任务反馈收口：除了全局横向滚动消息路由，还把进度窗改成即使后台暂时没有新上报，也会持续刷新已用时间、预计剩余时间和已处理字节。这样长任务看起来不会像“窗口出来了，但内容停住了”。",
                NextActionText = "继续盯个别极端大任务的磁盘吞吐和后台结果条联动，把最后少数仍可能感到“内容更新速度没跟上”的点再压下去。",
                NotesText = "这块已经不是早期那种“一运行后台任务整个界面都跟着抖”的状态了；当前归到“主体完成”，后面继续看你真实机器上连续多任务并发和触摸板手势下的体感。"
            },
            new RegressionAuditEntry
            {
                Order = 140,
                Category = "界面细节",
                Title = "给软件列表补程序图标，对标 Geek 的识别感",
                Status = RegressionAuditStatus.FollowUp,
                ConclusionText = "长期未用软件视图、C盘建议窗口，以及主窗口里的 清理候选 / C盘总览 都已经补上了左侧程序图标。这一轮继续把图标来源往前补：会优先走卸载项 DisplayIcon、安装根目录、验证用 EXE、附属目录和主程序可执行文件，再在后台静默替换成对应程序图标。",
                NextActionText = "下一步继续补强少数仍只能落回通用图标的纯聚合项，以及个别没有稳定安装信息的软件目录。",
                NotesText = "图标读取继续走后台缓存，不为了视觉识别感去牺牲首屏打开速度；这轮重点把目录型应用和迁移项的通用图标比例再压下去。"
            }
        ];
    }

    private Task OpenCDriveAdviceAsync()
    {
        if (_activeCDriveSuggestionDialog is { IsDisposed: false })
        {
            _activeCDriveSuggestionDialog.ApplySnapshot(BuildInitialCDriveAdviceSnapshot());
            PrimeCDriveAdviceDialogAsync(_activeCDriveSuggestionDialog);
            _activeCDriveSuggestionDialog.Activate();
            _activeCDriveSuggestionDialog.BringToFront();
            return Task.CompletedTask;
        }

        var candidates = _snapshot?.MigrationCandidates ?? [];
        var dialog = new CDriveSuggestionDialog(_settings.AppName, candidates, _migrationService, _readOnlyMode);
        _activeCDriveSuggestionDialog = dialog;
        dialog.ApplySnapshot(BuildInitialCDriveAdviceSnapshot());
        PrimeCDriveAdviceDialogAsync(dialog);
        dialog.FormClosed += async (_, _) =>
        {
            if (ReferenceEquals(_activeCDriveSuggestionDialog, dialog))
            {
                _activeCDriveSuggestionDialog = null;
            }

            if (dialog.DeleteCandidates.Count > 0)
            {
                var cleanupItems = CreateCleanupItemsFromMigrationCandidates(dialog.DeleteCandidates);
                if (cleanupItems.Count > 0)
                {
                    await StartCleanupFlowAsync(cleanupItems, safeOnly: false);
                }

                return;
            }

            if (!dialog.RefreshRequired)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(dialog.ResultLogPath))
            {
                _lastResultLogPath = dialog.ResultLogPath;
            }

            QueueScanRefresh(userInitiated: false);
            if (dialog.LastRunResult is not null)
            {
                ShowMigrationResultBanner(dialog.LastRunResult);
            }
        };
        dialog.Show(this);
        return Task.CompletedTask;
    }

    private ScanSnapshot BuildInitialCDriveAdviceSnapshot()
    {
        if (_snapshot is not null && (_snapshot.LoadedDrives.Count > 0 || _snapshot.MigrationCandidates.Count > 0 || _snapshot.IsPartialResult))
        {
            return _snapshot;
        }

        if (_operationManager.HasActiveJob(OperationJobKind.Scan))
        {
            return new ScanSnapshot
            {
                CleanupItems = _snapshot?.CleanupItems ?? [],
                CDriveOverviewEntries = _snapshot?.CDriveOverviewEntries ?? [],
                InfrequentSoftwareEntries = _snapshot?.InfrequentSoftwareEntries ?? [],
                MigrationCandidates = _snapshot?.MigrationCandidates ?? [],
                LoadedDrives = _snapshot?.LoadedDrives ?? [],
                PendingDrives = _snapshot?.PendingDrives ?? [],
                IsPartialResult = true,
                PhaseLabel = "正在等待首批 C盘建议结果，会自动补齐",
                Warnings = _snapshot?.Warnings ?? [],
                SafeDefaultSelectionIds = _snapshot?.SafeDefaultSelectionIds ?? new HashSet<Guid>(),
                AdditionalReviewSelectionIds = _snapshot?.AdditionalReviewSelectionIds ?? new HashSet<Guid>(),
                InstalledAppMappings = _snapshot?.InstalledAppMappings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                DuplicateGroups = _snapshot?.DuplicateGroups ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                WhitelistHints = _snapshot?.WhitelistHints ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
        }

        return _snapshot ?? new ScanSnapshot
        {
            CleanupItems = [],
            CDriveOverviewEntries = [],
            InfrequentSoftwareEntries = [],
            MigrationCandidates = [],
            LoadedDrives = [],
            PendingDrives = [],
            IsPartialResult = false,
            PhaseLabel = string.Empty,
            Warnings = [],
            SafeDefaultSelectionIds = new HashSet<Guid>(),
            AdditionalReviewSelectionIds = new HashSet<Guid>(),
            InstalledAppMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DuplicateGroups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
            WhitelistHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private void PrimeCDriveAdviceDialogAsync(CDriveSuggestionDialog dialog)
    {
        if ((_snapshot?.MigrationCandidates.Count ?? 0) > 0 || !_operationManager.HasActiveJob(OperationJobKind.Scan))
        {
            return;
        }

        _ = Task.Run(() => _scanService.ScanStartupCAdviceSeedSnapshot(_settings))
            .ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    return;
                }

                OnUiThread(() =>
                {
                    if (!ReferenceEquals(_activeCDriveSuggestionDialog, dialog) || dialog.IsDisposed)
                    {
                        return;
                    }

                    if ((_snapshot?.MigrationCandidates.Count ?? 0) > 0)
                    {
                        return;
                    }

                    dialog.ApplySnapshot(task.Result);
                });
            }, TaskScheduler.Default);
    }

    private IReadOnlyList<CleanupItem> CreateCleanupItemsFromMigrationCandidates(IEnumerable<MigrationCandidate> candidates)
    {
        return candidates
            .DistinctBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(candidate =>
            {
                var category = candidate.Category == "用户数据目录" ? "C盘建议删除" : candidate.Category;
                var impactSeverity = candidate.Category switch
                {
                    "用户数据目录" => CleanupImpactSeverity.High,
                    "未知大目录" => CleanupImpactSeverity.High,
                    "应用缓存/数据" => CleanupImpactSeverity.Medium,
                    _ => CleanupImpactSeverity.High
                };

                return new CleanupItem
                {
                    Id = candidate.Id,
                    DriveName = "C",
                    Category = category,
                    Name = candidate.Name,
                    TypeDescription = "C盘建议里的目录删除",
                    Path = candidate.SourcePath,
                    IconSourcePath = candidate.IconSourcePath,
                    IconInstallRoot = candidate.IconInstallRoot,
                    NormalizedPath = candidate.SourcePath,
                    RuleSource = "CDriveAdviceDelete",
                    CandidateKind = CleanupCandidateKind.LargeDirectory,
                    InstalledAppId = candidate.InstalledAppName,
                    AppIdentityKey = candidate.AdapterKey,
                    CompanionPaths = candidate.CompanionPaths,
                    IsApplicationRelated = !string.IsNullOrWhiteSpace(candidate.InstalledAppName),
                    DeepCleanupEligible = !string.IsNullOrWhiteSpace(candidate.InstalledAppName),
                    TargetKind = CleanupTargetKind.DirectoryTree,
                    Note = $"这是从 C盘建议 里手动发起的删除。{candidate.RecommendationText}",
                    ImpactText = BuildCDriveAdviceImpactText(candidate),
                    ImpactSeverity = impactSeverity,
                    SizeBytes = candidate.SizeBytes,
                    SafeAuto = false,
                    Recommended = false,
                    WhitelistHintText = "如果这是你经常要看的目录，可以先加入白名单，让它暂时不再显示。"
                };
            })
            .ToList();
    }

    private static string BuildCDriveAdviceImpactText(MigrationCandidate candidate)
    {
        var displayName = string.IsNullOrWhiteSpace(candidate.InstalledAppName) ? candidate.Name : candidate.InstalledAppName;
        return candidate.Category switch
        {
            "第三方已安装应用" => $"删除后：会整目录删掉“{displayName}”的安装文件。最常见的结果是软件打不开、桌面快捷方式失效、后续升级或卸载找不到原文件。适合现在删：你已经决定彻底不用它，并准备接受之后重新安装。先别删：你还希望现在的快捷方式、更新流程和现有安装继续可用。更稳妥：先正常卸载，再重装到 D/E。",
            "应用缓存/数据" => $"删除后：会整目录删掉“{candidate.Name}”里的缓存、日志、下载包或公共数据。轻则软件下次启动重新生成缓存、第一次打开变慢；重则离线资源、下载内容、部分设置或更新包一起丢失。适合现在删：你确认这里主要只是缓存，且不介意重新生成。先别删：你还要保留离线内容，或者对应软件现在还在运行。",
            "用户数据目录" => $"删除后：会整目录删掉“{candidate.Name}”及其里面的个人文件。照片、视频、文档、安装包或便携工具都会一起没了。适合现在删：你确认这里都是不要的旧内容。先别删：里面还混着日常在用的资料。若你只是想腾空间，更建议先迁移，或先打开后只处理大的子目录。",
            _ => $"删除后：会整目录删掉“{candidate.Name}”及其内容，里面所有文件都会一起消失。适合现在删：你确认整个目录都不要了。先别删：里面还有在用的程序、项目、视频、安装包或备份。"
        };
    }

    private IReadOnlyList<MigrationCandidate> GetSelectedMigrationCandidatesFromOverview()
    {
        if (_snapshot is null)
        {
            return [];
        }

        var byId = _snapshot.MigrationCandidates.ToDictionary(candidate => candidate.Id);
        var selected = new List<MigrationCandidate>();
        foreach (var entry in _overviewRows.Where(entry => entry.Selected && entry.SelectionEnabled))
        {
            if (byId.TryGetValue(entry.Id, out var candidate))
            {
                selected.Add(candidate);
            }
        }

        return selected;
    }

    private IReadOnlyList<MigrationCandidate> GetSelectedDeletionCandidatesFromOverview()
    {
        if (_snapshot is null)
        {
            return [];
        }

        var byId = _snapshot.MigrationCandidates.ToDictionary(candidate => candidate.Id);
        return _overviewRows
            .Where(entry => entry.Selected && entry.CanDelete)
            .Where(entry => byId.ContainsKey(entry.Id))
            .Select(entry => byId[entry.Id])
            .ToList();
    }

    private int GetWhitelistSelectionCount()
    {
        if (_viewMode == MainViewMode.CDriveOverview)
        {
            return _overviewRows.Count(entry => entry.Selected && !string.IsNullOrWhiteSpace(entry.Path));
        }

        if (_viewMode == MainViewMode.InfrequentApps)
        {
            return _infrequentRows.Count(entry => entry.Selected && !string.IsNullOrWhiteSpace(entry.AppIdentityKey));
        }

        return _allRows.Count(row => row.Selected && !string.IsNullOrWhiteSpace(row.Path));
    }

    private void AddCurrentSelectionToWhitelist()
    {
        if (_viewMode == MainViewMode.InfrequentApps)
        {
            var selectedApps = _infrequentRows
                .Where(entry => entry.Selected && !string.IsNullOrWhiteSpace(entry.AppIdentityKey))
                .ToList();
            if (selectedApps.Count == 0)
            {
                var currentEntry = GetCurrentInfrequentEntry();
                if (currentEntry is not null)
                {
                    selectedApps.Add(currentEntry);
                }
            }

            if (selectedApps.Count == 0)
            {
                return;
            }

            foreach (var app in selectedApps)
            {
                _settings.WhitelistedAppIdentityKeys.Add(app.AppIdentityKey);
                if (!string.IsNullOrWhiteSpace(app.InstallRoot))
                {
                    _settings.WhitelistedPaths.Add(app.InstallRoot);
                }
            }

            _settings.WhitelistedAppIdentityKeys = _settings.WhitelistedAppIdentityKeys
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _settings.WhitelistedPaths = _settings.WhitelistedPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _settingsService.Save(_settings);
            SetStatusMessage($"已把 {selectedApps.Count} 个软件加入白名单");
            QueueScanRefresh(userInitiated: false);
            return;
        }

        var selectedPaths = _viewMode == MainViewMode.CDriveOverview
            ? _overviewRows.Where(entry => entry.Selected && !string.IsNullOrWhiteSpace(entry.Path)).Select(entry => entry.Path).ToList()
            : _allRows.Where(row => row.Selected && !string.IsNullOrWhiteSpace(row.Path)).Select(row => row.Path).ToList();

        if (selectedPaths.Count == 0)
        {
            var currentPath = _viewMode == MainViewMode.CDriveOverview
                ? GetCurrentOverviewEntry()?.Path
                : GetCurrentRow()?.Path;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                selectedPaths.Add(currentPath);
            }
        }

        if (selectedPaths.Count == 0)
        {
            return;
        }

        foreach (var path in selectedPaths)
        {
            _settings.WhitelistedPaths.Add(path);
        }

        _settings.WhitelistedPaths = _settings.WhitelistedPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _settingsService.Save(_settings);
        HideWhitelistedPaths(selectedPaths);
        SetStatusMessage($"已把 {selectedPaths.Count} 个路径加入白名单");
        ApplyCurrentView();
        QueueScanRefresh(userInitiated: false);
    }

    private void HideWhitelistedPaths(IEnumerable<string> paths)
    {
        var normalized = paths
            .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _allRows.Where(row => normalized.Contains(row.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))).ToList())
        {
            row.PropertyChanged -= CleanupSelectionRowChanged;
            _allRows.Remove(row);
        }

        _overviewRows.RemoveAll(entry => normalized.Contains(entry.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
    }

    private async Task StartMigrationFlowAsync(IReadOnlyCollection<MigrationCandidate> candidates)
    {
        if (_isBusy)
        {
            return;
        }

        if (_readOnlyMode)
        {
            MessageBox.Show("当前处于只读模式。请重新以管理员身份启动后再执行迁移。", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (candidates.Count == 0)
        {
            MessageBox.Show("当前没有勾选可自动迁移的目录。只有标成“可自动迁移”的项才能直接搬到 D/E。", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        MigrationPlan plan;
        try
        {
            plan = await Task.Run(() => _migrationService.BuildPlan(candidates));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"生成迁移计划失败：{ex.Message}", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            $"将把 {plan.TotalCount} 个目录迁到 {plan.TargetDriveName} 盘的“{plan.TargetRoot}”。\r\n\r\n只会处理标成“可自动迁移”的目录。迁移后会在原路径保留兼容联接，旧快捷方式和常见启动方式通常还能继续用。\r\n\r\n是否开始迁移？",
            _settings.AppName,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (confirmation != DialogResult.OK)
        {
            return;
        }

        QueueMigrationExecution(candidates, plan);
        await Task.CompletedTask;
    }

    private void ShowMigrationResultBanner(MigrationRunResult result)
    {
        _lastResultLogPath = result.LogPath;
        _resultBannerLabel.Text = $"迁移完成：成功 {result.SuccessCount} 项，失败 {result.FailedCount} 项，已迁出 {result.MigratedBytesText}，实际搬运 {result.MigratedPathCount} 处目录，修正快捷方式 {result.UpdatedShortcutCount} 个。原路径会保留兼容联接，方便继续像以前一样打开。"
            + (result.RolledBackCount > 0 ? $" 其中有 {result.RolledBackCount} 项已自动回滚到原位置。" : string.Empty);
        _resultBannerLink.Visible = !string.IsNullOrWhiteSpace(result.LogPath) && File.Exists(result.LogPath);
        _retryBlockedLink.Visible = false;
        _resultBannerPanel.BackColor = result.FailedCount > 0 ? Color.FromArgb(255, 247, 231) : Color.FromArgb(235, 248, 241);
        _resultBannerPanel.Visible = true;
        SetStatusMessage("迁移完成");
    }

    private async Task RetryBlockedRowsAsync()
    {
        if (_isBusy || _readOnlyMode)
        {
            return;
        }

        var targetRows = _allRows
            .Where(row => row.RetrySuggested || _lastRetrySuggestedItemIds.Contains(row.Item.Id))
            .ToList();
        if (targetRows.Count == 0)
        {
            SetStatusMessage("当前没有需要关闭占用后重试的项目。");
            return;
        }

        try
        {
            SetBusy(true, "正在尝试关闭占用程序并重试...");
            var probes = await Task.Run(() => targetRows.ToDictionary(row => row.Item.Id, row => _occupancyProbeService.Probe(row.Path)));
            var closeResult = await Task.Run(() =>
            {
                var closeAttempt = _occupancyProbeService.TryCloseProcesses(
                    probes.Values.SelectMany(result => result.BlockingProcesses),
                    TimeSpan.FromSeconds(8));
                Thread.Sleep(350);
                return closeAttempt;
            });

            var refreshedProbes = await Task.Run(() => targetRows.ToDictionary(row => row.Item.Id, row => _occupancyProbeService.Probe(row.Path)));
            var rerunRows = new List<CleanupSelectionRow>();
            var stillBlockedRows = new List<(CleanupSelectionRow Row, IReadOnlyList<string> ProcessNames)>();

            foreach (var row in targetRows)
            {
                if (!refreshedProbes.TryGetValue(row.Item.Id, out var probe) || probe.BlockingProcesses.Count == 0)
                {
                    rerunRows.Add(row);
                    continue;
                }

                var remainingForRow = probe.BlockingProcesses
                    .Select(process => process.DisplayName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (remainingForRow.Count == 0)
                {
                    rerunRows.Add(row);
                    continue;
                }

                stillBlockedRows.Add((row, remainingForRow));
                row.ApplyRunResult(new CleanupResult
                {
                    ItemId = row.Item.Id,
                    Name = row.Item.Name,
                    NormalizedPath = row.Item.NormalizedPath,
                    TargetKind = row.Item.TargetKind,
                    OriginalBytes = row.Item.SizeBytes,
                    RemainingBytes = row.EffectiveSizeBytes,
                    Path = row.Path,
                    ExistsAfter = true,
                    BlockingProcessNames = remainingForRow,
                    RetrySuggested = true,
                    Status = "失败",
                    Message = $"仍被 {string.Join("、", remainingForRow.Take(3))} 占用。请先把对应程序和后台进程都关掉，再重试。"
                });
            }

            if (rerunRows.Count > 0)
            {
                var rerunItems = rerunRows.Select(CreateEffectiveCleanupItem).ToList();
                var includeResidueCleanup = rerunItems.Any(item => item.IsApplicationRelated || item.DeepCleanupEligible);
                QueueCleanupExecution(rerunItems, includeResidueCleanup, safeOnly: false, customTitle: "关闭占用后重试");
                SetStatusMessage($"已释放 {closeResult.ClosedProcesses.Count} 个占用程序，正在自动重试 {rerunRows.Count} 项。");
            }

            if (stillBlockedRows.Count > 0)
            {
                var processNames = string.Join("、", stillBlockedRows
                    .SelectMany(entry => entry.ProcessNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                if (rerunRows.Count == 0)
                {
                    SetStatusMessage($"仍有后台程序未退出：{processNames}");
                }
            }
            else if (rerunRows.Count == 0)
            {
                SetStatusMessage("这次没有可继续重试的项目。");
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyStartupQuickSnapshot(ScanSnapshot snapshot)
    {
        ReplaceSnapshot(snapshot);
        UpdateDriveSummary();
        UpdateDriveTabs();
        UpdateScheduleStatus();

        if (!_preserveStartupView && _viewMode != MainViewMode.CleanupCandidates && snapshot.CleanupItems.Count > 0)
        {
            _deferredStartupViewMode = _viewMode;
            _viewMode = MainViewMode.CleanupCandidates;
            EnsureActiveDriveSelection();
            ConfigureFilterControlsForMode();
            UpdateViewModeButtons();
            ApplyCurrentView();
            SetStatusMessage("已先显示 C盘候选，当前视图内容正在继续分析中");
            return;
        }

        SetStatusMessage(string.IsNullOrWhiteSpace(snapshot.PhaseLabel)
            ? "已先显示 C盘候选，正在补全当前视图和其它盘"
            : snapshot.PhaseLabel);
    }

    private void RestoreDeferredStartupViewIfReady(ScanSnapshot snapshot)
    {
        if (_deferredStartupViewMode is null)
        {
            return;
        }

        var targetView = _deferredStartupViewMode.Value;
        var canRestore = targetView switch
        {
            MainViewMode.CDriveOverview => snapshot.CDriveOverviewEntries.Count > 0,
            MainViewMode.InfrequentApps => snapshot.InfrequentSoftwareEntries.Count > 0,
            _ => true
        };

        if (!canRestore)
        {
            return;
        }

        _viewMode = targetView;
        _deferredStartupViewMode = null;
        EnsureActiveDriveSelection();
        ConfigureFilterControlsForMode();
        UpdateViewModeButtons();
        ApplyCurrentView();
    }

    private void QueueScanRefresh(bool userInitiated)
    {
        if (_operationManager.HasActiveJob(OperationJobKind.Scan))
        {
            if (userInitiated)
            {
                SetStatusMessage("后台已经有一个扫描任务正在运行。");
            }

            UpdateActionStates();
            return;
        }

        _operationManager.Enqueue(
            OperationJobKind.Scan,
            userInitiated ? "手动重新扫描" : "启动后后台刷新",
            ["scan:all"],
            (progress, _) =>
            {
                progress.Report(new OperationProgress
                {
                    Percent = 2,
                    Phase = "准备扫描",
                    Message = "正在准备先显示 C盘候选",
                    IsIndeterminate = false,
                    JobScope = "固定盘扫描"
                });

                var cCleanupSnapshot = _scanService.ScanStartupCCleanupSnapshot(_settings, progress, 4, 10, "固定盘扫描");
                OnUiThread(() => ApplyStartupQuickSnapshot(cCleanupSnapshot));

                progress.Report(new OperationProgress
                {
                    Percent = 12,
                    Phase = "第一批 C盘建议",
                    Message = "已显示 C盘候选，正在补全第一批 C盘建议",
                    IsIndeterminate = false,
                    JobScope = "固定盘扫描"
                });

                var cQuickSnapshot = _scanService.ScanStartupCQuickSnapshot(_settings, progress, 12, 22, "固定盘扫描");
                OnUiThread(() =>
                {
                    ReplaceSnapshot(cQuickSnapshot);
                    RestoreDeferredStartupViewIfReady(cQuickSnapshot);
                    UpdateDriveSummary();
                    UpdateDriveTabs();
                    UpdateScheduleStatus();
                    SetStatusMessage(string.IsNullOrWhiteSpace(cQuickSnapshot.PhaseLabel)
                        ? "已先显示 C盘候选和第一批 C盘建议，正在补全完整 C盘分析"
                        : cQuickSnapshot.PhaseLabel);
                });

                progress.Report(new OperationProgress
                {
                    Percent = 24,
                    Phase = "C盘完整结果",
                    Message = "基础结果已显示，正在补全 C盘完整分析",
                    IsIndeterminate = false,
                    JobScope = "固定盘扫描"
                });

                var cDriveSnapshot = _scanService.ScanSnapshot(_settings, ScanDriveScope.SystemDriveOnly, progress, 26, 58, "固定盘扫描");
                OnUiThread(() =>
                {
                    ReplaceSnapshot(cDriveSnapshot);
                    RestoreDeferredStartupViewIfReady(cDriveSnapshot);
                    UpdateDriveSummary();
                    UpdateDriveTabs();
                    UpdateScheduleStatus();
                    SetStatusMessage(string.IsNullOrWhiteSpace(cDriveSnapshot.PhaseLabel)
                        ? "已先显示 C盘，正在补全其它盘"
                        : cDriveSnapshot.PhaseLabel);
                });

                progress.Report(new OperationProgress
                {
                    Percent = 60,
                    Phase = "补全其它盘",
                    Message = "C盘结果已显示，正在补全 D/E 等其它固定盘",
                    IsIndeterminate = false,
                    JobScope = "固定盘扫描"
                });

                return _scanService.ScanSnapshot(_settings, ScanDriveScope.AllFixedDrives, progress, 62, 96, "固定盘扫描");
            },
            warningCountSelector: snapshot => GetVisibleScanWarnings(snapshot.Warnings).Count,
            warningSummarySelector: BuildScanWarningSummary,
            completed: snapshot =>
            {
                OnUiThread(() =>
                {
                    ReplaceSnapshot(snapshot);
                    RestoreDeferredStartupViewIfReady(snapshot);
                    _snapshotCacheService.Save(_settings, snapshot);
                    _settingsService.Save(_settings);
                    UpdateDriveSummary();
                    UpdateDriveTabs();
                    UpdateScheduleStatus();
                    var visibleWarningCount = GetVisibleScanWarnings(snapshot.Warnings).Count;
                    var informationalWarningCount = snapshot.Warnings.Count - visibleWarningCount;
                    var message = visibleWarningCount > 0
                        ? $"扫描完成：发现 {snapshot.CleanupItems.Count} 个候选项目，{visibleWarningCount} 处已跳过受限目录或取证阶段。"
                        : informationalWarningCount > 0
                            ? $"扫描完成：发现 {snapshot.CleanupItems.Count} 个候选项目，并静默跳过了 {informationalWarningCount} 处常见受限目录。"
                        : $"扫描完成：发现 {snapshot.CleanupItems.Count} 个候选项目，并更新了 C 盘总览";
                    SetStatusMessage(message);
                });
            },
            failed: ex =>
            {
                OnUiThread(() =>
                {
                    var fatalLogPath = WriteFatalScanFailureLog(ex);
                    SetStatusMessage($"扫描未完全可用：{ex.Message}");
                    UpdateScanWarningBanner(new[]
                    {
                        new ScanWarning
                        {
                            TimestampUtc = DateTime.UtcNow,
                            Phase = "扫描",
                            Message = $"扫描未完全可用：{ex.Message}",
                            AffectedPath = string.Empty,
                            LogPath = fatalLogPath,
                            Severity = ScanWarningSeverity.Error
                        }
                    }, fatal: true, overrideMessage: $"扫描未完全可用：{ex.Message}");
                });
            });

        SetStatusMessage(userInitiated ? "已加入后台扫描任务" : "已开始后台刷新扫描");
        UpdateActionStates();
    }

    private void QueueCleanupExecution(
        IReadOnlyCollection<CleanupItem> items,
        bool includeResidueCleanup,
        bool safeOnly,
        string? customTitle = null)
    {
        var selection = items
            .DistinctBy(item => item.Id)
            .ToList();
        if (selection.Count == 0)
        {
            return;
        }

        var title = customTitle ?? (safeOnly ? "低风险后台清理" : "后台深度清理");
        var resourceKeys = BuildCleanupResourceKeys(selection);
        var queuedIds = selection.Select(item => item.Id).ToHashSet();
        foreach (var row in _allRows.Where(row => queuedIds.Contains(row.Item.Id)))
        {
            row.MarkQueued(title);
        }

        _operationManager.Enqueue(
            OperationJobKind.Cleanup,
            title,
            resourceKeys,
            (progress, _) => ExecuteCleanup(selection, includeResidueCleanup, progress, Guid.Empty)
                ?? throw new InvalidOperationException("这次清理没有返回结果文件，可能是提权进程被取消或提前退出。"),
            warningCountSelector: runResult => runResult.Results.Count(result => !string.Equals(result.Status, "成功", StringComparison.OrdinalIgnoreCase))
                + runResult.ResidualWarnings.Count,
            warningSummarySelector: runResult =>
            {
                var incompleteCount = runResult.Results.Count(result => !string.Equals(result.Status, "成功", StringComparison.OrdinalIgnoreCase));
                var firstResidualWarning = runResult.ResidualWarnings.FirstOrDefault();
                var residualBreakdown = BuildResidualBreakdownText(runResult);
                var firstFollowUpRecommendation = runResult.FollowUpRecommendations.FirstOrDefault();
                return incompleteCount == 0 && runResult.ResidualWarnings.Count == 0
                    ? string.Empty
                    : $"本轮清理有 {incompleteCount} 项未完全清理，残留警告 {runResult.ResidualWarnings.Count} 项。"
                      + (runResult.OfficialUninstallAttempted
                          ? $" 官方卸载成功 {runResult.OfficialUninstallSucceededCount} 项"
                            + (runResult.OfficialUninstallFailedCount > 0 ? $"，失败 {runResult.OfficialUninstallFailedCount} 项并已退回文件级深删。" : "。")
                          : string.Empty)
                      + (string.IsNullOrWhiteSpace(residualBreakdown) ? string.Empty : $" 主要剩余：{residualBreakdown}。")
                      + (string.IsNullOrWhiteSpace(firstFollowUpRecommendation) ? string.Empty : $" 建议下一步：{firstFollowUpRecommendation}")
                      + (string.IsNullOrWhiteSpace(firstResidualWarning) ? string.Empty : $" 例如：{BuildResidualWarningPreview(firstResidualWarning)}。");
            },
            completed: runResult =>
            {
                OnUiThread(() =>
                {
                    ApplyRunResultToRows(runResult);
                    SyncSnapshotAfterCleanupRun(runResult);
                    UpdateDriveSummary();
                    ShowResultBanner(runResult);
                    ApplyCurrentView();
                    if (runResult.DeletedItemCount > 0)
                    {
                        QueueScanRefresh(userInitiated: false);
                    }
                });
            },
            failed: ex =>
            {
                OnUiThread(() =>
                {
                    MessageBox.Show($"执行清理失败：{ex.Message}", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetStatusMessage("后台清理失败");
                });
            });

        SetStatusMessage($"{title}已加入后台任务");
        ApplyCurrentView();
    }

    private void QueueMigrationExecution(IReadOnlyCollection<MigrationCandidate> candidates, MigrationPlan plan)
    {
        var selection = candidates
            .DistinctBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selection.Count == 0)
        {
            return;
        }

        _operationManager.Enqueue(
            OperationJobKind.Migration,
            "后台迁移目录",
            BuildMigrationResourceKeys(selection),
            (progress, _) => _migrationService.Run(selection, progress),
            warningCountSelector: result => result.FailedCount,
            warningSummarySelector: result => result.FailedCount > 0
                ? $"迁移完成，但仍有 {result.FailedCount} 项失败，已写入日志。"
                : string.Empty,
            completed: result =>
            {
                OnUiThread(() =>
                {
                    foreach (var entry in _overviewRows)
                    {
                        entry.Selected = false;
                    }

                    ShowMigrationResultBanner(result);
                    QueueScanRefresh(userInitiated: false);
                    ApplyCurrentView();
                });
            },
            failed: ex =>
            {
                OnUiThread(() =>
                {
                    MessageBox.Show($"执行迁移失败：{ex.Message}", _settings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetStatusMessage("后台迁移失败");
                });
            });

        SetStatusMessage($"已把 {plan.TotalCount} 个目录加入后台迁移任务");
        ApplyCurrentView();
    }

    private IReadOnlyList<string> BuildCleanupResourceKeys(IEnumerable<CleanupItem> items)
    {
        var keys = new List<string>();
        foreach (var item in items)
        {
            keys.Add($"drive:{item.DriveName}");
            keys.Add($"path:{item.NormalizedPath}");
            if (!string.IsNullOrWhiteSpace(item.AppIdentityKey))
            {
                keys.Add($"app:{item.AppIdentityKey}");
            }
        }

        return keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> BuildMigrationResourceKeys(IEnumerable<MigrationCandidate> candidates)
    {
        return candidates
            .Select(candidate => $"path:{candidate.SourcePath}")
            .Concat(candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.AdapterKey))
                .Select(candidate => $"adapter:{candidate.AdapterKey}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void OperationManagerStateChanged(object? sender, OperationQueueState state)
    {
        OnUiThread(() => QueueJobCenterUpdate(state));
    }

    private void QueueJobCenterUpdate(OperationQueueState state)
    {
        lock (_jobCenterStateSync)
        {
            _pendingJobCenterState = state;
            _hasPendingJobCenterState = true;
        }

        FlushPendingJobCenterState();

        if ((state.RunningCount + state.QueuedCount) > 0 && !_jobCenterRefreshTimer.Enabled)
        {
            _jobCenterRefreshTimer.Start();
        }
    }

    private void FlushPendingJobCenterState()
    {
        OperationQueueState? pendingState = null;
        var hasPendingState = false;
        lock (_jobCenterStateSync)
        {
            if (_hasPendingJobCenterState)
            {
                pendingState = _pendingJobCenterState;
                _hasPendingJobCenterState = false;
                hasPendingState = true;
            }
        }

        if (hasPendingState && pendingState is not null)
        {
            UpdateJobCenter(pendingState);
            UpdateActionStates();
        }
        else if (_queueState.RunningCount > 0 || _queueState.QueuedCount > 0)
        {
            UpdateJobCenter(_queueState);
        }

        if ((_queueState.RunningCount + _queueState.QueuedCount) <= 0)
        {
            lock (_jobCenterStateSync)
            {
                if (!_hasPendingJobCenterState)
                {
                    _jobCenterRefreshTimer.Stop();
                }
            }
        }
    }

    private void UpdateJobCenter(OperationQueueState state)
    {
        _queueState = state;
        var activeCount = state.RunningCount + state.QueuedCount;
        var collapseCompleted = activeCount == 0 && state.CompletedCount > 0;
        var compactSummaryOnly = IsCompactLayout;
        _jobCenterPanel.Visible = compactSummaryOnly
            ? activeCount > 0
            : activeCount > 0 || state.Jobs.Count > 0;
        _jobCenterLabel.Text = activeCount > 0
            ? $"后台任务：运行中 {state.RunningCount} 个，排队中 {state.QueuedCount} 个"
            : state.CompletedCount > 0
                ? (state.WarningCompletedCount > 0 ? $"最近后台任务（含 {state.WarningCompletedCount} 个部分降级）" : "最近后台任务")
                : "后台任务";
        if (IsCompactLayout && activeCount > 0)
        {
            _jobCenterLabel.Text = $"后台任务：运行 {state.RunningCount} / 排队 {state.QueuedCount}";
        }

        if (!_jobCenterPanel.Visible)
        {
            _jobCenterSummaryLabel.Visible = false;
            _jobListFlow.Visible = false;
            return;
        }

        if (compactSummaryOnly)
        {
            var activeJob = state.Jobs.FirstOrDefault(job => job.State is OperationJobState.Running or OperationJobState.Queued)
                ?? state.Jobs.FirstOrDefault();
            _jobCenterSummaryLabel.Text = activeJob is null
                ? $"后台任务：运行 {state.RunningCount} / 排队 {state.QueuedCount}"
                : BuildCompactJobSummary(activeJob);
            _jobCenterSummaryLabel.Visible = true;
            _jobListFlow.Visible = false;
            _toolTip.SetToolTip(_jobCenterSummaryLabel, activeJob is null
                ? $"后台任务：运行中 {state.RunningCount} 个，排队中 {state.QueuedCount} 个"
                : BuildCollapsedJobToolTip(activeJob));
            return;
        }

        if (collapseCompleted)
        {
            var latestJob = state.Jobs.FirstOrDefault();
            _jobCenterSummaryLabel.Text = latestJob is null
                ? "最近后台任务已完成。"
                : BuildCollapsedJobSummary(latestJob);
            _jobCenterSummaryLabel.Visible = true;
            _jobListFlow.Visible = false;
            _toolTip.SetToolTip(_jobCenterSummaryLabel, latestJob is null
                ? "最近后台任务已完成。"
                : BuildCollapsedJobToolTip(latestJob));
            return;
        }

        _jobCenterSummaryLabel.Visible = false;
        _jobListFlow.Visible = true;

        var visibleJobs = state.Jobs.Take(4).ToList();
        var visibleJobIds = visibleJobs.Select(job => job.Id).ToHashSet();

        _jobListFlow.SuspendLayout();

        foreach (var staleJobId in _jobCards.Keys.Where(id => !visibleJobIds.Contains(id)).ToList())
        {
            if (_jobCards.TryGetValue(staleJobId, out var staleBinding))
            {
                _jobListFlow.Controls.Remove(staleBinding.Panel);
                staleBinding.Panel.Dispose();
            }

            _jobCards.Remove(staleJobId);
        }

        for (var index = 0; index < visibleJobs.Count; index++)
        {
            var job = visibleJobs[index];
            if (!_jobCards.TryGetValue(job.Id, out var binding))
            {
                binding = CreateJobCard(job);
                _jobCards[job.Id] = binding;
                _jobListFlow.Controls.Add(binding.Panel);
            }

            UpdateJobCard(binding, job);
            _jobListFlow.Controls.SetChildIndex(binding.Panel, index);
        }

        _jobListFlow.ResumeLayout();
        RefreshJobCardWidths();
    }

    private JobCardBinding CreateJobCard(OperationJob job)
    {
        var panel = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(10, 10, 10, 10),
            BackColor = UiThemePalette.SurfaceRaised
        };
        UiThemePalette.EnableDoubleBuffering(panel);
        UiThemePalette.AttachBorderPainter(panel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty
        };
        UiThemePalette.EnableDoubleBuffering(layout);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(layout);

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold),
            ForeColor = UiThemePalette.TextPrimary,
            Margin = new Padding(0, 0, 10, 6)
        };
        layout.Controls.Add(titleLabel, 0, 0);

        var progressSummaryLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6),
            TextAlign = ContentAlignment.TopRight
        };
        layout.Controls.Add(progressSummaryLabel, 1, 0);

        var messageLabel = new Label
        {
            AutoSize = true,
            ForeColor = UiThemePalette.TextSecondary,
            Margin = Padding.Empty
        };
        layout.Controls.Add(messageLabel, 0, 1);
        layout.SetColumnSpan(messageLabel, 2);

        var progressBar = new ThemedProgressBar
        {
            Dock = DockStyle.Top,
            Height = 10,
            Margin = new Padding(0, 8, 0, 0)
        };
        progressBar.Value = GetJobProgressBarValue(job);
        progressBar.IsIndeterminate = job.IsIndeterminate && job.Percent <= 0 && job.State == OperationJobState.Running;

        layout.Controls.Add(progressBar, 0, 2);
        layout.SetColumnSpan(progressBar, 2);
        var binding = new JobCardBinding(panel, titleLabel, progressSummaryLabel, messageLabel, progressBar)
        {
            Job = job
        };
        ApplyJobCardLayout(binding);
        UpdateJobCard(binding, job);
        return binding;
    }

    private void UpdateJobCard(JobCardBinding binding, OperationJob job)
    {
        binding.Job = job;
        var titleText = $"{job.Title} · {GetJobStateText(job)}";
        if (!string.Equals(binding.TitleLabel.Text, titleText, StringComparison.Ordinal))
        {
            binding.TitleLabel.Text = titleText;
        }

        var progressColor = GetJobProgressColor(job);
        if (binding.ProgressSummaryLabel.ForeColor != progressColor)
        {
            binding.ProgressSummaryLabel.ForeColor = progressColor;
        }

        var progressText = BuildJobProgressText(job);
        if (!string.Equals(binding.ProgressSummaryLabel.Text, progressText, StringComparison.Ordinal))
        {
            binding.ProgressSummaryLabel.Text = progressText;
        }

        var messageText = BuildJobMessage(job);
        if (!string.Equals(binding.MessageLabel.Text, messageText, StringComparison.Ordinal))
        {
            binding.MessageLabel.Text = messageText;
        }

        var displayPercent = GetJobProgressBarValue(job);
        if (binding.ProgressBar.Value != displayPercent)
        {
            binding.ProgressBar.Value = displayPercent;
        }
        binding.ProgressBar.IsIndeterminate = job.IsIndeterminate && job.Percent <= 0 && job.State == OperationJobState.Running;
        ApplyJobCardToolTips(binding, job);
    }

    private void RefreshJobCardWidths()
    {
        foreach (var binding in _jobCards.Values)
        {
            ApplyJobCardLayout(binding);
        }
    }

    private static int GetJobProgressBarValue(OperationJob job)
    {
        if (job.Percent > 0)
        {
            return Math.Clamp(job.Percent, 0, 100);
        }

        return job.IsIndeterminate && job.State == OperationJobState.Running ? 8 : 0;
    }

    private int GetJobCardWidth()
    {
        var candidateWidth = Math.Max(_jobCenterPanel.ClientSize.Width, _jobListFlow.ClientSize.Width);
        return Math.Max(420, candidateWidth - 24);
    }

    private void ApplyJobCardLayout(JobCardBinding binding)
    {
        var cardWidth = GetJobCardWidth();
        if (binding.LastAppliedWidth == cardWidth
            && binding.LastCompactLayout == IsCompactLayout
            && binding.LastUltraCompactLayout == IsUltraCompactLayout)
        {
            return;
        }

        ApplyControlFont(binding.TitleLabel, IsCompactLayout ? 8.5f : 8.8f, FontStyle.Bold);
        ApplyControlFont(binding.ProgressSummaryLabel, IsCompactLayout ? 8.0f : 8.2f, FontStyle.Bold);
        ApplyControlFont(binding.MessageLabel, IsCompactLayout ? 8.35f : 8.75f);
        binding.Panel.Padding = IsCompactLayout ? new Padding(8, 8, 8, 8) : new Padding(10, 10, 10, 10);
        binding.ProgressBar.Height = IsCompactLayout ? 8 : 10;
        binding.ProgressBar.Margin = IsCompactLayout ? new Padding(0, 6, 0, 0) : new Padding(0, 8, 0, 0);

        binding.Panel.Width = cardWidth;

        var contentWidth = Math.Max(340, cardWidth - binding.Panel.Padding.Horizontal - 2);
        var messageWidth = Math.Max(320, contentWidth);
        binding.MessageLabel.MaximumSize = new Size(messageWidth, 0);

        var summaryPreferredWidth = MeasureSingleLineLabelWidth(binding.ProgressSummaryLabel.Text, binding.ProgressSummaryLabel.Font);
        var maxSummaryWidth = Math.Max(170, (int)Math.Round(contentWidth * 0.44));
        var summaryWidth = Math.Min(Math.Max(150, summaryPreferredWidth + 8), maxSummaryWidth);
        binding.ProgressSummaryLabel.MaximumSize = new Size(summaryWidth, 0);

        var titleWidth = Math.Max(180, contentWidth - summaryWidth - 18);
        binding.TitleLabel.MaximumSize = new Size(titleWidth, 0);
        binding.LastAppliedWidth = cardWidth;
        binding.LastCompactLayout = IsCompactLayout;
        binding.LastUltraCompactLayout = IsUltraCompactLayout;
    }

    private void ApplyJobCardToolTips(JobCardBinding binding, OperationJob job)
    {
        var progressText = BuildJobProgressText(job);
        var messageText = BuildJobMessage(job);
        var titleText = $"{job.Title}\r\n状态：{GetJobStateText(job)}";
        var combined = $"{titleText}\r\n{progressText}";
        if (!string.IsNullOrWhiteSpace(messageText))
        {
            combined += $"\r\n{messageText}";
        }

        _toolTip.SetToolTip(binding.Panel, combined);
        _toolTip.SetToolTip(binding.TitleLabel, titleText);
        _toolTip.SetToolTip(binding.ProgressSummaryLabel, progressText);
        _toolTip.SetToolTip(binding.MessageLabel, messageText);
        _toolTip.SetToolTip(binding.ProgressBar, progressText);
    }

    private static int MeasureSingleLineLabelWidth(string text, Font font)
    {
        var measured = TextRenderer.MeasureText(
            string.IsNullOrWhiteSpace(text) ? "示例文本" : text + " ",
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        return measured.Width;
    }

    private string BuildCollapsedJobSummary(OperationJob job)
    {
        var summary = $"最近后台任务：{job.Title} · {GetJobStateText(job)} · {BuildJobProgressText(job)}";
        if (job.HasWarnings && !string.IsNullOrWhiteSpace(job.WarningSummary))
        {
            summary += $" · {job.WarningSummary}";
        }

        return summary;
    }

    private string BuildCompactJobSummary(OperationJob job)
    {
        var progressText = BuildJobProgressText(job);
        if (string.IsNullOrWhiteSpace(progressText))
        {
            return $"{job.Title} · {GetJobStateText(job)}";
        }

        return $"{job.Title} · {GetJobStateText(job)} · {progressText}";
    }

    private static string BuildCollapsedJobToolTip(OperationJob job)
    {
        var combined = $"{job.Title}\r\n状态：{GetJobStateText(job)}\r\n{BuildJobProgressText(job)}";
        var message = BuildJobMessage(job);
        if (!string.IsNullOrWhiteSpace(message))
        {
            combined += $"\r\n{message}";
        }

        return combined;
    }

    private LayoutDensityMode ResolveLayoutDensityMode()
    {
        var normalizedHeight = GetDpiNormalizedClientHeight();
        if (normalizedHeight <= UltraCompactLayoutHeightThreshold)
        {
            return LayoutDensityMode.UltraCompact;
        }

        return normalizedHeight <= CompactLayoutHeightThreshold
            ? LayoutDensityMode.Compact
            : LayoutDensityMode.Regular;
    }

    private int GetDpiNormalizedClientHeight()
    {
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        return (int)Math.Round(ClientSize.Height * 96d / dpi);
    }

    private void ApplyLayoutDensity(bool force = false)
    {
        var targetMode = ResolveLayoutDensityMode();
        if (!force && _layoutDensityMode == targetMode)
        {
            return;
        }

        _layoutDensityMode = targetMode;
        var compact = IsCompactLayout;
        var ultraCompact = IsUltraCompactLayout;

        _rootLayout.SuspendLayout();
        try
        {
            _rootLayout.Padding = ultraCompact
                ? new Padding(6, 4, 6, 4)
                : compact
                    ? new Padding(12, 10, 12, 10)
                    : new Padding(16, 14, 16, 12);
            ApplySurfaceSpacing(_headerPanel, ultraCompact ? new Padding(8, 5, 8, 5) : compact ? new Padding(14, 10, 14, 10) : new Padding(18, 14, 18, 14), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            ApplySurfaceSpacing(_scanWarningPanel, ultraCompact ? new Padding(7, 4, 7, 4) : compact ? new Padding(12, 8, 12, 8) : new Padding(14, 10, 14, 10), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            ApplySurfaceSpacing(_resultBannerPanel, ultraCompact ? new Padding(7, 4, 7, 4) : compact ? new Padding(12, 8, 12, 8) : new Padding(14, 10, 14, 10), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            ApplySurfaceSpacing(_jobCenterPanel, ultraCompact ? new Padding(7, 4, 7, 4) : compact ? new Padding(12, 8, 12, 8) : new Padding(14, 10, 14, 10), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            ApplySurfaceSpacing(_viewModePanel, ultraCompact ? new Padding(5, 3, 5, 3) : compact ? new Padding(10, 6, 10, 6) : new Padding(12, 8, 12, 8), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            ApplySurfaceSpacing(_toolbarPanel, ultraCompact ? new Padding(5, 3, 5, 3) : compact ? new Padding(10, 8, 10, 8) : new Padding(12, 10, 12, 10), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            ApplySurfaceSpacing(_driveTabsPanel, ultraCompact ? new Padding(5, 3, 5, 2) : compact ? new Padding(10, 8, 10, 6) : new Padding(12, 10, 12, 8), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            ApplySurfaceSpacing(_filtersPanel, ultraCompact ? new Padding(7, 3, 7, 3) : compact ? new Padding(12, 8, 12, 8) : new Padding(14, 12, 14, 12), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            ApplySurfaceSpacing(_summaryPanel, ultraCompact ? new Padding(7, 3, 7, 3) : compact ? new Padding(12, 8, 12, 8) : new Padding(14, 10, 14, 10), ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 10));
            _statusStrip.Margin = ultraCompact ? new Padding(0, 4, 0, 0) : compact ? new Padding(0, 6, 0, 0) : new Padding(0, 10, 0, 0);
            _teachingPanel.Padding = ultraCompact ? new Padding(5, 3, 5, 3) : compact ? new Padding(10, 6, 10, 6) : new Padding(12, 8, 12, 8);
            _teachingPanel.Margin = ultraCompact ? new Padding(0, 0, 0, 2) : compact ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 8);
            _teachingPanel.Visible = !ultraCompact;
            _teachingSecondaryLabel.Visible = !ultraCompact && (!compact || GetDpiNormalizedClientHeight() > TeachingSecondaryHideHeightThreshold);
            _selectionHintLabel.Visible = !ultraCompact;
            ApplyDataFirstVisibility();

            ApplyControlFont(_headerTitleLabel, ultraCompact ? 12.6f : compact ? 13.6f : 15f, FontStyle.Bold);
            ApplyControlFont(_headerModeLabel, ultraCompact ? 7.7f : compact ? 8.1f : 8.5f, FontStyle.Bold);
            ApplyControlFont(_driveSummaryLabel, ultraCompact ? 8.1f : compact ? 8.5f : 9f);
            ApplyControlFont(_runtimeInfoLabel, ultraCompact ? 8.1f : compact ? 8.35f : 8.85f);
            ApplyControlFont(_jobCenterLabel, ultraCompact ? 8.55f : compact ? 9f : 9.5f, FontStyle.Bold);
            ApplyControlFont(_jobCenterSummaryLabel, ultraCompact ? 8.1f : compact ? 8.35f : 8.75f);
            ApplyControlFont(_teachingPrimaryLabel, ultraCompact ? 8.3f : compact ? 8.7f : 9f, FontStyle.Bold);
            ApplyControlFont(_teachingSecondaryLabel, ultraCompact ? 8.1f : compact ? 8.35f : 8.75f);
            ApplyControlFont(_viewSummaryLabel, ultraCompact ? 8.55f : compact ? 9f : 9.5f, FontStyle.Bold);
            ApplyControlFont(_selectionSummaryLabel, ultraCompact ? 8.55f : compact ? 9f : 9.5f, FontStyle.Bold);
            ApplyControlFont(_selectionHintLabel, ultraCompact ? 8.1f : compact ? 8.45f : 8.85f);
        }
        finally
        {
            _rootLayout.ResumeLayout(true);
        }

        UpdateTeachingStrip();
        UpdateViewSummary();
        UpdateSelectionSummary();
        UpdateSelectionHint();
        ApplyCompactHeaderTexts();
        UpdateJobCenter(_queueState);
    }

    private void UpdateResponsiveLabelWidths()
    {
        var headerPrimaryWidth = Math.Max(260, ClientSize.Width - (IsUltraCompactLayout ? 240 : IsCompactLayout ? 300 : 360));
        _headerTitleLabel.MaximumSize = new Size(headerPrimaryWidth, 0);
        _headerModeLabel.MaximumSize = new Size(Math.Max(180, Math.Min(360, ClientSize.Width / 3)), 0);
        _driveSummaryLabel.MaximumSize = new Size(Math.Max(280, ClientSize.Width - (IsUltraCompactLayout ? 160 : IsCompactLayout ? 220 : 280)), 0);
        _runtimeInfoLabel.MaximumSize = new Size(UiScaleHelper.MeasureWrapWidth(ClientSize.Width, IsUltraCompactLayout ? 220 : IsCompactLayout ? 150 : 100, minWidth: IsUltraCompactLayout ? 260 : IsCompactLayout ? 380 : 520), 0);
        _scanWarningLabel.MaximumSize = new Size(UiScaleHelper.MeasureWrapWidth(ClientSize.Width, IsUltraCompactLayout ? 220 : IsCompactLayout ? 180 : 140, minWidth: IsUltraCompactLayout ? 320 : 360), 0);
        _selectionHintLabel.MaximumSize = new Size(UiScaleHelper.MeasureWrapWidth(ClientSize.Width, IsUltraCompactLayout ? 220 : IsCompactLayout ? 190 : 140, minWidth: IsUltraCompactLayout ? 320 : 360), 0);
        _teachingPrimaryLabel.MaximumSize = new Size(UiScaleHelper.MeasureWrapWidth(ClientSize.Width, IsUltraCompactLayout ? 220 : IsCompactLayout ? 180 : 140, minWidth: IsUltraCompactLayout ? 320 : 360), 0);
        _teachingSecondaryLabel.MaximumSize = new Size(UiScaleHelper.MeasureWrapWidth(ClientSize.Width, IsUltraCompactLayout ? 220 : IsCompactLayout ? 180 : 140, minWidth: IsUltraCompactLayout ? 320 : 360), 0);

        var jobSummaryWidth = Math.Max(300, Math.Max(_jobCenterPanel.ClientSize.Width, ClientSize.Width) - (IsUltraCompactLayout ? 70 : IsCompactLayout ? 120 : 90));
        _jobCenterSummaryLabel.MaximumSize = new Size(jobSummaryWidth, 0);
    }

    private void ApplyCompactHeaderTexts()
    {
        var fullDriveSummary = _driveSummaryLabel.Tag as string ?? _driveSummaryLabel.Text;
        _driveSummaryLabel.Tag = fullDriveSummary;
        _toolTip.SetToolTip(_driveSummaryLabel, fullDriveSummary);
        _driveSummaryLabel.Visible = !IsUltraCompactLayout && !string.IsNullOrWhiteSpace(fullDriveSummary);
        _driveSummaryLabel.Text = IsUltraCompactLayout
            ? BuildUltraCompactDriveSummaryText(fullDriveSummary)
            : IsCompactLayout
                ? BuildCompactDriveSummaryText(fullDriveSummary)
                : fullDriveSummary;

        var fullRuntimeInfo = _runtimeInfoLabel.Tag as string ?? _runtimeInfoLabel.Text;
        _runtimeInfoLabel.Tag = fullRuntimeInfo;
        _toolTip.SetToolTip(_runtimeInfoLabel, fullRuntimeInfo);
        _runtimeInfoLabel.Visible = !IsCompactLayout && !string.IsNullOrWhiteSpace(fullRuntimeInfo);
        _runtimeInfoLabel.Text = _runtimeInfoLabel.Visible
            ? (IsCompactLayout ? BuildCompactRuntimeInfoText(fullRuntimeInfo) : fullRuntimeInfo)
            : string.Empty;
        var compactHeaderToolTip = IsUltraCompactLayout
            ? string.Join("\r\n", new[] { fullDriveSummary, fullRuntimeInfo }.Where(text => !string.IsNullOrWhiteSpace(text)))
            : IsCompactLayout
                ? fullRuntimeInfo
                : string.Empty;
        _toolTip.SetToolTip(_headerPanel, compactHeaderToolTip);
        _toolTip.SetToolTip(_headerModeLabel, compactHeaderToolTip);
    }

    private static void ApplySurfaceSpacing(Panel panel, Padding padding, Padding margin)
    {
        panel.Padding = padding;
        panel.Margin = margin;
    }

    private void ApplyDataFirstVisibility()
    {
        if (IsUltraCompactLayout)
        {
            // UltraCompact is a data-first mode: hide optional teaching/filter rows instead of starving the grid.
            _driveTabsPanel.Visible = false;
            _filtersPanel.Visible = false;
            _teachingPanel.Visible = false;
            _teachingSecondaryLabel.Visible = false;
            _selectionHintLabel.Visible = false;
            _quickFiltersHost.Visible = false;
            return;
        }

        _driveTabsPanel.Visible = true;
        _filtersPanel.Visible = true;
        _teachingPanel.Visible = true;
        _teachingSecondaryLabel.Visible = !IsCompactLayout || GetDpiNormalizedClientHeight() > TeachingSecondaryHideHeightThreshold;
        _selectionHintLabel.Visible = !IsCompactLayout;
        _quickFiltersHost.Visible = _viewMode == MainViewMode.CleanupCandidates;
    }

    private static void ApplyButtonFont(Button button, bool compactLayout, bool ultraCompactLayout, FontStyle? styleOverride = null, bool compactQuickFilter = false)
    {
        var fontSize = ultraCompactLayout
            ? (compactQuickFilter ? 8.0f : 8.35f)
            : compactLayout
                ? (compactQuickFilter ? 8.3f : 8.7f)
                : (compactQuickFilter ? 8.5f : 9f);
        var style = styleOverride ?? button.Font.Style;
        if (Math.Abs(button.Font.Size - fontSize) < 0.01f && button.Font.Style == style)
        {
            return;
        }

        button.Font = new Font("Microsoft YaHei UI", fontSize, style);
    }

    private static void ApplyControlFont(Control control, float size, FontStyle style = FontStyle.Regular)
    {
        if (Math.Abs(control.Font.Size - size) < 0.01f && control.Font.Style == style)
        {
            return;
        }

        control.Font = new Font("Microsoft YaHei UI", size, style);
    }

    private void HandleDeferredResize()
    {
        if (!IsHandleCreated || IsDisposed || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        _pendingResizeForceLayout = _pendingResizeForceLayout || _layoutDensityMode != ResolveLayoutDensityMode();
        _resizeRefreshPending = true;
        if (_resizeDragInProgress)
        {
            return;
        }

        _resizeRefreshTimer.Stop();
        _resizeRefreshTimer.Start();
    }

    private void HandleDpiChanged()
    {
        ReloadDynamicIconsForCurrentDpi();
        RefreshScaledUi(forceLayout: true);
        FlushPendingVisualRefreshes();
        ForceCleanRepaintAfterResize();
    }

    private void FlushDeferredResizeRefresh(bool forceLayout = false)
    {
        if (_resizeDragInProgress && !forceLayout)
        {
            return;
        }

        _resizeRefreshTimer.Stop();
        if (!_resizeRefreshPending && !forceLayout)
        {
            return;
        }

        var needsLayoutRefresh = forceLayout || _pendingResizeForceLayout || _layoutDensityMode != ResolveLayoutDensityMode();
        _resizeRefreshPending = false;
        _pendingResizeForceLayout = false;
        RefreshScaledUi(forceLayout: needsLayoutRefresh, refreshGridContent: !_resizeDragInProgress || forceLayout);
        FlushPendingVisualRefreshes();
        if (forceLayout || needsLayoutRefresh)
        {
            ForceCleanRepaintAfterResize();
        }
    }

    private void SuspendResizeSensitiveLayout()
    {
        FreezeAutoSizedControl(_headerPanel);
        FreezeAutoSizedControl(_scanWarningPanel);
        FreezeAutoSizedControl(_resultBannerPanel);
        FreezeAutoSizedControl(_jobCenterPanel);
        FreezeAutoSizedControl(_viewModePanel);
        FreezeAutoSizedControl(_toolbarPanel);
        FreezeAutoSizedControl(_driveTabsPanel);
        FreezeAutoSizedControl(_filtersPanel);
        FreezeAutoSizedControl(_summaryPanel);
        FreezeAutoSizedControl(_teachingPanel);
        FreezeAutoSizedControl(_jobListFlow);
        FreezeAutoSizedControl(_viewModeFlow);
        FreezeAutoSizedControl(_driveTabsFlow);
        FreezeAutoSizedControl(_quickFiltersHost);
    }

    private void ResumeResizeSensitiveLayout()
    {
        foreach (var (control, state) in _resizeFrozenControls.ToArray())
        {
            if (control.IsDisposed)
            {
                continue;
            }

            control.SuspendLayout();
            try
            {
                control.AutoSize = state.AutoSize;
                SetAutoSizeMode(control, state.AutoSizeMode);
                control.MinimumSize = state.MinimumSize;
                control.MaximumSize = state.MaximumSize;
            }
            finally
            {
                control.ResumeLayout(true);
            }
        }

        _resizeFrozenControls.Clear();
    }

    private void FreezeAutoSizedControl(Control control)
    {
        if (control.IsDisposed || _resizeFrozenControls.ContainsKey(control))
        {
            return;
        }

        var autoSizeMode = control is Panel panel
            ? panel.AutoSizeMode
            : control is FlowLayoutPanel flowLayoutPanel
                ? flowLayoutPanel.AutoSizeMode
                : control is TableLayoutPanel tableLayoutPanel
                    ? tableLayoutPanel.AutoSizeMode
                    : AutoSizeMode.GrowOnly;
        var state = new FrozenAutoSizeState(control.AutoSize, autoSizeMode, control.MinimumSize, control.MaximumSize);
        _resizeFrozenControls[control] = state;
        if (!control.AutoSize)
        {
            return;
        }

        control.SuspendLayout();
        try
        {
            var preferredSize = control.PreferredSize;
            control.AutoSize = false;
            control.MinimumSize = new Size(0, Math.Max(control.Height, preferredSize.Height));
            control.MaximumSize = Size.Empty;
            control.Height = Math.Max(control.Height, preferredSize.Height);
        }
        finally
        {
            control.ResumeLayout(false);
        }
    }

    private static void SetAutoSizeMode(Control control, AutoSizeMode autoSizeMode)
    {
        switch (control)
        {
            case FlowLayoutPanel flowLayoutPanel:
                flowLayoutPanel.AutoSizeMode = autoSizeMode;
                break;
            case TableLayoutPanel tableLayoutPanel:
                tableLayoutPanel.AutoSizeMode = autoSizeMode;
                break;
            case Panel panel:
                panel.AutoSizeMode = autoSizeMode;
                break;
        }
    }

    private void SetHeavyRedrawSuspended(bool suspend)
    {
        SetControlRedraw(_grid, !suspend);
        SetControlRedraw(_overviewGrid, !suspend);
        SetControlRedraw(_infrequentGrid, !suspend);
        SetControlRedraw(_jobCenterPanel, !suspend);
    }

    private static void SetControlRedraw(Control control, bool enabled)
    {
        if (!control.IsHandleCreated || control.IsDisposed)
        {
            return;
        }

        try
        {
            NativeMethods.SendMessage(control.Handle, NativeMethods.WmSetRedraw, enabled ? 1 : 0, 0);
            if (enabled)
            {
                control.Invalidate(true);
                control.Update();
            }
        }
        catch
        {
        }
    }

    private void HandleJobCardWidthRefreshRequest()
    {
        if (_resizeDragInProgress)
        {
            _pendingJobCardWidthRefresh = true;
            return;
        }

        RefreshJobCardWidths();
    }

    private void FlushPendingVisualRefreshes()
    {
        if (_pendingJobCardWidthRefresh)
        {
            _pendingJobCardWidthRefresh = false;
            RefreshJobCardWidths();
        }

        FlushPendingIconColumnInvalidations();
    }

    private void FlushPendingIconColumnInvalidations()
    {
        if (_pendingCleanupIconColumnRefresh)
        {
            _pendingCleanupIconColumnRefresh = false;
            SafeInvalidateIconColumn(_grid, _cleanupIconColumn.Index);
        }

        if (_pendingOverviewIconColumnRefresh)
        {
            _pendingOverviewIconColumnRefresh = false;
            SafeInvalidateIconColumn(_overviewGrid, _overviewIconColumn.Index);
        }

        if (_pendingInfrequentIconColumnRefresh)
        {
            _pendingInfrequentIconColumnRefresh = false;
            SafeInvalidateIconColumn(_infrequentGrid, _infrequentIconColumn.Index);
        }
    }

    private void SafeInvalidateIconColumn(DataGridView grid, int columnIndex)
    {
        if (IsDisposed || !IsHandleCreated || grid.IsDisposed || columnIndex < 0)
        {
            return;
        }

        try
        {
            grid.InvalidateColumn(columnIndex);
        }
        catch
        {
        }
    }

    private void ForceCleanRepaintAfterResize()
    {
        if (IsDisposed || !IsHandleCreated || _resizeDragInProgress)
        {
            return;
        }

        try
        {
            _rootLayout.PerformLayout();
            _contentPanel.PerformLayout();
            GetActiveContentControl().PerformLayout();
            RedrawNow(Handle);
            RedrawNow(_rootLayout.Handle);
            RedrawNow(_contentPanel.Handle);
            RedrawNow(GetActiveContentControl().Handle);
        }
        catch
        {
        }
    }

    private static void RedrawNow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.RedrawWindow(
            handle,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.RdwInvalidate
            | NativeMethods.RdwErase
            | NativeMethods.RdwAllChildren
            | NativeMethods.RdwUpdateNow
            | NativeMethods.RdwFrame);
    }

    private static string BuildCompactDriveSummaryText(string fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return string.Empty;
        }

        return fullText
            .Replace(" 盘可用 ", " 可用 ", StringComparison.Ordinal)
            .Replace(" / 总 ", "/", StringComparison.Ordinal)
            .Replace("    当前为只读模式", "    只读", StringComparison.Ordinal)
            .Replace("    当前已拿到管理员权限", "    已管理员", StringComparison.Ordinal);
    }

    private static string BuildUltraCompactDriveSummaryText(string fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return string.Empty;
        }

        return fullText
            .Replace(" 盘可用 ", " ", StringComparison.Ordinal)
            .Replace(" / 总 ", "/", StringComparison.Ordinal)
            .Replace("    当前为只读模式", " · 只读", StringComparison.Ordinal)
            .Replace("    当前已拿到管理员权限", " · 已管理员", StringComparison.Ordinal)
            .Replace("    ", " · ", StringComparison.Ordinal);
    }

    private static string BuildCompactRuntimeInfoText(string fullText)
    {
        if (string.IsNullOrWhiteSpace(fullText))
        {
            return string.Empty;
        }

        var parts = fullText
            .Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (parts.Count == 0)
        {
            return fullText;
        }

        var versionPart = parts.First();
        var currentPart = parts.LastOrDefault(part => part.Contains("当前", StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(currentPart)
            ? versionPart
            : $"{versionPart} · {currentPart}";
    }

    private Control GetActiveContentControl()
    {
        return _viewMode switch
        {
            MainViewMode.CDriveOverview => _overviewGrid,
            MainViewMode.InfrequentApps => _infrequentGrid,
            _ => _grid
        };
    }

    private void SetActiveContent(Control content)
    {
        if (_activeContentControl == content
            && _contentPanel.Controls.Count == 1
            && ReferenceEquals(_contentPanel.Controls[0], content))
        {
            return;
        }

        _contentPanel.SuspendLayout();
        try
        {
            if (_activeContentControl is not null && !ReferenceEquals(_activeContentControl, content))
            {
                _activeContentControl.Visible = false;
            }

            while (_contentPanel.Controls.Count > 0)
            {
                _contentPanel.Controls.RemoveAt(0);
            }

            content.Dock = DockStyle.Fill;
            content.Margin = Padding.Empty;
            content.Visible = true;
            _contentPanel.Controls.Add(content);
            content.BringToFront();
            _activeContentControl = content;
        }
        finally
        {
            _contentPanel.ResumeLayout(true);
        }
    }

    private static string GetJobStateText(OperationJob job)
    {
        return job.State switch
        {
            OperationJobState.Queued => "等待中",
            OperationJobState.Running => "进行中",
            OperationJobState.Succeeded => job.HasWarnings ? "已完成（部分降级）" : "已完成",
            OperationJobState.Failed => "失败",
            OperationJobState.Canceled => "已取消",
            _ => "未知"
        };
    }

    private static string BuildJobMessage(OperationJob job)
    {
        var message = string.IsNullOrWhiteSpace(job.Message) ? job.Phase : job.Message;
        if (job.HasWarnings && !string.IsNullOrWhiteSpace(job.WarningSummary))
        {
            message = $"{job.WarningSummary}\r\n{message}";
        }

        if (job.TotalBytes.HasValue && job.TotalBytes.Value > 0)
        {
            var processed = SizeFormatter.Format(job.ProcessedBytes ?? 0);
            var total = SizeFormatter.Format(job.TotalBytes.Value);
            return $"{message}\r\n当前阶段：{job.Phase}    已处理：{processed} / {total}";
        }

        if (job.ProcessedBytes.HasValue && job.ProcessedBytes.Value > 0)
        {
            var processed = SizeFormatter.Format(job.ProcessedBytes.Value);
            return $"{message}\r\n当前阶段：{job.Phase}    已处理：{processed}";
        }

        if (!string.IsNullOrWhiteSpace(job.Phase) && !string.Equals(job.Phase, message, StringComparison.OrdinalIgnoreCase))
        {
            return $"{message}\r\n当前阶段：{job.Phase}";
        }

        return message;
    }

    private static string BuildJobProgressText(OperationJob job)
    {
        var anchorTime = job.StartedAtUtc ?? job.CreatedAtUtc;
        var endTime = job.CompletedAtUtc ?? DateTime.UtcNow;
        var elapsed = endTime >= anchorTime ? endTime - anchorTime : TimeSpan.Zero;
        var parts = new List<string>();
        var percent = Math.Clamp(job.Percent, 0, 100);

        if (job.State != OperationJobState.Queued || percent > 0)
        {
            parts.Add($"{percent}%");
        }

        if (job.State == OperationJobState.Running)
        {
            parts.Add($"已用 {FormatDuration(elapsed)}");
            var remaining = EstimateRemaining(job, elapsed);
            if (remaining.HasValue)
            {
                parts.Add($"预计还要 {FormatDuration(remaining.Value)}");
            }
            else if (job.IsIndeterminate || percent <= 0)
            {
                parts.Add("正在估算剩余时间");
            }
        }
        else if (job.State == OperationJobState.Queued)
        {
            parts.Add($"已等待 {FormatDuration(elapsed)}");
            parts.Add("等待前面任务完成");
        }
        else if (job.State is OperationJobState.Succeeded or OperationJobState.Failed or OperationJobState.Canceled)
        {
            parts.Add($"总耗时 {FormatDuration(elapsed)}");
        }

        return parts.Count == 0 ? "等待开始" : string.Join(" · ", parts);
    }

    private static Color GetJobProgressColor(OperationJob job)
    {
        return job.State switch
        {
            OperationJobState.Failed => UiThemePalette.Danger,
            OperationJobState.Canceled => UiThemePalette.Warning,
            OperationJobState.Succeeded when job.HasWarnings => UiThemePalette.Warning,
            _ => UiThemePalette.AccentStrong
        };
    }

    private static TimeSpan? EstimateRemaining(OperationJob job, TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
        {
            return null;
        }

        double progressRatio;
        if (job.TotalBytes.HasValue && job.TotalBytes.Value > 0 && job.ProcessedBytes.HasValue && job.ProcessedBytes.Value > 0)
        {
            progressRatio = Math.Clamp((double)job.ProcessedBytes.Value / job.TotalBytes.Value, 0d, 1d);
        }
        else
        {
            var percent = Math.Clamp(job.Percent, 0, 100);
            progressRatio = Math.Clamp(percent / 100d, 0d, 1d);
        }

        if (progressRatio <= 0d || progressRatio >= 1d)
        {
            return null;
        }

        var estimatedTotalSeconds = elapsed.TotalSeconds / progressRatio;
        var remainingSeconds = Math.Max(estimatedTotalSeconds - elapsed.TotalSeconds, 0);
        if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds))
        {
            return null;
        }

        return TimeSpan.FromSeconds(Math.Min(remainingSeconds, 12 * 60 * 60));
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

    private sealed class JobCardBinding
    {
        public JobCardBinding(Panel panel, Label titleLabel, Label progressSummaryLabel, Label messageLabel, ThemedProgressBar progressBar)
        {
            Panel = panel;
            TitleLabel = titleLabel;
            ProgressSummaryLabel = progressSummaryLabel;
            MessageLabel = messageLabel;
            ProgressBar = progressBar;
        }

        public Panel Panel { get; }
        public Label TitleLabel { get; }
        public Label ProgressSummaryLabel { get; }
        public Label MessageLabel { get; }
        public ThemedProgressBar ProgressBar { get; }
        public OperationJob Job { get; set; } = new();
        public int LastAppliedWidth { get; set; } = -1;
        public bool LastCompactLayout { get; set; }
        public bool LastUltraCompactLayout { get; set; }
    }

    private void OnUiThread(Action action)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
            return;
        }

        action();
    }

    private static CleanupItem CreateEffectiveCleanupItem(CleanupSelectionRow row)
    {
        return new CleanupItem
        {
            Id = row.Item.Id,
            DriveName = row.Item.DriveName,
            Category = row.Item.Category,
            Name = row.Item.Name,
            TypeDescription = row.Item.TypeDescription,
            Path = row.Item.Path,
            IconSourcePath = row.Item.IconSourcePath,
            IconInstallRoot = row.Item.IconInstallRoot,
            NormalizedPath = row.Item.NormalizedPath,
            RuleSource = row.Item.RuleSource,
            CandidateKind = row.Item.CandidateKind,
            InstalledAppId = row.Item.InstalledAppId,
            AppIdentityKey = row.Item.AppIdentityKey,
            CompanionPaths = row.Item.CompanionPaths,
            IsApplicationRelated = row.Item.IsApplicationRelated,
            DeepCleanupEligible = row.Item.DeepCleanupEligible,
            DuplicateGroupKey = row.Item.DuplicateGroupKey,
            TargetKind = row.Item.TargetKind,
            Note = row.Item.Note,
            ImpactText = row.Item.ImpactText,
            ImpactSeverity = row.Item.ImpactSeverity,
            SizeBytes = row.EffectiveSizeBytes,
            SafeAuto = row.Item.SafeAuto,
            Recommended = row.Item.Recommended,
            WhitelistHintText = row.Item.WhitelistHintText,
            BlockerHintText = row.Item.BlockerHintText
        };
    }

    private void ApplyPersistedWindowBounds()
    {
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, DefaultWindowWidth, DefaultWindowHeight);
        var persistedBounds = new Rectangle(_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight);
        var bounds = IsValidWindowBounds(persistedBounds)
            ? persistedBounds
            : CalculateDefaultBounds(workingArea);

        Bounds = NormalizeWindowBounds(bounds, workingArea);
    }

    private void PersistWindowState()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowX = bounds.X;
        _settings.WindowY = bounds.Y;
        _settings.LastViewMode = _viewMode.ToString();
        _settingsService.Save(_settings);
    }

    private static Rectangle CalculateDefaultBounds(Rectangle workingArea)
    {
        var margin = 32;
        var minimumWidth = Math.Min(MinimumWindowWidth, Math.Max(960, workingArea.Width - 20));
        var minimumHeight = Math.Min(MinimumWindowHeight, Math.Max(640, workingArea.Height - 20));
        var maxWidth = Math.Max(minimumWidth, workingArea.Width - margin * 2);
        var maxHeight = Math.Max(minimumHeight, workingArea.Height - margin * 2);
        var scale = Math.Min(1d, Math.Min(maxWidth / (double)DefaultWindowWidth, maxHeight / (double)DefaultWindowHeight));
        var width = Math.Max(minimumWidth, (int)Math.Round(DefaultWindowWidth * scale));
        var height = Math.Max(minimumHeight, (int)Math.Round(DefaultWindowHeight * scale));

        width = Math.Min(width, workingArea.Width - 20);
        height = Math.Min(height, workingArea.Height - 20);

        var x = workingArea.X + Math.Max(0, (workingArea.Width - width) / 2);
        var y = workingArea.Y + Math.Max(0, (workingArea.Height - height) / 2);
        return new Rectangle(x, y, width, height);
    }

    private static bool IsValidWindowBounds(Rectangle bounds)
    {
        if (bounds.Width < MinimumWindowWidth || bounds.Height < MinimumWindowHeight)
        {
            return false;
        }

        return Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));
    }

    private static Rectangle NormalizeWindowBounds(Rectangle bounds, Rectangle workingArea)
    {
        var minimumWidth = Math.Min(MinimumWindowWidth, Math.Max(960, workingArea.Width - 20));
        var minimumHeight = Math.Min(MinimumWindowHeight, Math.Max(640, workingArea.Height - 20));
        var width = Math.Clamp(bounds.Width, minimumWidth, Math.Max(minimumWidth, workingArea.Width - 20));
        var height = Math.Clamp(bounds.Height, minimumHeight, Math.Max(minimumHeight, workingArea.Height - 20));
        var maxX = Math.Max(workingArea.Left, workingArea.Right - width);
        var maxY = Math.Max(workingArea.Top, workingArea.Bottom - height);
        var x = Math.Clamp(bounds.X, workingArea.Left, maxX);
        var y = Math.Clamp(bounds.Y, workingArea.Top, maxY);
        return new Rectangle(x, y, width, height);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        if (!string.IsNullOrWhiteSpace(message))
        {
            SetStatusMessage(message);
        }

        UpdateActionStates();
    }

    private void SetStatusMessage(string text)
    {
        _statusMessageLabel.Text = text;
    }

    private void SwitchView(MainViewMode viewMode)
    {
        _deferredStartupViewMode = null;
        _viewMode = viewMode;
        EnsureActiveDriveSelection();
        ConfigureFilterControlsForMode();
        UpdateViewModeButtons();
        ApplyCurrentView();
    }

    private void ConfigureFilterControlsForMode()
    {
        _suppressFilterEvents = true;

        if (_viewMode == MainViewMode.CDriveOverview)
        {
            _searchTextBox.PlaceholderText = "搜目录名、路径、用途、建议";
            _categoryFilterLabel.Visible = true;
            _categoryComboBox.Visible = true;
            _modeFilterLabel.Visible = false;
            _modeComboBox.Visible = false;
            _sortFilterLabel.Visible = true;
            _sortComboBox.Visible = true;
            _quickFiltersHost.Visible = false;

            _categoryComboBox.Items.Clear();
            _categoryComboBox.Items.Add(new FilterOption(CleanupFilterState.AllValue, "全部类型"));
            foreach (var category in _overviewRows
                         .Select(entry => entry.Category)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
            {
                _categoryComboBox.Items.Add(new FilterOption(category, category));
            }

            _sortComboBox.Items.Clear();
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortSmart, "按类型和体积"));
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortSizeDesc, "体积从大到小"));
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortNameAsc, "名称 A-Z"));
            _sortComboBox.SelectedIndex = Math.Min(_sortComboBox.Items.Count - 1, 0);
            SelectComboBoxValue(_categoryComboBox, CleanupFilterState.AllValue);
            _filterState.SelectedCategory = CleanupFilterState.AllValue;
            _filterState.SelectedAutoMode = CleanupFilterState.AllValue;
            _filterState.SortMode = CleanupFilterState.SortSmart;
        }
        else if (_viewMode == MainViewMode.InfrequentApps)
        {
            _searchTextBox.PlaceholderText = "搜软件名、路径、使用证据、建议";
            _categoryFilterLabel.Visible = true;
            _categoryComboBox.Visible = true;
            _modeFilterLabel.Visible = true;
            _modeComboBox.Visible = true;
            _sortFilterLabel.Visible = true;
            _sortComboBox.Visible = true;
            _quickFiltersHost.Visible = false;

            _categoryFilterLabel.Text = "状态";
            _modeFilterLabel.Text = "可信度";
            _sortFilterLabel.Text = "排序";

            _categoryComboBox.Items.Clear();
            _categoryComboBox.Items.Add(new FilterOption(CleanupFilterState.AllValue, "全部状态"));
            _categoryComboBox.Items.Add(new FilterOption("recommended", "建议删除"));
            _categoryComboBox.Items.Add(new FilterOption("review", "建议确认"));
            _categoryComboBox.Items.Add(new FilterOption("running", "当前正在用"));
            _categoryComboBox.Items.Add(new FilterOption("active", "当前常用"));
            _categoryComboBox.Items.Add(new FilterOption("unknown_record", "无可靠记录"));
            _categoryComboBox.Items.Add(new FilterOption("whitelist", "已白名单"));

            _modeComboBox.Items.Clear();
            _modeComboBox.Items.Add(new FilterOption(CleanupFilterState.AllValue, "全部可信度"));
            _modeComboBox.Items.Add(new FilterOption("confidence_high", "高可信"));
            _modeComboBox.Items.Add(new FilterOption("confidence_medium", "中可信"));
            _modeComboBox.Items.Add(new FilterOption("confidence_low", "低可信"));
            _modeComboBox.Items.Add(new FilterOption("confidence_unknown", "未知"));

            _sortComboBox.Items.Clear();
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortSmart, "按盘符和未用天数"));
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortSizeDesc, "体积从大到小"));
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortNameAsc, "名称 A-Z"));

            SelectComboBoxValue(_categoryComboBox, string.IsNullOrWhiteSpace(_settings.InfrequentAppsViewLastFilter) ? "recommended" : _settings.InfrequentAppsViewLastFilter);
            SelectComboBoxValue(_modeComboBox, CleanupFilterState.AllValue);
            SelectComboBoxValue(_sortComboBox, CleanupFilterState.SortSmart);
            _filterState.SelectedCategory = GetComboBoxValue(_categoryComboBox);
            _filterState.SelectedAutoMode = CleanupFilterState.AllValue;
            _filterState.SortMode = CleanupFilterState.SortSmart;
        }
        else
        {
            _searchTextBox.PlaceholderText = "搜名称、路径、影响说明";
            _categoryFilterLabel.Visible = true;
            _categoryComboBox.Visible = true;
            _modeFilterLabel.Visible = true;
            _modeComboBox.Visible = true;
            _sortFilterLabel.Visible = true;
            _sortComboBox.Visible = true;
            _quickFiltersHost.Visible = true;
            _categoryFilterLabel.Text = "分类";
            _modeFilterLabel.Text = "建议";
            _sortFilterLabel.Text = "排序";

            _modeComboBox.Items.Clear();
            _modeComboBox.Items.Add(new FilterOption(CleanupFilterState.AllValue, "全部建议"));
            _modeComboBox.Items.Add(new FilterOption(CleanupFilterState.AutoValue, "只看可安全删"));
            _modeComboBox.Items.Add(new FilterOption(CleanupFilterState.ReviewValue, "只看需要确认"));
            _modeComboBox.Items.Add(new FilterOption(AutoModeAppOnly, "只看应用相关"));

            _sortComboBox.Items.Clear();
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortSmart, "智能排序"));
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortSizeDesc, "体积从大到小"));
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortNameAsc, "名称 A-Z"));
            _sortComboBox.Items.Add(new FilterOption(CleanupFilterState.SortDriveSize, "按盘符和体积"));
            RefreshCategoryItems();
            SelectComboBoxValue(_modeComboBox, _filterState.SelectedAutoMode);
            SelectComboBoxValue(_sortComboBox, _filterState.SortMode);
        }

        _suppressFilterEvents = false;
    }

    private void GridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].DataBoundItem is not CleanupSelectionRow row)
        {
            return;
        }

        if (e.ColumnIndex == _cleanupIconColumn.Index)
        {
            e.Value = GetCleanupIcon(row);
            e.FormattingApplied = true;
            return;
        }

        var column = _grid.Columns[e.ColumnIndex];
        if (column.DataPropertyName == nameof(CleanupSelectionRow.ImpactSeverityText))
        {
            e.CellStyle.ForeColor = row.Item.ImpactSeverity switch
            {
                CleanupImpactSeverity.Low => Color.FromArgb(27, 125, 78),
                CleanupImpactSeverity.Medium => Color.FromArgb(176, 103, 12),
                CleanupImpactSeverity.High => Color.FromArgb(179, 52, 52),
                _ => e.CellStyle.ForeColor
            };
            e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            return;
        }

        if (column.DataPropertyName == nameof(CleanupSelectionRow.RecommendationText))
        {
            e.CellStyle.ForeColor = row.RetrySuggested
                ? Color.FromArgb(176, 103, 12)
                : row.Item.SafeAuto
                    ? Color.FromArgb(29, 123, 82)
                    : row.Item.ImpactSeverity == CleanupImpactSeverity.High
                        ? Color.FromArgb(179, 52, 52)
                        : Color.FromArgb(176, 103, 12);
            e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        }
    }

    private void OverviewGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_overviewGrid.Rows[e.RowIndex].DataBoundItem is not CDriveOverviewEntry entry)
        {
            return;
        }

        if (e.ColumnIndex == _overviewIconColumn.Index)
        {
            e.Value = GetOverviewIcon(entry);
            e.FormattingApplied = true;
            return;
        }

        var column = _overviewGrid.Columns[e.ColumnIndex];
        if (column.DataPropertyName == nameof(CDriveOverviewEntry.ProtectionText))
        {
            e.CellStyle.ForeColor = entry.ProtectionLevel switch
            {
                CDriveProtectionLevel.SystemProtected => Color.FromArgb(179, 52, 52),
                CDriveProtectionLevel.SharedComponent => Color.FromArgb(176, 103, 12),
                CDriveProtectionLevel.MoveRecommended => Color.FromArgb(27, 125, 78),
                _ => e.CellStyle.ForeColor
            };
            e.CellStyle.Font = new Font(_overviewGrid.Font, FontStyle.Bold);
            return;
        }

        if (column.DataPropertyName == nameof(CDriveOverviewEntry.OperationHintText))
        {
            e.CellStyle.ForeColor = entry.MigrationMode switch
            {
                MigrationMode.AutoSafe => Color.FromArgb(27, 125, 78),
                MigrationMode.GuideOnly => Color.FromArgb(176, 103, 12),
                _ => Color.FromArgb(179, 52, 52)
            };
            e.CellStyle.Font = new Font(_overviewGrid.Font, FontStyle.Bold);
            return;
        }

        if (column.DataPropertyName == nameof(CDriveOverviewEntry.Selected) && !entry.SelectionEnabled)
        {
            e.CellStyle.BackColor = UiThemePalette.SurfaceMuted;
            e.CellStyle.SelectionBackColor = UiThemePalette.SurfaceMuted;
            e.CellStyle.SelectionForeColor = UiThemePalette.DisabledText;
        }
    }

    private void InfrequentGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (_infrequentGrid.Rows[e.RowIndex].DataBoundItem is not InfrequentSoftwareEntry entry)
        {
            return;
        }

        var column = _infrequentGrid.Columns[e.ColumnIndex];
        if (column.DataPropertyName == nameof(InfrequentSoftwareEntry.StatusText))
        {
            e.CellStyle.ForeColor = entry.StatusKind switch
            {
                InfrequentSoftwareStatusKind.RecommendedDeletion => Color.FromArgb(179, 52, 52),
                InfrequentSoftwareStatusKind.CurrentRunning => Color.FromArgb(29, 96, 166),
                InfrequentSoftwareStatusKind.ActiveRecent => Color.FromArgb(27, 125, 78),
                InfrequentSoftwareStatusKind.NoReliableUsageRecord => Color.FromArgb(92, 105, 117),
                InfrequentSoftwareStatusKind.Whitelisted => Color.FromArgb(92, 105, 117),
                InfrequentSoftwareStatusKind.Protected => Color.FromArgb(179, 52, 52),
                _ => Color.FromArgb(176, 103, 12)
            };
            e.CellStyle.Font = new Font(_infrequentGrid.Font, FontStyle.Bold);
            return;
        }

        if (column.DataPropertyName == nameof(InfrequentSoftwareEntry.UsageConfidenceText))
        {
            e.CellStyle.ForeColor = entry.UsageConfidence switch
            {
                AppUsageConfidence.High => Color.FromArgb(27, 125, 78),
                AppUsageConfidence.Medium => Color.FromArgb(176, 103, 12),
                AppUsageConfidence.Low => Color.FromArgb(179, 109, 31),
                _ => Color.FromArgb(92, 105, 117)
            };
            e.CellStyle.Font = new Font(_infrequentGrid.Font, FontStyle.Bold);
            return;
        }

        if (column.DataPropertyName == nameof(InfrequentSoftwareEntry.Selected) && (entry.IsProtected || entry.IsWhitelisted || !entry.CanDeepDelete))
        {
            e.CellStyle.BackColor = UiThemePalette.SurfaceMuted;
            e.CellStyle.SelectionBackColor = UiThemePalette.SurfaceMuted;
            e.CellStyle.SelectionForeColor = UiThemePalette.DisabledText;
        }
    }

    private void GridCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not CleanupSelectionRow row)
        {
            return;
        }

        var column = _grid.Columns[e.ColumnIndex].DataPropertyName;
        e.ToolTipText = column switch
        {
            _ when e.ColumnIndex == _cleanupIconColumn.Index => BuildCleanupIconTooltip(row),
            nameof(CleanupSelectionRow.Name) => $"{row.Name}\r\n路径：{row.Path}",
            nameof(CleanupSelectionRow.TypeDescription) => row.TypeDescription,
            nameof(CleanupSelectionRow.CandidateKindText) => $"{row.CandidateKindText}\r\n{row.Note}",
            nameof(CleanupSelectionRow.LocationSummary) => $"位置：{row.Path}",
            nameof(CleanupSelectionRow.RecommendationText) => row.HasRecentRunResult
                ? $"上次处理：{row.LastRunStatus}\r\n{row.LastRunMessage}"
                : $"建议：{row.RecommendationText}\r\n说明：{row.Note}\r\n白名单提示：{row.WhitelistHintText}",
            nameof(CleanupSelectionRow.ImpactText) => row.ImpactText,
            nameof(CleanupSelectionRow.ImpactSeverityText) => $"严重度：{row.ImpactSeverityText}\r\n{row.ImpactText}",
            _ => string.Empty
        };
    }

    private void OverviewGridCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _overviewGrid.Rows[e.RowIndex].DataBoundItem is not CDriveOverviewEntry entry)
        {
            return;
        }

        var column = _overviewGrid.Columns[e.ColumnIndex].DataPropertyName;
        e.ToolTipText = column switch
        {
            _ when e.ColumnIndex == _overviewIconColumn.Index => BuildOverviewIconTooltip(entry),
            nameof(CDriveOverviewEntry.Name) => $"{entry.Name}\r\n路径：{entry.Path}",
            nameof(CDriveOverviewEntry.Path) => entry.Path,
            nameof(CDriveOverviewEntry.PurposeText) => entry.PurposeText,
            nameof(CDriveOverviewEntry.RecommendationText) => $"{entry.RecommendationText}\r\n迁移提示：{entry.MigrationHintText}",
            nameof(CDriveOverviewEntry.OperationHintText) => $"{entry.OperationHintText}\r\n{entry.MigrationHintText}\r\n双击行会打开文件夹，单击勾选用于迁移/删除。",
            nameof(CDriveOverviewEntry.ProtectionText) => $"{entry.ProtectionText}\r\n{entry.RecommendationText}",
            _ => string.Empty
        };
    }

    private void InfrequentGridCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _infrequentGrid.Rows[e.RowIndex].DataBoundItem is not InfrequentSoftwareEntry entry)
        {
            return;
        }

        var column = _infrequentGrid.Columns[e.ColumnIndex].DataPropertyName;
        e.ToolTipText = column switch
        {
            nameof(InfrequentSoftwareEntry.IconImage) => string.IsNullOrWhiteSpace(entry.IconSourcePath)
                ? $"{entry.DisplayName}\r\n暂时没有拿到专属图标，先用通用程序图标显示。"
                : $"{entry.DisplayName}\r\n图标来源：{entry.IconSourcePath}",
            nameof(InfrequentSoftwareEntry.DisplayName) => $"{entry.DisplayName}\r\n主目录：{entry.InstallRoot}",
            nameof(InfrequentSoftwareEntry.InstallRoot) => entry.InstallRoot,
            nameof(InfrequentSoftwareEntry.LastUsedText) => $"最近使用：{entry.LastUsedText}\r\n{entry.UsageEvidenceText}",
            nameof(InfrequentSoftwareEntry.UnusedDaysText) => entry.IsCurrentlyRunning
                ? $"当前正在使用\r\n{entry.UsageEvidenceText}"
                : entry.UnusedDays.HasValue
                ? $"距今 {entry.UnusedDays.Value} 天未用"
                : "没有拿到可靠使用记录，不自动建议删除。",
            nameof(InfrequentSoftwareEntry.UsageSourceText) => $"当前依据：{entry.UsageSourceText}\r\n{entry.UsageEvidenceText}",
            nameof(InfrequentSoftwareEntry.UsageConfidenceText) => $"可信度：{entry.UsageConfidenceText}\r\n{entry.UsageEvidenceText}",
            nameof(InfrequentSoftwareEntry.StatusText) => $"{entry.StatusText}\r\n{entry.RecommendationText}\r\n{entry.RunningProcessText}",
            nameof(InfrequentSoftwareEntry.RecommendationText) => $"{entry.RecommendationText}\r\n删除后果：{entry.DeletionImpactText}",
            _ => string.Empty
        };
    }

    private Image GetCleanupIcon(CleanupSelectionRow row)
    {
        var iconKey = GetCleanupIconKey(row);
        if (!string.IsNullOrWhiteSpace(iconKey) && _cleanupIcons.TryGetValue(iconKey, out var image))
        {
            return image;
        }

        return ApplicationIconCache.GetIcon(ResolveCleanupIconRequest(row), GetCurrentIconSize(_grid, _cleanupIconColumn));
    }

    private Image GetOverviewIcon(CDriveOverviewEntry entry)
    {
        var iconKey = GetOverviewIconKey(entry);
        if (!string.IsNullOrWhiteSpace(iconKey) && _overviewIcons.TryGetValue(iconKey, out var image))
        {
            return image;
        }

        return ApplicationIconCache.GetIcon(ResolveOverviewIconRequest(entry), GetCurrentIconSize(_overviewGrid, _overviewIconColumn));
    }

    private static string BuildCleanupIconTooltip(CleanupSelectionRow row)
    {
        var iconSourcePath = ResolveCleanupIconSourcePath(row);
        return string.IsNullOrWhiteSpace(iconSourcePath)
            ? $"{row.Name}\r\n暂时没有识别到专属图标，先用通用图标显示。"
            : $"{row.Name}\r\n图标来源：{iconSourcePath}";
    }

    private static string BuildOverviewIconTooltip(CDriveOverviewEntry entry)
    {
        var iconSourcePath = ResolveOverviewIconSourcePath(entry);
        return string.IsNullOrWhiteSpace(iconSourcePath)
            ? $"{entry.Name}\r\n暂时没有识别到专属图标，先用通用图标显示。"
            : $"{entry.Name}\r\n图标来源：{iconSourcePath}";
    }

    private string GetCleanupIconKey(CleanupSelectionRow row)
    {
        var request = ResolveCleanupIconRequest(row);
        var primary = string.IsNullOrWhiteSpace(row.Path) ? row.Name : row.Path.Trim();
        return $"{GetCurrentIconSize(_grid, _cleanupIconColumn)}|{request.BuildCacheKey()}|{primary}";
    }

    private string GetOverviewIconKey(CDriveOverviewEntry entry)
    {
        var request = ResolveOverviewIconRequest(entry);
        var primary = string.IsNullOrWhiteSpace(entry.Path) ? entry.Name : entry.Path.Trim();
        return $"{GetCurrentIconSize(_overviewGrid, _overviewIconColumn)}|{request.BuildCacheKey()}|{primary}";
    }

    private static IconLookupRequest ResolveCleanupIconRequest(CleanupSelectionRow row)
    {
        var iconSourcePath = ResolveCleanupIconSourcePath(row);
        var installRoot = ResolveCleanupIconInstallRoot(row);
        return IconSemanticResolver.ForCleanupRow(row, iconSourcePath, installRoot);
    }

    private static IconLookupRequest ResolveOverviewIconRequest(CDriveOverviewEntry entry)
    {
        var iconSourcePath = ResolveOverviewIconSourcePath(entry);
        var installRoot = ResolveOverviewIconInstallRoot(entry);
        return IconSemanticResolver.ForOverviewEntry(entry, iconSourcePath, installRoot);
    }

    private static string ResolveCleanupIconSourcePath(CleanupSelectionRow row)
    {
        if (File.Exists(row.Item.IconSourcePath))
        {
            return row.Item.IconSourcePath;
        }

        var path = row.Path;
        if (File.Exists(path))
        {
            return path;
        }

        var validationTarget = ResolveExecutable(path, string.Empty, row.Item.CompanionPaths);
        if (!string.IsNullOrWhiteSpace(validationTarget))
        {
            return validationTarget;
        }

        return string.Empty;
    }

    private static string ResolveCleanupIconInstallRoot(CleanupSelectionRow row)
    {
        if (Directory.Exists(row.Item.IconInstallRoot))
        {
            return row.Item.IconInstallRoot;
        }

        if (Directory.Exists(row.Path))
        {
            return row.Path;
        }

        return row.Item.CompanionPaths.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static string ResolveOverviewIconSourcePath(CDriveOverviewEntry entry)
    {
        if (File.Exists(entry.IconSourcePath))
        {
            return entry.IconSourcePath;
        }

        if (File.Exists(entry.Path))
        {
            return entry.Path;
        }

        return ResolveExecutable(entry.Path, entry.ValidationTargetExe, entry.CompanionPaths);
    }

    private static string ResolveOverviewIconInstallRoot(CDriveOverviewEntry entry)
    {
        if (Directory.Exists(entry.IconInstallRoot))
        {
            return entry.IconInstallRoot;
        }

        if (Directory.Exists(entry.Path))
        {
            return entry.Path;
        }

        return entry.CompanionPaths.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static string ResolveExecutable(string rootPath, string validationTargetExe, IReadOnlyList<string> companionPaths)
    {
        if (!string.IsNullOrWhiteSpace(validationTargetExe))
        {
            var directValidationTarget = TryResolveExecutable(rootPath, validationTargetExe);
            if (!string.IsNullOrWhiteSpace(directValidationTarget))
            {
                return directValidationTarget;
            }

            foreach (var companionPath in companionPaths)
            {
                var companionValidationTarget = TryResolveExecutable(companionPath, validationTargetExe);
                if (!string.IsNullOrWhiteSpace(companionValidationTarget))
                {
                    return companionValidationTarget;
                }
            }
        }

        var firstExecutable = TryResolveFirstExecutable(rootPath);
        if (!string.IsNullOrWhiteSpace(firstExecutable))
        {
            return firstExecutable;
        }

        foreach (var companionPath in companionPaths)
        {
            var companionExecutable = TryResolveFirstExecutable(companionPath);
            if (!string.IsNullOrWhiteSpace(companionExecutable))
            {
                return companionExecutable;
            }
        }

        return string.Empty;
    }

    private static string TryResolveExecutable(string rootPath, string executableName)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(executableName) || !Directory.Exists(rootPath))
        {
            return string.Empty;
        }

        try
        {
            return Directory.EnumerateFiles(rootPath, executableName, SearchOption.TopDirectoryOnly).FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryResolveFirstExecutable(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return string.Empty;
        }

        try
        {
            return Directory.EnumerateFiles(rootPath, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Panel CreateSurfacePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = UiThemePalette.SurfaceRaised,
            Margin = Padding.Empty
        };
        UiThemePalette.EnableDoubleBuffering(panel);
        UiThemePalette.AttachBorderPainter(panel);
        return panel;
    }

    private void ConfigureViewModeButton(Button button, string text, MainViewMode viewMode)
    {
        button.Text = text;
        button.Click += (_, _) => SwitchView(viewMode);
        button.Margin = new Padding(0, 0, 8, 0);
        button.FlatStyle = FlatStyle.Flat;
        UiScaleHelper.ApplyButtonSizing(button, text, 116, 36, minHeight: 38, verticalPadding: 16);
    }

    private void UpdateViewModeButtons()
    {
        ApplyPillStyle(_cleanupViewButton, _viewMode == MainViewMode.CleanupCandidates);
        ApplyPillStyle(_overviewViewButton, _viewMode == MainViewMode.CDriveOverview);
        ApplyPillStyle(_infrequentViewButton, _viewMode == MainViewMode.InfrequentApps);
    }

    private void RefreshToolbarButtonSizing()
    {
        var dense = IsCompactLayout;
        var ultra = IsUltraCompactLayout;
        ApplyButtonFont(_scanButton, dense, ultra);
        ApplyButtonFont(_recommendedButton, dense, ultra);
        ApplyButtonFont(_cDriveAdviceButton, dense, ultra);
        ApplyButtonFont(_cleanSelectedButton, dense, ultra);
        ApplyButtonFont(_safeCleanButton, dense, ultra, FontStyle.Bold);
        ApplyButtonFont(_migrateSelectedButton, dense, ultra);
        ApplyButtonFont(_deleteOverviewSelectedButton, dense, ultra);
        ApplyButtonFont(_scheduleSettingsButton, dense, ultra);
        ApplyButtonFont(_whitelistButton, dense, ultra);
        ApplyButtonFont(_moreActionsButton, dense, ultra);

        ApplyActionButtonSizing(_scanButton, "重新扫描", 112, dense, ultra);
        ApplyActionButtonSizing(_recommendedButton, "勾选建议", 118, dense, ultra);
        ApplyActionButtonSizing(_cDriveAdviceButton, "C盘建议", 112, dense, ultra);
        ApplyActionButtonSizing(_cleanSelectedButton, "处理勾选", 132, dense, ultra);
        ApplyActionButtonSizing(_safeCleanButton, "一键安全清理", 154, dense, ultra);
        ApplyActionButtonSizing(_migrateSelectedButton, "开始迁移已勾选", 160, dense, ultra);
        ApplyActionButtonSizing(_deleteOverviewSelectedButton, "删除已勾选", 136, dense, ultra);
        ApplyActionButtonSizing(_scheduleSettingsButton, "自动清理", 118, dense, ultra);
        ApplyActionButtonSizing(_whitelistButton, "加入白名单", 126, dense, ultra);
        ApplyActionButtonSizing(_moreActionsButton, "更多", 92, dense, ultra);
    }

    private void RefreshScaledUi(bool forceLayout = false, bool refreshGridContent = true)
    {
        SuspendLayout();
        _rootLayout.SuspendLayout();
        try
        {
            ApplyLayoutDensity(forceLayout);
            RefreshToolbarButtonSizing();
            RefreshPillButtonSizing();
            RefreshFilterControlSizing();
            UpdateResponsiveLabelWidths();
            RefreshGridDensity();
            UpdateGridPresentation(refreshGridContent);
            RefreshJobCardWidths();
            ApplyCompactHeaderTexts();
        }
        finally
        {
            _rootLayout.ResumeLayout(true);
            ResumeLayout(true);
        }
    }

    private void RefreshPillButtonSizing()
    {
        var dense = IsCompactLayout;
        var ultra = IsUltraCompactLayout;
        ApplyButtonFont(_cleanupViewButton, dense, ultra);
        ApplyButtonFont(_overviewViewButton, dense, ultra);
        ApplyButtonFont(_infrequentViewButton, dense, ultra);
        RefreshPillButtonSizing(_cleanupViewButton, compact: false, dense, ultra);
        RefreshPillButtonSizing(_overviewViewButton, compact: false, dense, ultra);
        RefreshPillButtonSizing(_infrequentViewButton, compact: false, dense, ultra);

        foreach (var button in _driveButtons.Values)
        {
            ApplyButtonFont(button, dense, ultra);
            RefreshPillButtonSizing(button, compact: false, dense, ultra);
        }

        foreach (var button in _quickFilterButtons.Values)
        {
            ApplyButtonFont(button, dense, ultra, FontStyle.Regular, compactQuickFilter: true);
            RefreshPillButtonSizing(button, compact: true, dense, ultra);
        }
    }

    private void RefreshFilterControlSizing()
    {
        ApplyControlFont(_searchTextBox, IsUltraCompactLayout ? 8.35f : IsCompactLayout ? 8.6f : 9f);
        ApplyControlFont(_categoryComboBox, IsUltraCompactLayout ? 8.35f : IsCompactLayout ? 8.6f : 9f);
        ApplyControlFont(_modeComboBox, IsUltraCompactLayout ? 8.35f : IsCompactLayout ? 8.6f : 9f);
        ApplyControlFont(_sortComboBox, IsUltraCompactLayout ? 8.35f : IsCompactLayout ? 8.6f : 9f);

        var searchMinimumWidth = IsUltraCompactLayout ? 220 : IsCompactLayout ? 250 : 300;
        var searchHorizontalPadding = IsUltraCompactLayout ? 60 : IsCompactLayout ? 70 : 88;
        _searchTextBox.Width = Math.Max(searchMinimumWidth, UiScaleHelper.MeasureTextWidth(_searchTextBox.PlaceholderText ?? "搜索", searchMinimumWidth, searchHorizontalPadding, _searchTextBox.Font));
        ApplyComboBoxSizing(_categoryComboBox, IsUltraCompactLayout ? 126 : IsCompactLayout ? 138 : 150, IsUltraCompactLayout ? 54 : IsCompactLayout ? 62 : 74);
        ApplyComboBoxSizing(_modeComboBox, IsUltraCompactLayout ? 126 : IsCompactLayout ? 138 : 150, IsUltraCompactLayout ? 54 : IsCompactLayout ? 62 : 74);
        ApplyComboBoxSizing(_sortComboBox, IsUltraCompactLayout ? 142 : IsCompactLayout ? 156 : 172, IsUltraCompactLayout ? 54 : IsCompactLayout ? 62 : 74);
    }

    private void RefreshGridDensity()
    {
        ApplyGridDensity(_grid);
        ApplyGridDensity(_overviewGrid);
        ApplyGridDensity(_infrequentGrid);
        RefreshIconColumnPresentation();
    }

    private void ApplyGridDensity(DataGridView grid)
    {
        var cellFontSize = IsUltraCompactLayout ? 8.35f : IsCompactLayout ? 8.75f : 9f;
        var headerFontSize = IsUltraCompactLayout ? 8.4f : IsCompactLayout ? 8.8f : 9f;
        var headerMinHeight = IsUltraCompactLayout ? 32 : IsCompactLayout ? 36 : 42;
        var headerVerticalPadding = IsUltraCompactLayout ? 10 : IsCompactLayout ? 12 : 18;
        var rowMinHeight = IsUltraCompactLayout ? 28 : IsCompactLayout ? 32 : 38;
        var rowVerticalPadding = IsUltraCompactLayout ? 10 : IsCompactLayout ? 12 : 18;

        ApplyControlFont(grid, cellFontSize);
        grid.DefaultCellStyle.Font = grid.Font;

        var headerFont = grid.ColumnHeadersDefaultCellStyle.Font;
        if (headerFont is null || Math.Abs(headerFont.Size - headerFontSize) >= 0.01f || headerFont.Style != FontStyle.Bold)
        {
            headerFont = new Font("Microsoft YaHei UI", headerFontSize, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Font = headerFont;
        }

        grid.ColumnHeadersHeight = UiScaleHelper.MeasureGridHeaderHeight(headerFont, minHeight: headerMinHeight, verticalPadding: headerVerticalPadding);
        var rowHeight = UiScaleHelper.MeasureGridRowHeight(grid.Font, minHeight: rowMinHeight, verticalPadding: rowVerticalPadding);
        grid.RowTemplate.Height = rowHeight;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.Height != rowHeight)
            {
                row.Height = rowHeight;
            }
        }
    }

    private void RefreshIconColumnPresentation()
    {
        var cleanupIconSize = GetCurrentIconSize(_grid, _cleanupIconColumn);
        var overviewIconSize = GetCurrentIconSize(_overviewGrid, _overviewIconColumn);
        var infrequentIconSize = GetCurrentIconSize(_infrequentGrid, _infrequentIconColumn);

        _cleanupIconColumn.Width = ResolveIconColumnWidth(cleanupIconSize);
        _cleanupIconColumn.DefaultCellStyle.NullValue = ApplicationIconCache.GetIcon(IconSemanticResolver.DefaultCleanup(), cleanupIconSize);

        _overviewIconColumn.Width = ResolveIconColumnWidth(overviewIconSize);
        _overviewIconColumn.DefaultCellStyle.NullValue = ApplicationIconCache.GetIcon(IconSemanticResolver.DefaultOverview(), overviewIconSize);

        _infrequentIconColumn.Width = ResolveIconColumnWidth(infrequentIconSize);
        _infrequentIconColumn.DefaultCellStyle.NullValue = ApplicationIconCache.GetIcon(IconSemanticResolver.DefaultInfrequentApp(), infrequentIconSize);
    }

    private void ReloadDynamicIconsForCurrentDpi()
    {
        _cleanupIcons.Clear();
        _overviewIcons.Clear();

        foreach (var entry in _infrequentRows)
        {
            entry.IconImage = null;
        }

        RefreshIconColumnPresentation();

        QueueCleanupIconLoad(_visibleRows.ToList());
        QueueOverviewIconLoad(_visibleOverviewRows.ToList());
        QueueInfrequentIconLoad(_visibleInfrequentRows.ToList());
    }

    private int GetCurrentIconSize(DataGridView grid, DataGridViewImageColumn column)
    {
        var recommended = ApplicationIconCache.GetRecommendedIconSizeForDpi(DeviceDpi, logicalSize: 18);
        var displayBudget = ResolveIconDisplayBudget(grid, column);
        return ApplicationIconCache.GetRecommendedIconSize(Math.Min(recommended, displayBudget));
    }

    private static int ResolveIconColumnWidth(int iconSize)
    {
        return Math.Max(58, UiScaleHelper.MeasureGridColumnWidth("图标", 58, Math.Max(28, iconSize + 16)));
    }

    private static int ResolveIconDisplayBudget(DataGridView grid, DataGridViewImageColumn column)
    {
        var rowBudget = Math.Max(16, (grid.RowTemplate?.Height ?? 36) - 8);
        var columnBudget = Math.Max(16, (column.Width > 0 ? column.Width : 58) - 16);
        return Math.Min(rowBudget, columnBudget);
    }

    private static void ConfigureActionButton(Button button, string text, int minimumWidth, bool primary = false)
    {
        button.AutoSize = false;
        button.Text = text;
        button.AutoEllipsis = false;
        button.Padding = new Padding(18, 0, 18, 0);
        ApplyActionButtonSizing(button, text, minimumWidth);
        button.Margin = new Padding(0, 0, 10, 0);
        button.TextAlign = ContentAlignment.MiddleCenter;
        UiThemePalette.ApplyButtonStyle(button, primary);
    }

    private static void ApplyActionButtonSizing(Button button, string text, int minimumWidth, bool dense = false, bool ultraDense = false)
    {
        UiScaleHelper.ApplyButtonSizing(
            button,
            text,
            minimumWidth,
            ultraDense ? 38 : dense ? 44 : 56,
            minHeight: ultraDense ? 32 : dense ? 36 : 42,
            verticalPadding: ultraDense ? 8 : dense ? 12 : 18);
    }

    private static void ConfigureFilterLabel(Label label, string text)
    {
        label.AutoSize = true;
        label.Anchor = AnchorStyles.Left;
        label.ForeColor = UiThemePalette.TextSecondary;
        label.Margin = new Padding(0, 6, 8, 0);
        label.Text = text;
    }

    private static Label CreateFilterLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = UiThemePalette.TextSecondary,
            Margin = new Padding(0, 6, 8, 0),
            Text = text
        };
    }

    private static void ConfigureComboBox(ComboBox comboBox, int width)
    {
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Width = width;
        comboBox.Margin = new Padding(0, 0, 16, 0);
        UiThemePalette.ApplyComboBoxStyle(comboBox);
    }

    private static void ApplyComboBoxSizing(ComboBox comboBox, int minimumWidth, int horizontalPadding)
    {
        var optionTexts = comboBox.Items
            .Cast<object>()
            .Select(item => item is FilterOption option ? option.Text : item?.ToString() ?? string.Empty)
            .ToList();
        var width = UiScaleHelper.MeasureOptionWidth(optionTexts, minimumWidth, horizontalPadding, comboBox.Font);
        comboBox.Width = width;
        comboBox.DropDownWidth = Math.Max(width, UiScaleHelper.MeasureOptionWidth(optionTexts, width, horizontalPadding + 18, comboBox.Font));
    }

    private static Button CreatePillButton(string text, bool compact = false)
    {
        var button = new ThemedButton
        {
            AutoSize = false,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(compact ? 10 : 12, 0, compact ? 10 : 12, 0),
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            FlatStyle = FlatStyle.Flat
        };
        RefreshPillButtonSizing(button, compact);
        ApplyPillStyle(button, selected: false, compact);
        return button;
    }

    private static void RefreshPillButtonSizing(Button button, bool compact, bool dense = false, bool ultraDense = false)
    {
        var minimumWidth = compact
            ? (ultraDense ? 68 : dense ? 76 : 84)
            : (ultraDense ? 78 : dense ? 86 : 96);
        var horizontalPadding = compact
            ? (ultraDense ? 14 : dense ? 20 : 28)
            : (ultraDense ? 20 : dense ? 28 : 36);
        var minimumHeight = compact
            ? (ultraDense ? 26 : dense ? 30 : 34)
            : (ultraDense ? 28 : dense ? 32 : 36);
        var verticalPadding = compact
            ? (ultraDense ? 6 : dense ? 10 : 14)
            : (ultraDense ? 8 : dense ? 12 : 16);
        UiScaleHelper.ApplyButtonSizing(button, button.Text, minimumWidth, horizontalPadding, minimumHeight, verticalPadding);
    }

    private static void ApplyPillStyle(Button button, bool selected, bool compact = false)
    {
        UiThemePalette.ApplyPillButtonStyle(button, selected);
        if (compact)
        {
            var targetStyle = selected ? FontStyle.Bold : FontStyle.Regular;
            if (button.Font.Style != targetStyle)
            {
                button.Font = new Font("Microsoft YaHei UI", button.Font.Size, targetStyle);
            }
        }
    }

    private static int MeasureGridColumnWidth(string text, int extraPadding)
    {
        return UiScaleHelper.MeasureGridColumnWidth(text, 72, extraPadding);
    }

    private static bool MatchesOverviewSearch(CDriveOverviewEntry entry, string search)
    {
        return entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Path.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.PurposeText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.RecommendationText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.InstalledAppName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesInfrequentSearch(InfrequentSoftwareEntry entry, string search)
    {
        return entry.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.InstallRoot.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.RecommendationText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.UsageSourceText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.UsageEvidenceText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || entry.PrimaryDrive.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetOverviewCategoryPriority(CDriveOverviewEntry entry)
    {
        return entry.Category switch
        {
            "第三方已安装应用" => 0,
            "用户数据目录" => 1,
            "应用缓存/数据" => 2,
            "未知大目录" => 3,
            "系统共享组件" => 4,
            "系统核心" => 5,
            _ => 9
        };
    }

    private static bool MatchesSearch(CleanupSelectionRow row, string search)
    {
        return row.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Path.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.TypeDescription.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.Note.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.ImpactText.Contains(search, StringComparison.OrdinalIgnoreCase)
            || row.DriveName.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpecificDrive(string driveName)
    {
        return !string.IsNullOrWhiteSpace(driveName)
            && !string.Equals(driveName, "全部盘", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(driveName, DriveAllKey, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetDrivePriority(string? driveName)
    {
        if (string.IsNullOrWhiteSpace(driveName))
        {
            return 99;
        }

        if (driveName.Equals("C", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (driveName.Equals("全部盘", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        var first = char.ToUpperInvariant(driveName[0]);
        return first is >= 'A' and <= 'Z' ? 10 + first - 'A' : 98;
    }

    private static string GetComboBoxValue(ComboBox comboBox)
    {
        return comboBox.SelectedItem is FilterOption option ? option.Value : CleanupFilterState.AllValue;
    }

    private static void SelectComboBoxValue(ComboBox comboBox, string value)
    {
        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is FilterOption option && string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private readonly record struct FrozenAutoSizeState(
        bool AutoSize,
        AutoSizeMode AutoSizeMode,
        Size MinimumSize,
        Size MaximumSize);

    private static class NativeMethods
    {
        public const int WmSetRedraw = 0x000B;
        public const int RdwInvalidate = 0x0001;
        public const int RdwErase = 0x0004;
        public const int RdwAllChildren = 0x0080;
        public const int RdwUpdateNow = 0x0100;
        public const int RdwFrame = 0x0400;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, int flags);
    }

    private sealed record FilterOption(string Value, string Text)
    {
        public override string ToString() => Text;
    }
}
