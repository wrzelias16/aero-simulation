# Baut Windkanal 2D (App + Installer) mit dem in Windows enthaltenen C#-Compiler.
# Aufruf:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Split-Path -Parent $root
$bin  = Join-Path $root 'bin'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $bin | Out-Null

# --- Icon erzeugen (Windkanal-Motiv, mehrere Größen, PNG-komprimiert) ---
Add-Type -AssemblyName System.Drawing
$ico = Join-Path $bin 'app.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $r = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, ([System.Drawing.Color]::FromArgb(24, 40, 72)), ([System.Drawing.Color]::FromArgb(12, 18, 30)), 90
    $rad = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $rad * 2, $rad * 2, 180, 90)
    $path.AddArc($s - $rad * 2 - 1, 0, $rad * 2, $rad * 2, 270, 90)
    $path.AddArc($s - $rad * 2 - 1, $s - $rad * 2 - 1, $rad * 2, $rad * 2, 0, 90)
    $path.AddArc(0, $s - $rad * 2 - 1, $rad * 2, $rad * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($bg, $path)
    $colors = @([System.Drawing.Color]::FromArgb(84, 200, 255), [System.Drawing.Color]::FromArgb(106, 253, 98), [System.Drawing.Color]::FromArgb(255, 159, 67))
    $cy = $s * 0.5; $cx = $s * 0.42; $cr = $s * 0.13
    for ($i = 0; $i -lt 3; $i++) {
        $pen = New-Object System.Drawing.Pen $colors[$i], ([single][Math]::Max(1.2, $s * 0.055))
        foreach ($sign in -1, 1) {
            $off = $sign * $s * (0.10 + 0.11 * $i)
            $pts = for ($k = 0; $k -le 24; $k++) {
                $x = $s * (0.08 + 0.84 * $k / 24)
                $d = ($x - $cx) / ($s * 0.18)
                $bump = $sign * $s * (0.16 - 0.035 * $i) * [Math]::Exp(-$d * $d)
                New-Object System.Drawing.PointF ([single]$x), ([single]($cy + $off + $bump))
            }
            $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)
        }
        $pen.Dispose()
    }
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 233, 238))), [single]($cx - $cr), [single]($cy - $cr), [single]($cr * 2), [single]($cr * 2))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($ico)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()

# --- App ---
$app = Join-Path $bin 'Windkanal2D.exe'
& $csc /nologo /target:winexe /unsafe /optimize+ /platform:x64 /win32icon:$ico `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:$app `
    (Join-Path $root 'App\*.cs')
if ($LASTEXITCODE -ne 0) { throw 'App-Build fehlgeschlagen' }

# --- Validierungstest (Konsole) ---
$test = Join-Path $bin 'ValidationTest.exe'
& $csc /nologo /unsafe /optimize+ /platform:x64 /r:System.Drawing.dll /out:$test `
    (Join-Path $root 'App\Solver.cs') (Join-Path $root 'App\GpuLbm.cs') (Join-Path $root 'App\Shapes.cs') `
    (Join-Path $root 'App\Visuals.cs') (Join-Path $root 'Test\ValidationTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test-Build fehlgeschlagen' }

# --- Installer (App als eingebettete Ressource) ---
$setup = Join-Path $bin 'Setup.exe'
& $csc /nologo /target:winexe /optimize+ /win32icon:$ico `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll "/resource:$app,Payload.Windkanal2D.exe" /out:$setup `
    (Join-Path $root 'Setup\Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Setup-Build fehlgeschlagen' }

Copy-Item $setup (Join-Path $dist 'Windkanal2D-Setup.exe') -Force
Copy-Item $setup (Join-Path $dist 'Windkanal2D-Deinstallieren.exe') -Force
Write-Host "Fertig: $dist"
