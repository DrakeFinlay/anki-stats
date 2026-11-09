# Run with Windows PowerShell (STA). The committed assets require no fonts at runtime.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$assetDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'src/AnkiStats.App/Assets'
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
$culture = [Globalization.CultureInfo]::InvariantCulture
$blue = '#3D659B'
$cream = '#FFF8E8'

function New-Lettering([string]$text, [string]$font, [double]$x, [double]$y, [double]$width, [double]$height) {
    $typeface = [Windows.Media.Typeface]::new([Windows.Media.FontFamily]::new($font), [Windows.FontStyles]::Normal, [Windows.FontWeights]::Bold, [Windows.FontStretches]::Normal)
    $formatted = [Windows.Media.FormattedText]::new($text, [Globalization.CultureInfo]::GetCultureInfo('ja-JP'), [Windows.FlowDirection]::LeftToRight, $typeface, 220, [Windows.Media.Brushes]::White, 1)
    $glyph = $formatted.BuildGeometry([Windows.Point]::new(0, 0)).GetOutlinedPathGeometry()
    $bounds = $glyph.Bounds
    $scale = [Math]::Min($width / $bounds.Width, $height / $bounds.Height)
    $matrix = [Windows.Media.Matrix]::new($scale, 0, 0, $scale, $x + $width / 2 - ($bounds.X + $bounds.Width / 2) * $scale, $y + $height / 2 - ($bounds.Y + $bounds.Height / 2) * $scale)
    $glyph.Transform = [Windows.Media.MatrixTransform]::new($matrix)
    return $glyph.GetFlattenedPathGeometry()
}

function Render-Png($drawing, [int]$width, [int]$height, [double]$scale) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($scale, $scale))
    $context.DrawDrawing($drawing)
    $context.Pop()
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($width, $height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() } finally { $stream.Dispose() }
}

function Save-Vector([string]$name, [string]$viewBox, [string]$label, [string]$background, $glyphs) {
    $paths = foreach ($glyph in $glyphs) {
        '<path fill="' + $cream + '" fill-rule="evenodd" d="' + ($glyph.ToString($culture) -replace '^F[01]', '') + '"/>'
    }
    $svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="' + $viewBox + '" role="img" aria-label="' + $label + '">' + $background + ($paths -join '') + '</svg>'
    [IO.File]::WriteAllText((Join-Path $assetDirectory $name), $svg, [Text.UTF8Encoding]::new($false))
}

# Match Gengo's rounded tile, cream lettering and generous inset; blue belongs to Nakama.
$iconGlyph = New-Lettering '仲' 'Yu Gothic UI' 33 30 190 196
$icon = [Windows.Media.DrawingGroup]::new()
$context = $icon.Open()
$context.DrawRoundedRectangle([Windows.Media.BrushConverter]::new().ConvertFromString($blue), $null, [Windows.Rect]::new(8, 8, 240, 240), 48, 48)
$context.DrawGeometry([Windows.Media.BrushConverter]::new().ConvertFromString($cream), $null, $iconGlyph)
$context.Close()
$icon.Freeze()
[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'Nakama.png'), (Render-Png $icon 512 512 2))
Save-Vector 'Nakama.svg' '0 0 256 256' 'Nakama: 仲, companion' ('<rect x="8" y="8" width="240" height="240" rx="48" fill="' + $blue + '"/>') @($iconGlyph)

# Full name, outlined so the Japanese wordmark displays identically on every machine.
$kanji = New-Lettering '仲間' 'Yu Gothic UI' 20 16 280 130
$roman = New-Lettering 'Nakama' 'Segoe UI' 64 172 192 38
$wordmark = [Windows.Media.DrawingGroup]::new()
$context = $wordmark.Open()
foreach ($glyph in @($kanji, $roman)) { $context.DrawGeometry([Windows.Media.BrushConverter]::new().ConvertFromString($cream), $null, $glyph) }
$context.Close()
$wordmark.Freeze()
[IO.File]::WriteAllBytes((Join-Path $assetDirectory 'Nakama-wordmark.png'), (Render-Png $wordmark 640 452 2))
Save-Vector 'Nakama-wordmark.svg' '0 0 320 226' 'Nakama: 仲間, なかま, companion' '' @($kanji, $roman)

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $frames.Add((Render-Png $icon $size $size ($size / 256))) }
$file = [IO.File]::Create((Join-Path $assetDirectory 'Nakama.ico'))
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Length)
    $offset = 6 + 16 * $sizes.Length
    for ($i = 0; $i -lt $sizes.Length; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output "Created Nakama SVG, PNG and nine-size Windows icon assets in $assetDirectory"
