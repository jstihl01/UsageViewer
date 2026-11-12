$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
if (-not ('UsageViewer.Assets.AppLogoRenderer' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'AppLogoRenderer.cs')
}

$destination = Join-Path $PSScriptRoot '..\src\Assets'
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stream = [IO.MemoryStream]::new()
    try {
        $pixels = [UsageViewer.Assets.AppLogoRenderer]::Render($size)
        $bounds = [Drawing.Rectangle]::new(0, 0, $size, $size)
        $locked = $bitmap.LockBits($bounds, [Drawing.Imaging.ImageLockMode]::WriteOnly,
            [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try { [Runtime.InteropServices.Marshal]::Copy($pixels, 0, $locked.Scan0, $pixels.Length) }
        finally { $bitmap.UnlockBits($locked) }
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $frames.Add($stream.ToArray())
        if ($size -eq 256) {
            [IO.File]::WriteAllBytes((Join-Path $destination 'UsageViewer.png'), $stream.ToArray())
        }
    } finally {
        $stream.Dispose()
        $bitmap.Dispose()
    }
}

$writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $destination 'UsageViewer.ico')))
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $encodedSize = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$encodedSize)
        $writer.Write([byte]$encodedSize)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose() }
