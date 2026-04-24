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
- 落地“去闪动、缩放不卡、纯黑科技感、高清官方兜底图标、秒开感与跨 AI 持续记忆”第一轮代码收口

### 目标
- 去掉整块闪动和跑马灯，只保留百分比文字与静态绿色填充
- 收口主窗口缩放期间的高频重排、图标列刷新和任务卡宽度抖动
- 开始统一主窗与常驻工作窗口的纯黑主题
- 把列表 fallback 图标换成官方主图标兜底
- 把本轮状态持续写回 GitHub 持久记忆

### 计划涉及文件
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
- `MEMORY.md`
- `CURRENT_TASK.md`
- `TODO_NOT_FIXED.md`
- `AI_STATE.json`

### 预期动作
- 移除 `Marquee`
- 去掉整窗 `Refresh()`
- 给后台任务状态发布加节流和内容去重
- 把缩放过程改成轻量刷新，重布局延后
- 给图标列重绘与缩放解耦
- 增加统一暗黑主题与官方兜底图标

## 已完成源码修改

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

## 进行中 / 被打断

### 当前正在做
- 任务：
  - 下一轮收口：去闪动、缩放不卡、纯黑科技感、清晰图标、秒开感与跨 AI 持续记忆
- 当前进度：
  - GitHub 持久记忆基座已完成
  - 已确认进度闪动和缩放卡顿的直接代码根因
  - 即将开始修改进度反馈、缩放刷新、主题和图标
- 当前未完成：
  - 进度区整块闪动仍存在
  - 缩放卡顿 / 变形仍存在
  - 纯黑主题仍未落地
  - 图标 fallback 仍不够清晰
  - 启动体感仍需继续压缩
  - 主窗口顶部区仍然过高
  - 小窗口数据区仍然不够大
- 下一步精确落点：
  - 先完成本轮 Start checkpoint
  - 然后先改 `Forms/OperationProgressDialog.cs`、`Forms/MainForm.cs`、`Services/OperationManager.cs`

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
  - `D:\磁盘清理器\磁盘清理器.exe`
  - `2026-04-23 02:47:53`
- 当前 GitHub 私有仓库：
  - `https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git`
