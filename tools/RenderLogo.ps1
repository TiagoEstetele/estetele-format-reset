# Builds application-owned artwork using WPF. No Windows settings are read or written.
param([string]$Output = (Join-Path $PSScriptRoot '..\assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
[System.IO.Directory]::CreateDirectory($Output) | Out-Null
$paths = @(
    @('#A879FF','M 2,4 L 43,4 39,14 15,14 15,27 35,27 31,37 15,37 15,50 43,50 39,60 2,60 Z'),
    @('#F2EEF9','M 51,60 L 51,29 45,29 45,20 51,20 51,15 C 51,5 58,0 68,0 L 78,0 78,10 70,10 C 64,10 62,12 62,17 L 62,20 76,20 72,29 62,29 62,60 Z'),
    @('#F2EEF9','M 81,60 L 81,20 92,20 92,27 C 96,21 102,19 110,20 L 110,31 C 99,29 92,34 92,43 L 92,60 Z'),
    @('#7942DC','M 117,49 L 129,49 125,60 113,60 Z')
)
function Render-Png([int]$size, [bool]$icon) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $drawing = $visual.RenderOpen()
    $brushes = [System.Windows.Media.BrushConverter]::new()
    if ($icon) {
        $drawing.DrawRoundedRectangle($brushes.ConvertFromString('#100B19'), $null, [System.Windows.Rect]::new(0,0,$size,$size), $size*0.21, $size*0.21)
        $drawing.PushTransform([System.Windows.Media.TranslateTransform]::new($size*0.28,$size*0.17))
        $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size/95,$size/95))
        $drawing.DrawGeometry($brushes.ConvertFromString('#A879FF'),$null,[System.Windows.Media.Geometry]::Parse($paths[0][1]))
        $drawing.DrawGeometry($brushes.ConvertFromString('#E8DCFF'),$null,[System.Windows.Media.Geometry]::Parse('M 42,50 L 54,50 50,60 38,60 Z'))
        $drawing.Pop(); $drawing.Pop()
    } else {
        $drawing.PushTransform([System.Windows.Media.ScaleTransform]::new($size/129,$size/129))
        foreach ($item in $paths) { $drawing.DrawGeometry($brushes.ConvertFromString($item[0]),$null,[System.Windows.Media.Geometry]::Parse($item[1])) }
        $drawing.Pop()
    }
    $drawing.Close()
    $height = if ($icon) { $size } else { [int]($size*60/129) }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size,$height,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memory = [System.IO.MemoryStream]::new(); $encoder.Save($memory)
    return ,$memory.ToArray()
}
[System.IO.File]::WriteAllBytes((Join-Path $Output 'efr-logo.png'),(Render-Png 1032 $false))
[System.IO.File]::WriteAllBytes((Join-Path $Output 'efr-icon.png'),(Render-Png 256 $true))
$sizes = @(16,24,32,48,64,128,256)
$frames = @($sizes | ForEach-Object { ,(Render-Png $_ $true) })
$stream = [System.IO.File]::Create((Join-Path $Output 'efr.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++) {
        $dimension = if($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
Write-Output 'Logo SVG/PNG e ícone ICO preparados.'
