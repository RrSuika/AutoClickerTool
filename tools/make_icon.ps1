# Generate the app icon (src\app.ico) + a preview PNG for inspection.
# Design: rounded-square indigo gradient background + white mouse-cursor arrow (classic pointer, up-left)
#         + a soft drop shadow. Multi-size (16..256) PNG-compressed ICO (valid on Vista+).
# ASCII only. Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools\make_icon.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root          # repo root
$icoPath = Join-Path $root "src\app.ico"
$previewPath = Join-Path $root "tools\icon_preview.png"

function Draw-Icon {
    param([System.Drawing.Graphics]$g, [float]$s)

    # 1) rounded-square background with vertical gradient
    $r = $s * 0.225
    $body = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($s - $d, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $path.AddArc(0, $s - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $c1 = [System.Drawing.Color]::FromArgb(255, 126, 161, 255)  # #7EA1FF
    $c2 = [System.Drawing.Color]::FromArgb(255, 61, 90, 224)    # #3D5AE0
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($body, $c1, $c2, 90)
    $g.FillPath($brush, $path)

    # subtle top-light stroke
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 255, 255, 255), [float]($s * 0.02))
    $g.DrawPath($pen, $path)
    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()

    # 2) cursor arrow (classic pointer, pointing up-left) as a single polygon in normalized coords
    #    tip near top-left, tail toward bottom-right.
    $pts = @(
        @(0.22, 0.22),   # tip
        @(0.80, 0.80),   # outer corner (top-right edge of arrowhead)
        @(0.72, 0.80),   # arrowhead bottom-right corner
        @(0.72, 0.90),   # tail bottom-right
        @(0.60, 0.90),   # tail bottom-left
        @(0.60, 0.80),   # tail top-left
        @(0.38, 0.58)    # inner notch (bottom-left edge of arrowhead)
    )
    $poly = New-Object 'System.Drawing.PointF[]' $pts.Count
    for ($i = 0; $i -lt $pts.Count; $i++) {
        $poly[$i] = New-Object System.Drawing.PointF([float]($pts[$i][0] * $s), [float]($pts[$i][1] * $s))
    }

    # drop shadow (offset down-right, translucent)
    $shadowOff = $s * 0.03
    $shadowPoly = New-Object 'System.Drawing.PointF[]' $poly.Count
    for ($i = 0; $i -lt $poly.Count; $i++) {
        $shadowPoly[$i] = New-Object System.Drawing.PointF(($poly[$i].X + $shadowOff), ($poly[$i].Y + $shadowOff))
    }
    $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(90, 20, 30, 80))
    $g.FillPolygon($shadowBrush, $shadowPoly)
    $shadowBrush.Dispose()

    # white arrow
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPolygon($white, $poly)
    $white.Dispose()
}

function New-IconBitmap {
    param([int]$s)
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $bmp.SetResolution(96, 96)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Icon -g $g -s ([float]$s)
    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = New-Object 'System.Collections.Generic.List[byte[]]'

foreach ($size in $sizes) {
    $bmp = New-IconBitmap -s $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs.Add($ms.ToArray())
    $ms.Dispose()

    # save a preview of the largest size
    if ($size -eq 256) {
        $bmp.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $bmp.Dispose()
}

# write ICO (ICONDIR + ICONDIRENTRY[] + PNG blobs)
$fs = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)          # reserved
$bw.Write([UInt16]1)          # type: icon
$bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $b = if ($sz -ge 256) { 0 } else { $sz }   # 256 -> 0
    $bw.Write([byte]$b)         # width
    $bw.Write([byte]$b)         # height
    $bw.Write([byte]0)          # color count
    $bw.Write([byte]0)          # reserved
    $bw.Write([UInt16]1)        # planes
    $bw.Write([UInt16]32)       # bit count
    $bw.Write([UInt32]$pngs[$i].Length)
    $bw.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()
$fs.Close()

Write-Host "Icon written: $icoPath ($(($sizes -join ',')) px)"
Write-Host "Preview: $previewPath"
