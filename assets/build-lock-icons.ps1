# Builds lock.ico and unlock.ico using Segoe MDL2 Assets glyphs. These are
# used by the TaskbarItemInfo thumbnail button to indicate click-through state.
#
# Usage:  pwsh -File assets/build-lock-icons.ps1

Add-Type -AssemblyName PresentationCore, WindowsBase

function Draw-Glyph([string]$glyph) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $typeface = New-Object System.Windows.Media.Typeface(
        (New-Object System.Windows.Media.FontFamily 'Segoe MDL2 Assets'),
        [System.Windows.FontStyles]::Normal,
        [System.Windows.FontWeights]::Normal,
        [System.Windows.FontStretches]::Normal)
    $ft = New-Object System.Windows.Media.FormattedText(
        $glyph,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Windows.FlowDirection]::LeftToRight,
        $typeface,
        220.0,
        [System.Windows.Media.Brushes]::White,
        1.0)
    $ft.TextAlignment = [System.Windows.TextAlignment]::Center
    $origin = New-Object System.Windows.Point 128, (128 - $ft.Height / 2)
    $dc.DrawText($ft, $origin)
    $dc.Close()
    return $dv
}

function Render-Size($drawing, [int]$size) {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $scale = $size / 256.0
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

function Write-Ico($pngBytesList, [int[]]$sizes, [string]$path) {
    $icoStream = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $icoStream
    $w.Write([UInt16]0)
    $w.Write([UInt16]1)
    $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $dim = if ($size -eq 256) { 0 } else { [byte]$size }
        $w.Write([byte]$dim)
        $w.Write([byte]$dim)
        $w.Write([byte]0)
        $w.Write([byte]0)
        $w.Write([UInt16]1)
        $w.Write([UInt16]32)
        $w.Write([UInt32]$pngBytesList[$i].Length)
        $w.Write([UInt32]$offset)
        $offset += $pngBytesList[$i].Length
    }
    foreach ($bytes in $pngBytesList) { $w.Write($bytes) }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $icoStream.ToArray())
    Write-Host "Wrote $path ($($icoStream.Length) bytes)"
}

$sizes = 16, 20, 24, 32, 48

# Segoe MDL2 Assets: E72E = Lock, E785 = Unlock
$items = @(
    @{ Name = 'lock';   Code = 0xE72E },
    @{ Name = 'unlock'; Code = 0xE785 }
)

foreach ($item in $items) {
    $glyph = [string][char]$item.Code
    $drawing = (Draw-Glyph $glyph).Drawing
    $pngs = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($s in $sizes) {
        $bytes = Render-Size $drawing $s
        Write-Host ("  {0,3}x{0,3} -> {1,6} bytes" -f $s, $bytes.Length)
        [void]$pngs.Add($bytes)
    }
    $path = Join-Path $PSScriptRoot "$($item.Name).ico"
    Write-Ico $pngs $sizes $path
}
