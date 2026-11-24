# Gera assets/voltpulse.ico com resolucoes 16, 32, 48 e 256 px em PNG embutido (ICO moderno Vista+)
Add-Type -AssemblyName System.Drawing

$projDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$assetsDir = Join-Path $projDir "assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
$icoPath = Join-Path $assetsDir "voltpulse.ico"

function New-VoltPulsePngBytes([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [float]($size * 0.06)
    $dia = [float]($size - $pad * 2)
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, $dia, $dia)

    # Fundo circular escuro com gradiente azul profundo
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 11, 18, 34),
        [System.Drawing.Color]::FromArgb(255, 24, 46, 96),
        45.0)
    $g.FillEllipse($bgBrush, $rect)

    # Anel luminoso ciano -> esmeralda
    $ringWidth = [Math]::Max(1.5, [float]($size * 0.065))
    $ringRect = New-Object System.Drawing.RectangleF(($pad + $ringWidth/2), ($pad + $ringWidth/2), ($dia - $ringWidth), ($dia - $ringWidth))
    $ringBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 56, 189, 248),
        [System.Drawing.Color]::FromArgb(255, 52, 211, 153),
        45.0)
    $ringPen = New-Object System.Drawing.Pen($ringBrush, $ringWidth)
    $g.DrawEllipse($ringPen, $ringRect)

    # Raio central
    $s = [float]$size
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF(($s * 0.56), ($s * 0.17))),
        (New-Object System.Drawing.PointF(($s * 0.30), ($s * 0.54))),
        (New-Object System.Drawing.PointF(($s * 0.49), ($s * 0.54))),
        (New-Object System.Drawing.PointF(($s * 0.43), ($s * 0.84))),
        (New-Object System.Drawing.PointF(($s * 0.70), ($s * 0.46))),
        (New-Object System.Drawing.PointF(($s * 0.51), ($s * 0.46)))
    )
    $boltBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 56, 189, 248),
        [System.Drawing.Color]::FromArgb(255, 52, 211, 153),
        90.0)
    $g.FillPolygon($boltBrush, $pts)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()

    $ms.Dispose(); $boltBrush.Dispose(); $ringPen.Dispose(); $ringBrush.Dispose(); $bgBrush.Dispose(); $g.Dispose(); $bmp.Dispose()
    return ,$bytes
}

$sizes = @(16, 32, 48, 256)
$images = @()
foreach ($sz in $sizes) {
    $images += ,@($sz, (New-VoltPulsePngBytes $sz))
}

$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
# ICONDIR header
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)

$offset = 6 + ($images.Count * 16)
foreach ($img in $images) {
    $sz = [int]$img[0]
    $data = [byte[]]$img[1]
    $dim = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
    $bw.Write($dim)
    $bw.Write($dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($img in $images) {
    $bw.Write([byte[]]$img[1])
}
$bw.Flush(); $bw.Close(); $fs.Close()

Write-Host "Ícone gerado com sucesso: $icoPath ($((Get-Item $icoPath).Length) bytes)"
