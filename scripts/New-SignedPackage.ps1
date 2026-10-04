param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedSignerSubject,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$SourceRevision
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$signedDir = Join-Path $projectRoot 'dist\signed'
$signedExe = Join-Path $signedDir 'Dolly Paste.exe'
$releaseDir = Join-Path $projectRoot 'dist\release'

if (Test-Path -LiteralPath $releaseDir) {
    throw 'dist\release already exists. Use a clean build directory to prevent stale release artifacts.'
}
if (-not (Test-Path -LiteralPath $signedExe -PathType Leaf)) {
    throw 'The signed Dolly Paste.exe is missing. Unsigned build output is never used as a fallback.'
}
$signedFiles = @(Get-ChildItem -LiteralPath $signedDir -File -Recurse -Force)
if ($signedFiles.Count -ne 1 -or $signedFiles[0].FullName -ne $signedExe) {
    throw 'The signing result must contain only Dolly Paste.exe.'
}

$signature = Get-AuthenticodeSignature -LiteralPath $signedExe
if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate) {
    throw "The executable must have a valid, trusted Authenticode signature. Status: $($signature.Status)"
}
if ($signature.SignatureType -ne 'Authenticode') {
    throw 'The executable must have an embedded Authenticode signature; catalog-only trust is insufficient.'
}
if ($signature.SignerCertificate.Subject -cne $ExpectedSignerSubject) {
    throw 'The Authenticode publisher does not match the approved certificate subject.'
}
if ($null -eq $signature.TimeStamperCertificate) {
    throw 'The Authenticode signature must include a trusted timestamp.'
}

[xml]$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'app.manifest') -Raw
$version = $manifest.assembly.assemblyIdentity.version
$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($signedExe)
if ($metadata.ProductName -ne 'Dolly Paste' -or
    $metadata.FileDescription -ne 'Dolly Paste' -or
    $metadata.CompanyName -ne 'Smet Software Solutions' -or
    $metadata.FileVersion -ne $version -or $metadata.ProductVersion -ne $version) {
    throw 'Signed executable metadata must identify Dolly Paste and match the manifest version.'
}

$exeHash = (Get-FileHash -LiteralPath $signedExe -Algorithm SHA256).Hash
$packageDir = Join-Path $releaseDir 'contents'
[System.IO.Directory]::CreateDirectory($packageDir) | Out-Null
Copy-Item -LiteralPath $signedExe -Destination (Join-Path $packageDir 'Dolly Paste.exe')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $packageDir 'LICENSE')

$readme = @"
Dolly Paste for Windows
======================

Version: $version
Requirements: Windows 10 or Windows 11 (32-bit or 64-bit), .NET Framework 4.8.

1. Extract all files from this ZIP into a folder of your choice.
2. Open Dolly Paste.exe. No installer or administrator rights are needed.
3. Press Ctrl+Shift+V to open the clipboard history. Type to search.
4. Select a clip and press Enter to copy it, then paste in your application.
5. Right-click the sheep tray icon and choose Quit to close the app completely.

History stays in memory by default and is discarded when you quit. Optional
local encrypted persistence and retention settings are available in Settings.
The application works locally, without cloud synchronization or telemetry.

This executable has a verified, timestamped Authenticode signature. Check
Properties > Digital Signatures on Dolly Paste.exe to inspect the publisher.
Windows SmartScreen may still warn about a newly released application.
Verified publisher certificate subject: $ExpectedSignerSubject

Nederlands
----------
Vereist Windows 10 of 11 (32- of 64-bit) met .NET Framework 4.8.
Pak de volledige ZIP uit en open Dolly Paste.exe. Installatie is niet nodig.
Open de geschiedenis met Ctrl+Shift+V, typ om te zoeken en druk op Enter om
een clip te kopieren. Plak die daarna in uw toepassing. Kies Quit via het
schapenpictogram in het systeemvak om de app volledig af te sluiten.
Geschiedenis blijft standaard in het geheugen. Versleuteld lokaal bewaren is
optioneel. De toepassing is digitaal ondertekend met een tijdstempel.
Bekijk de uitgever via Eigenschappen > Digitale handtekeningen van het EXE-bestand.
Windows SmartScreen kan bij nieuwe versies nog een waarschuwing tonen.

Source: https://github.com/sander1993s/Dolly_Paste
Website: https://smetsoftwaresolutions.be/
License: MIT; see LICENSE included in this archive.
Source checkout: $SourceRevision
Dolly Paste.exe SHA256:
$exeHash
"@
[System.IO.File]::WriteAllText((Join-Path $packageDir 'README.txt'), $readme, [System.Text.UTF8Encoding]::new($false))

$archivePath = Join-Path $releaseDir 'dolly-paste-windows.zip'
Compress-Archive -LiteralPath @(
    (Join-Path $packageDir 'Dolly Paste.exe'),
    (Join-Path $packageDir 'LICENSE'),
    (Join-Path $packageDir 'README.txt')
) -DestinationPath $archivePath

Write-Host "Verified signed package: $archivePath"
Write-Host "ZIP SHA256: $((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash)"
