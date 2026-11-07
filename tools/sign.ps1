<#
.SYNOPSIS
  Authenticode-signs files with signtool. Works with any certificate provider:
    - a .pfx file:            -PfxPath / $env:SIGN_PFX_PATH, password in $env:SIGN_PFX_PASSWORD
    - a base64 .pfx (CI):     $env:SIGN_PFX_BASE64 + $env:SIGN_PFX_PASSWORD (GitHub secrets)
    - the certificate store:  -Thumbprint / $env:SIGN_CERT_THUMBPRINT (USB tokens, Certum SimplySign, anything with a CSP/KSP)
  With no certificate configured, it warns and exits successfully without signing.

.EXAMPLE
  ./tools/sign.ps1 dist/Downloads.exe
  ./tools/sign.ps1 dist/Downloads.exe -Thumbprint 0123456789ABCDEF0123456789ABCDEF01234567
  ./tools/sign.ps1 -IsConfigured   # writes $true / $false
#>
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)] [string[]] $Files,
    [string] $PfxPath = $env:SIGN_PFX_PATH,
    [string] $Thumbprint = $env:SIGN_CERT_THUMBPRINT,
    [string] $TimestampUrl = $env:SIGN_TIMESTAMP_URL,
    [string] $Description = "DownloadsOSX",
    [string] $DescriptionUrl = "https://github.com/fhnb16/DownloadsOSX",
    [switch] $IsConfigured
)

$ErrorActionPreference = "Stop"

$configured = [bool]($Thumbprint -or $PfxPath -or $env:SIGN_PFX_BASE64)
if ($IsConfigured) { return $configured }
if (-not $Files) { throw "No files to sign." }

if (-not $configured) {
    $message = "No code signing certificate configured (SIGN_PFX_BASE64 / SIGN_PFX_PATH / SIGN_CERT_THUMBPRINT); leaving unsigned: $($Files -join ', ')"
    if ($env:GITHUB_ACTIONS) { Write-Host "::warning::$message" } else { Write-Warning $message }
    return
}

function Find-SignTool {
    $onPath = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $arch = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
    $found = Get-ChildItem $kits -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq $arch } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $found) { throw "signtool.exe not found. Install the Windows SDK (Signing Tools for Desktop Apps)." }
    return $found.FullName
}

$signtool = Find-SignTool
$tempPfx = $null
try {
    if ($Thumbprint) {
        $certArgs = @("/sha1", ($Thumbprint -replace '\s', ''))
    } else {
        if (-not $PfxPath) {
            $tempPfx = Join-Path ([IO.Path]::GetTempPath()) ("sign-" + [guid]::NewGuid().ToString("N") + ".pfx")
            [IO.File]::WriteAllBytes($tempPfx, [Convert]::FromBase64String(($env:SIGN_PFX_BASE64 -replace '\s', '')))
            $PfxPath = $tempPfx
        }
        $certArgs = @("/f", $PfxPath)
        if ($env:SIGN_PFX_PASSWORD) { $certArgs += @("/p", $env:SIGN_PFX_PASSWORD) }
    }

    # Timestamp servers are flaky now and then: try the configured one, then a few public RFC 3161 servers.
    $timestampUrls = @($TimestampUrl, "http://timestamp.digicert.com", "http://timestamp.sectigo.com", "http://time.certum.pl") |
        Where-Object { $_ } | Select-Object -Unique

    foreach ($file in $Files) {
        $path = (Resolve-Path $file).Path
        $signed = $false
        foreach ($url in $timestampUrls) {
            & $signtool sign /fd sha256 /tr $url /td sha256 /d $Description /du $DescriptionUrl @certArgs $path
            if ($LASTEXITCODE -eq 0) { $signed = $true; break }
            Write-Warning "Signing $path with timestamp server $url failed (exit $LASTEXITCODE), trying the next one"
        }
        if (-not $signed) { throw "Signing failed: $path" }

        # Self-signed certificates report UnknownError (untrusted root); only a missing or broken signature is fatal.
        $sig = Get-AuthenticodeSignature $path
        if (-not $sig.SignerCertificate -or $sig.Status -eq "HashMismatch" -or $sig.Status -eq "NotSigned") {
            throw "Signature check failed for ${path}: $($sig.Status) $($sig.StatusMessage)"
        }
        Write-Host "Signed $path by $($sig.SignerCertificate.Subject) [$($sig.Status)]"
    }
}
finally {
    if ($tempPfx) { Remove-Item $tempPfx -Force -ErrorAction SilentlyContinue }
}
