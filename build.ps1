# build.ps1
# Compiles HTPCAVRVolume.  Run directly for iterative builds.
# Usage:
#   .\build.ps1                    # Debug build (default)
#   .\build.ps1 -Configuration Release

param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$RepoRoot  = $PSScriptRoot
$SlnFile   = Join-Path $RepoRoot "HTPCAVRVolume.sln"
$OutputDir = Join-Path $RepoRoot "bin\$Configuration"

Write-Host "Building $Configuration..." -ForegroundColor Cyan

# ── Locate a build tool ───────────────────────────────────────────────────────
$buildTool = $null

# 1. Prefer dotnet SDK (ships with VS 2017+ and standalone .NET SDK)
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $buildTool = "dotnet"
}

# 2. Fall back to msbuild on PATH (Developer Command Prompt)
if (-not $buildTool -and (Get-Command msbuild -ErrorAction SilentlyContinue)) {
    $buildTool = "msbuild"
}

# 3. Try to locate msbuild via vswhere (checks both Program Files locations)
if (-not $buildTool) {
    $vswherePaths = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
    )
    foreach ($vswhere in $vswherePaths) {
        if (Test-Path $vswhere) {
            $vsPath  = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
            $msbuild = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
            if (Test-Path $msbuild) { $buildTool = $msbuild; break }
        }
    }
}

# 4. Hard-coded fallback for known VS2022 Community / BuildTools locations
if (-not $buildTool) {
    $knownPaths = @(
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    foreach ($p in $knownPaths) {
        if (Test-Path $p) { $buildTool = $p; break }
    }
}

if (-not $buildTool) {
    Write-Error "No build tool found. Install Visual Studio or the .NET SDK."
    exit 1
}

# ── Build ─────────────────────────────────────────────────────────────────────
Write-Host "  Tool:   $buildTool"
Write-Host "  Config: $Configuration"
Write-Host "  Output: $OutputDir"
Write-Host ""

if ($buildTool -eq "dotnet") {
    dotnet build $SlnFile -c $Configuration --nologo
} else {
    & $buildTool $SlnFile /p:Configuration=$Configuration /m /nologo /verbosity:minimal
}

# $LASTEXITCODE can be null when the process exits cleanly in some environments
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) {
    Write-Error "Build FAILED (exit $LASTEXITCODE)"
    exit $LASTEXITCODE
}

# Confirm the output file actually exists as a definitive success check
if (-not (Test-Path "$OutputDir\HTPC-AVR-sync.exe")) {
    Write-Error "Build appeared to succeed but exe not found at $OutputDir\HTPC-AVR-sync.exe"
    exit 1
}

Write-Host ""
Write-Host "Build succeeded -> $OutputDir\HTPC-AVR-sync.exe" -ForegroundColor Green
