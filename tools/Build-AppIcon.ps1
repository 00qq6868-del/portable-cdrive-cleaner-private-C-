param(
    [string]$OutputPng = "",
    [string]$OutputIco = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $arc = New-Object System.Drawing.RectangleF($X, $Y, $diameter, $diameter)
    $path.AddArc($arc, 180, 90)
    $arc.X = $X + $Width - $diameter
    $path.AddArc($arc, 270, 90)
    $arc.Y = $Y + $Height - $diameter
    $path.AddArc($arc, 0, 90)
    $arc.X = $X
    $path.AddArc($arc, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    try {
        $scale = $Size / 256.0
        $accent = [System.Drawing.ColorTranslator]::FromHtml("#31D07A")
        $accentStrong = [System.Drawing.ColorTranslator]::FromHtml("#69F0AE")
        $cyan = [System.Drawing.ColorTranslator]::FromHtml("#7FE6FF")
        $surface = [System.Drawing.ColorTranslator]::FromHtml("#0A0F15")
        $surfaceHigh = [System.Drawing.ColorTranslator]::FromHtml("#141D27")
        $surfaceGlow = [System.Drawing.ColorTranslator]::FromHtml("#1A2530")
        $border = [System.Drawing.ColorTranslator]::FromHtml("#27333D")

        $outerPath = New-RoundedRectanglePath -X (14 * $scale) -Y (14 * $scale) -Width (228 * $scale) -Height (228 * $scale) -Radius (54 * $scale)
        $outerBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF (28 * $scale), (18 * $scale)),
            (New-Object System.Drawing.PointF (230 * $scale), (238 * $scale)),
            $surface,
            $surfaceHigh)
        $graphics.FillPath($outerBrush, $outerPath)

        $innerPath = New-RoundedRectanglePath -X (28 * $scale) -Y (28 * $scale) -Width (200 * $scale) -Height (200 * $scale) -Radius (40 * $scale)
        $innerBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF (36 * $scale), (34 * $scale)),
            (New-Object System.Drawing.PointF (220 * $scale), (226 * $scale)),
            $surfaceHigh,
            $surfaceGlow)
        $graphics.FillPath($innerBrush, $innerPath)

        $highlightPath = New-RoundedRectanglePath -X (28 * $scale) -Y (28 * $scale) -Width (200 * $scale) -Height (104 * $scale) -Radius (40 * $scale)
        $highlightBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF (28 * $scale), (28 * $scale)),
            (New-Object System.Drawing.PointF (180 * $scale), (140 * $scale)),
            ([System.Drawing.Color]::FromArgb(54, 255, 255, 255)),
            ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)))
        $graphics.FillPath($highlightBrush, $highlightPath)

        $borderPen = New-Object System.Drawing.Pen($border, [Math]::Max(2.0, 3.0 * $scale))
        $graphics.DrawPath($borderPen, $outerPath)

        $glowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(92, $accentStrong), [Math]::Max(4.0, 8.0 * $scale))
        $glowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawArc($glowPen, 54 * $scale, 48 * $scale, 142 * $scale, 142 * $scale, 44, 272)

        $accentPen = New-Object System.Drawing.Pen($accent, [Math]::Max(8.0, 18.0 * $scale))
        $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $accentPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawArc($accentPen, 58 * $scale, 52 * $scale, 136 * $scale, 136 * $scale, 48, 264)

        $slashPen = New-Object System.Drawing.Pen($cyan, [Math]::Max(5.0, 11.0 * $scale))
        $slashPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $slashPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawLine($slashPen, 154 * $scale, 84 * $scale, 190 * $scale, 56 * $scale)

        $slotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 20, 28, 38))
        $slotPath = New-RoundedRectanglePath -X (120 * $scale) -Y (126 * $scale) -Width (78 * $scale) -Height (24 * $scale) -Radius (12 * $scale)
        $graphics.FillPath($slotBrush, $slotPath)

        $slotGlowBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF (124 * $scale), (126 * $scale)),
            (New-Object System.Drawing.PointF (194 * $scale), (148 * $scale)),
            ([System.Drawing.Color]::FromArgb(255, $accent)),
            ([System.Drawing.Color]::FromArgb(255, $cyan)))
        $graphics.FillRectangle($slotGlowBrush, 132 * $scale, 135 * $scale, 54 * $scale, 6 * $scale)

        $dotBrush = New-Object System.Drawing.SolidBrush($accentStrong)
        $graphics.FillEllipse($dotBrush, 178 * $scale, 172 * $scale, 18 * $scale, 18 * $scale)

        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

function Write-MultiResolutionIcon {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $frames = foreach ($size in $Sizes) {
        $bitmap = New-IconBitmap -Size $size
        try {
            $stream = New-Object System.IO.MemoryStream
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            [PSCustomObject]@{
                Size = $size
                Bytes = $stream.ToArray()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $fileStream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter($fileStream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)

        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -ge 256) { 0 } else { [byte]$frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frame.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frame.Bytes.Length
        }

        foreach ($frame in $frames) {
            $writer.Write($frame.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $fileStream.Dispose()
    }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$assetsRoot = Join-Path $projectRoot "Assets"
if ([string]::IsNullOrWhiteSpace($OutputPng)) {
    $OutputPng = Join-Path $assetsRoot "AppIcon-preview.png"
}

if ([string]::IsNullOrWhiteSpace($OutputIco)) {
    $OutputIco = Join-Path $assetsRoot "App.ico"
}

New-Item -ItemType Directory -Force -Path $assetsRoot | Out-Null

$previewBitmap = New-IconBitmap -Size 512
try {
    $previewBitmap.Save($OutputPng, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $previewBitmap.Dispose()
}

Write-MultiResolutionIcon -Path $OutputIco -Sizes @(16, 20, 24, 32, 40, 48, 64, 128, 256)

Write-Host "图标已生成:"
Write-Host "PNG  -> $OutputPng"
Write-Host "ICO  -> $OutputIco"
