Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
$distRoot = Join-Path $projectRoot "dist"
$publishRoot = Join-Path $projectRoot "publish"
$readmeSource = Join-Path $projectRoot "README.md"
$iconBuilder = Join-Path $projectRoot "tools\Build-AppIcon.ps1"
$desktopRoot = [Environment]::GetFolderPath("Desktop")
$desktopReleaseFolder = Join-Path $desktopRoot "磁盘清理器-最新版本"
$installedExecutableName = "磁盘清理器.exe"
$desktopShortcutName = "磁盘清理器.lnk"
$installFolderShortcutName = "启动磁盘清理器.lnk"
$elevatedLauncherTaskName = "磁盘清理器-高权限启动"

function Normalize-FilePath {
    param(
        [string]$PathValue
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    try {
        return [System.IO.Path]::GetFullPath($PathValue.Trim('"')).TrimEnd('\')
    }
    catch {
        return $PathValue.Trim('"').TrimEnd('\')
    }
}

function Get-ElevatedLauncherTaskCommand {
    param(
        [Parameter(Mandatory = $true)][string]$TaskName
    )

    try {
        $xmlText = & schtasks.exe /Query /TN $TaskName /XML 2>$null
        if ([string]::IsNullOrWhiteSpace($xmlText)) {
            return $null
        }

        [xml]$taskXml = $xmlText
        $command = [string]$taskXml.Task.Actions.Exec.Command
        $arguments = [string]$taskXml.Task.Actions.Exec.Arguments
        if ([string]::IsNullOrWhiteSpace($command)) {
            return $null
        }

        return [PSCustomObject]@{
            Command = $command
            Arguments = $arguments
        }
    }
    catch {
        return $null
    }
}

function New-OrUpdateShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$Description,
        [string]$Arguments = "",
        [string]$IconLocation = ""
    )

    $shortcutDirectory = Split-Path -Parent $ShortcutPath
    if (-not [string]::IsNullOrWhiteSpace($shortcutDirectory)) {
        New-Item -ItemType Directory -Force -Path $shortcutDirectory | Out-Null
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Description = $Description
    $shortcut.Arguments = $Arguments
    $shortcut.IconLocation = if ([string]::IsNullOrWhiteSpace($IconLocation)) { $TargetPath } else { $IconLocation }
    $shortcut.Save()
}

function Get-ShortcutInstallDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$InstalledExecutableName
    )

    if (-not (Test-Path -LiteralPath $ShortcutPath)) {
        return $null
    }

    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        $targetPath = [string]$shortcut.TargetPath
        $workingDirectory = [string]$shortcut.WorkingDirectory

        if (-not [string]::IsNullOrWhiteSpace($targetPath) -and [System.IO.Path]::GetFileName($targetPath) -ieq $InstalledExecutableName) {
            return Split-Path -Parent $targetPath
        }

        if (-not [string]::IsNullOrWhiteSpace($workingDirectory) -and (Test-Path -LiteralPath (Join-Path $workingDirectory $InstalledExecutableName))) {
            return $workingDirectory
        }
    }
    catch {
    }

    return $null
}

function Resolve-LocalInstallDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$DesktopRoot,
        [Parameter(Mandatory = $true)][string]$InstallFolderName,
        [Parameter(Mandatory = $true)][string]$InstalledExecutableName
    )

    $shortcutPath = Join-Path $DesktopRoot $desktopShortcutName
    $shortcutInstallDirectory = Get-ShortcutInstallDirectory -ShortcutPath $shortcutPath -InstalledExecutableName $InstalledExecutableName
    if (-not [string]::IsNullOrWhiteSpace($shortcutInstallDirectory)) {
        return $shortcutInstallDirectory
    }

    $preferredPath = "D:\$InstallFolderName"
    if (Test-Path -LiteralPath (Join-Path $preferredPath $InstalledExecutableName)) {
        return $preferredPath
    }

    $fixedDrives = [System.IO.DriveInfo]::GetDrives() | Where-Object {
        try {
            $_.IsReady -and $_.DriveType -eq [System.IO.DriveType]::Fixed
        }
        catch {
            $false
        }
    }

    $existingInstall = $fixedDrives |
        ForEach-Object { Join-Path $_.RootDirectory.FullName $InstallFolderName } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ $InstalledExecutableName) } |
        Sort-Object { $_ -like 'C:\*' }, @{ Expression = { (Get-Item -LiteralPath (Join-Path $_ $InstalledExecutableName)).LastWriteTimeUtc }; Descending = $true } |
        Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($existingInstall)) {
        return $existingInstall
    }

    $recommendedDrive = $fixedDrives |
        Sort-Object { $_.RootDirectory.FullName -like 'C:\*' }, @{ Expression = { $_.AvailableFreeSpace }; Descending = $true }, @{ Expression = { $_.TotalSize }; Descending = $true } |
        Select-Object -First 1
    if ($null -ne $recommendedDrive) {
        return (Join-Path $recommendedDrive.RootDirectory.FullName $InstallFolderName)
    }

    return $null
}

function Sync-LocalInstallArtifacts {
    param(
        [Parameter(Mandatory = $true)][string]$PublishedExecutablePath,
        [Parameter(Mandatory = $true)][string]$DesktopRoot,
        [Parameter(Mandatory = $true)][string]$InstallFolderName,
        [Parameter(Mandatory = $true)][string]$InstalledExecutableName
    )

    $installDirectory = Resolve-LocalInstallDirectory -DesktopRoot $DesktopRoot -InstallFolderName $InstallFolderName -InstalledExecutableName $InstalledExecutableName
    if ([string]::IsNullOrWhiteSpace($installDirectory)) {
        Write-Host "未找到本机安装目录，跳过本机安装副本同步。"
        return
    }

    New-Item -ItemType Directory -Force -Path $installDirectory | Out-Null
    $installedExecutablePath = Join-Path $installDirectory $InstalledExecutableName
    if (Test-Path -LiteralPath $installedExecutablePath) {
        Remove-Item -LiteralPath $installedExecutablePath -Force
    }

    Copy-Item -LiteralPath $PublishedExecutablePath -Destination $installedExecutablePath -Force

    $taskInfo = Get-ElevatedLauncherTaskCommand -TaskName $elevatedLauncherTaskName
    $taskTarget = if ($null -eq $taskInfo) { $null } else { Normalize-FilePath -PathValue $taskInfo.Command }
    $normalizedInstalledExecutablePath = Normalize-FilePath -PathValue $installedExecutablePath
    $useElevatedShortcut = $taskTarget -and ($taskTarget -ieq $normalizedInstalledExecutablePath)

    $desktopShortcutPath = Join-Path $DesktopRoot $desktopShortcutName
    if ($useElevatedShortcut) {
        New-OrUpdateShortcut `
            -ShortcutPath $desktopShortcutPath `
            -TargetPath (Join-Path $env:WINDIR "System32\\schtasks.exe") `
            -WorkingDirectory $installDirectory `
            -Description "便携式磁盘清理器（高权限启动）" `
            -Arguments "/Run /TN `"$elevatedLauncherTaskName`"" `
            -IconLocation $installedExecutablePath
    }
    else {
        New-OrUpdateShortcut `
            -ShortcutPath $desktopShortcutPath `
            -TargetPath $installedExecutablePath `
            -WorkingDirectory $installDirectory `
            -Description "便携式磁盘清理器" `
            -IconLocation $installedExecutablePath
    }

    $installFolderShortcutPath = Join-Path $installDirectory $installFolderShortcutName
    if ($useElevatedShortcut) {
        New-OrUpdateShortcut `
            -ShortcutPath $installFolderShortcutPath `
            -TargetPath (Join-Path $env:WINDIR "System32\\schtasks.exe") `
            -WorkingDirectory $installDirectory `
            -Description "磁盘清理器（安装目录高权限入口）" `
            -Arguments "/Run /TN `"$elevatedLauncherTaskName`"" `
            -IconLocation $installedExecutablePath
    }
    else {
        New-OrUpdateShortcut `
            -ShortcutPath $installFolderShortcutPath `
            -TargetPath $installedExecutablePath `
            -WorkingDirectory $installDirectory `
            -Description "磁盘清理器（安装目录入口）" `
            -IconLocation $installedExecutablePath
    }

    Write-Host "已同步本机安装目录：$installedExecutablePath"
    Write-Host "已同步桌面快捷方式：$desktopShortcutPath"
    Write-Host "已同步安装目录入口：$installFolderShortcutPath"
    Write-Host ("快捷方式模式：" + $(if ($useElevatedShortcut) { "高权限任务" } else { "直接 EXE" }))
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

& powershell -ExecutionPolicy Bypass -File $iconBuilder

$targets = @(
    @{ Rid = "win-x64"; Name = "PortableCDriveCleaner-Windows" },
    @{ Rid = "win-arm64"; Name = "PortableCDriveCleaner-MPC-ARM64" }
)

foreach ($target in $targets) {
    $outDir = Join-Path $publishRoot $target.Name
    if (Test-Path -LiteralPath $outDir) {
        Remove-Item -LiteralPath $outDir -Recurse -Force
    }

    & $dotnet publish (Join-Path $projectRoot "PortableCDriveCleaner.csproj") `
        -c Release `
        -r $target.Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishReadyToRun=true `
        -p:ReadyToRunUseCrossgen2=true `
        -p:DebugType=None `
        -o $outDir

    Copy-Item -LiteralPath $readmeSource -Destination (Join-Path $outDir "README.md") -Force

    $zipPath = Join-Path $distRoot ($target.Name + ".zip")
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
}

$installerSource = Join-Path $publishRoot "PortableCDriveCleaner-Windows\\PortableCDriveCleaner.exe"
$installerTarget = Join-Path $distRoot "磁盘清理器安装器.exe"

Get-ChildItem -LiteralPath $distRoot -File -Filter *.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "磁盘清理器安装器.exe" } |
    Remove-Item -Force -ErrorAction SilentlyContinue

if (Test-Path -LiteralPath $installerTarget) {
    Remove-Item -LiteralPath $installerTarget -Force
}

Copy-Item -LiteralPath $installerSource -Destination $installerTarget -Force

$desktopCleanupPaths = @(
    $desktopReleaseFolder,
    (Join-Path $desktopRoot "磁盘清理器发布包"),
    (Join-Path $desktopRoot "磁盘清理器-最新包"),
    (Join-Path $desktopRoot "PortableCDriveCleaner发布包"),
    (Join-Path $desktopRoot "磁盘清理器安装器.exe"),
    (Join-Path $desktopRoot "PortableCDriveCleaner-Windows.zip"),
    (Join-Path $desktopRoot "PortableCDriveCleaner-MPC-ARM64.zip")
)

foreach ($cleanupPath in $desktopCleanupPaths | Select-Object -Unique) {
    if (Test-Path -LiteralPath $cleanupPath) {
        Remove-Item -LiteralPath $cleanupPath -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $desktopReleaseFolder | Out-Null
Copy-Item -LiteralPath $installerTarget -Destination (Join-Path $desktopReleaseFolder "磁盘清理器安装器.exe") -Force
Copy-Item -LiteralPath (Join-Path $distRoot "PortableCDriveCleaner-Windows.zip") -Destination (Join-Path $desktopReleaseFolder "PortableCDriveCleaner-Windows.zip") -Force
Copy-Item -LiteralPath (Join-Path $distRoot "PortableCDriveCleaner-MPC-ARM64.zip") -Destination (Join-Path $desktopReleaseFolder "PortableCDriveCleaner-MPC-ARM64.zip") -Force

$desktopReadme = @"
这是桌面上的最新磁盘清理器发布包。

1. 优先双击“磁盘清理器安装器.exe”
2. 安装时建议授予管理员权限
3. 默认建议安装到非 C 固定盘

每次重新打包时，这个文件夹会自动更新为最新版，旧的同类发布包会先被清掉。
"@

Set-Content -LiteralPath (Join-Path $desktopReleaseFolder "说明.txt") -Value $desktopReadme -Encoding UTF8

Sync-LocalInstallArtifacts `
    -PublishedExecutablePath $installerSource `
    -DesktopRoot $desktopRoot `
    -InstallFolderName "磁盘清理器" `
    -InstalledExecutableName $installedExecutableName

Get-ChildItem -LiteralPath $distRoot | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
