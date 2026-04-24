param(
    [string]$Message = "docs: save handoff checkpoint",
    [switch]$IncludeCode,
    [switch]$Push
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$requiredDocs = @(
    "PROJECT_CONTEXT.md",
    "MEMORY.md",
    "CURRENT_TASK.md",
    "OPTIMIZATION_LOG.md",
    "TODO_NOT_FIXED.md",
    "HANDOVER_FOR_OTHER_AI.md",
    "AI_PROMPT_TEMPLATES.md",
    "GITHUB_SYNC_CHECKLIST.md",
    "README.md"
)

if (-not (Test-Path (Join-Path $projectRoot ".git"))) {
    throw "当前项目还没有 Git 仓库，请先执行 git init。"
}

Push-Location $projectRoot
try {
    if ($IncludeCode) {
        git add -A
    }
    else {
        foreach ($doc in $requiredDocs) {
            if (Test-Path (Join-Path $projectRoot $doc)) {
                git add -- $doc
            }
        }
    }

    $staged = git diff --cached --name-only
    if ([string]::IsNullOrWhiteSpace(($staged | Out-String))) {
        Write-Host "没有新的已暂存变更，本次无需提交。"
        exit 0
    }

    git commit -m $Message

    if ($Push) {
        $remoteUrl = ""
        try {
            $remoteUrl = (git remote get-url origin 2>$null)
        }
        catch {
            $remoteUrl = ""
        }

        if ([string]::IsNullOrWhiteSpace($remoteUrl)) {
            Write-Host "当前没有配置 origin，已完成本地提交，但未推送。"
            exit 0
        }

        git push origin HEAD
    }
}
finally {
    Pop-Location
}

