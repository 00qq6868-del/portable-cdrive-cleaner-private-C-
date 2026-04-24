using System.ComponentModel;
using PortableCDriveCleaner.Infrastructure;
using PortableCDriveCleaner.Models;
using PortableCDriveCleaner.Services;

namespace PortableCDriveCleaner.Forms;

public sealed class CDriveSuggestionDialog : Form
{
    private readonly MigrationService _migrationService;
    private readonly bool _readOnlyMode;
    private readonly BindingList<MigrationCandidate> _rows;
    private readonly ScrollFriendlyDataGridView _grid = new();
    private readonly DataGridViewCheckBoxColumn _selectColumn = new();
    private readonly DataGridViewImageColumn _iconColumn = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _phaseStatusLabel = new();
    private readonly Label _teachingLabel = new();
    private readonly Button _migrateButton = new();
    private readonly Button _deleteButton = new();
    private readonly Button _revealButton = new();
    private readonly Dictionary<string, Image> _candidateIcons = new(StringComparer.OrdinalIgnoreCase);
    private bool _resizeDragInProgress;
    private bool _pendingIconColumnRefresh;
    private bool _isPartialResult;
    private string _phaseLabel = string.Empty;
    private CancellationTokenSource? _iconLoadCts;

    public CDriveSuggestionDialog(
        string appName,
        IReadOnlyList<MigrationCandidate> candidates,
        MigrationService migrationService,
        bool readOnlyMode)
    {
        _migrationService = migrationService;
        _readOnlyMode = readOnlyMode;
        _rows = new BindingList<MigrationCandidate>(CloneCandidates(candidates));

        Text = $"{appName} - C盘建议";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimizeBox = true;
        MaximizeBox = true;
        ShowInTaskbar = true;
        MinimumSize = new Size(1000, 620);
        ClientSize = new Size(1220, 760);
        UiThemePalette.ApplyFormChrome(this);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

        BuildUi();
        ApplyThemeColors();
        Resize += (_, _) =>
        {
            if (!_resizeDragInProgress)
            {
                UpdateResponsiveLayout();
            }
        };
        ResizeBegin += (_, _) => _resizeDragInProgress = true;
        ResizeEnd += (_, _) =>
        {
            _resizeDragInProgress = false;
            UpdateResponsiveLayout();
            FlushPendingIconInvalidate();
        };
        Shown += (_, _) => UpdateResponsiveLayout();
        DpiChanged += (_, _) => BeginInvoke(new Action(() =>
        {
            UiScaleHelper.RefreshRegisteredButtonSizing(this);
            RefreshFooterButtonSizing();
            UpdateResponsiveLayout();
        }));
        FormClosing += (_, _) => _iconLoadCts?.Cancel();
        UpdateSummary();
    }

    public bool RefreshRequired { get; private set; }
    public string? ResultLogPath { get; private set; }
    public MigrationRunResult? LastRunResult { get; private set; }
    public IReadOnlyList<MigrationCandidate> DeleteCandidates { get; private set; } = [];

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 16)
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
            Text = "C盘建议"
        }, 0, 0);

        var summaryCard = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            Padding = new Padding(16, 14, 16, 14),
            Margin = Padding.Empty
        };
        UiThemePalette.ApplySurface(summaryCard);
        root.Controls.Add(summaryCard, 0, 1);

        var summaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2
        };
        summaryCard.Controls.Add(summaryLayout);

        _summaryLabel.AutoSize = true;
        _summaryLabel.MaximumSize = new Size(1120, 0);
        _summaryLabel.ForeColor = Color.FromArgb(72, 84, 96);
        summaryLayout.Controls.Add(_summaryLabel, 0, 0);

        _phaseStatusLabel.AutoSize = true;
        _phaseStatusLabel.MaximumSize = new Size(1120, 0);
        _phaseStatusLabel.ForeColor = Color.FromArgb(46, 96, 78);
        _phaseStatusLabel.Font = new Font("Microsoft YaHei UI", 8.6f, FontStyle.Bold);
        _phaseStatusLabel.Margin = new Padding(0, 6, 0, 0);
        summaryLayout.Controls.Add(_phaseStatusLabel, 0, 1);

        _teachingLabel.AutoSize = true;
        _teachingLabel.Margin = new Padding(0, 8, 0, 0);
        _teachingLabel.ForeColor = Color.FromArgb(22, 94, 74);
        _teachingLabel.Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        _teachingLabel.Text = "双击行 = 打开文件夹    在资源管理器中定位 = 打开并选中当前项目    非系统保护项都可勾选；绿色可直接迁移，黄色/红色按下面按钮继续处理";
        summaryLayout.Controls.Add(_teachingLabel, 0, 2);

        var gridCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(0),
            Margin = new Padding(0, 12, 0, 0)
        };
        UiThemePalette.ApplySurface(gridCard);
        root.Controls.Add(gridCard, 0, 2);

        ConfigureGrid();
        _grid.DataSource = _rows;
        gridCard.Controls.Add(_grid);
        QueueIconLoad(_rows.ToList());

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        root.Controls.Add(footer, 0, 3);

        var closeButton = CreateFooterButton("关闭");
        closeButton.Click += (_, _) => Close();
        footer.Controls.Add(closeButton);

        _revealButton.Text = "在资源管理器中定位";
        ConfigureFooterButton(_revealButton);
        _revealButton.Click += (_, _) => OpenSelectedSuggestion();
        footer.Controls.Add(_revealButton);

        _migrateButton.Text = "开始迁移已勾选";
        ConfigureFooterButton(_migrateButton, primary: true);
        _migrateButton.Click += async (_, _) => await StartMigrationAsync();
        footer.Controls.Add(_migrateButton);

        _deleteButton.Text = "删除已勾选";
        ConfigureFooterButton(_deleteButton);
        _deleteButton.Click += (_, _) => RequestDeleteSelected();
        footer.Controls.Add(_deleteButton);
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
        _grid.ReadOnly = false;
        _grid.BorderStyle = BorderStyle.None;
        _grid.BackgroundColor = Color.White;
        _grid.GridColor = Color.FromArgb(226, 231, 236);
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.ColumnHeadersHeight = UiScaleHelper.MeasureGridHeaderHeight(new Font("Microsoft YaHei UI", 9, FontStyle.Bold), minHeight: 42, verticalPadding: 18);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.RowTemplate.Height = UiScaleHelper.MeasureGridRowHeight(_grid.Font, minHeight: 36, verticalPadding: 18);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(242, 245, 247);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(40, 55, 68);
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.DefaultCellStyle.BackColor = Color.White;
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(43, 56, 66);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(239, 246, 243);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(36, 48, 58);
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (e.ColumnIndex == _selectColumn.Index)
            {
                ToggleCandidateSelection(e.RowIndex);
                return;
            }

            var focusColumnIndex = Math.Min(_grid.Columns.Count - 1, Math.Max(2, e.ColumnIndex));
            _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[focusColumnIndex];
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex != _selectColumn.Index)
            {
                if (_grid.Rows[e.RowIndex].DataBoundItem is MigrationCandidate candidate)
                {
                    ShellHelper.RevealPath(candidate.SourcePath);
                }
            }
        };
        _grid.DataError += (_, _) => { };
        _grid.DataBindingComplete += (_, _) =>
        {
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                if (gridRow.DataBoundItem is not MigrationCandidate candidate)
                {
                    continue;
                }

                gridRow.Cells[_selectColumn.Index].ReadOnly = _readOnlyMode || !IsCandidateCheckable(candidate);
            }

            if (_grid.Rows.Count > 0 && _grid.CurrentCell is null)
            {
                _grid.CurrentCell = _grid.Rows[0].Cells[Math.Min(_grid.Columns.Count - 1, 2)];
            }
        };
        _grid.SelectionChanged += (_, _) => UpdateSummary();
        _grid.CellFormatting += GridCellFormatting;
        _grid.CellToolTipTextNeeded += GridCellToolTipTextNeeded;

        _selectColumn.DataPropertyName = nameof(MigrationCandidate.Selected);
        _selectColumn.HeaderText = "勾选";
        _selectColumn.Width = Math.Max(72, TextRenderer.MeasureText("勾选", SystemFonts.MessageBoxFont).Width + 34);
        _selectColumn.ReadOnly = false;
        _selectColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        _selectColumn.ThreeState = false;
        _grid.Columns.Add(_selectColumn);
        _iconColumn.Name = "CandidateIcon";
        _iconColumn.HeaderText = "图标";
        _iconColumn.Width = UiScaleHelper.MeasureGridColumnWidth("图标", 58, 24);
        _iconColumn.ReadOnly = true;
        _iconColumn.ImageLayout = DataGridViewImageCellLayout.Zoom;
        _iconColumn.SortMode = DataGridViewColumnSortMode.NotSortable;
        _iconColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _iconColumn.DefaultCellStyle.NullValue = ApplicationIconCache.GetSmallIcon(null, null);
        _grid.Columns.Add(_iconColumn);
        _grid.Columns.Add(CreateTextColumn(nameof(MigrationCandidate.Name), "名称", 180));
        _grid.Columns.Add(CreateTextColumn(nameof(MigrationCandidate.SizeText), "体积", 96, alignRight: true));
        _grid.Columns.Add(CreateTextColumn(nameof(MigrationCandidate.Category), "类型", 128));
        _grid.Columns.Add(CreateTextColumn(nameof(MigrationCandidate.MigrationModeText), "迁移方式", 120));
        _grid.Columns.Add(CreateFillColumn(nameof(MigrationCandidate.SourcePath), "当前位置", 28));
        _grid.Columns.Add(CreateFillColumn(nameof(MigrationCandidate.PurposeText), "目录用途", 22));
        _grid.Columns.Add(CreateFillColumn(nameof(MigrationCandidate.RecommendationText), "说明", 24));
    }

    public void ApplySnapshot(ScanSnapshot snapshot)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => ApplySnapshot(snapshot));
            return;
        }

        SyncCandidates(snapshot.MigrationCandidates, snapshot.IsPartialResult, snapshot.PhaseLabel);
    }

    private void ToggleCandidateSelection(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count || _grid.Rows[rowIndex].DataBoundItem is not MigrationCandidate candidate)
        {
            return;
        }

        if (_readOnlyMode || !IsCandidateCheckable(candidate))
        {
            UpdateSummary();
            return;
        }

        candidate.Selected = !candidate.Selected;
        var focusColumnIndex = Math.Min(_grid.Columns.Count - 1, 2);
        _grid.CurrentCell = _grid.Rows[rowIndex].Cells[focusColumnIndex];
        _grid.InvalidateRow(rowIndex);
        UpdateSummary();
    }

    private async Task StartMigrationAsync()
    {
        if (_readOnlyMode)
        {
            MessageBox.Show("当前处于只读模式。请重新以管理员身份启动后再执行迁移。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _rows.Where(candidate => candidate.Selected && IsCandidateCheckable(candidate)).ToList();
        var selectedForMigration = selected.Where(CanAutoMigrateCandidate).ToList();
        if (selectedForMigration.Count == 0)
        {
            MessageBox.Show("当前没有勾选可自动迁移的目录。黄色/红色项现在也能勾选，但它们需要按提示手动处理，不能直接自动搬家。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        MigrationPlan plan;
        try
        {
            plan = await Task.Run(() => _migrationService.BuildPlan(selectedForMigration));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"生成迁移计划失败：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirmation = MessageBox.Show(
            $"本次会把已勾选项目里标成“可自动迁移”的 {plan.TotalCount} 个目录迁到 {plan.TargetDriveName} 盘的“{plan.TargetRoot}”。\r\n\r\n迁移后会在原位置保留兼容联接，旧快捷方式和常见启动方式通常还能继续用。\r\n\r\n黄色/红色的手动项不会在这一步被强行自动搬家。\r\n\r\n是否开始迁移？",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);

        if (confirmation != DialogResult.OK)
        {
            return;
        }

        try
        {
            UseWaitCursor = true;
            Enabled = false;
            using var progressDialog = new OperationProgressDialog(Text, "正在迁移已勾选目录", "正在分析并迁移目录，请稍候。");
            progressDialog.Show(this);
            var progress = new Progress<DeploymentProgressUpdate>(update => progressDialog.Apply(update));
            var migrationProgress = new Progress<OperationProgress>(update =>
            {
                progressDialog.Apply(new DeploymentProgressUpdate
                {
                    Percent = update.IsIndeterminate ? Math.Max(8, update.Percent) : update.Percent,
                    Message = string.IsNullOrWhiteSpace(update.Message) ? update.Phase : $"{update.Phase}：{update.Message}",
                    DetailText = string.IsNullOrWhiteSpace(update.JobScope) ? string.Empty : $"处理范围：{update.JobScope}",
                    IsIndeterminate = update.IsIndeterminate,
                    ProcessedBytes = update.ProcessedBytes,
                    TotalBytes = update.TotalBytes
                });
            });
            var result = await Task.Run(() => _migrationService.Run(selected, migrationProgress));
            progressDialog.Close();
            LastRunResult = result;
            ResultLogPath = result.LogPath;
            RefreshRequired = result.SuccessCount > 0;

            if (result.SuccessCount > 0)
            {
                MessageBox.Show(
                    $"迁移完成：成功 {result.SuccessCount} 项，失败 {result.FailedCount} 项，已迁出 {result.MigratedBytesText}，实际搬运 {result.MigratedPathCount} 处目录，修正快捷方式 {result.UpdatedShortcutCount} 个。"
                    + (result.RolledBackCount > 0 ? $"\r\n其中有 {result.RolledBackCount} 项已自动回滚到原位置。" : string.Empty)
                    + "\r\n\r\n原路径会保留兼容联接，方便继续像以前一样打开。",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Close();
                return;
            }

            MessageBox.Show("这次迁移没有成功的项目，请查看日志后再重试。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"执行迁移失败：{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var autoSafeCount = _rows.Count(item => item.MigrationMode == MigrationMode.AutoSafe);
        var selectableCount = _rows.Count(IsCandidateCheckable);
        var guideOnlyCount = _rows.Count(item => item.MigrationMode == MigrationMode.GuideOnly);
        var protectedCount = _rows.Count(item => item.MigrationMode == MigrationMode.Protected);
        var selected = _rows.Where(item => item.Selected && IsCandidateCheckable(item)).ToList();
        var migrationReadyCount = selected.Count(CanAutoMigrateCandidate);
        var deleteReadyCount = selected.Count(item => item.CanDelete);
        var manualReviewCount = selected.Count(item => !CanAutoMigrateCandidate(item) && !item.CanDelete);

        _summaryLabel.Text = _rows.Count == 0
            ? _isPartialResult
                ? $"正在整理 C 盘建议，当前阶段：{GetPhaseDisplayText()}。这个窗口会自动补齐，不需要关掉重开。"
                : "当前没有整理出明显的 C 盘迁移建议。"
            : $"这里把 C 盘项目分成三类：可自动迁移 {autoSafeCount} 项，需手动处理 {guideOnlyCount} 项，高风险确认 {protectedCount} 项。当前可勾选 {selectableCount} 项。"
              + (selected.Count > 0
                  ? $" 当前已勾选 {selected.Count} 项 / {SizeFormatter.Format(selected.Sum(item => item.SizeBytes))}，其中可自动迁移 {migrationReadyCount} 项，可继续确认删除 {deleteReadyCount} 项，需按提示手动处理 {manualReviewCount} 项。"
                  : " 当前还没有勾选。");
        _phaseStatusLabel.Text = _isPartialResult
            ? $"后台还在继续补齐：{GetPhaseDisplayText()}。当前窗口会自动刷新。"
            : _rows.Count > 0
                ? $"已同步 {selectableCount} 项可勾选建议。绿色可直接迁移，黄色/红色按按钮继续确认。"
                : "当前没有可展示的 C 盘建议。";

        _migrateButton.Enabled = !_readOnlyMode && selected.Any(CanAutoMigrateCandidate);
        _deleteButton.Enabled = !_readOnlyMode && selected.Any(item => item.CanDelete);
    }

    private void RequestDeleteSelected()
    {
        if (_readOnlyMode)
        {
            MessageBox.Show("当前处于只读模式。请重新以管理员身份启动后再执行删除。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var selected = _rows
            .Where(candidate => candidate.Selected && candidate.CanDelete)
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("当前没有勾选可删除的目录。黄色/红色项现在可以勾选，但只有允许删除的目录才会进入删除确认；标准安装型应用仍不会被当垃圾整目录直接删掉。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            $"将把已勾选项目里允许删除的 {selected.Count} 个目录交给主窗口继续确认删除。\r\n\r\n这些目录删掉后会整目录消失，只有在你确定里面没有还要继续用的文件时才应该继续。",
            Text,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirmation != DialogResult.OK)
        {
            return;
        }

        DeleteCandidates = selected;
        Close();
    }

    private void OpenSelectedSuggestion()
    {
        if (_grid.CurrentRow?.DataBoundItem is not MigrationCandidate suggestion)
        {
            return;
        }

        ShellHelper.RevealPath(suggestion.SourcePath);
    }

    private void GridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not MigrationCandidate candidate)
        {
            return;
        }

        var column = _grid.Columns[e.ColumnIndex];
        if (column.DataPropertyName == nameof(MigrationCandidate.Selected) && !IsCandidateCheckable(candidate))
        {
            e.CellStyle.BackColor = UiThemePalette.SurfaceMuted;
            e.CellStyle.SelectionBackColor = UiThemePalette.SurfaceMuted;
            e.CellStyle.SelectionForeColor = UiThemePalette.DisabledText;
            return;
        }

        if (e.ColumnIndex == _iconColumn.Index)
        {
            e.Value = GetCandidateIcon(candidate);
            e.FormattingApplied = true;
            return;
        }

        if (column.DataPropertyName == nameof(MigrationCandidate.MigrationModeText))
        {
            e.CellStyle.ForeColor = candidate.MigrationMode switch
            {
                MigrationMode.AutoSafe => Color.FromArgb(27, 125, 78),
                MigrationMode.GuideOnly => Color.FromArgb(176, 103, 12),
                _ => Color.FromArgb(179, 52, 52)
            };
            e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
        }
    }

    private void GridCellToolTipTextNeeded(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not MigrationCandidate candidate)
        {
            return;
        }

        var propertyName = _grid.Columns[e.ColumnIndex].DataPropertyName;
        e.ToolTipText = propertyName switch
        {
            _ when e.ColumnIndex == _iconColumn.Index => BuildIconTooltip(candidate),
            nameof(MigrationCandidate.Name) => $"{candidate.Name}\r\n路径：{candidate.SourcePath}",
            nameof(MigrationCandidate.SourcePath) => candidate.SourcePath,
            nameof(MigrationCandidate.PurposeText) => candidate.PurposeText,
            nameof(MigrationCandidate.RecommendationText) => $"{candidate.RecommendationText}\r\n迁移提示：{candidate.MigrationHintText}",
            nameof(MigrationCandidate.MigrationModeText) => $"{candidate.MigrationModeText}\r\n{candidate.MigrationHintText}",
            _ => string.Empty
        };
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(string propertyName, string header, int width, bool alignRight = false)
    {
        return new DataGridViewTextBoxColumn
        {
            DataPropertyName = propertyName,
            HeaderText = header,
            Width = Math.Max(width, UiScaleHelper.MeasureGridColumnWidth(header, width, 30)),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = alignRight ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft
            }
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

    private void SyncCandidates(IReadOnlyList<MigrationCandidate> candidates, bool isPartialResult, string phaseLabel)
    {
        var selectedKeys = _rows
            .Where(candidate => candidate.Selected)
            .Select(GetCandidateSelectionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentKey = _grid.CurrentRow?.DataBoundItem is MigrationCandidate currentCandidate
            ? GetCandidateSelectionKey(currentCandidate)
            : string.Empty;
        var firstDisplayedRowIndex = -1;
        try
        {
            firstDisplayedRowIndex = _grid.FirstDisplayedScrollingRowIndex;
        }
        catch
        {
        }

        var ordered = CloneCandidates(candidates, selectedKeys);
        _isPartialResult = isPartialResult;
        _phaseLabel = phaseLabel ?? string.Empty;

        _grid.SuspendLayout();
        _rows.RaiseListChangedEvents = false;
        _rows.Clear();
        foreach (var candidate in ordered)
        {
            _rows.Add(candidate);
        }

        _rows.RaiseListChangedEvents = true;
        _rows.ResetBindings();
        QueueIconLoad(_rows.ToList());
        RestoreGridState(currentKey, firstDisplayedRowIndex);
        _grid.ResumeLayout();
        UpdateSummary();
    }

    private void RestoreGridState(string currentKey, int firstDisplayedRowIndex)
    {
        if (_grid.Rows.Count == 0)
        {
            return;
        }

        var targetRowIndex = 0;
        if (!string.IsNullOrWhiteSpace(currentKey))
        {
            var matchedRow = _grid.Rows
                .Cast<DataGridViewRow>()
                .FirstOrDefault(row => row.DataBoundItem is MigrationCandidate candidate
                    && string.Equals(GetCandidateSelectionKey(candidate), currentKey, StringComparison.OrdinalIgnoreCase));
            if (matchedRow is not null)
            {
                targetRowIndex = matchedRow.Index;
            }
        }

        var focusColumnIndex = Math.Min(_grid.Columns.Count - 1, 2);
        _grid.CurrentCell = _grid.Rows[targetRowIndex].Cells[focusColumnIndex];

        if (firstDisplayedRowIndex >= 0 && firstDisplayedRowIndex < _grid.Rows.Count)
        {
            try
            {
                _grid.FirstDisplayedScrollingRowIndex = firstDisplayedRowIndex;
            }
            catch
            {
            }
        }
    }

    private static List<MigrationCandidate> CloneCandidates(
        IEnumerable<MigrationCandidate> candidates,
        IReadOnlySet<string>? selectedKeys = null)
    {
        return candidates
            .OrderBy(candidate => candidate.MigrationMode)
            .ThenByDescending(candidate => candidate.SizeBytes)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate =>
            {
                var selectionKey = GetCandidateSelectionKey(candidate);
                return new MigrationCandidate
                {
                    Id = candidate.Id,
                    Name = candidate.Name,
                    SourcePath = candidate.SourcePath,
                    SizeBytes = candidate.SizeBytes,
                    Category = candidate.Category,
                    PurposeText = candidate.PurposeText,
                    RecommendationText = candidate.RecommendationText,
                    MigrationHintText = candidate.MigrationHintText,
                    MigrationMode = candidate.MigrationMode,
                    MigrationStrategy = candidate.MigrationStrategy,
                    AdapterKey = candidate.AdapterKey,
                    CompanionPaths = candidate.CompanionPaths,
                    ValidationTargetExe = candidate.ValidationTargetExe,
                    CanUseGenericJunctionRelocation = candidate.CanUseGenericJunctionRelocation,
                    SelectionEnabled = candidate.SelectionEnabled,
                    CanDelete = candidate.CanDelete,
                    InstalledAppName = candidate.InstalledAppName,
                    Selected = (selectedKeys?.Contains(selectionKey) ?? false) || candidate.Selected
                };
            })
            .ToList();
    }

    private static string GetCandidateSelectionKey(MigrationCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.SourcePath)
            ? candidate.Id.ToString("N")
            : candidate.SourcePath.Trim();
    }

    private void QueueIconLoad(IReadOnlyList<MigrationCandidate> candidates)
    {
        _iconLoadCts?.Cancel();
        if (candidates.Count == 0)
        {
            _grid.Invalidate();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _iconLoadCts = cancellation;
        _ = Task.Run(() => LoadCandidateIcons(candidates, cancellation.Token), cancellation.Token);
    }

    private void LoadCandidateIcons(IReadOnlyList<MigrationCandidate> candidates, CancellationToken cancellationToken)
    {
        var refreshed = 0;
        foreach (var candidate in candidates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var key = GetCandidateSelectionKey(candidate);
            if (_candidateIcons.ContainsKey(key))
            {
                continue;
            }

            _candidateIcons[key] = ApplicationIconCache.GetSmallIcon(ResolveCandidateIconSourcePath(candidate), ResolveCandidateInstallRoot(candidate));
            refreshed++;
            if (refreshed == 1 || refreshed % 8 == 0)
            {
                InvalidateIconColumn();
            }
        }

        InvalidateIconColumn();
    }

    private void InvalidateIconColumn()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (_resizeDragInProgress)
        {
            _pendingIconColumnRefresh = true;
            return;
        }

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _iconColumn.Index < 0)
                {
                    return;
                }

                if (_resizeDragInProgress)
                {
                    _pendingIconColumnRefresh = true;
                    return;
                }

                _grid.InvalidateColumn(_iconColumn.Index);
            }));
        }
        catch
        {
        }
    }

    private Image GetCandidateIcon(MigrationCandidate candidate)
    {
        var key = GetCandidateSelectionKey(candidate);
        if (_candidateIcons.TryGetValue(key, out var image))
        {
            return image;
        }

        return ApplicationIconCache.GetSmallIcon(null, null);
    }

    private static string BuildIconTooltip(MigrationCandidate candidate)
    {
        var iconSourcePath = ResolveCandidateIconSourcePath(candidate);
        return string.IsNullOrWhiteSpace(iconSourcePath)
            ? $"{candidate.Name}\r\n暂时没有识别到专属程序图标，先用官方兜底图标显示。"
            : $"{candidate.Name}\r\n图标来源：{iconSourcePath}";
    }

    private static string ResolveCandidateInstallRoot(MigrationCandidate candidate)
    {
        if (Directory.Exists(candidate.IconInstallRoot))
        {
            return candidate.IconInstallRoot;
        }

        return Directory.Exists(candidate.SourcePath) ? candidate.SourcePath : string.Empty;
    }

    private static string ResolveCandidateIconSourcePath(MigrationCandidate candidate)
    {
        if (File.Exists(candidate.IconSourcePath))
        {
            return candidate.IconSourcePath;
        }

        if (File.Exists(candidate.SourcePath))
        {
            return candidate.SourcePath;
        }

        if (!string.IsNullOrWhiteSpace(candidate.ValidationTargetExe))
        {
            var directValidationTarget = TryResolveExecutable(candidate.SourcePath, candidate.ValidationTargetExe);
            if (!string.IsNullOrWhiteSpace(directValidationTarget))
            {
                return directValidationTarget;
            }

            foreach (var companionPath in candidate.CompanionPaths)
            {
                var companionValidationTarget = TryResolveExecutable(companionPath, candidate.ValidationTargetExe);
                if (!string.IsNullOrWhiteSpace(companionValidationTarget))
                {
                    return companionValidationTarget;
                }
            }
        }

        var firstExecutable = TryResolveFirstExecutable(candidate.SourcePath);
        if (!string.IsNullOrWhiteSpace(firstExecutable))
        {
            return firstExecutable;
        }

        foreach (var companionPath in candidate.CompanionPaths)
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

    private string GetPhaseDisplayText()
    {
        if (!string.IsNullOrWhiteSpace(_phaseLabel))
        {
            return _phaseLabel;
        }

        return _rows.Count > 0 ? "已同步当前 C 盘建议" : "正在等待 C 盘完整结果";
    }

    private static bool IsCandidateCheckable(MigrationCandidate candidate)
    {
        return candidate.SelectionEnabled
            || candidate.CanDelete
            || candidate.MigrationMode != MigrationMode.Protected;
    }

    private static bool CanAutoMigrateCandidate(MigrationCandidate candidate)
    {
        return candidate.SelectionEnabled
            && candidate.MigrationMode == MigrationMode.AutoSafe
            && candidate.MigrationStrategy != MigrationStrategy.Protected;
    }

    private static Button CreateFooterButton(string text)
    {
        var button = new Button();
        button.Text = text;
        ConfigureFooterButton(button);
        return button;
    }

    private static void ConfigureFooterButton(Button button, bool primary = false)
    {
        var text = string.IsNullOrWhiteSpace(button.Text) ? "处理中" : button.Text;
        button.Margin = new Padding(10, 0, 0, 0);
        button.Padding = new Padding(18, 0, 18, 0);
        button.AutoEllipsis = false;
        UiThemePalette.ApplyButtonStyle(button, primary);
        button.TextAlign = ContentAlignment.MiddleCenter;
        UiScaleHelper.RegisterButtonSizing(button, text, 128, 48, minHeight: 42, verticalPadding: 18);
    }

    private void RefreshFooterButtonSizing()
    {
        RefreshFooterButtonSizing(_migrateButton);
        RefreshFooterButtonSizing(_deleteButton);
        RefreshFooterButtonSizing(_revealButton);
    }

    private static void RefreshFooterButtonSizing(Button button)
    {
        if (button is null)
        {
            return;
        }

        UiScaleHelper.RefreshRegisteredButtonSizing(button);
    }

    private void UpdateResponsiveLayout()
    {
        var wrapWidth = UiScaleHelper.MeasureWrapWidth(ClientSize.Width, 100, minWidth: 520);
        _summaryLabel.MaximumSize = new Size(wrapWidth, 0);
        _phaseStatusLabel.MaximumSize = new Size(wrapWidth, 0);
        _teachingLabel.MaximumSize = new Size(wrapWidth, 0);
    }

    private void ApplyThemeColors()
    {
        UiThemePalette.ApplyTreeTheme(this);
        UiThemePalette.ApplyDataGridTheme(_grid);
        _summaryLabel.ForeColor = UiThemePalette.TextSecondary;
        _phaseStatusLabel.ForeColor = UiThemePalette.AccentStrong;
        _teachingLabel.ForeColor = UiThemePalette.TextSecondary;
    }

    private void FlushPendingIconInvalidate()
    {
        if (!_pendingIconColumnRefresh || IsDisposed || !IsHandleCreated || _iconColumn.Index < 0)
        {
            return;
        }

        _pendingIconColumnRefresh = false;
        try
        {
            _grid.InvalidateColumn(_iconColumn.Index);
        }
        catch
        {
        }
    }
}
