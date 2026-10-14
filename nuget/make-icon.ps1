<#
  Generates nuget/icon.png — a timeline-themed package icon for nuget.org.
  Run:  ./nuget/make-icon.ps1   (re-run after tweaking colours/layout)
  Pure GDI+ (System.Drawing), no external tools. 256x256 PNG, transparent corners.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$size = 256
$bmp  = New-Object System.Drawing.Bitmap $size, $size
$g    = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x,         $y,         $d, $d, 180, 90)
    $p.AddArc($x+$w-$d,   $y,         $d, $d, 270, 90)
    $p.AddArc($x+$w-$d,   $y+$h-$d,   $d, $d,   0, 90)
    $p.AddArc($x,         $y+$h-$d,   $d, $d,  90, 90)
    $p.CloseFigure()
    return $p
}
function Fill([System.Drawing.Drawing2D.GraphicsPath]$path, [int]$r, [int]$gr, [int]$b) {
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $r, $gr, $b))
    $g.FillPath($brush, $path); $brush.Dispose()
}

# Background (dark editor canvas)
Fill (New-RoundRect 8 8 240 240 40) 26 29 35

# Track lanes
$laneCol = 45,49,57
foreach ($y in 60,116,172) { Fill (New-RoundRect 36 $y 184 36 8) $laneCol[0] $laneCol[1] $laneCol[2] }

# Clips (Interpole-ish palette): x, y, w, r,g,b
$clips = @(
    @(44,  60, 78, 76,141,230),  @(132, 60, 80, 235,165,70),   # lane 1: blue, amber
    @(60, 116, 96, 95,190,110),                                  # lane 2: green
    @(44, 172, 64, 170,120,230), @(120, 172, 92, 76,141,230)    # lane 3: violet, blue
)
foreach ($c in $clips) { Fill (New-RoundRect $c[0] ($c[1]+5) $c[2] 26 6) $c[3] $c[4] $c[5] }

# Playhead: bright line + handle triangle
$ph = 150
$red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 70, 60))
$g.FillRectangle($red, ($ph-2), 52, 4, 160)
$tri = @(
    (New-Object System.Drawing.PointF(($ph-9), 44)),
    (New-Object System.Drawing.PointF(($ph+9), 44)),
    (New-Object System.Drawing.PointF($ph,     58))
)
$g.FillPolygon($red, $tri); $red.Dispose()

$out = Join-Path $PSScriptRoot 'icon.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "Wrote $out"
