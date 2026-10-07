# Baut Windkanal 2D (App + Installer) mit dem in Windows enthaltenen C#-Compiler.
# Aufruf:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Split-Path -Parent $root
$bin  = Join-Path $root 'bin'
$csc  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
New-Item -ItemType Directory -Force $bin | Out-Null

# --- Icon erzeugen (Windkanal-Motiv wie im App-Logo, mehrere Größen, PNG-komprimiert) ---
Add-Type -AssemblyName System.Drawing
$ico = Join-Path $bin 'app.ico'
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $r = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(21, 23, 26))
    $rad = [single]($s * 0.3)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $rad * 2, $rad * 2, 180, 90)
    $path.AddArc($s - $rad * 2 - 1, 0, $rad * 2, $rad * 2, 270, 90)
    $path.AddArc($s - $rad * 2 - 1, $s - $rad * 2 - 1, $rad * 2, $rad * 2, 0, 90)
    $path.AddArc(0, $s - $rad * 2 - 1, $rad * 2, $rad * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($bg, $path)
    # gleiches Motiv wie Theme.DrawLogo: zwei Stromlinienpaare um einen Zylinder, einfarbig weiß
    $cy = $s * 0.5; $cx = $s * 0.42; $cr = $s * 0.12
    $offs = 0.13, 0.25; $alpha = 255, 150
    for ($i = 0; $i -lt 2; $i++) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb($alpha[$i], 255, 255, 255)), ([single][Math]::Max(1.3, $s * 0.06))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        foreach ($sign in -1, 1) {
            $pts = for ($k = 0; $k -le 20; $k++) {
                $x = $s * (0.18 + 0.66 * $k / 20)
                $d = ($x - $cx) / ($s * 0.17)
                $y = $cy + $sign * $s * ($offs[$i] + (0.13 - 0.04 * $i) * [Math]::Exp(-$d * $d))
                New-Object System.Drawing.PointF ([single]$x), ([single]$y)
            }
            $g.DrawLines($pen, [System.Drawing.PointF[]]$pts)
        }
        $pen.Dispose()
    }
    $g.FillEllipse([System.Drawing.Brushes]::White, [single]($cx - $cr), [single]($cy - $cr), [single]($cr * 2), [single]($cr * 2))
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

# --- Modelle (Datendateien) als eingebettete Ressourcen ---
$modelRes = @("/resource:$(Join-Path $root 'Modelle\katalog.txt'),Modelle.katalog.txt")
$modelRes += Get-ChildItem (Join-Path $root 'Modelle') -Filter *.modell | ForEach-Object { "/resource:$($_.FullName),Modelle.$($_.Name)" }
$modelRes += Get-ChildItem (Join-Path $root 'Modelle\Profile') -Filter *.dat | ForEach-Object { "/resource:$($_.FullName),Profile.$($_.Name)" }

# --- App ---
$app = Join-Path $bin 'Windkanal2D.exe'
& $csc /nologo /target:winexe /unsafe /optimize+ /platform:x64 /win32icon:$ico `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:$app `
    "/resource:$(Join-Path $root 'Fonts\Outfit-Regular.ttf'),Fonts.Outfit-Regular.ttf" `
    "/resource:$(Join-Path $root 'Fonts\Outfit-Medium.ttf'),Fonts.Outfit-Medium.ttf" `
    $modelRes (Join-Path $root 'App\*.cs') (Join-Path $root 'App3D\*.cs')
if ($LASTEXITCODE -ne 0) { throw 'App-Build fehlgeschlagen' }

# --- Validierungstest (Konsole) ---
$test = Join-Path $bin 'ValidationTest.exe'
& $csc /nologo /unsafe /optimize+ /platform:x64 /r:System.Drawing.dll /out:$test $modelRes `
    (Join-Path $root 'App\Solver.cs') (Join-Path $root 'App\GpuLbm.cs') (Join-Path $root 'App\Shapes.cs') `
    (Join-Path $root 'App\Models.cs') (Join-Path $root 'App\Visuals.cs') (Join-Path $root 'Test\ValidationTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test-Build fehlgeschlagen' }

# --- Modell-Vorschau (prüft alle Modelle und zeichnet eine Übersicht) ---
$preview = Join-Path $bin 'ModellVorschau.exe'
& $csc /nologo /optimize+ /platform:x64 /r:System.Drawing.dll /out:$preview $modelRes `
    (Join-Path $root 'App\Shapes.cs') (Join-Path $root 'App\Models.cs') (Join-Path $root 'Test\ModellVorschau.cs')
if ($LASTEXITCODE -ne 0) { throw 'Vorschau-Build fehlgeschlagen' }

# --- 3D-Rechenkern-Test (Konsole, eigenständig, nutzt keinen 2D-Code) ---
$test3d = Join-Path $bin 'Test3D.exe'
& $csc /nologo /optimize+ /platform:x64 /out:$test3d `
    (Join-Path $root 'App3D\Lbm3D.cs') (Join-Path $root 'Test\Test3D.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test3D-Build fehlgeschlagen' }

# --- 3D-Vorschau (prüft Import und Zellen, speichert Bilder des 3D-Fensters) ---
$preview3d = Join-Path $bin 'Vorschau3D.exe'
& $csc /nologo /unsafe /optimize+ /platform:x64 /main:Vorschau3D `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:$preview3d `
    "/resource:$(Join-Path $root 'Fonts\Outfit-Regular.ttf'),Fonts.Outfit-Regular.ttf" `
    "/resource:$(Join-Path $root 'Fonts\Outfit-Medium.ttf'),Fonts.Outfit-Medium.ttf" `
    $modelRes (Join-Path $root 'App\*.cs') (Join-Path $root 'App3D\*.cs') (Join-Path $root 'Test\Vorschau3D.cs')
if ($LASTEXITCODE -ne 0) { throw 'Vorschau3D-Build fehlgeschlagen' }

# --- Installer (App als eingebettete Ressource) ---
$setup = Join-Path $bin 'Setup.exe'
# Der Installer nutzt Design, Schrift und Strömungsbühne des Programms (Theme, Bedienelemente, FlowStage)
& $csc /nologo /target:winexe /unsafe /optimize+ /platform:x64 /win32icon:$ico `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll "/resource:$app,Payload.Windkanal2D.exe" /out:$setup `
    "/resource:$(Join-Path $root 'Fonts\Outfit-Regular.ttf'),Fonts.Outfit-Regular.ttf" `
    "/resource:$(Join-Path $root 'Fonts\Outfit-Medium.ttf'),Fonts.Outfit-Medium.ttf" `
    (Join-Path $root 'Setup\Setup.cs') (Join-Path $root 'App\AppInfo.cs') (Join-Path $root 'App\Ui.cs') `
    (Join-Path $root 'App\FlowStage.cs') (Join-Path $root 'App\Visuals.cs') (Join-Path $root 'App\Solver.cs') `
    (Join-Path $root 'App\GpuLbm.cs') (Join-Path $root 'App\Shapes.cs') (Join-Path $root 'App\Models.cs')
if ($LASTEXITCODE -ne 0) { throw 'Setup-Build fehlgeschlagen' }

Copy-Item $setup (Join-Path $dist 'Windkanal2D-Setup.exe') -Force
Copy-Item $setup (Join-Path $dist 'Windkanal2D-Deinstallieren.exe') -Force
Write-Host "Fertig: $dist"
