# Builds dist\MobiTop-Setup.exe: the launcher, scrcpy + adb, the laptop script and the phone APK in one installer.
# Needs only what's on this PC: Visual Studio Build Tools' C# compiler and .NET Framework 4.8 (built into Windows).
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$root = Split-Path $here
$csc = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = @("$fw\WPF\PresentationFramework.dll", "$fw\WPF\PresentationCore.dll", "$fw\WPF\WindowsBase.dll", "$fw\System.Xaml.dll",
          "$fw\System.IO.Compression.dll", "$fw\System.IO.Compression.FileSystem.dll", "$fw\Microsoft.CSharp.dll", "$fw\System.Core.dll") |
        ForEach-Object { "/r:$_" }
$stage = Join-Path $here 'stage'; $dist = Join-Path $root 'dist'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage, $dist | Out-Null

# Icon: the monitor outline in the sizes Windows asks for, as PNGs inside one .ico
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile((Join-Path $root 'tools\scrcpy.png'))
$images = foreach ($s in 256, 48, 32, 16) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s; $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.DrawImage($src, 0, 0, $s, $s)
    $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    @{ size = $s; data = $ms.ToArray() }
}
$src.Dispose()
$ico = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($i in $images) {
    $d = if ($i.size -ge 256) { 0 } else { $i.size } # 0 means 256 in the ICO format
    $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$i.data.Length); $w.Write([uint32]$offset)
    $offset += $i.data.Length
}
foreach ($i in $images) { $w.Write($i.data) }
[System.IO.File]::WriteAllBytes((Join-Path $stage 'mobitop.ico'), $ico.ToArray())

# Launcher
& $csc /nologo /target:winexe /optimize+ /out:"$stage\MobiTop.exe" /win32icon:"$stage\mobitop.ico" $refs "$here\Ui.cs" "$here\Launcher.cs"
if ($LASTEXITCODE) { throw 'launcher build failed' }
$p = Start-Process "$stage\MobiTop.exe" -ArgumentList '--selftest' -Wait -PassThru
if ($p.ExitCode) { throw 'launcher self-test failed' }

# Everything it needs at run time
Copy-Item (Join-Path $root 'tools\scrcpy-win64-v5.0.1') $stage -Recurse
Copy-Item (Join-Path $root 'tools\mobitop-laptop.ps1'), (Join-Path $root 'tools\Frames.cs'),(Join-Path $root 'tools\scrcpy.png'), (Join-Path $root 'tools\disconnected.png') $stage
Copy-Item (Join-Path $root 'Mobi-Top.apk') $stage

# Installer with the whole stage folder inside it
$zip = Join-Path $here 'payload.zip'; Remove-Item $zip -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
& $csc /nologo /target:winexe /optimize+ /out:"$dist\MobiTop-Setup.exe" /win32icon:"$stage\mobitop.ico" /resource:"$zip,payload.zip" $refs "$here\Ui.cs" "$here\Setup.cs"
if ($LASTEXITCODE) { throw 'installer build failed' }
Remove-Item $zip
Get-Item "$dist\MobiTop-Setup.exe" | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
