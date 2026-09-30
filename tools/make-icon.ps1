# Generates app.ico (16/24/32/48/64/128/256 px, PNG-compressed) for vibeRacingOverlays.
# Usage: .\tools\make-icon.ps1   -> writes src\vibeRacingOverlays.App\app.ico
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\vibeRacingOverlays.App\app.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.Clear([System.Drawing.Color]::Transparent)

    # rounded dark tile
    $r = [Math]::Max(2, $s * 0.2); $d = $r * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($s - $d - 1, 0, $d, $d, 270, 90)
    $path.AddArc($s - $d - 1, $s - $d - 1, $d, $d, 0, 90); $path.AddArc(0, $s - $d - 1, $d, $d, 90, 90); $path.CloseFigure()
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $s),
        [System.Drawing.Color]::FromArgb(255, 36, 40, 47), [System.Drawing.Color]::FromArgb(255, 18, 20, 24))
    $g.FillPath($bg, $path)

    # three "standings" bars, the middle one highlighted like the player row
    $x = $s * 0.18; $w = $s * 0.64; $h = [Math]::Max(1.5, $s * 0.09); $gap = $s * 0.055
    $y0 = $s * 0.52
    $colors = @([System.Drawing.Color]::FromArgb(255, 90, 96, 105), [System.Drawing.Color]::FromArgb(255, 200, 52, 52), [System.Drawing.Color]::FromArgb(255, 90, 96, 105))
    for ($i = 0; $i -lt 3; $i++) {
        $b = New-Object System.Drawing.SolidBrush $colors[$i]
        $g.FillRectangle($b, [float]$x, [float]($y0 + $i * ($h + $gap)), [float]($w * (1 - $i * 0.12)), [float]$h)
    }

    # blue "V" chevron on top
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 47, 140, 255)), ([Math]::Max(1.6, $s * 0.12))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $pts = @((New-Object System.Drawing.PointF ($s * 0.27), ($s * 0.19)), (New-Object System.Drawing.PointF ($s * 0.5), ($s * 0.40)), (New-Object System.Drawing.PointF ($s * 0.73), ($s * 0.19)))
    $g.DrawLines($pen, $pts)
    $g.Dispose()
    return $bmp
}

$images = foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}

# ICO container: header + directory + PNG images
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $images[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($img in $images) { $bw.Write($img) }
$bw.Close()
Write-Host "Wrote $out"

# preview for design checks
(Draw-Icon 256).Save((Join-Path $PSScriptRoot 'app-icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
