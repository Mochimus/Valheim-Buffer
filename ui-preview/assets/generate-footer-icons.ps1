Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$out = $PSScriptRoot
$gold = [System.Drawing.Color]::FromArgb(255, 218, 181, 119)
$light = [System.Drawing.Color]::FromArgb(255, 245, 217, 164)
$shadow = [System.Drawing.Color]::FromArgb(255, 99, 76, 52)
$fill = [System.Drawing.Color]::FromArgb(255, 35, 42, 43)

function New-IconCanvas {
    $bitmap = [System.Drawing.Bitmap]::new(128, 128, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(4, 4)
    return @{ Bitmap = $bitmap; Graphics = $graphics }
}

function New-Pen([System.Drawing.Color]$color, [float]$width) {
    $pen = [System.Drawing.Pen]::new($color, $width)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    return $pen
}

function Points([float[]]$coordinates) {
    $result = [System.Drawing.PointF[]]::new($coordinates.Length / 2)
    for ($i = 0; $i -lt $coordinates.Length; $i += 2) {
        $result[$i / 2] = [System.Drawing.PointF]::new($coordinates[$i], $coordinates[$i + 1])
    }
    return $result
}

$canvas = New-IconCanvas
$g = $canvas.Graphics
$outline = New-Pen $gold 1.45
$fine = New-Pen $light 0.8
$dark = New-Pen $shadow 0.8
$body = [System.Drawing.PointF[]](Points ([float[]](8, 9, 24, 9, 22.3, 28, 9.7, 28)))
$g.FillPolygon([System.Drawing.SolidBrush]::new($fill), $body)
$g.DrawPolygon($outline, $body)
$g.FillRectangle([System.Drawing.SolidBrush]::new($fill), 5.5, 6, 21, 3.2)
$g.DrawRectangle($outline, 5.5, 6, 21, 3.2)
$g.DrawLine($fine, 11.5, 4.3, 20.5, 4.3)
$g.DrawLine($fine, 13, 3, 19, 3)
$g.DrawLine($dark, 11.5, 12, 12.5, 24.5)
$g.DrawLine($fine, 16, 12, 16, 24.5)
$g.DrawLine($dark, 20.5, 12, 19.5, 24.5)
$g.DrawLine($fine, 11.5, 25.5, 20.5, 25.5)
$canvas.Bitmap.Save((Join-Path $out 'trash-icon-v1.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$canvas.Bitmap.Dispose()

$canvas = New-IconCanvas
$g = $canvas.Graphics
$outline = New-Pen $gold 1.45
$fine = New-Pen $light 0.8
$dark = New-Pen $shadow 0.8
$body = [System.Drawing.PointF[]](Points ([float[]](9, 12.5, 23, 12.5, 27, 27, 5, 27)))
$g.FillPolygon([System.Drawing.SolidBrush]::new($fill), $body)
$g.DrawPolygon($outline, $body)
$g.DrawArc($outline, 12, 5, 8, 10, 185, 170)
$g.DrawLine($fine, 8, 24.5, 24, 24.5)
$g.DrawLine($dark, 16, 15.5, 16, 22.5)
$g.DrawLine($fine, 13.5, 17, 16, 14.5)
$g.DrawLine($fine, 16, 14.5, 18.5, 17)
$canvas.Bitmap.Save((Join-Path $out 'weight-icon-v1.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$canvas.Bitmap.Dispose()
