# Generates branding assets (Logo.png, AppIcon.ico, MSIX tile art) from scratch.
# Safe to re-run; output goes to src/CircleQuickLauncher/Assets.
$ErrorActionPreference = 'Stop'

$assetsDir = Join-Path $PSScriptRoot '..\src\CircleQuickLauncher\Assets'
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
$assetsDir = (Resolve-Path $assetsDir).Path

Add-Type -AssemblyName System.Drawing

function New-LogoBitmap {
    param([int]$Size)
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $Size, $Size)
    $pad = [int]($Size * 0.04)
    $inner = New-Object System.Drawing.Rectangle($pad, $pad, ($Size - 2 * $pad), ($Size - 2 * $pad))

    # Background disc with vertical gradient (sky -> indigo)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($inner)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($inner,
        [System.Drawing.Color]::FromArgb(255, 14, 165, 233),
        [System.Drawing.Color]::FromArgb(255, 99, 102, 241),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $path)

    # White ring
    $ringWidth = [Math]::Max(2, [int]($Size * 0.055))
    $ringInset = [int]($Size * 0.16)
    $ringRect = New-Object System.Drawing.Rectangle($ringInset, $ringInset, ($Size - 2 * $ringInset), ($Size - 2 * $ringInset))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $ringWidth)
    $pen.Alignment = [System.Drawing.Drawing2D.PenAlignment]::Inset
    $g.DrawEllipse($pen, $ringRect)

    # Center dot
    $dotSize = [int]($Size * 0.22)
    $dotRect = New-Object System.Drawing.Rectangle(
        [int](($Size - $dotSize) / 2), [int](($Size - $dotSize) / 2), $dotSize, $dotSize)
    $g.FillEllipse([System.Drawing.Brushes]::White, $dotRect)

    $g.Dispose()
    return $bmp
}

function Save-Png {
    param([System.Drawing.Bitmap]$Bitmap, [string]$Path)
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $Bitmap.Dispose()
}

# Primary logo (256)
Save-Png (New-LogoBitmap 256) (Join-Path $assetsDir 'Logo.png')

# MSIX / shortcut art
Save-Png (New-LogoBitmap 44)   (Join-Path $assetsDir 'Square44x44Logo.png')
Save-Png (New-LogoBitmap 150)  (Join-Path $assetsDir 'Square150x150Logo.png')
Save-Png (New-LogoBitmap 50)   (Join-Path $assetsDir 'StoreLogo.png')
Save-Png (New-LogoBitmap 120)  (Join-Path $assetsDir 'SplashScreen.png')

# Wide tile: logo centered on a branded band
$wide = New-Object System.Drawing.Bitmap(310, 150)
$gw = [System.Drawing.Graphics]::FromImage($wide)
$gw.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$gw.Clear([System.Drawing.Color]::Transparent)
$bandBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, 310, 150)),
    [System.Drawing.Color]::FromArgb(255, 15, 23, 42), [System.Drawing.Color]::FromArgb(255, 30, 41, 59),
    [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
$gw.FillRectangle($bandBrush, 0, 0, 310, 150)
$logo = New-LogoBitmap 110
$gw.DrawImage($logo, 24, 20, 110, 110)
$logo.Dispose()
$font = New-Object System.Drawing.Font('Segoe UI', 26, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$white = [System.Drawing.Brushes]::White
$gw.DrawString('CircleQuickLauncher', $font, $white, 148, 62)
$gw.Dispose()
$wide.Save((Join-Path $assetsDir 'Wide310x150Logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$wide.Dispose()

# Multi-size .ico (PNG-compressed entries, supported since Vista)
function New-IconFile {
    param([string]$Path, [int[]]$Sizes = @(16, 24, 32, 48, 64, 256))
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([uint16]0)            # reserved
    $bw.Write([uint16]1)            # type: icon
    $bw.Write([uint16]$Sizes.Count)

    $entries = @()
    foreach ($size in $Sizes) {
        $bmp = New-LogoBitmap $size
        $png = New-Object System.IO.MemoryStream
        $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $entries += ,@($size, $png.ToArray())
        $png.Dispose()
    }

    $offset = 6 + 16 * $Sizes.Count
    foreach ($e in $entries) {
        $size = $e[0]
        $dim = if ($size -ge 256) { 0 } else { $size }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)           # colors in palette
        $bw.Write([byte]0)           # reserved
        $bw.Write([uint16]1)         # color planes
        $bw.Write([uint16]32)        # bits per pixel
        $bw.Write([uint32]$e[1].Length)
        $bw.Write([uint32]$offset)
        $offset += $e[1].Length
    }
    foreach ($e in $entries) { $bw.Write([byte[]]$e[1]) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes($Path, $ms.ToArray())
    $bw.Dispose()
    $ms.Dispose()
}

New-IconFile (Join-Path $assetsDir 'AppIcon.ico')
Write-Host "Assets generated in $assetsDir"
Get-ChildItem $assetsDir | Select-Object Name, Length
