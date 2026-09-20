$ErrorActionPreference = "Stop"

# Resolve project root parent of PSScriptRoot
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $projectRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Dolly Paste Integration Test Runner   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Locate .NET Framework v4 csc.exe compiler
$cscCandidates = @(
    "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:SystemRoot\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$csc = $null
foreach ($cand in $cscCandidates) {
    if (Test-Path -LiteralPath $cand) {
        $csc = $cand
        break
    }
}

if (-not $csc) {
    Write-Error "Could not locate .NET Framework 4.0 csc.exe compiler."
    exit 1
}

Write-Host "Found C# compiler: $csc" -ForegroundColor Gray

# 2. Locate WPF reference assemblies by full absolute paths under selected framework
$cscDir = Split-Path -Parent $csc
$wpfDir = Join-Path $cscDir "WPF"
if (-not (Test-Path -LiteralPath $wpfDir)) {
    if (Test-Path -LiteralPath "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\WPF") {
        $wpfDir = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\WPF"
    } elseif (Test-Path -LiteralPath "$env:SystemRoot\Microsoft.NET\Framework\v4.0.30319\WPF") {
        $wpfDir = "$env:SystemRoot\Microsoft.NET\Framework\v4.0.30319\WPF"
    } else {
        Write-Error "Could not locate .NET Framework WPF reference assembly directory."
        exit 1
    }
}

$refUIAuthClient = Join-Path $wpfDir "UIAutomationClient.dll"
$refUIAuthTypes = Join-Path $wpfDir "UIAutomationTypes.dll"
$refWindowsBase = Join-Path $wpfDir "WindowsBase.dll"

foreach ($wpfRef in @($refUIAuthClient, $refUIAuthTypes, $refWindowsBase)) {
    if (-not (Test-Path -LiteralPath $wpfRef)) {
        Write-Error "Required WPF reference assembly not found: $wpfRef"
        exit 1
    }
}

# 3. Allocate unique scoped temporary directory OUTSIDE repository tree
$tempRoot = [System.IO.Path]::GetTempPath()
$tempDir = Join-Path $tempRoot ("DollyPaste_Tests_" + [System.Guid]::NewGuid().ToString("N"))
$tempDirFull = [System.IO.Path]::GetFullPath($tempDir)
$tempRootFull = [System.IO.Path]::GetFullPath($tempRoot)

if (-not $tempDirFull.StartsWith($tempRootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    Write-Error "Security verification failed: test temp directory is not inside system temp root."
    exit 1
}

# Use [System.IO.Directory]::CreateDirectory instead of New-Item
[System.IO.Directory]::CreateDirectory($tempDirFull) | Out-Null
Write-Host "Scoped test directory: $tempDirFull" -ForegroundColor Gray

$commonRefs = @(
    "/r:System.dll",
    "/r:System.Core.dll",
    "/r:System.Drawing.dll",
    "/r:System.Windows.Forms.dll",
    "/r:System.Security.dll",
    "/r:System.Runtime.Serialization.dll",
    "/r:$refUIAuthClient",
    "/r:$refUIAuthTypes",
    "/r:$refWindowsBase"
)

$srcDir = Join-Path $projectRoot "src"
$testsDir = Join-Path $projectRoot "tests"

try {
    # 4. Suite 1: CoreTests (Required)
    Write-Host "`n--- [1/3] CoreTests Suite ---" -ForegroundColor Cyan

    $coreSrcFile = Join-Path $srcDir "Core.cs"
    if (-not (Test-Path -LiteralPath $coreSrcFile)) {
        Write-Error "Required source file missing: $coreSrcFile"
        exit 1
    }

    $coreTestFile = Join-Path $testsDir "CoreTests.cs"
    if (-not (Test-Path -LiteralPath $coreTestFile)) {
        Write-Error "Required Core test suite file not found: $coreTestFile"
        exit 1
    }

    $coreExe = Join-Path $tempDirFull "CoreTests.exe"
    $coreArgs = @(
        "/nologo",
        "/target:exe",
        "/out:$coreExe"
    ) + $commonRefs + @($coreSrcFile, $coreTestFile)

    Write-Host "Compiling CoreTests..." -ForegroundColor Gray
    & $csc @coreArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $coreExe)) {
        Write-Error "CoreTests compilation failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }

    Write-Host "Running CoreTests..." -ForegroundColor Gray
    & $coreExe
    if ($LASTEXITCODE -ne 0) {
        Write-Error "CoreTests execution failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
    Write-Host "Passed: CoreTests" -ForegroundColor Green

    # 5. Suite 2: PlatformTests (Required)
    Write-Host "`n--- [2/3] PlatformTests Suite ---" -ForegroundColor Cyan

    $clipSrcFile = Join-Path $srcDir "WindowsClipboard.cs"
    if (-not (Test-Path -LiteralPath $clipSrcFile)) {
        Write-Error "Required source file missing: $clipSrcFile"
        exit 1
    }

    $platformTestFile = Join-Path $testsDir "PlatformTests.cs"
    if (-not (Test-Path -LiteralPath $platformTestFile)) {
        Write-Error "Required Platform test suite file not found: $platformTestFile"
        exit 1
    }

    $platformExe = Join-Path $tempDirFull "PlatformTests.exe"
    $platformArgs = @(
        "/nologo",
        "/target:exe",
        "/out:$platformExe"
    ) + $commonRefs + @($coreSrcFile, $clipSrcFile, $platformTestFile)

    Write-Host "Compiling PlatformTests..." -ForegroundColor Gray
    & $csc @platformArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $platformExe)) {
        Write-Error "PlatformTests compilation failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }

    Write-Host "Running PlatformTests..." -ForegroundColor Gray
    & $platformExe
    if ($LASTEXITCODE -ne 0) {
        Write-Error "PlatformTests execution failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
    Write-Host "Passed: PlatformTests" -ForegroundColor Green

    # 6. Suite 3: UiTests (Desktop UI & Layout Verification)
    $uiTestFile = Join-Path $testsDir "UiTests.cs"
    if (-not (Test-Path -LiteralPath $uiTestFile)) {
        Write-Error "Required UI test suite file not found: $uiTestFile"
        exit 1
    }

    Write-Host "`n--- [3/3] UiTests Suite ---" -ForegroundColor Cyan

    $allSrcFiles = Get-ChildItem -Path $srcDir -Filter "*.cs" | ForEach-Object { $_.FullName }
    $manifestPath = Join-Path $projectRoot "app.manifest"
    $uiExe = Join-Path $tempDirFull 'UiTests.exe'
    $uiArgs = @(
        "/nologo",
        "/target:exe",
        "/main:DollyPaste.Tests.UiTests",
        "/out:$uiExe"
    )
    if (Test-Path -LiteralPath $manifestPath) {
        $uiArgs += "/win32manifest:$manifestPath"
    }
    $uiArgs += $commonRefs + $allSrcFiles + @($uiTestFile)

    Write-Host "Compiling UiTests..." -ForegroundColor Gray
    & $csc @uiArgs
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $uiExe)) {
        Write-Error "UiTests compilation failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }

    Write-Host "Running UiTests..." -ForegroundColor Gray
    & $uiExe
    if ($LASTEXITCODE -ne 0) {
        Write-Error "UiTests execution failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
    Write-Host "Passed: UiTests" -ForegroundColor Green

    Write-Host "`nAll test suites passed successfully." -ForegroundColor Green
}
finally {
    # Clean up scoped temporary directory using -LiteralPath
    if ($tempDirFull -and (Test-Path -LiteralPath $tempDirFull)) {
        Remove-Item -LiteralPath $tempDirFull -Recurse -Force -ErrorAction SilentlyContinue
    }
}
