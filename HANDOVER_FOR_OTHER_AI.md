# 给下一个 AI 的交接说明

## 先读顺序
1. `AI_STATE.json`
2. `PROJECT_CONTEXT.md`
3. `MEMORY.md`
4. `CURRENT_TASK.md`
5. `TODO_NOT_FIXED.md`
6. `OPTIMIZATION_LOG.md`
7. `SOURCE_CHANGE_LEDGER.md`
8. `INTERRUPTED_PROGRESS.md`
9. `AI_PROMPT_TEMPLATES.md`
10. `GITHUB_SYNC_CHECKLIST.md`

## 当前必须知道的事实
- 这是 WinForms 项目，不是 Web 项目
- GitHub 私有仓库：`https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git`
- 源码目录：`<LOCAL_REPO_PATH>`
- 实际安装版：`<INSTALLED_EXE_PATH>`
- 桌面入口通过计划任务 `磁盘清理器-高权限启动` 拉起
- GitHub 持久记忆系统已经建立，后续不要再把聊天窗口当唯一记忆
- 当前最新真实软件问题仍然是：
  - 主窗口小窗里看到的数据仍然太少
  - 顶部仍然占高过多

## 当前不能做错的事
- 不要只改源码不更新安装版
- 不要只更新桌面快捷方式不更新安装目录
- 不要把“已尝试过”当成“已修好”
- 不要只在聊天里记录进度，必须写回项目文件
- 不要跳过 `AI_STATE.json` 和 `checkpoints/`

## 开始工作前必须做
1. 先 `git pull --rebase origin main`
2. 先读 `AI_STATE.json`
3. 再更新 `CURRENT_TASK.md`
4. 做任何较大修改前，先写 checkpoint 或计划摘要

## 每完成一个子步骤后必须做
1. 先执行 `tools/Record-Checkpoint.ps1`
2. 更新 `OPTIMIZATION_LOG.md`
3. 如果还有未结案内容，更新 `TODO_NOT_FIXED.md`
4. 如果用户新加了硬性规则，更新 `MEMORY.md`
5. 如果动了源码，更新 `SOURCE_CHANGE_LEDGER.md`
6. 如果做到一半准备停下，更新 `INTERRUPTED_PROGRESS.md`

## 如果对话快被截断
1. 先不要继续写代码
2. 先更新：
   - `AI_STATE.json`
   - `CURRENT_TASK.md`
   - `OPTIMIZATION_LOG.md`
   - `TODO_NOT_FIXED.md`
   - `SOURCE_CHANGE_LEDGER.md`
   - `INTERRUPTED_PROGRESS.md`
   - `checkpoints/` 最新快照
3. 如果 Git 已配置远程，优先执行：
   - `powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Interrupt -Task "当前任务名" -Summary "中断前保存当前进度" -IncludeCode -Push`

## 当前最应该继续的方向
- 继续处理主窗口小窗布局
- 目标不是“有一点改善”，而是“数据区明显变大，用户一眼就看出来”

