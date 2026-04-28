# 源码修改台账

> 用途：专门记录“源码层面”的计划、已改、未改、改到一半被打断、已发布到哪一版。  
> 这里不是泛化日志，而是给任何 AI / 开发者一眼看懂“代码动到哪了”的总账。

## 使用规则
- 开始改代码前：先写“本轮准备修改”
- 每完成一个源码阶段：立即移到“已完成源码修改”
- 中途被打断：把未做完部分写到“进行中 / 被打断”
- 发布安装版后：更新“发布状态”
- 如果回滚、返工、改方向：写到“回查 / 回调 / 回退记录”

## 当前项目核心源码范围
- 主窗口：`Forms/MainForm.cs`
- C盘建议：`Forms/CDriveSuggestionDialog.cs`
- 扫描：`Services/ScanService.cs`
- 清理：`Services/CleanupService.cs`
- 后台任务：`Services/OperationManager.cs`
- 启动链路：`Program.cs`
- 发布与同步：`publish.ps1`
- GitHub 记忆与检查点：`tools/Record-Checkpoint.ps1`、`tools/Save-Handoff.ps1`

## 本轮准备修改

### 任务
- 图标高清显示与缩放清晰度专项收口第二轮

### 目标
- 把 `ApplicationIconCache` 从 `ExtractAssociatedIcon` 主链路升级为更接近桌面快捷方式质量的 Shell / 精确尺寸提取链
- 去掉主窗口和 `C盘建议` 里 `DataGridViewImageColumn.Zoom` 带来的二次缩放发糊
- 让列表图标尽量按目标尺寸直接输出，再以居中方式显示，而不是运行时再次拉伸
- 保留自动化三轮 QA，并在每次代码修改后重新发布安装版再跑 3 轮完整流程
- 把本轮开始、进行中、完成或中断状态持续写回 GitHub 持久记忆

### 计划涉及文件
- `Forms/MainForm.cs`
- `Forms/CDriveSuggestionDialog.cs`
- `Infrastructure/ApplicationIconCache.cs`
- `CURRENT_TASK.md`
- `INTERRUPTED_PROGRESS.md`
- `OPTIMIZATION_LOG.md`
- `AI_STATE.json`

### 预期动作
- 为 EXE / ICO / 普通文件分别走更合适的图标提取路径
- 优先请求与当前 DPI 桶匹配的图标尺寸，减少运行期重采样
- 主表和建议表的图标列改成不再 `Zoom`
- 继续把开始/进行中/完成/中断状态写回 GitHub

## 已完成源码修改

### 2026-04-27 01:56 图标提取链与二次缩放链第二轮收口
- 范围：
  - `Infrastructure/ApplicationIconCache.cs`
  - `Forms/MainForm.cs`
  - `Forms/CDriveSuggestionDialog.cs`
  - `tools/Run-Icon-Clarity-QA.ps1`
- 已做：
  - `ApplicationIconCache` 已移除 `Icon.ExtractAssociatedIcon(...)` 主提取链
  - 新增更接近桌面快捷方式显示链路的 Shell 图标提取：
    - 支持保留图标资源索引
    - 支持 `DisplayIcon` 常见的 `路径,索引` 形式
    - 优先按目标尺寸提取 Shell 资源图标
    - 目录和普通文件补上 Shell 文件图标提取回退
  - `ApplicationIconCache` 的位图生成改成“有原尺寸就原尺寸拷贝、过大时高质量缩小、过小时居中不强行放大”，减少低清素材被二次放大后的发糊
  - `MainForm` 的三张主表图标列已从 `DataGridViewImageCellLayout.Zoom` 改成 `Normal`
  - `CDriveSuggestionDialog` 图标列已从 `DataGridViewImageCellLayout.Zoom` 改成 `Normal`
  - 主窗和建议窗的图标尺寸计算改成“按当前 DPI + 当前行高 + 当前列宽”共同决定，避免图标先生成过大，再在单元格里被运行时缩放
  - `tools/Run-Icon-Clarity-QA.ps1` 新增稳定态截图 `04-settled.png`
  - 已完成 `dotnet build PortableCDriveCleaner.csproj`
  - 已完成基于安装版路径 `<INSTALLED_EXE_PATH>` 的新一组三轮完整 QA
- 状态：
  - 代码修改已完成，编译通过，安装版已重新发布，三轮自动化完整流程已通过
- 结果：
  - 已真实修到“提取 API”与“列表二次缩放”这两条最可疑链路
  - 但当前仍未通过最终人眼验收，不能宣称已经达到桌面快捷方式级清晰度
  - 新 QA 的稳定态截图还抓到了控件叠影，说明缩放/重绘链仍有未结案问题
- 安装版时间戳：
  - `<INSTALLED_EXE_PATH>`
  - `2026-04-27 01:56:00`
  - QA 证据目录：`artifacts/icon-qa/2026-04-27_015122`

### 2026-04-27 图标清晰度 QA 闭环脚本与三轮实跑
- 范围：
  - `tools/Run-Icon-Clarity-QA.ps1`
  - `CURRENT_TASK.md`
- 已做：
  - 新增“从安装开始”的图标清晰度 QA 脚本 `tools/Run-Icon-Clarity-QA.ps1`
  - 脚本固定每轮执行：
    - 重新发布并同步安装版
    - 启动安装版 `<INSTALLED_EXE_PATH>`
    - 截取启动、大窗、小窗三张截图
    - 记录启动耗时、当前窗口 DPI、窗口尺寸
    - 验证关闭后是否残留进程
  - 已完成 3 轮实跑，输出目录：
    - `artifacts/icon-qa/2026-04-27_012521`
  - 当前机器环境已确认：
    - 主屏 DPI：`168`，即 `175%`
    - 三轮启动耗时：`2.25s / 1.95s / 2.31s`
    - 三轮关闭后残留进程：`0`
- 状态：
  - QA 脚本已可用，三轮自动化流程已跑通
- 结果：
  - 自动化层面已验证“重新发布安装 -> 启动 -> 缩放 -> 截图 -> 关闭”闭环可重复执行
  - 但“图标是否达到桌面快捷方式级清晰度”仍未通过人眼验收，不能宣称问题已完成

### 2026-04-27 00:05 第三轮稳定性与图标链收口
- 范围：
  - `Forms/MainForm.cs`
  - `Forms/CDriveSuggestionDialog.cs`
  - `Infrastructure/ApplicationIconCache.cs`
  - `Infrastructure/IconSemanticResolver.cs`
  - `tools/Build-AppIcon.ps1`
  - `Assets/App.ico`
  - `Assets/AppIcon-preview.png`
- 已做：
  - `MainForm` 新增缩放期高成本布局冻结，拖动窗口时会先冻结顶部 AutoSize 面板和主表重绘，等 `ResizeEnd` 再一次性恢复并批量刷新
  - `MainForm` 的三张主表图标列改成按当前 DPI 走尺寸桶，图标 key 也带上语义和当前尺寸，避免 100% / 125% / 150% 切换后继续拿旧图
  - 新增 `Infrastructure/IconSemanticResolver.cs`，把清理候选、C盘总览、C盘建议、长期未用软件都接到统一语义解析链，彻底切断“目录路径 = 文件夹图标”的旧兜底逻辑
  - `ApplicationIconCache` 升级成“语义 + 尺寸桶 + 官方 stock icon 兜底”模式，新增 `16/20/24/32/40/48/64/128/256` 尺寸桶，并针对 `应用 / 文件夹 / 盘符 / 用户目录 / 缓存 / 日志 / 下载包 / 重复文件 / 清理 / 系统 / 文档` 分类型兜底
  - `CDriveSuggestionDialog` 同步接入图标语义解析、DPI 图标刷新和缩放期表格重绘暂停，避免主窗修好后建议窗仍沿用旧链路
  - `MainForm` 在 UltraCompact 下继续压缩顶部间距，直接隐藏教学条和选择提示，把更多高度还给数据区，并把表头/行高再下压一档
  - `tools/Build-AppIcon.ps1` 重写为现代纯黑科技感图标生成器，可直接输出多尺寸 `ico`，并已重生成 `Assets/App.ico` 与 `Assets/AppIcon-preview.png`
  - 已完成 `dotnet build PortableCDriveCleaner.csproj`
  - 已完成 `publish.ps1`，并把最新安装版同步到 `<INSTALLED_EXE_PATH>`
- 状态：
  - 代码修改已完成，编译通过，安装版已同步
- 结果：
  - 这一轮已经把“缩放止血 + 图标语义纠正 + DPI 图标桶 + 程序外部图标升级”落到安装版
  - 仍需用户继续实机验收：缩放是否仍花屏、列表图标是否仍糊、文件夹误用是否彻底消失、小窗可见行数是否达标、整体暗黑科技感是否足够高级
- 安装版时间戳：
  - `<INSTALLED_EXE_PATH>`
  - `2026-04-27 00:05:02`
- 额外记录：
  - `2026-04-27 00:04` 首次同步安装版时再次命中 `<INSTALLED_EXE_PATH>` 被旧进程占用，手动结束进程 `磁盘清理器 (PID 25460)` 后重新发布成功

### 2026-04-24 下一轮收口第二轮代码落地
- 范围：
  - `Forms/ThemedProgressBar.cs`
  - `Forms/ThemedButton.cs`
  - `Forms/OperationProgressDialog.cs`
  - `Forms/MainForm.cs`
  - `Forms/CDriveSuggestionDialog.cs`
  - `Forms/ScheduleSettingsDialog.cs`
  - `Forms/RegressionAuditDialog.cs`
  - `Forms/CleanupConfirmationDialog.cs`
  - `Forms/DeploymentDialog.cs`
  - `Forms/ElevationPromptDialog.cs`
  - `Forms/UiScaleHelper.cs`
  - `Services/OperationManager.cs`
  - `Infrastructure/ApplicationIconCache.cs`
  - `Infrastructure/UiThemePalette.cs`
- 已做：
  - 新增自绘深色按钮 `ThemedButton`，解决深色主题下禁用按钮文字发黑/难读问题
  - 新增自绘静态进度条 `ThemedProgressBar`，彻底摆脱原生进度条的浅色轨道和整块闪动体感
  - 主窗后台任务区改成“状态变化立即收口 + 被动 1 秒刷新耗时文案”，不再 `140ms` 整区反复刷新
  - 后台任务卡改成仅在尺寸变化时重排，状态变化只改文字和值
  - 主窗 UltraCompact 进一步压缩顶部区，并在最小布局下隐藏盘符摘要行，把空间优先还给数据区
  - 为主窗与主要布局面板补双缓冲，减少缩放时白屏/撕裂体感
  - 图标兜底改成 Windows Shell 官方 stock icon，并把图标列后台刷新频率降到每 24 个一批
  - 主要工作窗口标题栏切到沉浸式深色，主按钮统一到纯黑科技感样式
  - `UiScaleHelper` 增加尺寸测量缓存，减少缩放和重布局时的重复测量
- 状态：
  - 已完成代码修改、编译通过、安装版已同步
- 结果：
  - 第二轮收口已经落地到安装版 `<INSTALLED_EXE_PATH>`，但“是否仍闪、是否仍卡顿、图标是否已足够清晰、暗黑风是否达标”仍需用户继续以真实界面验收
- 安装版时间戳：
  - `2026-04-24 23:28:16`

### 2026-04-24 下一轮收口第一轮代码落地
- 范围：
  - `Forms/OperationProgressDialog.cs`
  - `Forms/MainForm.cs`
  - `Forms/CDriveSuggestionDialog.cs`
  - `Forms/ScheduleSettingsDialog.cs`
  - `Forms/RegressionAuditDialog.cs`
  - `Forms/CleanupConfirmationDialog.cs`
  - `Forms/DeploymentDialog.cs`
  - `Forms/ElevationPromptDialog.cs`
  - `Services/OperationManager.cs`
  - `Infrastructure/ApplicationIconCache.cs`
  - `Infrastructure/UiThemePalette.cs`
- 已做：
  - 进度窗与主窗后台任务卡移除 `Marquee`
  - 进度窗去掉每次上报后的整窗 `Refresh()`
  - 后台任务状态发布增加节流与内容去重
  - 主窗缩放改成拖动时轻量、稳定后批量刷新
  - 主窗与 `C盘建议` 的图标列刷新与缩放过程解耦
  - `C盘建议`、自动清理设置、历史问题清单改成单实例非模态工作窗口
  - 引入统一暗黑主题令牌并覆盖主窗与主要对话框
  - 应用图标 fallback 改为官方主图标兜底并支持尺寸感知
- 状态：
  - 已完成代码修改、编译通过、安装版已同步
- 结果：
  - 代码层主要收口已落地，但仍需要用户在真实界面上继续验收“体感是否达标”

### 2026-04-23 第一轮
- 范围：
  - `Forms/MainForm.cs`
- 已做：
  - 引入 `Regular / Compact / UltraCompact`
  - 加入缩放节流与批处理刷新
  - 开始折叠部分说明区和任务区
- 状态：
  - 已完成代码修改并发布
- 结果：
  - 有改善，但未结案

### 2026-04-23 第二轮
- 范围：
  - `Forms/MainForm.cs`
- 已做：
  - 提高 `Compact / UltraCompact` 触发阈值
  - 紧凑模式下隐藏运行版本行
  - 紧凑模式下后台任务改单行摘要
  - 紧凑模式下隐藏汇总说明第二行
- 状态：
  - 已完成代码修改并发布
- 结果：
  - 用户仍反馈“小窗数据太少”

### 2026-04-24 记忆与交接系统
- 范围：
  - `PROJECT_CONTEXT.md`
  - `MEMORY.md`
  - `CURRENT_TASK.md`
  - `OPTIMIZATION_LOG.md`
  - `TODO_NOT_FIXED.md`
  - `HANDOVER_FOR_OTHER_AI.md`
  - `AI_PROMPT_TEMPLATES.md`
  - `GITHUB_SYNC_CHECKLIST.md`
  - `tools/Save-Handoff.ps1`
  - `tools/Connect-PrivateGitHub.ps1`
- 已做：
  - 建立跨窗口 / 跨 AI 继续机制
  - 建立 GitHub 私有同步和中断前保存机制
- 状态：
  - 已完成并已推送 GitHub

### 2026-04-24 GitHub 持久记忆与检查点收口
- 范围：
  - `AI_STATE.json`
  - `checkpoints/`
  - `tools/Record-Checkpoint.ps1`
  - `tools/Save-Handoff.ps1`
  - `README.md`
  - `PROJECT_CONTEXT.md`
  - `HANDOVER_FOR_OTHER_AI.md`
  - `AI_PROMPT_TEMPLATES.md`
  - `GITHUB_SYNC_CHECKLIST.md`
- 已做：
  - 增加机器可读状态文件
  - 增加开始 / 进行中 / 完成 / 中断四类 checkpoint
  - 把仓库地址、本地路径、安装路径写进跨 AI 模板
  - 把桌面保存入口改成调用标准 checkpoint 脚本
- 状态：
  - 本轮已完成，接下来应使用标准 checkpoint 流程继续后续软件优化任务

### 2026-04-29 商业级首屏稳定与真实刷新 QA 收口
- 范围：
  - `Forms/MainForm.cs`
  - `Forms/UiScaleHelper.cs`
  - `Models/CommandLineOptions.cs`
  - `Program.cs`
  - `tools/Run-Icon-Clarity-QA.ps1`
- 已做：
  - 将主表、C盘总览表、长期未用软件表从“每次新建 `BindingList` 并重绑 `DataSource`”改为“原 `BindingList` 原地批量替换 + 表格暂停重绘 + 恢复后单次 `RedrawWindow`”，降低加载分阶段刷新时的重复行、残影和白块风险。
  - 进一步压缩顶部按钮测量：移除 `GetPreferredSize()` 反向撑宽后，又减少按钮测量固定余量，工具按钮和视图按钮现在按文字宽度加小 padding 输出。
  - UltraCompact 下隐藏主标题大头区，标题保留在系统标题栏，内容区占比稳定在约 `0.757`。
  - 新增加载态紧凑绿色进度条，加载中只让绿色条、百分比和时间变化，减少整块任务卡视觉干扰。
  - 新增 QA 参数 `--qa-refresh` / `-AllowStartupRefresh`，可在 QA 视图下保留真实后台刷新，用来覆盖加载抖动路径。
  - QA 脚本新增加载前 10 秒截图序列、按钮宽度硬指标、内容区占比硬指标和截图健康检查。
- 验证：
  - `dotnet build .\PortableCDriveCleaner.csproj`：0 warning / 0 error。
  - 真实后台刷新安装版 QA 三轮通过，证据目录：`artifacts/icon-qa/2026-04-29_014029/`（本地原始截图，不提交公开仓库）。
  - 三轮启动时间：约 `2.40s / 2.44s / 2.14s`，DPI `168`，无残留进程。
  - 人工复查关键截图：首帧白块残影已消失，加载中绿色进度条可见，resize-stress 和小窗截图未见重复行/白条。
- 状态：
  - 自动硬门槛通过，人工自查通过；仍需用户最终视觉确认，不把 `PASS_PENDING_VISUAL` 当成用户验收完成。

### 2026-04-29 加载稳定展示模式与顶部紧凑命令区收口
- 范围：
  - `Forms/MainForm.cs`
- 已做：
  - 为扫描中的 `CleanupCandidates` + UltraCompact 增加“稳定展示模式”：后台扫描活跃时，隐藏工具条和摘要条，只保留任务条与盘符/视图条，避免顶部多层面板在加载中反复重排。
  - 将中间阶段快照切换改为“首批稳定候选先显示，后续中间快照尽量延后到最终完成再替换”，减少扫描中 `C盘候选 -> 第一批建议 -> C盘完整结果` 连续换表导致的残影和重复行。
  - `ReplaceGridItems()` 进一步加上父容器级 redraw suspend / resume，数据替换后同时 `RedrawWindow` 子表格和内容宿主，压掉旧像素残留。
  - 顶部视图按钮改成更短的产品文案：`候选 / 总览 / 未用`，`C盘建议` 在 UltraCompact 下改为 `C盘`；同时把视图按钮最小宽度重新调到“刚好完整显示，不再省略号截断”。
  - UltraCompact 下盘符下拉进一步缩短到更接近 `C盘 + 倒三角` 的紧凑宽度。
- 验证：
  - `dotnet build .\PortableCDriveCleaner.csproj`：0 warning / 0 error。
  - 真实后台刷新安装版 QA 三轮通过，证据目录：`artifacts/icon-qa/2026-04-29_021337/`（本地原始截图，不提交公开仓库）。
  - 三轮启动时间：约 `2.39s / 2.18s / 2.18s`，DPI `168`，无残留进程。
  - QA 状态显示：扫描中 `ToolbarVisible=false`、`SummaryVisible=false`、`ContentHeightRatio=0.848`、`TopChromeHeight=141`、视图按钮 `候选 / 总览 / 未用` 宽度均为 `70px`。
  - 安全复查：`gitleaks detect --source . --no-banner` 结果为 `no leaks found`；本轮新增本地辅助工具 `PresentMon Console 2.4.1.0`，用于后续拖动/加载帧时间抓取。
- 状态：
  - 加载态的顶部空间和中间快照抖动已继续收口，但最终视觉是否“足够酷炫”和图标是否达到你的主观标准，仍需继续按真实截图迭代。

## 进行中 / 被打断

### 当前正在做
- 任务：
  - 主窗口加载态和科技感继续往商业级质感收口
- 当前进度：
  - GitHub 持久记忆基座已完成
  - 已完成 `ApplicationIconCache` 提取链和表格图标显示链的第二轮真实修改
  - 已完成 3 组三轮自动化 QA，最近一组证据目录为 `artifacts/icon-qa/2026-04-29_021337`
  - 已安装本地帧时间辅助工具 `PresentMon Console`，可继续量化拖动和加载时的帧时间尖峰
- 当前未完成：
  - 内部列表图标是否已达到桌面快捷方式级清晰度，仍需人眼验收
  - 自动化截图还没有给出“酷炫度 / 高级感”的视觉分数，仍需补强审美验收路径
  - 图标误用与通用 fallback 是否仍然显得过糊、过泛，仍需人眼验收
  - 主窗口内部黑底科技感还不够“商业第一”，仍需继续重做顶部 HUD、表格内饰和图标风格
- 下一步精确落点：
  - 先把这轮真实修改和三轮 QA 结果写回 checkpoint 并推送
  - 再把主窗口顶部从“系统工具条”继续提炼成更强的 HUD 控制条
  - 然后继续补强图标清晰度和视觉验收路径

## 尚未开始的源码方案
- 退出后进程残留的专项收口
- GitHub 进度保存更细粒度自动化
- 更细粒度的“每个任务阶段自动写回日志”能力

## 回查 / 回调 / 回退记录
- 当前还没有正式回退操作
- 如果以后某轮改动效果变差，必须在这里记录：
  - 回退了哪一版
  - 回退原因
  - 受影响文件

## 发布状态
- 最近一次已知安装版时间戳：
  - `<INSTALLED_EXE_PATH>`
  - `2026-04-29 02:13:37`
- 当前 GitHub 私有仓库：
  - `https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git`

