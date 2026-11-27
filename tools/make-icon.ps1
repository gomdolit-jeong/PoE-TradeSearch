# Draws the app icon (gold magnifying glass over an amber gem on a dark rounded tile)
# and writes a multi-size .ico plus a 256px .png.
#
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#
# Output: src\PoETradeSearch.App\Assets\app.ico and app.png
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\PoETradeSearch.App\Assets")
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = "Stop"

function New-Color([string]$hex, [int]$alpha = 255) {
    $c = [System.Drawing.ColorTranslator]::FromHtml($hex)
    return [System.Drawing.Color]::FromArgb($alpha, $c.R, $c.G, $c.B)
}

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Renders the icon at $size px. Everything is designed on a 256 grid and scaled.
# Small sizes drop the fine gem facets and get slightly thicker strokes so they stay readable.
function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0
    $g.ScaleTransform($s, $s)
    $small = $size -le 32
    $boost = if ($small) { 1.35 } else { 1.0 }

    # Tile: dark warm gradient with a gold rim.
    $tile = New-RoundRect 10 10 236 236 54
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 10)), (New-Object System.Drawing.PointF(0, 246)),
        (New-Color "#3B2D14"), (New-Color "#110E09"))
    $g.FillPath($bg, $tile)
    $rimBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 10)), (New-Object System.Drawing.PointF(0, 246)),
        (New-Color "#F0D48A"), (New-Color "#7A5A22"))
    $rim = New-Object System.Drawing.Pen($rimBrush, (7 * $boost))
    $g.DrawPath($rim, $tile)

    # Handle (drawn first so the lens ring sits on top of its end).
    $handleBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(150, 150)), (New-Object System.Drawing.PointF(215, 215)),
        (New-Color "#E9C878"), (New-Color "#8B6424"))
    $handle = New-Object System.Drawing.Pen($handleBrush, (30 * $boost))
    $handle.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $handle.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($handle, 160, 160, 206, 206)

    # Lens glass.
    $cx = 110; $cy = 108; $r = 66
    $glass = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glass.AddEllipse($cx - $r, $cy - $r, $r * 2, $r * 2)
    $glassBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush($glass)
    $glassBrush.CenterPoint = New-Object System.Drawing.PointF(($cx - 18), ($cy - 22))
    $glassBrush.CenterColor = New-Color "#2E2414"
    $glassBrush.SurroundColors = @(New-Color "#0C0906")
    $g.FillPath($glassBrush, $glass)

    # Gem inside the lens: crown (top) and pavilion (bottom).
    $gem = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gemPts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(86, 82)), (New-Object System.Drawing.PointF(134, 82)),
        (New-Object System.Drawing.PointF(152, 102)), (New-Object System.Drawing.PointF(110, 150)),
        (New-Object System.Drawing.PointF(68, 102)))
    $gem.AddPolygon($gemPts)
    $gemBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(70, 80)), (New-Object System.Drawing.PointF(150, 150)),
        (New-Color "#FFB45A"), (New-Color "#C2361C"))
    $g.FillPath($gemBrush, $gem)

    if (-not $small) {
        # Facets: girdle line and lines from the crown to the culet.
        $facet = New-Object System.Drawing.Pen((New-Color "#FFE2B0" 170), 3)
        $facet.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawLine($facet, 68, 102, 152, 102)
        $g.DrawLine($facet, 98, 82, 90, 102)
        $g.DrawLine($facet, 122, 82, 130, 102)
        $g.DrawLine($facet, 90, 102, 110, 150)
        $g.DrawLine($facet, 130, 102, 110, 150)
        # Top-left table highlight.
        $shine = New-Object System.Drawing.SolidBrush((New-Color "#FFFFFF" 120))
        $g.FillPolygon($shine, [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(90, 85)), (New-Object System.Drawing.PointF(104, 85)),
            (New-Object System.Drawing.PointF(98, 99)), (New-Object System.Drawing.PointF(80, 99))))
    }
    $gemEdge = New-Object System.Drawing.Pen((New-Color "#6E1A0C"), (2.5 * $boost))
    $gemEdge.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($gemEdge, $gem)

    # Lens ring.
    $ringBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(44, 42)), (New-Object System.Drawing.PointF(176, 174)),
        (New-Color "#FBE3A0"), (New-Color "#9A7030"))
    $ring = New-Object System.Drawing.Pen($ringBrush, (16 * $boost))
    $g.DrawEllipse($ring, $cx - $r, $cy - $r, $r * 2, $r * 2)

    if (-not $small) {
        # Glass reflection arc.
        $glint = New-Object System.Drawing.Pen((New-Color "#FFFFFF" 90), 6)
        $glint.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $glint.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawArc($glint, $cx - 48, $cy - 48, 96, 96, 200, 50)
    }

    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

# Classic ICO image: BITMAPINFOHEADER + bottom-up 32-bit BGRA pixels + 1-bit AND mask.
# Used for every size below 256 so that all icon readers (not just the Windows shell) can open it.
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter($ms)
    $w.Write([uint32]40); $w.Write([int32]$size); $w.Write([int32]($size * 2))
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]0)
    $w.Write([uint32]($size * $size * 4)); $w.Write([int32]0); $w.Write([int32]0)
    $w.Write([uint32]0); $w.Write([uint32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    # AND mask: all zero (transparency comes from the alpha channel), rows padded to 4 bytes.
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return , $ms.ToArray()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$sizes = 256, 128, 64, 48, 32, 24, 16
$images = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    if ($size -eq 256) {
        $bmp.Save((Join-Path $OutDir "app.png"), [System.Drawing.Imaging.ImageFormat]::Png)
        , (Get-PngBytes $bmp)
    }
    else {
        , (Get-DibBytes $bmp)
    }
    $bmp.Dispose()
}

# ICO container: header, one 16-byte directory entry per size, then the PNG data.
$ms = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ms)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "app.ico"), $ms.ToArray())
"app.ico ($($sizes -join ', ') px) and app.png written to $OutDir"
