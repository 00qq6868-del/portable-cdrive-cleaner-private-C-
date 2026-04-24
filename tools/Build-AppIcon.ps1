param(
    [string]$OutputPng = "",
    [string]$OutputIco = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class IconNativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

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

$projectRoot = Split-Path -Parent $PSScriptRoot
$assetsRoot = Join-Path $projectRoot "Assets"
if ([string]::IsNullOrWhiteSpace($OutputPng)) {
    $OutputPng = Join-Path $assetsRoot "AppIcon-preview.png"
}

if ([string]::IsNullOrWhiteSpace($OutputIco)) {
    $OutputIco = Join-Path $assetsRoot "App.ico"
}

New-Item -ItemType Directory -Force -Path $assetsRoot | Out-Null

$canvas = New-Object System.Drawing.Bitmap 256, 256
$graphics = [System.Drawing.Graphics]::FromImage($canvas)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.Clear([System.Drawing.Color]::Transparent)

try {
    $backgroundPath = New-RoundedRectanglePath -X 16 -Y 16 -Width 224 -Height 224 -Radius 50
    $backgroundBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point 20, 16),
        (New-Object System.Drawing.Point 236, 240),
        ([System.Drawing.ColorTranslator]::FromHtml("#11283F")),
        ([System.Drawing.ColorTranslator]::FromHtml("#18B980")))
    $graphics.FillPath($backgroundBrush, $backgroundPath)

    $glowBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($backgroundPath)
    $glowBrush.CenterColor = [System.Drawing.Color]::FromArgb(80, 255, 255, 255)
    $glowBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $graphics.FillPath($glowBrush, $backgroundPath)

    $trayBody = New-RoundedRectanglePath -X 52 -Y 110 -Width 132 -Height 84 -Radius 26
    $trayBodyBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245, 248, 252, 255))
    $graphics.FillPath($trayBodyBrush, $trayBody)

    $trayCut = New-RoundedRectanglePath -X 68 -Y 126 -Width 100 -Height 52 -Radius 16
    $trayCutBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#17344A"))
    $graphics.FillPath($trayCutBrush, $trayCut)

    $trayLip = New-RoundedRectanglePath -X 60 -Y 100 -Width 116 -Height 30 -Radius 14
    $trayLipBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(252, 255, 255, 255))
    $graphics.FillPath($trayLipBrush, $trayLip)

    $tilePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tilePath.AddPolygon(@(
        (New-Object System.Drawing.PointF 78, 84),
        (New-Object System.Drawing.PointF 126, 58),
        (New-Object System.Drawing.PointF 172, 84),
        (New-Object System.Drawing.PointF 126, 108))
    )
    $tileBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(242, 255, 255, 255))
    $graphics.FillPath($tileBrush, $tilePath)

    $tileFold = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tileFold.AddPolygon(@(
        (New-Object System.Drawing.PointF 126, 58),
        (New-Object System.Drawing.PointF 172, 84),
        (New-Object System.Drawing.PointF 142, 100),
        (New-Object System.Drawing.PointF 110, 82))
    )
    $tileFoldBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(115, 26, 185, 128))
    $graphics.FillPath($tileFoldBrush, $tileFold)

    $swooshPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $swooshPath.AddPolygon(@(
        (New-Object System.Drawing.PointF 136, 134),
        (New-Object System.Drawing.PointF 178, 106),
        (New-Object System.Drawing.PointF 210, 132),
        (New-Object System.Drawing.PointF 170, 166),
        (New-Object System.Drawing.PointF 152, 188),
        (New-Object System.Drawing.PointF 130, 180),
        (New-Object System.Drawing.PointF 158, 152),
        (New-Object System.Drawing.PointF 120, 138))
    )
    $swooshBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point 132, 120),
        (New-Object System.Drawing.Point 208, 188),
        ([System.Drawing.ColorTranslator]::FromHtml("#32F0D0")),
        ([System.Drawing.ColorTranslator]::FromHtml("#1AA1FF")))
    $graphics.FillPath($swooshBrush, $swooshPath)

    $sparkBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 255, 255, 255))
    $graphics.FillEllipse($sparkBrush, 186, 54, 22, 22)

    $sparkPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235, 255, 255, 255), 5)
    $sparkPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $sparkPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($sparkPen, 197, 42, 197, 88)
    $graphics.DrawLine($sparkPen, 174, 65, 220, 65)

    $canvas.Save($OutputPng, [System.Drawing.Imaging.ImageFormat]::Png)

    $iconHandle = $canvas.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($iconHandle)
        try {
            $stream = [System.IO.File]::Create($OutputIco)
            try {
                $icon.Save($stream)
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $icon.Dispose()
        }
    }
    finally {
        [void][IconNativeMethods]::DestroyIcon($iconHandle)
    }
}
finally {
    $graphics.Dispose()
    $canvas.Dispose()
}

Write-Host "图标已生成:"
Write-Host "PNG  -> $OutputPng"
Write-Host "ICO  -> $OutputIco"
