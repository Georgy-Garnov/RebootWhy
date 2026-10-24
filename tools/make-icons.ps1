<#
    Draws the RebootWhy icons: a circular "restart" arrow with a question mark on a round background.
    Each size is drawn separately so that small icons stay crisp.
    Output: src\RebootWhy.App\Assets\*.ico (+ PNG previews in tools\preview with -Preview).

    powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1 [-Preview]
#>
param([switch]$Preview)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src\RebootWhy.App\Assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-Art([int]$size, [System.Drawing.Color]$top, [System.Drawing.Color]$bottom) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [double]$size
    # Small sizes get thicker lines and smaller margins; otherwise the picture blurs.
    $small = $size -le 24
    $margin = if ($small) { 0.0 } else { 0.03 * $s }

    $bounds = New-Object System.Drawing.RectangleF ([single]$margin), ([single]$margin), ([single]($s - 2 * $margin)), ([single]($s - 2 * $margin))
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bounds, $top, $bottom, ([single]90)
    $g.FillEllipse($fill, $bounds)

    # A clockwise circular arrow with a gap at the top right.
    $cx = $s / 2; $cy = $s / 2
    $tiny = $size -le 24
    $r = if ($tiny) { 0.30 * $s } elseif ($small) { 0.315 * $s } else { 0.30 * $s }
    $w = if ($tiny) { [Math]::Max(2.0, 0.13 * $s) } elseif ($small) { [Math]::Max(1.7, 0.115 * $s) } else { 0.085 * $s }
    $start = -40.0; $sweep = 285.0
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]$w)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, [single]($cx - $r), [single]($cy - $r), [single](2 * $r), [single](2 * $r), [single]$start, [single]$sweep)

    # The arrowhead at the end of the arc, tangent to it.
    $theta = ($start + $sweep) * [Math]::PI / 180
    $px = $cx + $r * [Math]::Cos($theta); $py = $cy + $r * [Math]::Sin($theta)
    $tx = -[Math]::Sin($theta); $ty = [Math]::Cos($theta)
    $nx = [Math]::Cos($theta); $ny = [Math]::Sin($theta)
    $head = if ($tiny) { 0.36 * $s } elseif ($small) { 0.22 * $s } else { 0.17 * $s }
    $half = $head * 0.62
    $points = @(
        (New-Object System.Drawing.PointF ([single]($px + $tx * $head * 0.85)), ([single]($py + $ty * $head * 0.85))),
        (New-Object System.Drawing.PointF ([single]($px + $nx * $half - $tx * $head * 0.15)), ([single]($py + $ny * $half - $ty * $head * 0.15))),
        (New-Object System.Drawing.PointF ([single]($px - $nx * $half - $tx * $head * 0.15)), ([single]($py - $ny * $half - $ty * $head * 0.15)))
    )
    $g.FillPolygon([System.Drawing.Brushes]::White, [System.Drawing.PointF[]]$points)

    # The question mark means "why". It is centered by its actual outline rather than the font box:
    # the "?" glyph has uneven top, bottom and side bearings, which made it look off-center.
    # At 16-24 px there is no room for it inside the ring, so only the arrow remains, like system icons.
    if (-not $tiny) {
    $glyph = New-Object System.Drawing.Drawing2D.GraphicsPath
    $family = New-Object System.Drawing.FontFamily 'Segoe UI'
    $glyph.AddString('?', $family, [int][System.Drawing.FontStyle]::Bold, [single]100, (New-Object System.Drawing.PointF 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    $raw = $glyph.GetBounds()
    $inkHeight = if ($small) { 0.40 * $s } else { 0.34 * $s }
    $scale = $inkHeight / $raw.Height
    $fit = New-Object System.Drawing.Drawing2D.Matrix
    $fit.Translate([single]($cx - ($raw.X + $raw.Width / 2) * $scale), [single]($cy - ($raw.Y + $raw.Height / 2) * $scale))
    $fit.Scale([single]$scale, [single]$scale)
    $glyph.Transform($fit)
    $g.FillPath([System.Drawing.Brushes]::White, $glyph)
    $glyph.Dispose(); $family.Dispose(); $fit.Dispose()
    }

    $g.Dispose(); $fill.Dispose(); $pen.Dispose()
    return $bitmap
}

# An ICO frame in DIB format (bottom-up BGRA + mask): every Windows component understands it for small sizes.
function Get-DibBytes([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter $stream
    $writer.Write([int]40); $writer.Write([int]$size); $writer.Write([int]($size * 2))
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]0)
    $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
    $writer.Write([int]($size * $size * 4 + $maskStride * $size))
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$c.B); $writer.Write([byte]$c.G); $writer.Write([byte]$c.R); $writer.Write([byte]$c.A)
        }
    }
    $writer.Write((New-Object byte[] ($maskStride * $size)))
    $writer.Flush()
    # The comma keeps PowerShell from unrolling the byte array into a stream of separate values.
    return ,$stream.ToArray()
}

function Save-Ico([string]$path, [int[]]$sizes, [System.Drawing.Color]$top, [System.Drawing.Color]$bottom) {
    $frames = foreach ($size in $sizes) {
        $art = New-Art $size $top $bottom
        if ($size -ge 256) {
            $png = New-Object System.IO.MemoryStream
            $art.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
            $bytes = $png.ToArray()
        } else {
            $bytes = Get-DibBytes $art
        }
        if ($Preview) {
            $dir = Join-Path $PSScriptRoot 'preview'; New-Item -ItemType Directory -Force $dir | Out-Null
            $art.Save((Join-Path $dir ("{0}-{1}.png" -f [IO.Path]::GetFileNameWithoutExtension($path), $size)), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        $art.Dispose()
        [pscustomobject]@{ Size = $size; Bytes = $bytes }
    }

    $out = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($path))
    $out.Write([int16]0); $out.Write([int16]1); $out.Write([int16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($f in $frames) {
        $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
        $out.Write([byte]$dim); $out.Write([byte]$dim); $out.Write([byte]0); $out.Write([byte]0)
        $out.Write([int16]1); $out.Write([int16]32); $out.Write([int]$f.Bytes.Length); $out.Write([int]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $out.Write([byte[]]$f.Bytes) }
    $out.Close()
}

$c = { param($hex) [System.Drawing.ColorTranslator]::FromHtml($hex) }

$green = @((& $c '#34B85E'), (& $c '#178A3B'))
$red = @((& $c '#EF5A4C'), (& $c '#C02A1E'))

# The program icon is green.
Save-Ico (Join-Path $assets 'RebootWhy.ico') @(16, 20, 24, 32, 40, 48, 64, 128, 256) $green[0] $green[1]

# Tray icon: green means all calm, red means Windows is waiting for a restart.
$traySizes = @(16, 20, 24, 32, 40, 48)
Save-Ico (Join-Path $assets 'tray-calm.ico')    $traySizes $green[0] $green[1]
Save-Ico (Join-Path $assets 'tray-pending.ico') $traySizes $red[0] $red[1]

Get-ChildItem $assets -Filter *.ico | ForEach-Object { "{0,-20} {1,7} bytes" -f $_.Name, $_.Length }

if ($Preview) {
    # A sheet with enlarged small sizes shows whether the picture reads well in the tray and on the taskbar.
    $dir = Join-Path $PSScriptRoot 'preview'
    $sizes = @(16, 20, 24, 32, 48); $names = @('RebootWhy', 'tray-pending'); $scale = 6
    $width = [int](20 + ($sizes | ForEach-Object { $_ * $scale + 20 } | Measure-Object -Sum).Sum)
    $height = [int](20 + $names.Count * (48 * $scale + 20))
    $sheet = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $g.Clear([System.Drawing.Color]::FromArgb(40, 40, 40))
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
    $y = 20
    foreach ($name in $names) {
        $x = 20
        foreach ($size in $sizes) {
            $image = [System.Drawing.Image]::FromFile((Join-Path $dir "$name-$size.png"))
            $g.DrawImage($image, $x, $y, $size * $scale, $size * $scale); $image.Dispose()
            $x += $size * $scale + 20
        }
        $y += 48 * $scale + 20
    }
    $g.Dispose(); $sheet.Save((Join-Path $dir 'sheet.png')); $sheet.Dispose()
}
