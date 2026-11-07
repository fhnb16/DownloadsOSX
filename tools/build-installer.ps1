<#
.SYNOPSIS
  Builds DownloadsOSX-Setup.exe with Inno Setup 6. If a code signing certificate is configured (see sign.ps1),
  the setup and its uninstaller are signed. Sign Downloads.exe itself before calling this, so the signed exe is packed.

.EXAMPLE
  ./tools/sign.ps1 dist/Downloads.exe
  ./tools/build-installer.ps1 -Exe dist/Downloads.exe -Version 1.2.0 -OutDir dist
#>
param(
    [string] $Exe = "src/DownloadsOSX/bin/Release/net48/Downloads.exe",
    [string] $Version,
    [string] $OutDir = "dist"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Find-Iscc {
    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $found = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $found) { throw "Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup (or choco install innosetup)." }
    return $found
}

$exePath = (Resolve-Path $Exe).Path
if (-not $Version) { $Version = (Get-Item $exePath).VersionInfo.ProductVersion.Split('+')[0] }
New-Item -ItemType Directory -Force $OutDir | Out-Null

$isccArgs = @(
    "/Q",
    "/DSourceExe=$exePath",
    "/DAppVersion=$Version",
    "/DFileVersion=$(($Version -split '[-+]')[0])",
    "/O$((Resolve-Path $OutDir).Path)"
)

if (& "$PSScriptRoot\sign.ps1" -IsConfigured) {
    # ISCC runs this for the setup and the uninstaller ($q = quote, $f = quoted file name); the certificate comes from the environment.
    $shell = (Get-Process -Id $PID).Path
    $isccArgs += "/Ssigntool=`$q$shell`$q -NoProfile -ExecutionPolicy Bypass -File `$q$PSScriptRoot\sign.ps1`$q `$f"
    $isccArgs += "/DSign"
} else {
    Write-Host "No code signing certificate configured: the setup and uninstaller stay unsigned."
}

& (Find-Iscc) @isccArgs (Join-Path $root "installer\DownloadsOSX.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
Write-Host "Built $(Join-Path $OutDir 'DownloadsOSX-Setup.exe')"
