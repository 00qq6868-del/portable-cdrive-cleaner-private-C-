param(
    [string]$RepoUrl = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot

Push-Location $projectRoot
try {
    if (-not (Test-Path (Join-Path $projectRoot ".git"))) {
        git init | Out-Null
    }

    if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
        $RepoUrl = Read-Host "请粘贴 GitHub 私有仓库 HTTPS 地址"
    }

    if ([string]::IsNullOrWhiteSpace($RepoUrl)) {
        throw "没有提供仓库地址，已停止。"
    }

    $existingRemote = ""
    try {
        $existingRemote = (git remote get-url origin 2>$null)
    }
    catch {
        $existingRemote = ""
    }

    if ([string]::IsNullOrWhiteSpace($existingRemote)) {
        git remote add origin $RepoUrl
    }
    else {
        git remote set-url origin $RepoUrl
    }

    git branch -M main

    $hasCommits = $false
    try {
        git rev-parse --verify HEAD *> $null
        $hasCommits = $true
    }
    catch {
        $hasCommits = $false
    }

    if (-not $hasCommits) {
        git add -A
        git commit -m "chore: initialize project state before first push"
    }

    git push -u origin main
    Write-Host ""
    Write-Host "已连接 GitHub 私有仓库并完成首推。"
}
finally {
    Pop-Location
}

