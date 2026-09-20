$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Building Dolly Paste (WinForms 4.8)   " -ForegroundColor Cyan
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

# 3. Ensure dist directory exists
$distDir = Join-Path $scriptDir "dist"
if (-not (Test-Path -LiteralPath $distDir)) {
    [System.IO.Directory]::CreateDirectory($distDir) | Out-Null
}

# 4. Generate dist/sheep.ico locally using System.Drawing without leaking HICON
$icoPath = Join-Path $distDir "sheep.ico"
$bmp = $null
$g = $null
$hIcon = [System.IntPtr]::Zero
$icon = $null
$fs = $null

try {
    Add-Type -AssemblyName System.Drawing
    if (-not ([System.Management.Automation.PSTypeName]'DollyPasteBuild.NativeMethods').Type) {
        Add-Type -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)] public static extern bool DestroyIcon(System.IntPtr hIcon);' -Name "NativeMethods" -Namespace "DollyPasteBuild"
    }

    $bmp = New-Object System.Drawing.Bitmap 32, 32
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 4 Little sturdy legs
    $backLegBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(46, 54, 64))
    $frontLegBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 36, 40))
    $g.FillRectangle($backLegBrush, 10, 24, 2, 4)
    $g.FillRectangle($frontLegBrush, 13, 24, 2, 4)
    $g.FillRectangle($frontLegBrush, 17, 24, 2, 4)
    $g.FillRectangle($backLegBrush, 20, 24, 2, 4)
    $backLegBrush.Dispose()
    $frontLegBrush.Dispose()

    # Wool body lobes
    $woolBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(250, 248, 245))
    $g.FillEllipse($woolBrush, 4, 10, 24, 16)
    $g.FillEllipse($woolBrush, 7, 7, 18, 14)
    $woolBrush.Dispose()

    # Curled Merino horns
    $hornBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(212, 178, 118))
    $g.FillEllipse($hornBrush, 2, 9, 7, 7)
    $g.FillEllipse($hornBrush, 23, 9, 7, 7)
    $hornBrush.Dispose()

    # Sheep face
    $faceBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(234, 219, 200))
    $g.FillEllipse($faceBrush, 11, 11, 10, 12)
    $faceBrush.Dispose()

    # Crown tuft
    $crownBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(250, 248, 245))
    $g.FillEllipse($crownBrush, 12, 8, 8, 5)
    $crownBrush.Dispose()

    # Eyes
    $eyeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(32, 36, 40))
    $g.FillEllipse($eyeBrush, 13, 14, 2, 2)
    $g.FillEllipse($eyeBrush, 17, 14, 2, 2)
    $eyeBrush.Dispose()

    $g.Flush()

    $hIcon = $bmp.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($hIcon)
    $fs = [System.IO.File]::OpenWrite($icoPath)
    $icon.Save($fs)
    Write-Host "Generated icon: $icoPath" -ForegroundColor Gray
} catch {
    Write-Warning "Failed to generate sheep.ico: $_. Compiling without win32icon."
    $icoPath = $null
} finally {
    if ($fs) {
        $fs.Close()
        $fs.Dispose()
    }
    if ($icon) {
        $icon.Dispose()
    }
    if ($hIcon -ne [System.IntPtr]::Zero) {
        [DollyPasteBuild.NativeMethods]::DestroyIcon($hIcon) | Out-Null
    }
    if ($g) {
        $g.Dispose()
    }
    if ($bmp) {
        $bmp.Dispose()
    }
}

# 5. Gather source files under src/
$srcFiles = Get-ChildItem -Path (Join-Path $scriptDir "src") -Filter "*.cs" | ForEach-Object { $_.FullName }
if ($srcFiles.Count -eq 0) {
    Write-Error "No C# source files found in src/."
    exit 1
}

$outputExe = Join-Path $distDir "Dolly Paste.exe"
$manifestPath = Join-Path $scriptDir "app.manifest"

# 6. Build compiler arguments array with full WPF assembly paths
$cscArgs = @(
    "/nologo",
    "/target:winexe",
    "/optimize+",
    "/out:$outputExe",
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

if (Test-Path -LiteralPath $manifestPath) {
    $cscArgs += "/win32manifest:$manifestPath"
}

if ($icoPath -and (Test-Path -LiteralPath $icoPath)) {
    $cscArgs += "/win32icon:$icoPath"
}

foreach ($src in $srcFiles) {
    $cscArgs += $src
}

Write-Host "Compiling $($srcFiles.Count) files into '$outputExe'..." -ForegroundColor Cyan

& $csc @cscArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Compilation failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "Build complete: $outputExe" -ForegroundColor Green
