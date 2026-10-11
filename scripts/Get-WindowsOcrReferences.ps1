# Windows OCR is supplied by the OS; these are compile-time SDK/framework references only.
function Get-WindowsOcrReferences {
    param([Parameter(Mandatory = $true)][string]$FrameworkDirectory)

    $metadataRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata'
    $metadata = $null
    if (Test-Path -LiteralPath $metadataRoot) {
        $sdkDirectories = @(Get-ChildItem -LiteralPath $metadataRoot -Directory |
            Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } |
            Sort-Object { [version]$_.Name } -Descending)
        foreach ($sdkDirectory in $sdkDirectories) {
            $candidate = Join-Path $sdkDirectory.FullName 'Windows.winmd'
            if (Test-Path -LiteralPath $candidate) { $metadata = $candidate; break }
        }
    }
    if (-not $metadata) { throw 'Windows OCR builds require the Windows 10/11 SDK (Windows.winmd). Install the SDK and rerun the build.' }

    $targetingRoot = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
    $references = @(
        $metadata,
        (Join-Path $FrameworkDirectory 'System.Runtime.WindowsRuntime.dll'),
        (Join-Path $targetingRoot 'System.Runtime.dll'),
        (Join-Path $targetingRoot 'System.Runtime.InteropServices.WindowsRuntime.dll')
    )
    foreach ($reference in $references) {
        if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) {
            throw "Missing OCR build reference: $reference. Install the .NET Framework 4.8 Developer Pack."
        }
        "/r:$reference"
    }
    # Windows.winmd's System.Runtime 4.0 contract is supplied by the .NET 4.8 facade.
    '/nowarn:1701'
}
