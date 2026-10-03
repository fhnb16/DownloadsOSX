<#
.SYNOPSIS
  Writes update.json for a built Downloads.exe (used by CI and for the own-server mirror).

.EXAMPLE
  ./tools/make-manifest.ps1 -Exe dist/Downloads.exe -Url https://root.fhnb.ru/DownloadsOSX/Downloads.exe -Out dist/update.json -Notes "Bug fixes"
#>
param(
    [Parameter(Mandatory = $true)] [string] $Exe,
    [Parameter(Mandatory = $true)] [string] $Url,
    [string] $Out = "update.json",
    [string] $Version,
    [string] $Notes = "",
    [string] $NotesFile,
    [int] $MinOsBuild = 7601
)

$ErrorActionPreference = "Stop"
$exePath = (Resolve-Path $Exe).Path

if (-not $Version) {
    # ProductVersion carries the informational version (e.g. 1.2.0 or 1.2.0-rc1); strip build metadata.
    $Version = (Get-Item $exePath).VersionInfo.ProductVersion.Split('+')[0]
}
if ($NotesFile -and (Test-Path $NotesFile)) { $Notes = Get-Content $NotesFile -Raw }

$manifest = [ordered]@{
    version    = $Version
    url        = $Url
    sha256     = (Get-FileHash $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
    notes      = $Notes.Trim()
    minOsBuild = $MinOsBuild
    published  = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
}

$json = $manifest | ConvertTo-Json -Depth 3
$outDir = Split-Path -Parent $Out
if ($outDir) { New-Item -ItemType Directory -Force $outDir | Out-Null }
[IO.File]::WriteAllText((Join-Path (Get-Location) $Out), $json, (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote $Out"
Write-Host $json
