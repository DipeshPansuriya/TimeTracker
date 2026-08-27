<#
.SYNOPSIS
    Generates src\TimeTracker.App\app.ico.

.DESCRIPTION
    The mark is a disc with a quarter bitten out of it — a day partly done. It is the
    same idea the widget's progress bar shows, at icon scale, and it stays legible at
    16px where anything more detailed turns to mush.

    Run once; the .ico is committed. Kept as a script rather than an opaque binary so
    the mark can be changed by editing something readable.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'src\TimeTracker.App\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The one accent colour. Amber survives on both dark and light grounds, which a tray
# icon has to do — it sits on whatever taskbar theme the user happens to run.
$amber = [System.Drawing.Color]::FromArgb(255, 242, 160, 61)

function New-MarkBitmap {
    param([int] $Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($Size * 0.10))
    $box = New-Object System.Drawing.Rectangle(
        $pad, $pad, ($Size - 2 * $pad), ($Size - 2 * $pad))

    # GDI+ angles run clockwise from 3 o'clock. Sweeping 0 to 270 fills three quarters
    # and leaves the top-right quadrant open.
    $brush = New-Object System.Drawing.SolidBrush($amber)
    $g.FillPie($brush, $box, 0, 270)

    $brush.Dispose(); $g.Dispose()
    return $bmp
}

# Frames are classic DIB, not PNG. The managed System.Drawing.Icon decoder cannot read
# PNG-encoded ICO entries, and that is what NotifyIcon goes through — a tray icon that
# silently fails to load is exactly the defect this file exists to avoid.
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
    $bw.Write((New-Object Byte[] ($maskRow * $h)))

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
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)

# ICONDIRENTRY table. Image offsets follow the whole table, hence the running total.
$offset = 6 + (16 * $frames.Count)
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }   # 0 means 256 in this format
    $w.Write([Byte]$dim); $w.Write([Byte]$dim)
    $w.Write([Byte]0);    $w.Write([Byte]0)
    $w.Write([UInt16]1);  $w.Write([UInt16]32)
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
