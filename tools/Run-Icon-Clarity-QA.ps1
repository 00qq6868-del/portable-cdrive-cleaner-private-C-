param(
    [int]$Cycles = 3,
    [string]$PublishedExe = "E:\vscode Claude\PortableCDriveCleaner\publish\PortableCDriveCleaner-Windows\PortableCDriveCleaner.exe",
    [Parameter(Mandatory = $true)]
    [string]$InstalledExe,
    [string]$PublishScript = "E:\vscode Claude\PortableCDriveCleaner\publish.ps1",
    [int]$LaunchTimeoutSeconds = 45,
    [ValidateSet("CleanupCandidates", "CDriveOverview", "InfrequentApps")]
    [string]$QaView = "InfrequentApps"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$signature = @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class Win32Qa
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool repaint);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    public const int SW_RESTORE = 9;
    public const int WM_CLOSE = 0x0010;
}
"@

Add-Type -TypeDefinition $signature -Language CSharp

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Force -Path $Path | Out-Null
    }
}

function Stop-KnownAppProcesses {
    param([string]$InstalledExePath)

    Get-Process | Where-Object {
        try {
            $_.Path -eq $InstalledExePath -or $_.ProcessName -eq "PortableCDriveCleaner"
        }
        catch {
            $_.ProcessName -eq "PortableCDriveCleaner"
        }
    } | ForEach-Object {
        try {
            $_.CloseMainWindow() | Out-Null
            Start-Sleep -Milliseconds 500
            if (-not $_.HasExited) {
                Stop-Process -Id $_.Id -Force
            }
        }
        catch {
        }
    }
}

function Reinstall-PortableBuild {
    param(
        [string]$SourceExe,
        [string]$TargetExe,
        [string]$PublishScriptPath
    )

    if (-not (Test-Path -LiteralPath $PublishScriptPath)) {
        throw "Publish script not found: $PublishScriptPath"
    }

    Stop-KnownAppProcesses -InstalledExePath $TargetExe
    & powershell -NoProfile -ExecutionPolicy Bypass -File $PublishScriptPath | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Publish/install step failed."
    }
    if (-not (Test-Path -LiteralPath $TargetExe)) {
        throw "Installed EXE not found after publish: $TargetExe"
    }
}

function Wait-AppMainWindowProcess {
    param(
        [string]$InstalledExePath,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $process = Get-Process | Where-Object {
            try {
                $_.Path -eq $InstalledExePath -and $_.MainWindowHandle -ne 0
            }
            catch {
                $false
            }
        } | Sort-Object StartTime -Descending | Select-Object -First 1

        if ($null -ne $process) {
            return $process
        }

        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Resize-AppWindow {
    param(
        [IntPtr]$Handle,
        [int]$Width,
        [int]$Height
    )

    [void][Win32Qa]::ShowWindow($Handle, [Win32Qa]::SW_RESTORE)
    [void][Win32Qa]::SetForegroundWindow($Handle)
    [void][Win32Qa]::MoveWindow($Handle, 20, 20, $Width, $Height, $true)
    Start-Sleep -Milliseconds 1000
}

function Save-WindowScreenshot {
    param(
        [IntPtr]$Handle,
        [string]$OutputPath
    )

    $rect = New-Object Win32Qa+RECT
    if (-not [Win32Qa]::GetWindowRect($Handle, [ref]$rect)) {
        throw "Failed to read window bounds."
    }

    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        [void][Win32Qa]::ShowWindow($Handle, [Win32Qa]::SW_RESTORE)
        [void][Win32Qa]::SetForegroundWindow($Handle)
        Start-Sleep -Milliseconds 200
        $hdc = $graphics.GetHdc()
        $printed = $false
        try {
            $printed = [Win32Qa]::PrintWindow($Handle, $hdc, 2)
        }
        finally {
            $graphics.ReleaseHdc($hdc)
        }

        if (-not $printed) {
            $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
        }

        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Close-AppWindow {
    param(
        [System.Diagnostics.Process]$Process,
        [IntPtr]$Handle
    )

    try {
        [void][Win32Qa]::SendMessage($Handle, [Win32Qa]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    catch {
    }

    if (-not $Process.WaitForExit(10000)) {
        Stop-Process -Id $Process.Id -Force
        return $false
    }

    return $true
}

function Get-WindowMetrics {
    param([IntPtr]$Handle)

    $rect = New-Object Win32Qa+RECT
    [void][Win32Qa]::GetWindowRect($Handle, [ref]$rect)
    [pscustomobject]@{
        Left = $rect.Left
        Top = $rect.Top
        Width = $rect.Right - $rect.Left
        Height = $rect.Bottom - $rect.Top
        Dpi = [Win32Qa]::GetDpiForWindow($Handle)
    }
}

function Invoke-OneCycle {
    param(
        [int]$CycleIndex,
        [string]$SourceExe,
        [string]$TargetExe,
        [string]$ExpectedTitle,
        [string]$PublishScriptPath,
        [int]$TimeoutSeconds,
        [string]$OutputRoot,
        [string]$QaViewMode
    )

    $cycleRoot = Join-Path $OutputRoot ("cycle-" + $CycleIndex.ToString("00"))
    Ensure-Directory -Path $cycleRoot
    Reinstall-PortableBuild -SourceExe $SourceExe -TargetExe $TargetExe -PublishScriptPath $PublishScriptPath

    $launchStarted = Get-Date
    $launchArgs = @("--readonly", "--skip-migration-prompt", "--qa-view", $QaViewMode)
    $process = Start-Process -FilePath $TargetExe -ArgumentList $launchArgs -PassThru
    $windowProcess = Wait-AppMainWindowProcess -InstalledExePath $TargetExe -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $windowProcess) {
        throw ("Cycle {0}: main window not found within {1} seconds." -f $CycleIndex, $TimeoutSeconds)
    }
    $handle = [IntPtr]$windowProcess.MainWindowHandle

    $launchSeconds = [Math]::Round(((Get-Date) - $launchStarted).TotalSeconds, 2)
    Start-Sleep -Seconds 6

    $launchShot = Join-Path $cycleRoot "01-launch.png"
    Save-WindowScreenshot -Handle $handle -OutputPath $launchShot

    Resize-AppWindow -Handle $handle -Width 1380 -Height 930
    $largeShot = Join-Path $cycleRoot "02-large.png"
    Save-WindowScreenshot -Handle $handle -OutputPath $largeShot
    $largeMetrics = Get-WindowMetrics -Handle $handle

    Resize-AppWindow -Handle $handle -Width 1220 -Height 760
    $smallShot = Join-Path $cycleRoot "03-small.png"
    Save-WindowScreenshot -Handle $handle -OutputPath $smallShot
    $smallMetrics = Get-WindowMetrics -Handle $handle

    Start-Sleep -Seconds 12
    $settledShot = Join-Path $cycleRoot "04-settled.png"
    Save-WindowScreenshot -Handle $handle -OutputPath $settledShot
    $settledMetrics = Get-WindowMetrics -Handle $handle

    Resize-AppWindow -Handle $handle -Width 1380 -Height 930
    Start-Sleep -Seconds 4
    $populatedLargeShot = Join-Path $cycleRoot "05-populated-large.png"
    Save-WindowScreenshot -Handle $handle -OutputPath $populatedLargeShot
    $populatedLargeMetrics = Get-WindowMetrics -Handle $handle

    Resize-AppWindow -Handle $handle -Width 1220 -Height 760
    Start-Sleep -Seconds 4
    $populatedSmallShot = Join-Path $cycleRoot "06-populated-small.png"
    Save-WindowScreenshot -Handle $handle -OutputPath $populatedSmallShot
    $populatedSmallMetrics = Get-WindowMetrics -Handle $handle

    Start-Sleep -Seconds 1
    $closedNormally = Close-AppWindow -Process $windowProcess -Handle $handle
    $stillRunning = @(Get-Process | Where-Object {
        try {
            $_.Path -eq $TargetExe
        }
        catch {
            $false
        }
    }).Count -gt 0

    $result = [pscustomobject]@{
        Cycle = $CycleIndex
        InstalledCopy = $TargetExe
        LaunchSeconds = $launchSeconds
        WindowDpi = $largeMetrics.Dpi
        LargeWidth = $largeMetrics.Width
        LargeHeight = $largeMetrics.Height
        SmallWidth = $smallMetrics.Width
        SmallHeight = $smallMetrics.Height
        SettledWidth = $settledMetrics.Width
        SettledHeight = $settledMetrics.Height
        PopulatedLargeWidth = $populatedLargeMetrics.Width
        PopulatedLargeHeight = $populatedLargeMetrics.Height
        PopulatedSmallWidth = $populatedSmallMetrics.Width
        PopulatedSmallHeight = $populatedSmallMetrics.Height
        QaView = $QaViewMode
        LaunchArguments = $launchArgs
        ClosedNormally = $closedNormally
        ResidualProcess = $stillRunning
        LaunchScreenshot = $launchShot
        LargeScreenshot = $largeShot
        SmallScreenshot = $smallShot
        SettledScreenshot = $settledShot
        PopulatedLargeScreenshot = $populatedLargeShot
        PopulatedSmallScreenshot = $populatedSmallShot
        Verdict = if (-not $closedNormally -or $stillRunning) { "FAIL" } else { "PASS_PENDING_VISUAL" }
    }

    ($result | ConvertTo-Json -Depth 4) | Set-Content -Path (Join-Path $cycleRoot "result.json") -Encoding UTF8
    return $result
}

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
$outputRoot = Join-Path "E:\vscode Claude\PortableCDriveCleaner\artifacts\icon-qa" $timestamp
Ensure-Directory -Path $outputRoot

$screen = [System.Windows.Forms.Screen]::PrimaryScreen
$windowMetrics = Get-ItemProperty "HKCU:\Control Panel\Desktop\WindowMetrics"
$environmentReport = [pscustomobject]@{
    Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    PrimaryScreenWidth = $screen.Bounds.Width
    PrimaryScreenHeight = $screen.Bounds.Height
    WorkingAreaWidth = $screen.WorkingArea.Width
    WorkingAreaHeight = $screen.WorkingArea.Height
    AppliedDpi = $windowMetrics.AppliedDPI
    ShellIconSize = $windowMetrics."Shell Icon Size"
    PublishedExe = $PublishedExe
    InstalledExe = $InstalledExe
    PublishScript = $PublishScript
    QaView = $QaView
}
$environmentReport | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $outputRoot "environment.json") -Encoding UTF8

$results = New-Object System.Collections.Generic.List[object]
for ($i = 1; $i -le $Cycles; $i++) {
    $results.Add((Invoke-OneCycle -CycleIndex $i -SourceExe $PublishedExe -TargetExe $InstalledExe -ExpectedTitle "" -PublishScriptPath $PublishScript -TimeoutSeconds $LaunchTimeoutSeconds -OutputRoot $outputRoot -QaViewMode $QaView))
}

$resultItems = $results.ToArray()

$summary = [pscustomobject]@{
    Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    OutputRoot = $outputRoot
    Cycles = $Cycles
    QaView = $QaView
    Results = $resultItems
    AllCyclesClosedCleanly = (@($resultItems | Where-Object { -not $_.ClosedNormally -or $_.ResidualProcess }).Count -eq 0)
    VisualReviewRequired = $true
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $outputRoot "summary.json") -Encoding UTF8
$resultItems | Format-Table Cycle, LaunchSeconds, WindowDpi, ClosedNormally, ResidualProcess, Verdict -AutoSize
Write-Host "QA output directory: $outputRoot"
