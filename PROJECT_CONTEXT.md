# 项目上下文

## 项目识别
- 项目名称：便携式磁盘清理器 / PortableCDriveCleaner
- 技术栈：WinForms + .NET Windows 桌面程序
- GitHub 私有仓库：`https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git`
- 本地源码目录：`<LOCAL_REPO_PATH>`
- 当前分支：`main`
- 实际安装目录：`<INSTALL_DIR>`
- 实际运行主程序：`<INSTALLED_EXE_PATH>`
- 当前桌面入口：`<DESKTOP_SHORTCUT>`
- 当前高权限计划任务：`磁盘清理器-高权限启动`

## 当前交付链
- 源码修改后，使用 `publish.ps1` 重新发布安装版。
- 发布脚本需要同步：
  - 安装目录 EXE
  - 桌面快捷方式
  - 安装目录内快捷方式
- 当前已锁定：安装目录和桌面入口必须始终同步，不能只更新其中一个。

## GitHub 持久记忆基线
- 记忆的唯一可信源是：
  - GitHub 私有仓库中的结构化文档
  - `AI_STATE.json`
  - `checkpoints/` 检查点快照
  - Git 提交历史
  - 实际源码差异
- 不再把聊天窗口本身当作唯一记忆来源。
- 原始聊天逐字稿不做跨 AI 硬依赖，结构化摘要才是标准记忆。

## 默认读取顺序
- 新 AI / 新窗口开始前，默认顺序改为：
  1. `AI_STATE.json`
  2. `PROJECT_CONTEXT.md`
  3. `MEMORY.md`
  4. `CURRENT_TASK.md`
  5. `TODO_NOT_FIXED.md`
  6. `OPTIMIZATION_LOG.md`
  7. `HANDOVER_FOR_OTHER_AI.md`
  8. `SOURCE_CHANGE_LEDGER.md`
  9. `INTERRUPTED_PROGRESS.md`
  10. `AI_PROMPT_TEMPLATES.md`
  11. `GITHUB_SYNC_CHECKLIST.md`

## 默认同步规则
- 每次开始新任务前，先执行 `git pull --rebase origin main`，再读取状态文件。
- 默认四类检查点必须写回仓库：
  - `Start`
  - `Progress`
  - `Finish`
  - `Interrupt`
- 标准入口：
  - `tools/Record-Checkpoint.ps1`
  - `tools/Save-Handoff.ps1`
- 推送失败时，不能假装已同步，必须把失败状态写回 `AI_STATE.json` 和检查点快照。

## 当前活动软件任务
- GitHub 持久记忆与跨 AI 接力收口已完成，本轮下一步返回主窗口布局问题。
- 当前尚未结案的真实软件问题：
  - 主窗口小窗里数据区仍然太少
  - 顶部区域仍然过高
- 当前主要目标文件：
  - `Forms/MainForm.cs`

## 当前重要事实
- 当前安装版时间戳：`2026-04-23 02:47:53`
- 当前安装版路径：`<INSTALLED_EXE_PATH>`
- 当前项目没有 GitHub CLI，但 Git 远程已接通且可推送。
- 仓库内保留了桌面脚本副本：
  - `tools/desktop-launchers/连接GitHub私有仓库.cmd`
  - `tools/desktop-launchers/保存当前进度到GitHub.cmd`

