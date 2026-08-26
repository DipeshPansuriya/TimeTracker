<#
.SYNOPSIS
    Generates src\TimeTracker.App\app.ico from the Kale mark.

.DESCRIPTION
    Run once; the .ico is committed. Kept as a script rather than a binary blob nobody
    can regenerate — if the mark changes, this is the thing to re-run.

    The mark geometry comes from the brand logo SVG. It is drawn rather than traced from
    a raster so the small sizes stay crisp.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\TimeTracker.App\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Kale "Noon" orange. Never altered — it is the one brand colour on the icon.
$noon = [System.Drawing.Color]::FromArgb(255, 235, 74, 38)

# The two chevrons of the Kale mark, in the SVG's own 172.5 x 142.12 coordinate space.
$upper = @(
    82,42, 43,80, 53,80, 105,132, 105,142, 66,142, 0,75, 39,36, 82,36
)
$lower = @(
    148,51, 172,75, 132,115, 89,115, 89,110, 128,70, 118,70, 59,11, 59,0, 96,0
)

function New-MarkBitmap {
    param([int] $Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fit the 172.5 x 142.12 artwork into the square with a little breathing room.
    $pad   = [Math]::Max(1, [int]($Size * 0.08))
    $scale = [Math]::Min(($Size - 2 * $pad) / 172.5, ($Size - 2 * $pad) / 142.12)
    $offX  = ($Size - 172.5 * $scale) / 2
    $offY  = ($Size - 142.12 * $scale) / 2

    $brush = New-Object System.Drawing.SolidBrush($noon)
    foreach ($shape in @($upper, $lower)) {
        $points = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
        for ($i = 0; $i -lt $shape.Count; $i += 2) {
            $points.Add((New-Object System.Drawing.PointF(
                [float]($offX + $shape[$i] * $scale),
                [float]($offY + $shape[$i + 1] * $scale))))
        }
        $g.FillPolygon($brush, $points.ToArray())
    }

    $brush.Dispose(); $g.Dispose()
    return $bmp
}

# Frames are written as classic DIB (BITMAPINFOHEADER + BGRA + AND mask), not PNG.
# PNG-encoded entries are smaller and Windows Explorer reads them, but the managed
# System.Drawing.Icon decoder does not — and that is what NotifyIcon and any tooling that
# inspects the icon go through. A tray icon that silently fails to load is exactly the
# defect being fixed here, so compatibility wins over file size.
function ConvertTo-DibFrame {
    param([System.Drawing.Bitmap] $Bitmap)

    $w = $Bitmap.Width; $h = $Bitmap.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)

    # BITMAPINFOHEADER. Height is doubled to cover the XOR image plus the AND mask.
    $bw.Write([UInt32]40); $bw.Write([Int32]$w); $bw.Write([Int32]($h * 2))
    $bw.Write([UInt16]1);  $bw.Write([UInt16]32); $bw.Write([UInt32]0)
    $bw.Write([UInt32]($w * $h * 4))
    $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)

    # XOR image: 32bpp BGRA, bottom-up.
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $Bitmap.GetPixel($x, $y)
            $bw.Write([Byte]$c.B); $bw.Write([Byte]$c.G)
            $bw.Write([Byte]$c.R); $bw.Write([Byte]$c.A)
        }
    }

    # AND mask: 1bpp, rows padded to 4 bytes. All zero — the alpha channel does the work.
    $maskRow = [Math]::Floor(($w + 31) / 32) * 4
    $blank = New-Object Byte[] ($maskRow * $h)
    $bw.Write($blank)

    $bw.Flush()
    $bytes = $ms.ToArray()
    $bw.Dispose(); $ms.Dispose()

    # Comma operator: without it PowerShell unrolls the byte array into the pipeline and
    # the caller receives Object[] instead of Byte[], which silently writes nothing.
    return ,$bytes
}

# 256 is omitted deliberately: as an uncompressed DIB it alone would be ~256 KB, and
# nothing in this application displays an icon that large.
$sizes = @(16, 24, 32, 48, 64, 128)
$frames = @()
foreach ($size in $sizes) {
    $bmp = New-MarkBitmap -Size $size
    $frames += ,@{ Size = $size; Bytes = (ConvertTo-DibFrame -Bitmap $bmp) }
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)

# ICONDIR
$w.Write([UInt16]0)               # reserved
$w.Write([UInt16]1)               # type: 1 = icon
$w.Write([UInt16]$frames.Count)

# ICONDIRENTRY table. Offsets follow the whole table, hence the running total.
$offset = 6 + (16 * $frames.Count)
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }   # 0 means 256 in this format
    $w.Write([Byte]$dim)          # width
    $w.Write([Byte]$dim)          # height
    $w.Write([Byte]0)             # palette count
    $w.Write([Byte]0)             # reserved
    $w.Write([UInt16]1)           # colour planes
    $w.Write([UInt16]32)          # bits per pixel
    $w.Write([UInt32] ([Byte[]] $f.Bytes).Length)
    $w.Write([UInt32]$offset)
    $offset += ([Byte[]] $f.Bytes).Length
}

foreach ($f in $frames) { $w.Write([Byte[]] $f.Bytes) }

$w.Flush()
[System.IO.File]::WriteAllBytes($OutputPath, $out.ToArray())
$w.Dispose(); $out.Dispose()

$kb = [Math]::Round((Get-Item $OutputPath).Length / 1KB, 1)
Write-Host "Wrote $OutputPath ($kb KB, $($frames.Count) sizes: $($sizes -join ', '))" -ForegroundColor Green
