param(
    [int]$Cycles = 3,
    [string]$PublishedExe = "",
    [Parameter(Mandatory = $true)]
    [string]$InstalledExe,
    [string]$PublishScript = "",
    [string]$OutputRoot = "",
    [int]$LaunchTimeoutSeconds = 45,
    [ValidateSet("CleanupCandidates", "CDriveOverview", "InfrequentApps")]
    [string]$QaView = "InfrequentApps",
    [switch]$AllowStartupRefresh
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -Scope Global -ErrorAction SilentlyContinue) {
    $global:PSNativeCommandUseErrorActionPreference = $false
}
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($PublishedExe)) {
    $PublishedExe = Join-Path $ProjectRoot "publish\PortableCDriveCleaner-Windows\PortableCDriveCleaner.exe"
}

if ([string]::IsNullOrWhiteSpace($PublishScript)) {
    $PublishScript = Join-Path $ProjectRoot "publish.ps1"
}

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

function Exercise-ResizePath {
    param([IntPtr]$Handle)

    $sizes = @(
        @{ Width = 1220; Height = 760 },
        @{ Width = 1280; Height = 800 },
        @{ Width = 1380; Height = 930 },
        @{ Width = 1260; Height = 780 },
        @{ Width = 1220; Height = 760 }
    )

    foreach ($size in $sizes) {
        Resize-AppWindow -Handle $Handle -Width $size.Width -Height $size.Height
        Start-Sleep -Milliseconds 350
    }
}

function Get-StableWindowRect {
    param([IntPtr]$Handle)

    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        [void][Win32Qa]::ShowWindow($Handle, [Win32Qa]::SW_RESTORE)
        [void][Win32Qa]::SetForegroundWindow($Handle)
        Start-Sleep -Milliseconds 250

        $rect = New-Object Win32Qa+RECT
        if ([Win32Qa]::GetWindowRect($Handle, [ref]$rect)) {
            $width = $rect.Right - $rect.Left
            $height = $rect.Bottom - $rect.Top
            if ($width -ge 760 -and $height -ge 520) {
                return $rect
            }
        }

        Start-Sleep -Milliseconds 250
    }

    [void][Win32Qa]::MoveWindow($Handle, 20, 20, 1220, 760, $true)
    Start-Sleep -Milliseconds 1000
    $fallbackRect = New-Object Win32Qa+RECT
    if (-not [Win32Qa]::GetWindowRect($Handle, [ref]$fallbackRect)) {
        throw "Failed to read window bounds."
    }

    return $fallbackRect
}

function Save-WindowScreenshot {
    param(
        [IntPtr]$Handle,
        [string]$OutputPath
    )

    $rect = Get-StableWindowRect -Handle $Handle

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

function Wait-QAStateFile {
    param(
        [string]$Path,
        [int]$TimeoutSeconds = 10
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $Path) {
            try {
                return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
            }
            catch {
            }
        }

        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)

    return $null
}

function Get-JsonPropertyValue {
    param(
        [object]$Object,
        [string]$Name,
        [object]$DefaultValue = $null
    )

    if ($null -eq $Object) {
        return $DefaultValue
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $DefaultValue
    }

    return $property.Value
}

function Add-ScreenshotHealthFailures {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [string[]]$ScreenshotPaths
    )

    foreach ($screenshotPath in $ScreenshotPaths) {
        if ([string]::IsNullOrWhiteSpace($screenshotPath)) {
            continue
        }

        if (-not (Test-Path -LiteralPath $screenshotPath)) {
            $Failures.Add("Screenshot missing: $screenshotPath")
            continue
        }

        $image = $null
        try {
            $image = [System.Drawing.Image]::FromFile($screenshotPath)
            if ($image.Width -lt 760 -or $image.Height -lt 520) {
                $Failures.Add(("Screenshot dimensions too small to trust: {0} ({1}x{2})." -f $screenshotPath, $image.Width, $image.Height))
            }
        }
        catch {
            $Failures.Add(("Screenshot unreadable: {0}. {1}" -f $screenshotPath, $_.Exception.Message))
        }
        finally {
            if ($null -ne $image) {
                $image.Dispose()
            }
        }
    }
}

function Resolve-ImageMagickPath {
    $command = Get-Command "magick.exe" -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $knownPaths = @(
        "C:\Program Files\ImageMagick-7.1.2-Q16-HDRI\magick.exe",
        "C:\Program Files\ImageMagick-7.1.1-Q16-HDRI\magick.exe",
        "C:\Program Files\ImageMagick-7.1.0-Q16-HDRI\magick.exe"
    )

    foreach ($path in $knownPaths) {
        if (Test-Path -LiteralPath $path) {
            return $path
        }
    }

    return ""
}

function Invoke-ImageMagick {
    param(
        [string]$MagickPath,
        [string[]]$Arguments
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $MagickPath
    $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join " ")
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $standardOutput = $process.StandardOutput.ReadToEnd()
    $standardError = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = (($standardOutput + "`n" + $standardError).Trim())
    }
}

function Get-PngDimensions {
    param([string]$Path)

    $image = $null
    try {
        $image = [System.Drawing.Image]::FromFile($Path)
        return [pscustomobject]@{
            Width = $image.Width
            Height = $image.Height
        }
    }
    finally {
        if ($null -ne $image) {
            $image.Dispose()
        }
    }
}

function Add-LoadingStabilityFailures {
    param(
        [System.Collections.Generic.List[string]]$Failures,
        [string[]]$LoadingScreenshots,
        [string]$CycleRoot
    )

    $metrics = New-Object System.Collections.Generic.List[object]
    $magickPath = Resolve-ImageMagickPath
    if ([string]::IsNullOrWhiteSpace($magickPath) -or -not (Test-Path -LiteralPath $magickPath)) {
        $Failures.Add("ImageMagick magick.exe not found; loading stability comparison is required.")
        return $metrics.ToArray()
    }

    $stabilityRoot = Join-Path $CycleRoot "loading-stability"
    Ensure-Directory -Path $stabilityRoot
    $startIndex = [Math]::Min(3, [Math]::Max(0, $LoadingScreenshots.Count - 2))
    $endIndex = [Math]::Max($startIndex, $LoadingScreenshots.Count - 2)
    for ($i = $startIndex; $i -le $endIndex; $i++) {
        $first = $LoadingScreenshots[$i]
        $second = $LoadingScreenshots[$i + 1]
        if (-not (Test-Path -LiteralPath $first) -or -not (Test-Path -LiteralPath $second)) {
            continue
        }

        $dimensions = Get-PngDimensions -Path $first
        $cropY = [Math]::Min(90, [Math]::Max(0, [int]($dimensions.Height * 0.18)))
        $cropHeight = [Math]::Min([Math]::Max(220, $dimensions.Height - 240), $dimensions.Height - $cropY - 24)
        if ($cropHeight -lt 160) {
            $Failures.Add(("Loading stability crop too small to trust: {0}x{1}+0+{2}" -f $dimensions.Width, $cropHeight, $cropY))
            continue
        }

        $cropGeometry = ("{0}x{1}+0+{2}" -f $dimensions.Width, $cropHeight, $cropY)
        $firstCrop = Join-Path $stabilityRoot ("loading-{0:00}-static.png" -f $i)
        $secondCrop = Join-Path $stabilityRoot ("loading-{0:00}-static.png" -f ($i + 1))
        $compareOutput = Join-Path $stabilityRoot ("loading-{0:00}-{1:00}-diff.png" -f $i, ($i + 1))

        [void](Invoke-ImageMagick -MagickPath $magickPath -Arguments @($first, "-crop", $cropGeometry, "+repage", $firstCrop))
        [void](Invoke-ImageMagick -MagickPath $magickPath -Arguments @($second, "-crop", $cropGeometry, "+repage", $secondCrop))
        $comparison = Invoke-ImageMagick -MagickPath $magickPath -Arguments @("compare", "-metric", "AE", $firstCrop, $secondCrop, $compareOutput)
        $match = [regex]::Match($comparison.Output, '^\s*(\d+)')
        if (-not $match.Success) {
            $Failures.Add(("ImageMagick compare did not return an AE metric for loading screenshots {0} and {1}: {2}" -f $i, ($i + 1), $comparison.Output))
            continue
        }

        $delta = [int]$match.Groups[1].Value
        $metric = [pscustomobject]@{
            From = [IO.Path]::GetFileName($first)
            To = [IO.Path]::GetFileName($second)
            Crop = $cropGeometry
            StaticAreaChangedPixels = $delta
            DiffImage = $compareOutput
            Tool = $magickPath
        }
        $metrics.Add($metric)
        if ($delta -gt 120) {
            $Failures.Add(("Loading static area still jitters between {0} and {1}: {2} changed pixels, crop {3}." -f $metric.From, $metric.To, $delta, $cropGeometry))
        }
    }

    return $metrics.ToArray()
}

function ConvertTo-CommandLineArgument {
    param([string]$Value)

    if ($null -eq $Value) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    return '"' + ($Value -replace '\\(?=")', '$0' -replace '"', '\"') + '"'
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
        [string]$QaViewMode,
        [bool]$AllowStartupRefresh
    )

    $cycleRoot = Join-Path $OutputRoot ("cycle-" + $CycleIndex.ToString("00"))
    Ensure-Directory -Path $cycleRoot
    Reinstall-PortableBuild -SourceExe $SourceExe -TargetExe $TargetExe -PublishScriptPath $PublishScriptPath

    $launchStarted = Get-Date
    $qaStateFile = Join-Path $cycleRoot "qa-state.json"
    $launchArgs = @("--readonly", "--skip-migration-prompt", "--qa-view", $QaViewMode, "--qa-state-file", $qaStateFile)
    if ($AllowStartupRefresh) {
        $launchArgs += "--qa-refresh"
    }
    $launchArgumentLine = ($launchArgs | ForEach-Object { ConvertTo-CommandLineArgument $_ }) -join " "
    $process = Start-Process -FilePath $TargetExe -ArgumentList $launchArgumentLine -PassThru
    $windowProcess = Wait-AppMainWindowProcess -InstalledExePath $TargetExe -TimeoutSeconds $TimeoutSeconds
    if ($null -eq $windowProcess) {
        throw ("Cycle {0}: main window not found within {1} seconds." -f $CycleIndex, $TimeoutSeconds)
    }
    $handle = [IntPtr]$windowProcess.MainWindowHandle

    $launchSeconds = [Math]::Round(((Get-Date) - $launchStarted).TotalSeconds, 2)
    $loadingScreenshots = New-Object System.Collections.Generic.List[string]
    for ($loadingIndex = 0; $loadingIndex -lt 10; $loadingIndex++) {
        $loadingShot = Join-Path $cycleRoot ("00-loading-" + $loadingIndex.ToString("00") + ".png")
        Save-WindowScreenshot -Handle $handle -OutputPath $loadingShot
        $loadingScreenshots.Add($loadingShot)
        Start-Sleep -Milliseconds 900
    }

    $qaState = Wait-QAStateFile -Path $qaStateFile
    $qaFailures = New-Object System.Collections.Generic.List[string]
    if ($null -eq $qaState) {
        $qaFailures.Add("QA state file was not written.")
    }
    else {
        if ($qaState.ActiveView -ne $QaViewMode) {
            $qaFailures.Add(("Expected QA view {0}, got {1}." -f $QaViewMode, $qaState.ActiveView))
        }

        if ([int]$qaState.ActiveVisibleRows -lt 3) {
            $qaFailures.Add(("Expected at least 3 active visible rows, got {0}." -f $qaState.ActiveVisibleRows))
        }

        if ($QaViewMode -eq "InfrequentApps" -and [int]$qaState.SnapshotInfrequentRows -lt 3) {
            $qaFailures.Add(("Expected cached infrequent-app rows, got {0}." -f $qaState.SnapshotInfrequentRows))
        }

        if ([int]$qaState.ContentHeight -lt 180) {
            $qaFailures.Add(("Expected content area height >= 180, got {0}." -f $qaState.ContentHeight))
        }

        $contentHeightRatio = [double](Get-JsonPropertyValue -Object $qaState -Name "ContentHeightRatio" -DefaultValue 0)
        if ($contentHeightRatio -lt 0.55) {
            $qaFailures.Add(("Expected content height ratio >= 0.55, got {0}." -f $contentHeightRatio))
        }

        $maxViewButtonExcess = [int](Get-JsonPropertyValue -Object $qaState -Name "MaxViewButtonExcess" -DefaultValue 0)
        if ($maxViewButtonExcess -gt 46) {
            $qaFailures.Add(("View mode buttons are still too wide: max excess {0}px." -f $maxViewButtonExcess))
        }

        $maxViewButtonRatio = [double](Get-JsonPropertyValue -Object $qaState -Name "MaxViewButtonRatio" -DefaultValue 0)
        if ($maxViewButtonRatio -gt 2.8) {
            $qaFailures.Add(("View mode buttons are still oversized: max ratio {0}." -f $maxViewButtonRatio))
        }

        $maxActionButtonExcess = [int](Get-JsonPropertyValue -Object $qaState -Name "MaxActionButtonExcess" -DefaultValue 0)
        if ($maxActionButtonExcess -gt 62) {
            $qaFailures.Add(("Toolbar buttons are still too wide: max excess {0}px." -f $maxActionButtonExcess))
        }
    }

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

    Exercise-ResizePath -Handle $handle
    $resizeStressShot = Join-Path $cycleRoot "03b-resize-stress.png"
    Save-WindowScreenshot -Handle $handle -OutputPath $resizeStressShot

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

    Add-ScreenshotHealthFailures -Failures $qaFailures -ScreenshotPaths @(
        $loadingScreenshots.ToArray()
        $launchShot
        $largeShot
        $smallShot
        $resizeStressShot
        $settledShot
        $populatedLargeShot
        $populatedSmallShot
    )
    $loadingStabilityMetrics = Add-LoadingStabilityFailures -Failures $qaFailures -LoadingScreenshots $loadingScreenshots.ToArray() -CycleRoot $cycleRoot

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
        LaunchArgumentLine = $launchArgumentLine
        QaStateFile = $qaStateFile
        QaState = $qaState
        QaFailures = $qaFailures.ToArray()
        ClosedNormally = $closedNormally
        ResidualProcess = $stillRunning
        LoadingScreenshots = $loadingScreenshots.ToArray()
        LoadingStabilityMetrics = $loadingStabilityMetrics
        LaunchScreenshot = $launchShot
        LargeScreenshot = $largeShot
        SmallScreenshot = $smallShot
        ResizeStressScreenshot = $resizeStressShot
        SettledScreenshot = $settledShot
        PopulatedLargeScreenshot = $populatedLargeShot
        PopulatedSmallScreenshot = $populatedSmallShot
        Verdict = if (-not $closedNormally -or $stillRunning -or $qaFailures.Count -gt 0) { "FAIL" } else { "PASS_PENDING_VISUAL" }
    }

    ($result | ConvertTo-Json -Depth 4) | Set-Content -Path (Join-Path $cycleRoot "result.json") -Encoding UTF8
    return $result
}

$timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $outputRoot = Join-Path (Join-Path $ProjectRoot "artifacts\icon-qa") $timestamp
}
else {
    $outputRoot = $OutputRoot
}
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
    AllowStartupRefresh = [bool]$AllowStartupRefresh
    ImageMagickPath = Resolve-ImageMagickPath
}
$environmentReport | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $outputRoot "environment.json") -Encoding UTF8

$results = New-Object System.Collections.Generic.List[object]
for ($i = 1; $i -le $Cycles; $i++) {
    $results.Add((Invoke-OneCycle -CycleIndex $i -SourceExe $PublishedExe -TargetExe $InstalledExe -ExpectedTitle "" -PublishScriptPath $PublishScript -TimeoutSeconds $LaunchTimeoutSeconds -OutputRoot $outputRoot -QaViewMode $QaView -AllowStartupRefresh ([bool]$AllowStartupRefresh)))
}

$resultItems = $results.ToArray()

$summary = [pscustomobject]@{
    Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    OutputRoot = $outputRoot
    Cycles = $Cycles
    QaView = $QaView
    AllowStartupRefresh = [bool]$AllowStartupRefresh
    Results = $resultItems
    AllCyclesClosedCleanly = (@($resultItems | Where-Object { -not $_.ClosedNormally -or $_.ResidualProcess }).Count -eq 0)
    AllCyclesPassedHardGate = (@($resultItems | Where-Object { $_.Verdict -eq "FAIL" }).Count -eq 0)
    VisualReviewRequired = $true
}

$summary | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $outputRoot "summary.json") -Encoding UTF8
$resultItems | Format-Table Cycle, LaunchSeconds, WindowDpi, ClosedNormally, ResidualProcess, Verdict -AutoSize
Write-Host "QA output directory: $outputRoot"
