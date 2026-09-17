# Genera app.ico (16, 24, 32, 48, 64, 128, 256 px) con System.Drawing.
# Diseño: fondo azul oscuro redondeado, pantalla de TV blanca y punto rojo de "grabando".
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function Draw-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    $radius = $s * 0.22
    $bg = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $bg.AddArc(0, 0, $d, $d, 180, 90)
    $bg.AddArc($s - $d, 0, $d, $d, 270, 90)
    $bg.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $bg.AddArc(0, $s - $d, $d, $d, 90, 90)
    $bg.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 41, 68))
    $g.FillPath($brush, $bg)

    # Pantalla de TV (contorno blanco)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1, $s * 0.075))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $x = $s * 0.17; $y = $s * 0.22; $w = $s * 0.66; $h = $s * 0.46
    $tvR = $s * 0.08
    $tv = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tv.AddArc($x, $y, $tvR*2, $tvR*2, 180, 90)
    $tv.AddArc($x+$w-$tvR*2, $y, $tvR*2, $tvR*2, 270, 90)
    $tv.AddArc($x+$w-$tvR*2, $y+$h-$tvR*2, $tvR*2, $tvR*2, 0, 90)
    $tv.AddArc($x, $y+$h-$tvR*2, $tvR*2, $tvR*2, 90, 90)
    $tv.CloseFigure()
    $g.DrawPath($pen, $tv)

    # Peana
    $g.DrawLine($pen, $s*0.38, $s*0.80, $s*0.62, $s*0.80)

    # Punto rojo de grabación
    $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 229, 57, 53))
    $r = $s * 0.13
    $g.FillEllipse($red, $s*0.5 - $r, $y + $h/2 - $r, $r*2, $r*2)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = Draw-Frame $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,$ms.ToArray()
    if ($sz -eq 256) { $bmp.Save("$PSScriptRoot\app-preview.png", [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}

# Ensamblar el .ico: cabecera + entradas + datos PNG
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $data = $pngs[$i]
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes("$PSScriptRoot\app.ico", $out.ToArray())
Write-Host "Generado $PSScriptRoot\app.ico ($($out.Length) bytes)"
