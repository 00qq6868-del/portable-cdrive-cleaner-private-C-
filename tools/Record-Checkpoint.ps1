param(
    [ValidateSet("Start", "Progress", "Finish", "Interrupt")]
    [string]$Mode,
    [string]$Task = "",
    [string]$Summary = "",
    [string[]]$ModifiedFiles = @(),
    [string[]]$Remaining = @(),
    [switch]$IncludeCode,
    [switch]$Push
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RepoValue {
    param(
        [string]$ProjectRoot,
        [string[]]$GitArgs,
        [string]$Fallback = ""
    )

    try {
        $value = & git -C $ProjectRoot @GitArgs 2>$null
        if ($LASTEXITCODE -ne 0) {
            return $Fallback
        }

        return (($value | Out-String).Trim())
    }
    catch {
        return $Fallback
    }
}

function Get-ArrayValue {
    param([object]$Value)

    if ($null -eq $Value) {
        return @()
    }

    if ($Value -is [string]) {
        if ([string]::IsNullOrWhiteSpace($Value)) {
            return @()
        }

        return @($Value)
    }

    return @($Value)
}

function Normalize-List {
    param([string[]]$Items)

    $result = New-Object System.Collections.Generic.List[string]

    foreach ($item in $Items) {
        if ($null -eq $item) {
            continue
        }

        $text = ([string]$item).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        foreach ($part in ($text -split "\s*,\s*")) {
            $clean = $part.Trim().Trim('"')
            if (-not [string]::IsNullOrWhiteSpace($clean)) {
                $result.Add($clean)
            }
        }
    }

    return $result.ToArray()
}

function Get-BulletBlock {
    param(
        [string[]]$Items,
        [string]$EmptyText = "无"
    )

    if (-not $Items -or $Items.Count -eq 0) {
        return "- $EmptyText"
    }

    return (($Items | ForEach-Object { "- $_" }) -join [Environment]::NewLine)
}

function Get-FirstOrDefault {
    param(
        [string[]]$Items,
        [string]$Fallback
    )

    if ($Items -and $Items.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($Items[0])) {
        return $Items[0]
    }

    return $Fallback
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$statePath = Join-Path $projectRoot "AI_STATE.json"
$currentTaskPath = Join-Path $projectRoot "CURRENT_TASK.md"
$optimizationLogPath = Join-Path $projectRoot "OPTIMIZATION_LOG.md"
$interruptedPath = Join-Path $projectRoot "INTERRUPTED_PROGRESS.md"
$checkpointDir = Join-Path $projectRoot "checkpoints"
$saveHandoffPath = Join-Path $PSScriptRoot "Save-Handoff.ps1"
$publicProjectRoot = "<LOCAL_REPO_PATH>"
$publicInstallPath = "<INSTALLED_EXE_PATH>"

if (-not (Test-Path $checkpointDir)) {
    New-Item -ItemType Directory -Path $checkpointDir | Out-Null
}

$existingState = $null
if (Test-Path $statePath) {
    try {
        $existingState = Get-Content -Path $statePath -Raw | ConvertFrom-Json
    }
    catch {
        $existingState = $null
    }
}

$repoUrl = Get-RepoValue -ProjectRoot $projectRoot -GitArgs @("remote", "get-url", "origin") -Fallback "未配置 origin"
$branch = Get-RepoValue -ProjectRoot $projectRoot -GitArgs @("rev-parse", "--abbrev-ref", "HEAD") -Fallback "unknown"
$lastCommit = Get-RepoValue -ProjectRoot $projectRoot -GitArgs @("rev-parse", "--short", "HEAD") -Fallback "no-commit"

$existingPending = if ($existingState) { Normalize-List -Items (Get-ArrayValue -Value $existingState.pending_items) } else { @() }
$existingModified = if ($existingState) { Normalize-List -Items (Get-ArrayValue -Value $existingState.modified_files) } else { @() }
$existingPhase = if ($existingState -and $existingState.current_phase) { [string]$existingState.current_phase } else { "历史问题收口 + 主窗口小窗布局继续收口" }
$existingTask = if ($existingState -and $existingState.current_task) { [string]$existingState.current_task } else { "主窗口小窗布局继续收口" }
$existingLastCompleted = if ($existingState -and $existingState.last_completed_step) { [string]$existingState.last_completed_step } else { "无" }

$timestamp = Get-Date
$timestampText = $timestamp.ToString("yyyy-MM-dd HH:mm:ss")
$filenameTimestamp = $timestamp.ToString("yyyy-MM-dd_HHmmss")
$modeKey = $Mode.ToLowerInvariant()
$checkpointFileName = "${filenameTimestamp}_${modeKey}.md"
$checkpointPath = Join-Path $checkpointDir $checkpointFileName
$checkpointRelativePath = "checkpoints/$checkpointFileName"

$currentTask = if ([string]::IsNullOrWhiteSpace($Task)) { $existingTask } else { $Task.Trim() }
$summaryText = if ([string]::IsNullOrWhiteSpace($Summary)) { "未提供额外摘要" } else { $Summary.Trim() }
$pendingItems = if ($Remaining.Count -gt 0) { Normalize-List -Items $Remaining } else { $existingPending }
$modifiedItems = if ($ModifiedFiles.Count -gt 0) { Normalize-List -Items $ModifiedFiles } else { $existingModified }

$taskStatus = switch ($Mode) {
    "Start" { "in_progress" }
    "Progress" { "in_progress" }
    "Finish" { "completed" }
    "Interrupt" { "interrupted" }
}

$statusLabel = switch ($taskStatus) {
    "in_progress" { "进行中" }
    "completed" { "已完成" }
    "interrupted" { "已中断" }
    default { $taskStatus }
}

$lastCompletedStep = switch ($Mode) {
    "Finish" { $summaryText }
    "Progress" { $summaryText }
    default { $existingLastCompleted }
}

$inProgressStep = switch ($Mode) {
    "Start" { $summaryText }
    "Progress" { Get-FirstOrDefault -Items $pendingItems -Fallback $currentTask }
    "Finish" { "" }
    default { $summaryText }
}

$nextStep = Get-FirstOrDefault -Items $pendingItems -Fallback "无"
$pushStatus = if ($Push) { "push_requested" } else { "local_checkpoint_recorded" }

$state = [ordered]@{
    repo_url = $repoUrl
    local_repo_path = $publicProjectRoot
    install_path = $publicInstallPath
    current_phase = $existingPhase
    current_task = $currentTask
    task_status = $taskStatus
    last_completed_step = $lastCompletedStep
    in_progress_step = $inProgressStep
    next_step = $nextStep
    pending_items = @($pendingItems)
    modified_files = @($modifiedItems)
    last_checkpoint_time = $timestampText
    last_commit = $lastCommit
    push_status = $pushStatus
    branch = $branch
    latest_checkpoint = $checkpointRelativePath
}

($state | ConvertTo-Json -Depth 6) | Set-Content -Path $statePath -Encoding UTF8

$modifiedBlock = Get-BulletBlock -Items $modifiedItems -EmptyText "本次未额外指定"
$pendingBlock = Get-BulletBlock -Items $pendingItems -EmptyText "当前无新的未完成项"

$checkpointContent = @"
# Checkpoint - $timestampText

## 基本信息
- 模式：$Mode
- 状态：$statusLabel
- 仓库地址：$repoUrl
- 分支：$branch
- 本地路径：$publicProjectRoot
- 安装版：$publicInstallPath
- 最近已知提交：$lastCommit

## 当前任务
- 当前阶段：$existingPhase
- 当前任务：$currentTask
- 本次摘要：$summaryText
- 下一步：$nextStep

## 已修改文件
$modifiedBlock

## 仍未完成
$pendingBlock

## 关键说明
- 本次 checkpoint 会把结构化状态写回 AI_STATE.json
- 跨 AI / 跨窗口继续时，应优先读取 AI_STATE.json
- 如果无法访问 GitHub 私有仓库，回退到 AI_PROMPT_TEMPLATES.md 中的 GitHub 不可访问回退模板
"@

Set-Content -Path $checkpointPath -Value $checkpointContent -Encoding UTF8

$currentTaskContent = @"
# 当前任务

## 当前活动任务
- 当前阶段：$existingPhase
- 当前任务：$currentTask
- 状态：$statusLabel
- 最近完成：$lastCompletedStep
- 当前进行中：$(if ([string]::IsNullOrWhiteSpace($inProgressStep)) { "无" } else { $inProgressStep })
- 下一步：$nextStep
- 最近检查点：$checkpointRelativePath

## 当前未完成
$pendingBlock

## 当前关键文件
$modifiedBlock

## GitHub 连续记忆
- 仓库地址：$repoUrl
- 本地路径：$publicProjectRoot
- 安装版：$publicInstallPath
- 默认读取顺序：AI_STATE.json -> PROJECT_CONTEXT.md -> MEMORY.md -> CURRENT_TASK.md -> TODO_NOT_FIXED.md -> OPTIMIZATION_LOG.md -> HANDOVER_FOR_OTHER_AI.md -> SOURCE_CHANGE_LEDGER.md -> INTERRUPTED_PROGRESS.md -> AI_PROMPT_TEMPLATES.md -> GITHUB_SYNC_CHECKLIST.md
"@

Set-Content -Path $currentTaskPath -Value $currentTaskContent -Encoding UTF8

$interruptedTitle = if ($Mode -eq "Interrupt") { "中断恢复状态" } else { "最近可恢复状态" }
$interruptedHint = if ($Mode -eq "Interrupt") {
    "当前任务已被主动标记为中断，下一次必须从下面的下一步恢复。"
}
else {
    "当前任务未被新的中断覆盖，本文件保留最近一次可直接恢复的状态。"
}

$interruptedContent = @"
# 被打断时的进度快照

## $interruptedTitle
- 当前任务：$currentTask
- 当前状态：$statusLabel
- 当前阶段：$existingPhase
- 本次摘要：$summaryText
- 最近检查点：$checkpointRelativePath

## 已完成到哪一步
- $lastCompletedStep

## 当前做到一半的内容
- $(if ([string]::IsNullOrWhiteSpace($inProgressStep)) { "无" } else { $inProgressStep })

## 当前还没完成的部分
$pendingBlock

## 当前已改的文件
$modifiedBlock

## 如果现在继续，下一步先做什么
- $nextStep

## 说明
- $interruptedHint
- 当前 GitHub 私有仓库：$repoUrl
"@

Set-Content -Path $interruptedPath -Value $interruptedContent -Encoding UTF8

$logBlock = @"

## $timestampText
- 模块：GitHub 持久记忆与 checkpoint
- 任务：$currentTask
- 检查点模式：$Mode
- 已完成：
  - $summaryText
- 修改文件：
$modifiedBlock
- 剩余项：
$pendingBlock
- 检查点文件：
  - $checkpointRelativePath
- 是否请求推送：$(if ($Push) { "是" } else { "否" })
- 结果：
  - 已写回 AI_STATE.json、CURRENT_TASK.md、INTERRUPTED_PROGRESS.md 和检查点快照
- 风险 / 备注：
  - last_commit 记录的是写 checkpoint 前最近已知的提交 SHA
"@

Add-Content -Path $optimizationLogPath -Value $logBlock -Encoding UTF8

$message = "checkpoint($modeKey): $currentTask"
$saveArgs = @(
    "-ExecutionPolicy", "Bypass",
    "-File", $saveHandoffPath,
    "-Message", $message
)

if ($IncludeCode) {
    $saveArgs += "-IncludeCode"
}

if ($Push) {
    $saveArgs += "-Push"
}

& powershell @saveArgs





