# GitHub 私有同步清单

## 目标
- 把“计划、进度、已完成、未完成、交接信息、优化日志”放进 GitHub 私有仓库
- 这样即使换 AI 窗口或换 AI，也能继续

## 当前现状
- 项目根目录已经有记忆/交接文档
- 项目已经初始化本地 Git 仓库
- 当前没有 GitHub CLI
- 当前 GitHub 私有远程已经接通
- 当前远程地址：`https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git`
- 默认远程分支：`main`

## 每次开始新任务前
1. 先拉最新状态：

```powershell
git pull --rebase origin main
```

2. 再读取：
   - `AI_STATE.json`
   - `PROJECT_CONTEXT.md`
   - `CURRENT_TASK.md`
   - `TODO_NOT_FIXED.md`
   - `SOURCE_CHANGE_LEDGER.md`
   - `INTERRUPTED_PROGRESS.md`

3. 如果要正式开始新的工作阶段，先写开始检查点：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Start -Task "当前任务名" -Summary "准备开始本轮任务" -Push
```

## 每完成一个部分后
1. 先更新 `CURRENT_TASK.md`
2. 如果用户新增规则，更新 `MEMORY.md`
3. 使用标准 checkpoint：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Progress -Task "当前任务名" -Summary "当前已完成的这一步" -Push
```

## 如果对话快截断
优先执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Interrupt -Task "当前任务名" -Summary "中断前保存当前进度" -IncludeCode -Push
```

## 如果本轮任务完成

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Finish -Task "当前任务名" -Summary "本轮任务已完成并完成收口" -IncludeCode -Push
```

## 底层提交脚本

`tools/Save-Handoff.ps1` 现在作为底层提交 / 推送器保留，标准流程优先通过 `tools/Record-Checkpoint.ps1` 调用。

如果只想手动提交当前已写好的文档或代码，也可以直接使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Save-Handoff.ps1 -Message "checkpoint: save docs and code progress" -IncludeCode -Push
```

## 当前推荐
- 平时直接双击桌面：
  - `保存当前进度到GitHub.cmd`
- 现在它会调用标准 checkpoint 脚本，而不是只做盲提交

## GitHub 不可访问回退
- 如果新的 AI / API 窗口无法直接访问私有仓库，不要假装已经同步成功。
- 默认回退方式：
  - 复制 `AI_PROMPT_TEMPLATES.md` 里的“GitHub 不可访问回退模板”
  - 再粘贴：
    - `AI_STATE.json`
    - `CURRENT_TASK.md`
    - `SOURCE_CHANGE_LEDGER.md`
    - `INTERRUPTED_PROGRESS.md`
    - 最新 checkpoint 文件
