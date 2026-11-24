$ErrorActionPreference = "Stop"

$projDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$libDir = Join-Path $projDir "lib"
$tmpDir = Join-Path $projDir "_tmp_deps"

New-Item -ItemType Directory -Force -Path $libDir, $tmpDir | Out-Null

function Get-NuGetPkg($pkgId, $version) {
    $idLower = $pkgId.ToLower()
    $url = "https://api.nuget.org/v3-flatcontainer/$idLower/$version/$idLower.$version.nupkg"
    $zip = Join-Path $tmpDir "$idLower.zip"
    $dst = Join-Path $tmpDir $idLower
    Write-Host "Baixando $pkgId $version..."
    Invoke-WebRequest -Uri $url -OutFile $zip
    Expand-Archive -Path $zip -DestinationPath $dst -Force
    return $dst
}

$lhm = Get-NuGetPkg "LibreHardwareMonitorLib" "0.9.6"
Copy-Item (Join-Path $lhm "runtimes\win-x64\lib\net472\LibreHardwareMonitorLib.dll") $libDir -Force

$hid = Get-NuGetPkg "HidSharp" "2.6.4"
Copy-Item (Join-Path $hid "lib\netstandard2.0\HidSharp.dll") $libDir -Force

$disk = Get-NuGetPkg "DiskInfoToolkit" "1.1.2"
Copy-Item (Join-Path $disk "lib\net472\DiskInfoToolkit.dll") $libDir -Force

$bs = Get-NuGetPkg "BlackSharp.Core" "1.0.7"
Copy-Item (Join-Path $bs "lib\net472\BlackSharp.Core.dll") $libDir -Force

$ram = Get-NuGetPkg "RAMSPDToolkit-NDD" "1.4.2"
Copy-Item (Join-Path $ram "lib\net472\RAMSPDToolkit-NDD.dll") $libDir -Force

$mem = Get-NuGetPkg "System.Memory" "4.6.3"
Copy-Item (Join-Path $mem "lib\net462\System.Memory.dll") $libDir -Force

$buf = Get-NuGetPkg "System.Buffers" "4.6.1"
Copy-Item (Join-Path $buf "lib\net462\System.Buffers.dll") $libDir -Force

$vec = Get-NuGetPkg "System.Numerics.Vectors" "4.6.1"
Copy-Item (Join-Path $vec "lib\net462\System.Numerics.Vectors.dll") $libDir -Force

$uns = Get-NuGetPkg "System.Runtime.CompilerServices.Unsafe" "6.1.2"
Copy-Item (Join-Path $uns "lib\net462\System.Runtime.CompilerServices.Unsafe.dll") $libDir -Force

$wv2 = Get-NuGetPkg "Microsoft.Web.WebView2" "1.0.4370.26"
Copy-Item (Join-Path $wv2 "lib\net462\Microsoft.Web.WebView2.Core.dll") $libDir -Force
Copy-Item (Join-Path $wv2 "lib\net462\Microsoft.Web.WebView2.WinForms.dll") $libDir -Force
Copy-Item (Join-Path $wv2 "runtimes\win-x64\native\WebView2Loader.dll") $libDir -Force

# Baixar instalador oficial PawnIO_setup.exe para instalacao automatica offline no primeiro uso
$depsDir = Join-Path $projDir "deps"
New-Item -ItemType Directory -Force -Path $depsDir | Out-Null
$pawnDest = Join-Path $depsDir "PawnIO_setup.exe"
Write-Host "Baixando PawnIO_setup.exe oficial..."
Invoke-WebRequest -Uri "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe" -OutFile $pawnDest -Headers @{"User-Agent"="VoltPulse-Build"}

Remove-Item -Recurse -Force $tmpDir
Write-Host "Dependências prontas em: $libDir e $depsDir"
