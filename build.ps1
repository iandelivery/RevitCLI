<#
.SYNOPSIS
    Local build script that mirrors the GitHub Release workflow.
    Builds the C# bridge for all Revit versions and the Go client,
    packs the Abstractions NuGet package per Revit version, then
    packages everything into distribution zips.

.DESCRIPTION
    Replicates the three CI jobs locally:
      1. build-bridge  — dotnet build for Revit 2019/2020/2021/2022
      2. build-client  — go vet + go build the CLI
      3. package       — create zips and NuGet packages identical to
                         the release artifacts

    Bridge artifacts are versioned Nice3point-style
    ({RevitYear}.{Major}.{Minor}, e.g. 2022.1.5) and the per-version
    bridge zips are named after that version.

.PARAMETER RevitVersions
    Comma-separated Revit versions to build the bridge for.
    Defaults to "2019,2020,2021,2022".

.PARAMETER SkipBridge
    Skip the bridge build (use existing bridge/dist/ output).

.PARAMETER SkipClient
    Skip the Go client build.

.PARAMETER SkipPackage
    Skip zip packaging (just build).

.PARAMETER SkipVet
    Skip 'go vet' for faster iteration.

.EXAMPLE
    .\build.ps1
    Full build + package, identical to a release.

.EXAMPLE
    .\build.ps1 -SkipBridge
    Build only the Go client and package.

.EXAMPLE
    .\build.ps1 -RevitVersions "2021,2022" -SkipVet
    Build bridge for Revit 2021/2022 only, skip go vet.
#>

param(
    [string]$RevitVersions = "2019,2020,2021,2022",
    [switch]$SkipBridge,
    [switch]$SkipClient,
    [switch]$SkipPackage,
    [switch]$SkipVet
)

$ErrorActionPreference = "Stop"

$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BridgeDir = Join-Path $RootDir "bridge"
$ClientDir = Join-Path $RootDir "client"
$DistDir = Join-Path $RootDir "dist"

# Derive version from git tag or fallback
$buildVersion = "dev"
$gitTag = git describe --tags --always --dirty 2>$null
if ($LASTEXITCODE -eq 0 -and $gitTag) {
    $buildVersion = $gitTag
}
$version = $buildVersion -replace '^v', ''

# Parse Major/Minor/Patch from a clean version tag so the bridge and
# Abstractions versions align with the release tag (mirrors the CI
# workflow). Dirty or missing tags fall back to the csproj defaults.
$major = $null; $minor = $null; $patch = $null
if ($version -match '^(\d+)\.(\d+)(?:\.(\d+))?') {
    $major = $Matches[1]
    $minor = $Matches[2]
    $patch = $Matches[3]
}
# Version overrides passed to dotnet build/pack (-p:Major etc.)
$versionArgs = @()
if ($major) { $versionArgs += "-p:Major=$major" }
if ($minor) { $versionArgs += "-p:Minor=$minor" }
if ($patch) { $versionArgs += "-p:Patch=$patch" }

$versions = $RevitVersions -split ',' | ForEach-Object { $_.Trim() }

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Revit CLI — Local Release Build" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Version      : $version"
Write-Host "Revit versions: $($versions -join ', ')"
Write-Host "Root         : $RootDir"
Write-Host ""

# ---------------------------------------------------------------------------
# 1. Build bridge
# ---------------------------------------------------------------------------
if (-not $SkipBridge) {
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "  [1/3] Building Bridge" -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Yellow

    # Ensure dotnet is available
    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        Write-Host "[ERROR] dotnet CLI not found. Install .NET SDK 6.0/7.0." -ForegroundColor Red
        exit 1
    }
    Write-Host "dotnet version: $(& dotnet --version)" -ForegroundColor Green

    foreach ($v in $versions) {
        $configTag = switch ($v) {
            "2019" { "R19" }
            "2020" { "R20" }
            "2021" { "R21" }
            "2022" { "R22" }
            default { throw "Unsupported Revit version: $v" }
        }
        $buildConfig = "Release $configTag"
        $outputDir = Join-Path $BridgeDir "dist\Revit$v"

        Write-Host ""
        Write-Host "  Building Revit $v ($buildConfig)..." -ForegroundColor Cyan
        & dotnet build (Join-Path $BridgeDir "RevitCliBridge\RevitCliBridge.csproj") `
            -p:Configuration="$buildConfig" `
            -p:Platform=x64 `
            @versionArgs `
            -o $outputDir

        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] Bridge build failed for Revit $v" -ForegroundColor Red
            exit 1
        }

        # Generate version-specific config
        $port = 5000 + ([int]$v - 2018) * 10 + 1
        $configDir = Join-Path $outputDir ".config"
        New-Item -ItemType Directory -Path $configDir -Force | Out-Null
        $setting = @{
            enabled               = $true
            port                  = $port
            auto_port             = $true
            timeout_seconds       = 180
            max_command_queue_size = 100
            allow_raw_execution   = $false
        } | ConvertTo-Json -Depth 10
        Set-Content -Path (Join-Path $configDir "cli_bridge_setting.json") -Value $setting -Encoding UTF8

        Write-Host "  Built Revit $v -> $outputDir (port $port)" -ForegroundColor Green
    }
} else {
    Write-Host "[1/3] Skipping bridge build (-SkipBridge)" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# 2. Build client
# ---------------------------------------------------------------------------
$clientExe = $null

if (-not $SkipClient) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "  [2/3] Building Client" -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Yellow

    # Ensure Go is available
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")
    $goCmd = Get-Command go -ErrorAction SilentlyContinue
    if (-not $goCmd) {
        Write-Host "[ERROR] Go is not installed or not on PATH." -ForegroundColor Red
        exit 1
    }
    Write-Host "Go version: $(& go version)" -ForegroundColor Green

    Push-Location $ClientDir

    if (-not $SkipVet) {
        Write-Host "  Running go vet..." -ForegroundColor Cyan
        & go vet ./...
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] go vet failed" -ForegroundColor Red
            Pop-Location
            exit 1
        }
        Write-Host "  go vet passed." -ForegroundColor Green
    }

    $clientExe = Join-Path $ClientDir "revit-cli.exe"
    $ldFlags = "-s -w -X main.Version=$buildVersion -X 'main.Author=Ian Chan'"

    Write-Host "  Building revit-cli.exe (version: $buildVersion)..." -ForegroundColor Cyan
    $env:GOOS = "windows"
    $env:GOARCH = "amd64"
    $env:CGO_ENABLED = "0"
    & go build -trimpath "-ldflags=$ldFlags" -o $clientExe ./cmd/revit-cli

    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Go build failed" -ForegroundColor Red
        Pop-Location
        exit 1
    }

    $fileSize = [math]::Round((Get-Item $clientExe).Length / 1MB, 2)
    Write-Host "  Built: revit-cli.exe ($fileSize MB)" -ForegroundColor Green

    # Quick smoke test
    & $clientExe --version

    Pop-Location
} else {
    Write-Host "[2/4] Skipping client build (-SkipClient)" -ForegroundColor DarkGray
    # Try to find existing exe
    $clientExe = Join-Path $ClientDir "revit-cli.exe"
    if (-not (Test-Path $clientExe)) {
        # Check skill dir
        $skillExe = Join-Path $ClientDir "skills\revit-cli\revit-cli.exe"
        if (Test-Path $skillExe) {
            $clientExe = $skillExe
        } else {
            Write-Host "[WARN] No existing revit-cli.exe found. Package will be incomplete." -ForegroundColor Yellow
            $clientExe = $null
        }
    }
}

# ---------------------------------------------------------------------------
# 3. Package
# ---------------------------------------------------------------------------
if (-not $SkipPackage) {
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "  [3/3] Packaging" -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Yellow

    if (Test-Path $DistDir) {
        Remove-Item -Recurse -Force $DistDir
    }
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

    # --- Abstractions NuGet package: single, pure-semantic-versioned ---
    # This SDK is netstandard2.0 with no Revit API reference, so the binary
    # is identical across every Revit version. One package (1.6.0) serves all
    # of them — no per-Revit packing, no RevitVersion in the version string.
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        $nugetDir = Join-Path $DistDir "nuget"
        New-Item -ItemType Directory -Path $nugetDir -Force | Out-Null
        $abstractionsProj = Join-Path $BridgeDir "RevitCliBridge.Abstractions\RevitCliBridge.Abstractions.csproj"

        Write-Host "  Packing Abstractions (single, branch-free version)..." -ForegroundColor Cyan
        & dotnet pack $abstractionsProj -c Release -o $nugetDir
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[ERROR] Abstractions pack failed" -ForegroundColor Red
            exit 1
        }

        Get-ChildItem $nugetDir -Filter *.nupkg | ForEach-Object {
            Write-Host "  Created: $($_.Name)" -ForegroundColor Green
        }
    } else {
        Write-Host "[WARN] dotnet not found - skipping Abstractions NuGet package." -ForegroundColor Yellow
    }

    # --- Primary package: revit-cli + bridge bundled ---
    $stagingDir = Join-Path $DistDir "staging\revit-cli"
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    # CLI client
    if ($clientExe -and (Test-Path $clientExe)) {
        Copy-Item -Path $clientExe -Destination (Join-Path $stagingDir "revit-cli.exe") -Force
    }

    # SKILL.md
    $skillMd = Join-Path $ClientDir "skills\revit-cli\SKILL.md"
    if (Test-Path $skillMd) {
        Copy-Item -Path $skillMd -Destination $stagingDir -Force
    }

    # Bridge files
    $bridgeStaging = Join-Path $stagingDir "bridge"
    New-Item -ItemType Directory -Path $bridgeStaging -Force | Out-Null
    foreach ($v in $versions) {
        $srcDir = Join-Path $BridgeDir "dist\Revit$v"
        if (Test-Path $srcDir) {
            $destDir = Join-Path $bridgeStaging "Revit$v"
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            Copy-Item -Path "$srcDir\*" -Destination $destDir -Recurse -Force
            Write-Host "  Added bridge/Revit$v" -ForegroundColor DarkGray
        } else {
            Write-Host "  [WARN] Bridge output not found for Revit $v at $srcDir" -ForegroundColor Yellow
        }
    }

    $fullZip = Join-Path $DistDir "revit-cli-$version.zip"
    Compress-Archive -Path "$stagingDir\*" -DestinationPath $fullZip -Force
    Write-Host "  Created: revit-cli-$version.zip" -ForegroundColor Green

    # --- Client-only package ---
    if ($clientExe -and (Test-Path $clientExe)) {
        $clientStaging = Join-Path $DistDir "staging\revit-cli-client"
        New-Item -ItemType Directory -Path $clientStaging -Force | Out-Null
        Copy-Item -Path $clientExe -Destination (Join-Path $clientStaging "revit-cli.exe") -Force
        if (Test-Path $skillMd) {
            Copy-Item -Path $skillMd -Destination $clientStaging -Force
        }
        $clientZip = Join-Path $DistDir "revit-cli-client-$version.zip"
        Compress-Archive -Path "$clientStaging\*" -DestinationPath $clientZip -Force
        Write-Host "  Created: revit-cli-client-$version.zip" -ForegroundColor Green
    }

    # --- Per-version bridge zips ---
    # Named after the bridge assembly version ({RevitYear}.{Major}.{Minor}),
    # e.g. RevitCliBridge-2022.1.5.zip
    foreach ($v in $versions) {
        $srcDir = Join-Path $BridgeDir "dist\Revit$v"
        if (Test-Path $srcDir) {
            $bridgeDll = Join-Path $srcDir "RevitCliBridge.dll"
            $bridgeVersion = (Get-Item $bridgeDll).VersionInfo.ProductVersion.Split('+')[0]
            $zipName = "RevitCliBridge-$bridgeVersion.zip"
            $zipPath = Join-Path $DistDir $zipName
            Compress-Archive -Path "$srcDir\*" -DestinationPath $zipPath -Force
            Write-Host "  Created: $zipName" -ForegroundColor Green
        }
    }

    # Clean up staging
    Remove-Item -Recurse -Force (Join-Path $DistDir "staging")

    # --- Verify ---
    Write-Host ""
    Write-Host "  Verifying packages..." -ForegroundColor Cyan
    Get-ChildItem -Path $DistDir -Filter *.zip | ForEach-Object {
        $size = [math]::Round($_.Length / 1KB, 1)
        Write-Host "    $($_.Name.PadRight(50)) $size KB" -ForegroundColor DarkGray
    }
    $nugetOutDir = Join-Path $DistDir "nuget"
    if (Test-Path $nugetOutDir) {
        Get-ChildItem -Path $nugetOutDir -Filter *.nupkg | ForEach-Object {
            $size = [math]::Round($_.Length / 1KB, 1)
            Write-Host "    nuget\$($_.Name.PadRight(43)) $size KB" -ForegroundColor DarkGray
        }
    }
} else {
    Write-Host "[4/4] Skipping packaging (-SkipPackage)" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  Build Complete" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Version : $version"
if (-not $SkipPackage) {
    Write-Host "Output  : $DistDir"
    Get-ChildItem $DistDir -Filter *.zip | ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 2)
        Write-Host "  $($_.Name) ($size MB)"
    }
    Get-ChildItem (Join-Path $DistDir "nuget") -Filter *.nupkg -ErrorAction SilentlyContinue | ForEach-Object {
        $size = [math]::Round($_.Length / 1KB, 1)
        Write-Host "  nuget\$($_.Name) ($size KB)"
    }
}
Write-Host ""
