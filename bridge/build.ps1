<#
.SYNOPSIS
    Automated build and deployment script for the RevitCliBridge add-in.

.DESCRIPTION
    This script performs the following steps:
    1. Detects all installed Revit versions on the system that are officially
       supported by the add-in (2019, 2020, 2021, 2022).
    2. For each detected supported version, builds the add-in using the
       corresponding MSBuild configuration (e.g. "Release R22").
    3. Copies all necessary add-in files (compiled DLLs, .addin manifest,
       and configuration) to the appropriate Revit add-ins directory.

    The script can also build all supported versions regardless of whether
    Revit is installed, producing a staged output directory suitable for
    packaging and distribution.

.PARAMETER Deploy
    When specified, copies built files to the Revit add-ins directories
    for each detected installed version. Without this flag, the script
    only builds to the staged output directory.

.PARAMETER Versions
    Build only the specified Revit versions (comma-separated).
    Example: -Versions "2021,2022"
    Default: all supported versions (2019, 2020, 2021, 2022).

.PARAMETER Configuration
    Build configuration prefix. Default: "Release".

.PARAMETER OutputDir
    Staged output root directory. Default: "dist" under the bridge folder.

.PARAMETER Clean
    Remove the output directory before building.

.EXAMPLE
    .\build.ps1
    Builds all supported Revit versions to the staged dist/ directory.

.EXAMPLE
    .\build.ps1 -Deploy
    Builds all versions and deploys to detected Revit installations.

.EXAMPLE
    .\build.ps1 -Versions "2021,2022" -Clean
    Rebuilds only Revit 2021 and 2022 versions from scratch.

.EXAMPLE
    .\build.ps1 -Configuration Debug -Versions "2022"
    Builds Revit 2022 in Debug configuration.
#>

param(
    [switch]$Deploy,
    [string]$Versions = "",
    [string]$Configuration = "Release",
    [string]$OutputDir = "",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
$SupportedVersions = @(2019, 2020, 2021, 2022)
$VersionConfigMap = @{
    2019 = "R19"
    2020 = "R20"
    2021 = "R21"
    2022 = "R22"
}

# ---------------------------------------------------------------------------
# Resolve paths
# ---------------------------------------------------------------------------
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionDir = $ScriptDir
$Solution    = Join-Path $SolutionDir "RevitCliBridge.sln"

if (-not $OutputDir) {
    $OutputDir = Join-Path $SolutionDir "dist"
}

# ---------------------------------------------------------------------------
# Determine which versions to build
# ---------------------------------------------------------------------------
if ($Versions -ne "") {
    $BuildVersions = $Versions -split "," | ForEach-Object { $_.Trim() } | ForEach-Object { [int]$_ }
    foreach ($v in $BuildVersions) {
        if ($v -notin $SupportedVersions) {
            Write-Host "[ERROR] Version $v is not supported. Supported: $($SupportedVersions -join ', ')" -ForegroundColor Red
            exit 1
        }
    }
} else {
    $BuildVersions = $SupportedVersions
}

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  RevitCliBridge — Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Solution     : $Solution"
Write-Host "Output       : $OutputDir"
Write-Host "Config       : $Configuration"
Write-Host "Versions     : $($BuildVersions -join ', ')"
Write-Host "Deploy       : $Deploy"
Write-Host ""

# ---------------------------------------------------------------------------
# 0. Prerequisites: MSBuild / dotnet
# ---------------------------------------------------------------------------
$msbuild = $null

# Try dotnet CLI first (works with .NET SDK-style projects)
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) {
    Write-Host "[INFO] Using dotnet CLI: $(& dotnet --version)" -ForegroundColor Green
} else {
    # Fall back to VSWhere to locate MSBuild
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null
        if ($vsPath) {
            $msbuild = Get-ChildItem "$vsPath\MSBuild\*\Bin\MSBuild.exe" -ErrorAction SilentlyContinue |
                       Sort-Object -Property Name -Descending |
                       Select-Object -First 1
            if ($msbuild) {
                Write-Host "[INFO] Using MSBuild: $($msbuild.FullName)" -ForegroundColor Green
            }
        }
    }

    if (-not $msbuild) {
        Write-Host "[ERROR] Neither 'dotnet' CLI nor MSBuild found." -ForegroundColor Red
        Write-Host "        Install .NET SDK or Visual Studio with MSBuild workload." -ForegroundColor Red
        exit 1
    }
}

# ---------------------------------------------------------------------------
# 1. Clean output directory
# ---------------------------------------------------------------------------
if ($Clean -and (Test-Path $OutputDir)) {
    Write-Host "[1/3] Cleaning output directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $OutputDir
    Write-Host "      Removed: $OutputDir" -ForegroundColor Green
} elseif ($Clean) {
    Write-Host "[1/3] Clean requested but output directory does not exist — skipping." -ForegroundColor DarkGray
} else {
    Write-Host "[1/3] Clean not requested — keeping existing output." -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# 2. Build each version (restore + build paired per version)
# ---------------------------------------------------------------------------
# Each configuration targets a different .NET Framework version (net47 vs
# net48).  We cannot batch-restore all configs first because they share a
# single project.assets.json that gets overwritten.  Instead we let each
# build perform its own restore by NOT passing --no-restore.
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[2/3] Building for each version..." -ForegroundColor Yellow

$projectFile = Join-Path $SolutionDir "RevitCliBridge\RevitCliBridge.csproj"
$buildResults = @{}

foreach ($version in $BuildVersions) {
    $configTag = $VersionConfigMap[$version]
    $buildConfig = "$Configuration $configTag"
    $versionOutput = Join-Path $OutputDir "Revit$version"

    Write-Host ""
    Write-Host "  ---- Revit $version ($buildConfig) ----" -ForegroundColor Cyan

    if ($dotnetCmd) {
        & dotnet build $projectFile -p:Configuration="$buildConfig" -p:Platform=x64 -o "$versionOutput"
    } else {
        & $msbuild.FullName $Solution /t:Restore /p:Configuration="$buildConfig" /v:minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [FAIL] Revit ${version}: restore failed." -ForegroundColor Red
            $buildResults[$version] = $false
            continue
        }
        & $msbuild.FullName $Solution /p:Configuration="$buildConfig" /p:Platform=x64 /p:OutputPath="$versionOutput" /v:minimal /nologo
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [FAIL] Revit $version build failed." -ForegroundColor Red
        $buildResults[$version] = $false
        continue
    }

    # Verify essential output files exist
    $bridgeDll = Join-Path $versionOutput "RevitCliBridge.dll"
    $abstractionsDll = Join-Path $versionOutput "RevitCliBridge.Abstractions.dll"
    $addinManifest = Join-Path $versionOutput "RevitCliBridge.addin"

    $missing = @()
    if (-not (Test-Path $bridgeDll))        { $missing += "RevitCliBridge.dll" }
    if (-not (Test-Path $abstractionsDll))  { $missing += "RevitCliBridge.Abstractions.dll" }
    if (-not (Test-Path $addinManifest))    { $missing += "RevitCliBridge.addin" }

    if ($missing.Count -gt 0) {
        Write-Host "  [FAIL] Revit ${version}: missing output files: $($missing -join ', ')" -ForegroundColor Red
        $buildResults[$version] = $false
        continue
    }

    # Generate version-specific cli_bridge_setting.json with correct port
    $port = 5000 + ($version - 2018) * 10 + 1
    $configDir = Join-Path $versionOutput ".config"
    if (-not (Test-Path $configDir)) {
        New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    }

    $settingJson = @{
        enabled               = $true
        port                  = $port
        auto_port             = $true
        timeout_seconds       = 180
        max_command_queue_size = 100
        allow_raw_execution   = $false
    } | ConvertTo-Json -Depth 10

    $settingPath = Join-Path $configDir "cli_bridge_setting.json"
    Set-Content -Path $settingPath -Value $settingJson -Encoding UTF8

    Write-Host "  [ OK ] Revit $version — port $port" -ForegroundColor Green
    $buildResults[$version] = $true
}

# ---------------------------------------------------------------------------
# 3. Deploy to Revit add-ins directories (optional)
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[3/3] Deployment..." -ForegroundColor Yellow

if (-not $Deploy) {
    Write-Host "      Deploy flag not set — skipping. Use -Deploy to install." -ForegroundColor DarkGray
} else {
    $appData = $env:APPDATA
    if (-not $appData) {
        Write-Host "  [ERROR] APPDATA environment variable not set." -ForegroundColor Red
    } else {
        $addinsRoot = Join-Path $appData "Autodesk\Revit\Addins"

        # Detect installed Revit versions by scanning the addins directory
        $installedVersions = @()
        if (Test-Path $addinsRoot) {
            $dirs = Get-ChildItem -Path $addinsRoot -Directory -ErrorAction SilentlyContinue
            foreach ($dir in $dirs) {
                $v = 0
                if ([int]::TryParse($dir.Name, [ref]$v) -and $v -in $SupportedVersions) {
                    $installedVersions += $v
                }
            }
        }

        # Also check Windows registry for Revit installations
        $regPaths = @(
            "HKLM:\SOFTWARE\Autodesk\Revit",
            "HKLM:\SOFTWARE\WOW6432Node\Autodesk\Revit"
        )
        foreach ($regPath in $regPaths) {
            if (Test-Path $regPath) {
                Get-ChildItem $regPath -ErrorAction SilentlyContinue | ForEach-Object {
                    $v = 0
                    if ([int]::TryParse($_.PSChildName, [ref]$v) -and $v -in $SupportedVersions -and $v -notin $installedVersions) {
                        $installedVersions += $v
                    }
                }
            }
        }

        if ($installedVersions.Count -eq 0) {
            Write-Host "  [WARN] No supported Revit installations detected." -ForegroundColor Yellow
            Write-Host "         Checked: $addinsRoot and registry." -ForegroundColor DarkGray
        } else {
            $installedVersions = $installedVersions | Sort-Object -Unique

            Write-Host "  Detected Revit installations: $($installedVersions -join ', ')" -ForegroundColor Green

            foreach ($version in $installedVersions) {
                # Only deploy if we successfully built this version
                if (-not $buildResults[$version]) {
                    Write-Host "  [SKIP] Revit $version — build failed, skipping deploy." -ForegroundColor Yellow
                    continue
                }

                $sourceDir = Join-Path $OutputDir "Revit$version"
                $targetAddinsDir = Join-Path $addinsRoot "$version"
                $targetBridgeDir = Join-Path $targetAddinsDir "RevitCliBridge"

                # Ensure target directories exist
                if (-not (Test-Path $targetAddinsDir)) {
                    New-Item -ItemType Directory -Path $targetAddinsDir -Force | Out-Null
                }
                if (-not (Test-Path $targetBridgeDir)) {
                    New-Item -ItemType Directory -Path $targetBridgeDir -Force | Out-Null
                }

                # Copy DLLs to RevitCliBridge/ subdirectory
                $dlls = @("RevitCliBridge.dll", "RevitCliBridge.Abstractions.dll")
                foreach ($dll in $dlls) {
                    $src = Join-Path $sourceDir $dll
                    $dst = Join-Path $targetBridgeDir $dll
                    if (Test-Path $src) {
                        Copy-Item -Path $src -Destination $dst -Force
                    }
                }

                # Copy Newtonsoft.Json.dll if present (needed for .NET Framework versions)
                $newtonsoftSrc = Join-Path $sourceDir "Newtonsoft.Json.dll"
                if (Test-Path $newtonsoftSrc) {
                    Copy-Item -Path $newtonsoftSrc -Destination (Join-Path $targetBridgeDir "Newtonsoft.Json.dll") -Force
                }

                # Copy .addin manifest to the version addins root
                $addinSrc = Join-Path $sourceDir "RevitCliBridge.addin"
                $addinDst = Join-Path $targetAddinsDir "RevitCliBridge.addin"
                if (Test-Path $addinSrc) {
                    Copy-Item -Path $addinSrc -Destination $addinDst -Force
                }

                # Copy version-specific config
                $configSrc = Join-Path $sourceDir ".config\cli_bridge_setting.json"
                $configDir = Join-Path $targetBridgeDir ".config"
                if (-not (Test-Path $configDir)) {
                    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
                }
                if (Test-Path $configSrc) {
                    Copy-Item -Path $configSrc -Destination (Join-Path $configDir "cli_bridge_setting.json") -Force
                }

                Write-Host "  [ OK ] Revit $version — deployed to $targetAddinsDir" -ForegroundColor Green
            }
        }
    }
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$allOk = $true
foreach ($version in $BuildVersions) {
    $status = if ($buildResults[$version]) { "OK" } else { "FAIL" }
    $color = if ($buildResults[$version]) { "Green" } else { "Red" }
    Write-Host "  Revit $version : $status" -ForegroundColor $color
    if (-not $buildResults[$version]) { $allOk = $false }
}

Write-Host ""
if ($allOk) {
    Write-Host "All builds succeeded." -ForegroundColor Green
    Write-Host "Output directory: $OutputDir" -ForegroundColor Cyan
    if ($Deploy) {
        Write-Host "Add-in deployed. Restart Revit to activate." -ForegroundColor Yellow
    } else {
        Write-Host "Use -Deploy flag to install into Revit add-ins directories." -ForegroundColor Yellow
    }
} else {
    Write-Host "Some builds failed — review errors above." -ForegroundColor Red
    exit 1
}
