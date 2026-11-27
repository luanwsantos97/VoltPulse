param(
    [switch]$ReleaseZip,
    [switch]$Installer,
    [string]$Version = "1.0.5"
)

$ErrorActionPreference = "Stop"

$projDir    = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir     = Join-Path $projDir "src"
$uiDir      = Join-Path $projDir "ui"
$libDir     = Join-Path $projDir "lib"
$depsDir    = Join-Path $projDir "deps"
$assetsDir  = Join-Path $projDir "assets"
$distDir    = Join-Path $projDir "dist\VoltPulse"
$releaseDir = Join-Path $projDir "releases"

# Baixa dependencias automaticamente se nao estiverem presentes
if (-not (Test-Path (Join-Path $libDir "LibreHardwareMonitorLib.dll")) -or
    -not (Test-Path (Join-Path $libDir "BlackSharp.Core.dll")) -or
    -not (Test-Path (Join-Path $libDir "Microsoft.Web.WebView2.WinForms.dll")) -or
    -not (Test-Path (Join-Path $depsDir "PawnIO_setup.exe"))) {
    Write-Host "Baixando dependências ausentes..."
    & (Join-Path $projDir "scripts\fetch-deps.ps1")
}

# Gera icone .ico multi-resolucao se nao existir
$iconPath = Join-Path $assetsDir "voltpulse.ico"
if (-not (Test-Path $iconPath)) {
    & (Join-Path $projDir "scripts\make-icon.ps1")
}

if (Test-Path $distDir) {
    try {
        Remove-Item -Recurse -Force $distDir -ErrorAction Stop
    } catch {
        Write-Warning "dist\VoltPulse está em uso (feche o VoltPulse na bandeja para sobrescrever). Compilando em dist\VoltPulse-$Version..."
        $distDir = Join-Path $projDir "dist\VoltPulse-$Version"
        if (Test-Path $distDir) { Remove-Item -Recurse -Force $distDir }
    }
}
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "Compilador C# (.NET Framework 4.x) não encontrado em $csc"
}

$exeOut = Join-Path $distDir "VoltPulse.exe"
$sources = @(
    (Join-Path $srcDir "Models.cs"),
    (Join-Path $srcDir "HardwareMonitorService.cs"),
    (Join-Path $srcDir "EnergyStorage.cs"),
    (Join-Path $srcDir "DependencyManager.cs"),
    (Join-Path $srcDir "GameDetector.cs"),
    (Join-Path $srcDir "FpsMonitor.cs"),
    (Join-Path $srcDir "SafePowerTuner.cs"),
    (Join-Path $srcDir "ProcessMonitor.cs"),
    (Join-Path $srcDir "LocalApiServer.cs"),
    (Join-Path $srcDir "UpdateChecker.cs"),
    (Join-Path $srcDir "OverlayForm.cs"),
    (Join-Path $srcDir "MainForm.cs"),
    (Join-Path $srcDir "Program.cs")
)

$args = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/out:$exeOut",
    "/win32manifest:$(Join-Path $srcDir 'app.manifest')",
    "/win32icon:$iconPath",
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Management.dll",
    "/r:System.Web.Extensions.dll",
    "/r:$(Join-Path $libDir 'LibreHardwareMonitorLib.dll')",
    "/r:$(Join-Path $libDir 'DiskInfoToolkit.dll')",
    "/r:$(Join-Path $libDir 'Microsoft.Web.WebView2.Core.dll')",
    "/r:$(Join-Path $libDir 'Microsoft.Web.WebView2.WinForms.dll')"
) + $sources

Write-Host "Compilando VoltPulse.exe v$Version..."
& $csc @args
if ($LASTEXITCODE -ne 0) {
    throw "Falha na compilação (exit code $LASTEXITCODE)"
}

Copy-Item (Join-Path $libDir "*.dll") -Destination $distDir -Force
Copy-Item (Join-Path $srcDir "VoltPulse.exe.config") -Destination $distDir -Force
Copy-Item $uiDir -Destination (Join-Path $distDir "ui") -Recurse -Force
if (Test-Path $depsDir) {
    Copy-Item $depsDir -Destination (Join-Path $distDir "deps") -Recurse -Force
}

Write-Host "Build concluído com sucesso em: $exeOut"

if ($ReleaseZip) {
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    $zipPath = Join-Path $releaseDir "VoltPulse-v$Version-win-x64.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path (Join-Path $distDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Pacote Portátil ZIP gerado em: $zipPath"
}

if ($Installer) {
    New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
    $isccPaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $iscc = $isccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($cmd) { $iscc = $cmd.Source }
    }
    if (-not $iscc) {
        throw "Inno Setup 6 (ISCC.exe) não encontrado. Instale com: winget install JRSoftware.InnoSetup"
    }

    $issFile = Join-Path $projDir "installer\VoltPulse.iss"
    Write-Host "Compilando instalador Windows via Inno Setup ($iscc)..."
    & $iscc "/DAppVersion=$Version" "/DDistDir=$distDir" "/DOutputDir=$releaseDir" $issFile
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao compilar o instalador Inno Setup (exit code $LASTEXITCODE)"
    }
    Write-Host "Instalador gerado em: $(Join-Path $releaseDir "VoltPulse-Setup-v$Version.exe")"
}

if ($ReleaseZip -or $Installer) {
    $sumLines = New-Object System.Collections.Generic.List[string]
    $candidates = @(
        (Join-Path $releaseDir "VoltPulse-Setup-v$Version.exe"),
        (Join-Path $releaseDir "VoltPulse-v$Version-win-x64.zip")
    )
    foreach ($f in $candidates) {
        if (Test-Path $f) {
            $h = (Get-FileHash -Path $f -Algorithm SHA256).Hash.ToLowerInvariant()
            $sumLines.Add("$h  $(Split-Path -Leaf $f)")
        }
    }
    if ($sumLines.Count -gt 0) {
        $sumsPath = Join-Path $releaseDir "SHA256SUMS.txt"
        [System.IO.File]::WriteAllText($sumsPath, ($sumLines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
        Write-Host "Checksums SHA-256 gerados em: $sumsPath"
    }
}
