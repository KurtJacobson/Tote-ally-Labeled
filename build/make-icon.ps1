# Draws tote.ico, the app's icon, at every size Windows uses. The same tote as the page header's SVG in
# wwwroot/index.html; change both together. Run from the repo root: build\make-icon.ps1 -Out tote.ico
param([string]$Out, [string]$PreviewDir)
Add-Type -AssemblyName System.Drawing
# Same drawing as the header's SVG (viewBox 8 24 112 74), on a 128 grid, centered vertically.
function RoundRect($x, $y, $w, $h, $r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = 2 * $r
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure(); $p
}
function Brush($hex) { New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($hex)) }

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($size in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
  $g.ScaleTransform($size / 128, $size / 128)
  $g.TranslateTransform(0, 3)   # 74 tall on a 128 grid: 27 above, 27 below

  $g.FillPath((Brush '#f2be22'), (RoundRect 8 24 112 24 6))
  $body = New-Object System.Drawing.Drawing2D.GraphicsPath
  $body.AddLine(17, 48, 111, 48); $body.AddLine(111, 48, 105.5, 92)
  $body.AddBezier(105.5, 92, 105, 96, 103, 97, 99.5, 97)
  $body.AddLine(99.5, 97, 28.5, 97)
  $body.AddBezier(28.5, 97, 25, 97, 23, 96, 22.5, 92)
  $body.CloseFigure()
  $g.FillPath((Brush '#2e3338'), $body)
  $rib = Brush '#42484f'
  foreach ($r in @(@(46, 55, 8), @(77, 55, 8), @(46, 88, 5), @(77, 88, 5))) { $g.FillRectangle($rib, $r[0], $r[1], 6, $r[2]) }
  $g.FillPath((Brush '#ffffff'), (RoundRect 40 64 48 24 4))
  $g.FillRectangle((Brush '#1e2429'), 50, 74, 28, 4)
  $g.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  if ($PreviewDir) { $bmp.Save((Join-Path $PreviewDir "tote-$size.png")) }
  $bmp.Dispose()
  $frames += , $ms.ToArray()
}

# ICO with PNG frames
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$frames[$i].Length); $w.Write([UInt32]$offset)
  $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write($f) }
$w.Close()
