# Builds app.ico (16/20/24/32/40/48/64/128/256 px, PNG-compressed) from the logo in assets\logo.png.
# Usage: .\tools\make-icon.ps1   -> writes src\vibeRacingOverlays.App\app.ico
Add-Type -AssemblyName System.Drawing
$root = Join-Path $PSScriptRoot '..'
$logoPath = Join-Path $root 'assets\logo.png'
$out = Join-Path $root 'src\vibeRacingOverlays.App\app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

$logo = [System.Drawing.Bitmap]::FromFile((Resolve-Path $logoPath))

# crop to the visible part (alpha > 0) so the logo fills the icon
$minX = $logo.Width; $minY = $logo.Height; $maxX = -1; $maxY = -1
$data = $logo.LockBits((New-Object System.Drawing.Rectangle 0, 0, $logo.Width, $logo.Height),
    [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$bytes = New-Object byte[] ($data.Stride * $logo.Height)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$logo.UnlockBits($data)
for ($y = 0; $y -lt $logo.Height; $y++) {
    $row = $y * $data.Stride
    for ($x = 0; $x -lt $logo.Width; $x++) {
        if ($bytes[$row + $x * 4 + 3] -gt 8) {
            if ($x -lt $minX) { $minX = $x }; if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }; if ($y -gt $maxY) { $maxY = $y }
        }
    }
}
$cw = $maxX - $minX + 1; $ch = $maxY - $minY + 1
$side = [Math]::Max($cw, $ch)

function Render([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'HighQuality'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    # small margin; a bit less on tiny sizes so the logo stays readable
    $margin = if ($s -le 24) { 0.02 } else { 0.05 }
    $inner = $s * (1 - 2 * $margin)
    $scale = $inner / $side
    $w = $cw * $scale; $h = $ch * $scale
    $dest = New-Object System.Drawing.RectangleF (($s - $w) / 2), (($s - $h) / 2), $w, $h
    $srcRect = New-Object System.Drawing.RectangleF $minX, $minY, $cw, $ch
    $attr = New-Object System.Drawing.Imaging.ImageAttributes
    $attr.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)   # no dark fringe at the edges
    $destPts = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF $dest.X, $dest.Y), (New-Object System.Drawing.PointF ($dest.X + $w), $dest.Y), (New-Object System.Drawing.PointF $dest.X, ($dest.Y + $h)))
    $g.DrawImage($logo, $destPts, $srcRect, [System.Drawing.GraphicsUnit]::Pixel, $attr)
    $g.Dispose()
    return $bmp
}

$images = foreach ($s in $sizes) {
    $bmp = Render $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
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
Write-Host "Wrote $out (logo area ${cw}x${ch} px)"

# preview for design checks: every size on a dark and a light background
$prev = New-Object System.Drawing.Bitmap 640, 330
$g = [System.Drawing.Graphics]::FromImage($prev)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 0, 640, 165)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(243, 243, 243))), 0, 165, 640, 165)
foreach ($band in 0, 165) {
    $x = 10
    foreach ($s in 16, 24, 32, 48, 64, 128) {
        $bmp = Render $s
        $g.DrawImage($bmp, $x, $band + (165 - $s) / 2)
        $bmp.Dispose()
        $x += $s + 16
    }
}
$g.Dispose()
$prev.Save((Join-Path $PSScriptRoot 'app-icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()
