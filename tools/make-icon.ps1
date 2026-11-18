# Purge uygulama ikonu: Fluent tarzı izometrik kutu, kapağı yukarı kalkmış (içi boşaltılıyor).
# WPF ile vektörden çizer, her boyutu ayrı render eder (16 px'de keskin kalsın), PNG'leri ve çok boyutlu .ico'yu yazar.
# Çalıştır: powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$OutDir = (Join-Path $PSScriptRoot '..\Assets'))
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
New-Item -ItemType Directory -Force $OutDir | Out-Null

function C($hex) { [Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Lin($a, $b, $x1, $y1, $x2, $y2) {
    $g = New-Object Windows.Media.LinearGradientBrush
    $g.StartPoint = New-Object Windows.Point($x1, $y1); $g.EndPoint = New-Object Windows.Point($x2, $y2)
    $g.GradientStops.Add((New-Object Windows.Media.GradientStop((C $a), 0))); $g.GradientStops.Add((New-Object Windows.Media.GradientStop((C $b), 1)))
    $g.Freeze(); $g
}
function Poly([double[]]$p) {
    $fig = New-Object Windows.Media.PathFigure; $fig.StartPoint = New-Object Windows.Point($p[0], $p[1]); $fig.IsClosed = $true
    for ($i = 2; $i -lt $p.Length; $i += 2) { $fig.Segments.Add((New-Object Windows.Media.LineSegment((New-Object Windows.Point($p[$i], $p[$i+1])), $true))) }
    $geo = New-Object Windows.Media.PathGeometry; $geo.Figures.Add($fig); $geo
}

# 256'lık tuvalde geometri. Kutu: yükseklik h, izometrik yarım genişlik w.
function Draw($dc, [int]$size) {
    $s = $size / 256.0
    $small = $size -le 32
    # Şekli tuvale yay: içerik merkezi (135,120); küçük boyutta gölge yok, biraz daha büyük.
    $k = if ($small) { 1.2 } else { 1.12 }
    $tg = New-Object Windows.Media.TransformGroup
    $tg.Children.Add((New-Object Windows.Media.TranslateTransform(-135, -122)))
    $tg.Children.Add((New-Object Windows.Media.ScaleTransform($k, $k)))
    $tg.Children.Add((New-Object Windows.Media.TranslateTransform(128, 128)))
    $tg.Children.Add((New-Object Windows.Media.ScaleTransform($s, $s)))
    $dc.PushTransform($tg)
    $edge = New-Object Windows.Media.Pen((New-Object Windows.Media.SolidColorBrush((C '#33000000'))), $(if ($small) { 5 } else { 3 })); $edge.LineJoin = 'Round'
    if (-not $small) {
        $shadow = New-Object Windows.Media.RadialGradientBrush((C '#55000000'), (C '#00000000'))
        $dc.DrawEllipse($shadow, $null, (New-Object Windows.Point(128, 226)), 92, 14)
    }
    # gövde: sol ve sağ yüzler (cx=128, üst kenar y=118, alt y=212)
    $left  = Poly @(36,118, 128,164, 128,224, 36,178)
    $right = Poly @(128,164, 220,118, 220,178, 128,224)
    $dc.DrawGeometry((Lin '#3FA9F5' '#1565C0' 0 0 1 1), $edge, $left)
    $dc.DrawGeometry((Lin '#1E7FE0' '#0D3F8C' 0 0 1 1), $edge, $right)
    # kutunun içi (karanlık ağız)
    $mouth = Poly @(36,118, 128,72, 220,118, 128,164)
    $dc.DrawGeometry((Lin '#0B2E66' '#123F82' 0 0 0 1), $null, $mouth)
    # kapak: yukarı kalkmış ve hafif sağa kaymış, kalın (üst + ön kenar)
    $dy = -58; $dx = 14
    $lidTop = Poly @((36+$dx),(118+$dy), (128+$dx),(72+$dy), (220+$dx),(118+$dy), (128+$dx),(164+$dy))
    $lidL   = Poly @((36+$dx),(118+$dy), (128+$dx),(164+$dy), (128+$dx),(178+$dy), (36+$dx),(132+$dy))
    $lidR   = Poly @((128+$dx),(164+$dy), (220+$dx),(118+$dy), (220+$dx),(132+$dy), (128+$dx),(178+$dy))
    $dc.DrawGeometry((Lin '#7FD3FF' '#2E9BF0' 0 0 1 1), $edge, $lidL)
    $dc.DrawGeometry((Lin '#4BB6FA' '#1C78D8' 0 0 1 1), $edge, $lidR)
    $dc.DrawGeometry((Lin '#E8F7FF' '#8FD8FF' 0 0 1 1), $edge, $lidTop)
    # kapak üstünde parlama
    $shine = Poly @((70+$dx),(104+$dy), (128+$dx),(76+$dy), (150+$dx),(87+$dy), (92+$dx),(115+$dy))
    $dc.DrawGeometry((New-Object Windows.Media.SolidColorBrush((C '#66FFFFFF'))), $null, $shine)
    $dc.Pop()
}

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$pngs = @{}
foreach ($sz in $sizes) {
    $dv = New-Object Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen(); Draw $dc $sz; $dc.Close()
    $bmp = New-Object Windows.Media.Imaging.RenderTargetBitmap($sz, $sz, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $ms = New-Object IO.MemoryStream; $enc.Save($ms); $pngs[$sz] = $ms.ToArray()
    [IO.File]::WriteAllBytes((Join-Path $OutDir "Purge-$sz.png"), $pngs[$sz])
}

# .ico: PNG gömülü girdiler (Vista+)
$ico = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $d = $pngs[$sz]; $b = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$b); $bw.Write([byte]$b); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$d.Length); $bw.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($sz in $sizes) { $bw.Write($pngs[$sz]) }
$bw.Flush(); [IO.File]::WriteAllBytes((Join-Path $OutDir 'Purge.ico'), $ico.ToArray())
"wrote $OutDir"
