# Generates src/PhraseFlow.App/Assets/PhraseFlow.ico and PhraseFlow.png.
# Run with Windows PowerShell:  powershell -ExecutionPolicy Bypass -File tools/generate-icon.ps1
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\PhraseFlow.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Max(0.5, $size * 0.03)
    $rect = New-Object System.Drawing.RectangleF $inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset)
    $radius = $size * 0.24
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 79, 70, 229)), ([System.Drawing.Color]::FromArgb(255, 6, 182, 212)), 45
    $g.FillPath($brush, $path)

    # Soft highlight on the top half
    $highlight = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.FillPath($highlight, $path)

    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $font = New-Object System.Drawing.Font 'Segoe UI', ($size * 0.72), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = 'Center'
    $format.LineAlignment = 'Center'
    $textRect = New-Object System.Drawing.RectangleF (-$size * 0.10), (-$size * 0.075), $size, $size
    $g.DrawString([string][char]0x00BB, $font, $white, $textRect, $format)

    # Text-cursor bar
    $barWidth = [Math]::Max(1.0, $size * 0.07)
    $g.FillRectangle($white, ($size * 0.68), ($size * 0.27), $barWidth, ($size * 0.46))

    $g.Dispose()
    return $bmp
}

function Get-PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return $ms.ToArray()
}

function Get-BmpFrameBytes($bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]($size * $size * 4)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $bytes = if ($s -ge 128) { Get-PngBytes $bmp } else { Get-BmpFrameBytes $bmp }
    $frames += , @($s, $bytes)
    if ($s -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $assets 'PhraseFlow.png'), (Get-PngBytes $bmp)) }
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $len = $f[1].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]$len); $writer.Write([int]$offset)
    $offset += $len
}
foreach ($f in $frames) { $writer.Write([byte[]]$f[1]) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'PhraseFlow.ico'), $out.ToArray())
Write-Output "Icon written to $assets"

