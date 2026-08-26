param([string]$OutputDirectory = "src/Etp.Reporting.Desktop/Assets")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
$target = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
[IO.Directory]::CreateDirectory($target) | Out-Null
$pngPath = Join-Path $target "EtpReporting.png"
$icoPath = Join-Path $target "EtpReporting.ico"

$bitmap = [Drawing.Bitmap]::new(256, 256, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $navy = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 19, 51, 76))
    $teal = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255, 32, 166, 154))
    $white = [Drawing.SolidBrush]::new([Drawing.Color]::White)
    try {
        $graphics.FillRectangle($navy, 8, 8, 240, 240)
        $graphics.FillRectangle($teal, 50, 151, 28, 47)
        $graphics.FillRectangle($teal, 98, 118, 28, 80)
        $graphics.FillRectangle($teal, 146, 80, 28, 118)
        $graphics.FillEllipse($white, 174, 47, 31, 31)
        $pen = [Drawing.Pen]::new([Drawing.Color]::White, 13)
        try { $graphics.DrawLines($pen, [Drawing.Point[]]@([Drawing.Point]::new(49,128), [Drawing.Point]::new(96,95), [Drawing.Point]::new(132,105), [Drawing.Point]::new(188,62))) } finally { $pen.Dispose() }
    } finally { $navy.Dispose(); $teal.Dispose(); $white.Dispose() }
    $bitmap.Save($pngPath, [Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $bitmap.Dispose() }

$png = [IO.File]::ReadAllBytes($pngPath)
$stream = [IO.File]::Create($icoPath)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22)
    $writer.Write($png)
} finally { $writer.Dispose(); $stream.Dispose() }
Write-Host "Generated $icoPath"
