# GitHub 私有同步清单

## 目标
- 把“计划、进度、已完成、未完成、交接信息、优化日志”放进 GitHub 私有仓库
- 这样即使换 AI 窗口或换 AI，也能继续

## 当前现状
- 项目根目录已经有记忆/交接文档
- 项目已经初始化本地 Git 仓库
- 当前没有 GitHub CLI
- 当前 GitHub 私有远程已经接通
- 默认远程分支：`main`

## 一次性准备

### 第 1 步：初始化本地 Git
在项目根目录执行：

```powershell
git init
git add .
git commit -m "chore: initialize project memory and handoff documents"
```

### 第 2 步：在 GitHub 网站创建私有仓库
- 登录 GitHub
- New repository
- 仓库设为 `Private`
- 仓库名建议：`portable-cdrive-cleaner-private`

### 第 3 步：绑定远程仓库
把下面的 `YOUR_REPO_URL` 换成你的 GitHub 私有仓库地址：

```powershell
git remote add origin YOUR_REPO_URL
git branch -M main
git push -u origin main
```

> 当前项目已完成这一步；后续主要使用“保存当前进度到GitHub”即可。

## 每次开始新任务前
1. 先更新 `CURRENT_TASK.md`
2. 如果用户新增规则，更新 `MEMORY.md`
3. 先 `git pull`，避免别的窗口已经写过新内容

```powershell
git pull --rebase origin main
```

## 每完成一个部分后
1. 更新：
   - `CURRENT_TASK.md`
   - `OPTIMIZATION_LOG.md`
   - `TODO_NOT_FIXED.md`
2. 然后提交并推送：

```powershell
git add CURRENT_TASK.md OPTIMIZATION_LOG.md TODO_NOT_FIXED.md MEMORY.md HANDOVER_FOR_OTHER_AI.md
git commit -m "docs: update progress and handoff state"
git push origin main
```

## 如果对话快截断
优先执行：

```powershell
git add CURRENT_TASK.md OPTIMIZATION_LOG.md TODO_NOT_FIXED.md MEMORY.md HANDOVER_FOR_OTHER_AI.md
git commit -m "docs: save handoff before interruption"
git push origin main
```

或者直接使用项目内脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Save-Handoff.ps1 -Message "docs: save handoff before interruption" -Push
```

默认建议直接保存“代码 + 文档”：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Save-Handoff.ps1 -Message "checkpoint: save docs and code progress" -IncludeCode -Push
```

## 当前推荐
- 平时直接双击桌面：
  - `保存当前进度到GitHub.cmd`
- 这样会把当前代码和文档一起提交并推送
