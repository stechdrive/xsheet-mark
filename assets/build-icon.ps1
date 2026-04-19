# Regenerates assets/icon.ico from the shape description below.
# Must match assets/icon.svg — edit both together if the design changes.
#
# Usage:  pwsh -File assets/build-icon.ps1

Add-Type -AssemblyName PresentationCore, WindowsBase

$bgColor = [System.Windows.Media.Color]::FromRgb(0x1f, 0x1f, 0x1f)
$textColor = [System.Windows.Media.Color]::FromRgb(0xe0, 0x40, 0x40)
$bgBrush = New-Object System.Windows.Media.SolidColorBrush $bgColor
$bgBrush.Freeze()
$textBrush = New-Object System.Windows.Media.SolidColorBrush $textColor
$textBrush.Freeze()
$whiteBrush = [System.Windows.Media.Brushes]::White

function New-Rect($x, $y, $w, $h, $r) {
    $rect = New-Object System.Windows.Rect $x, $y, $w, $h
    $g = New-Object System.Windows.Media.RectangleGeometry $rect, $r, $r
    $g.Freeze()
    return $g
}

function Draw-Master {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.PushClip((New-Rect 0 0 512 512 78))
    $dc.DrawRectangle($bgBrush, $null, (New-Object System.Windows.Rect 0, 0, 512, 512))
    $dc.DrawGeometry($whiteBrush, $null, (New-Rect 20 20 472 472 58))
    $dc.DrawGeometry($bgBrush, $null, (New-Rect 82 82 348 348 38))

    foreach ($x in 82, 144, 207, 269, 332, 394) {
        $dc.DrawGeometry($bgBrush, $null, (New-Rect $x 33 36 36 5))
        $dc.DrawGeometry($bgBrush, $null, (New-Rect $x 443 36 36 5))
    }
    foreach ($y in 82, 160, 238, 316, 394) {
        $dc.DrawGeometry($bgBrush, $null, (New-Rect 33 $y 36 36 5))
        $dc.DrawGeometry($bgBrush, $null, (New-Rect 443 $y 36 36 5))
    }

    $typeface = New-Object System.Windows.Media.Typeface(
        (New-Object System.Windows.Media.FontFamily 'Arial Black'),
        [System.Windows.FontStyles]::Normal,
        [System.Windows.FontWeights]::Black,
        [System.Windows.FontStretches]::Normal)
    $ft = New-Object System.Windows.Media.FormattedText(
        'xM',
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Windows.FlowDirection]::LeftToRight,
        $typeface,
        210.0,
        $textBrush,
        1.0)
    $ft.TextAlignment = [System.Windows.TextAlignment]::Center
    $origin = New-Object System.Windows.Point 256, (256 - $ft.Height / 2)
    $dc.DrawText($ft, $origin)

    $dc.Pop()
    $dc.Close()
    return $dv
}

function Render-Size($drawing, [int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $scale = $size / 512.0
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    $dc.DrawDrawing($drawing)
    $dc.Pop()
    $dc.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $size, $size, 96, 96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)
    $rtb.Freeze()

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    [void]$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = New-Object System.IO.MemoryStream
    $encoder.Save($ms)
    return $ms.ToArray()
}

$master = Draw-Master
$drawing = $master.Drawing
$sizes = 16, 32, 48, 64, 128, 256
$pngs = [System.Collections.Generic.List[byte[]]]::new()
foreach ($s in $sizes) {
    $bytes = Render-Size $drawing $s
    Write-Host ("  {0,3}x{0,3} -> {1,6} bytes" -f $s, $bytes.Length)
    [void]$pngs.Add($bytes)
}

# Write ICO: 6-byte header + 16-byte entries + PNG payloads.
$icoStream = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $icoStream
$w.Write([UInt16]0)             # reserved
$w.Write([UInt16]1)             # type = icon
$w.Write([UInt16]$sizes.Count)  # count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dim = if ($size -eq 256) { 0 } else { [byte]$size }  # 0 = 256 (spec quirk)
    $w.Write([byte]$dim)
    $w.Write([byte]$dim)
    $w.Write([byte]0)                   # palette color count (0 = truecolor)
    $w.Write([byte]0)                   # reserved
    $w.Write([UInt16]1)                 # color planes
    $w.Write([UInt16]32)                # bits per pixel
    $w.Write([UInt32]$pngs[$i].Length)
    $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($bytes in $pngs) { $w.Write($bytes) }
$w.Flush()

$icoPath = Join-Path $PSScriptRoot 'icon.ico'
[System.IO.File]::WriteAllBytes($icoPath, $icoStream.ToArray())
Write-Host "Wrote $icoPath ($($icoStream.Length) bytes, sizes: $($sizes -join ', '))"
