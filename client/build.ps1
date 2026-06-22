<#
.SYNOPSIS
    Automated build script for the RevitCliClientGo project.
    Builds the Go binary and packages it as an Open Skill protocol-compliant skill.

.DESCRIPTION
    This script performs the following steps:
    1. Executes the complete Go project build process (go build + go vet).
    2. On successful build, creates the skill directory at .trae/skills/revit-cli/.
    3. Moves the built revit-cli.exe executable into the skill directory.
    4. Generates the SKILL.md companion file per the Open Skill protocol spec.

.PARAMETER SkillName
    The skill identifier used for the directory name and SKILL.md frontmatter.
    Defaults to "revit-cli".

.PARAMETER SkipVet
    Skip the 'go vet' static analysis step (faster builds during iteration).

.EXAMPLE
    .\build-skill.ps1
    Builds and packages the skill with default settings.

.EXAMPLE
    .\build-skill.ps1 -SkillName "revit-cli-bridge" -SkipVet
    Builds with a custom skill name, skipping vet.
#>

param(
    [string]$SkillName = "revit-cli",
    [switch]$SkipVet
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# 0. Resolve paths and ensure Go is available
# ---------------------------------------------------------------------------
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = $ScriptDir                       # RevitCliClientGo/
$SkillDir    = Join-Path $ProjectRoot "\skills\$SkillName"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  RevitCliClientGo — Skill Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Project root : $ProjectRoot"
Write-Host "Skill dir    : $SkillDir"
Write-Host ""

# Ensure Go is on PATH (refresh from machine/user environment)
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("Path", "User")

$goCmd = Get-Command go -ErrorAction SilentlyContinue
if (-not $goCmd) {
    Write-Host "[ERROR] Go is not installed or not on PATH." -ForegroundColor Red
    Write-Host "        Install from https://go.dev/dl/ or run: winget install GoLang.Go"
    exit 1
}

Write-Host "[1/5] Go version: $(& go version)" -ForegroundColor Green
Set-Location $ProjectRoot

# ---------------------------------------------------------------------------
# 1. Build: go vet (optional)
# ---------------------------------------------------------------------------
if (-not $SkipVet) {
    Write-Host ""
    Write-Host "[2/5] Running go vet..." -ForegroundColor Yellow
    & go vet ./...
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] go vet failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
    Write-Host "      go vet passed." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "[2/5] Skipping go vet (-SkipVet)" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# 2. Build: go build
# ---------------------------------------------------------------------------
$exeName = "revit-cli.exe"
$buildOutput = Join-Path $ProjectRoot $exeName

Write-Host ""
Write-Host "[3/5] Building Go binary..." -ForegroundColor Yellow
& go build -o $buildOutput ./cmd/revit-cli
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ERROR] go build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $buildOutput)) {
    Write-Host "[ERROR] Build succeeded but executable not found at $buildOutput" -ForegroundColor Red
    exit 1
}

$fileSize = (Get-Item $buildOutput).Length
Write-Host "      Built: $exeName ($([math]::Round($fileSize / 1MB, 2)) MB)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 3. Create skill directory
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[4/5] Creating skill directory..." -ForegroundColor Yellow

if (Test-Path $SkillDir) {
    Write-Host "      Removing existing skill directory..." -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $SkillDir
}

New-Item -ItemType Directory -Path $SkillDir -Force | Out-Null
Write-Host "      Created: $SkillDir" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 4. Move executable into skill directory
# ---------------------------------------------------------------------------
Write-Host "      Moving $exeName into skill directory..." -ForegroundColor DarkGray
Move-Item -Path $buildOutput -Destination (Join-Path $SkillDir $exeName) -Force
Write-Host "      Moved: $exeName -> $SkillDir" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 5. Generate SKILL.md per Open Skill protocol
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "[5/5] Generating SKILL.md (Open Skill protocol)..." -ForegroundColor Yellow

$skillMdPath = Join-Path $SkillDir "SKILL.md"
$templatePath = Join-Path $ProjectRoot "skill-template.md"

if (-not (Test-Path $templatePath)) {
    Write-Host "[ERROR] Template file not found: $templatePath" -ForegroundColor Red
    exit 1
}

# Read template and replace placeholders
$skillContent = Get-Content -Path $templatePath -Raw -Encoding UTF8
$skillContent = $skillContent -replace '{{SkillName}}', $SkillName

Set-Content -Path $skillMdPath -Value $skillContent -Encoding UTF8
Write-Host "      Generated: SKILL.md" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build Complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Skill directory : $SkillDir"
Write-Host ""
Write-Host "Contents:" -ForegroundColor Cyan
Get-ChildItem $SkillDir | ForEach-Object {
    $size = if ($_.PSIsContainer) { "<DIR>" } else { "$([math]::Round($_.Length / 1KB, 1)) KB" }
    Write-Host "  $($_.Name.PadRight(20)) $size"
}
Write-Host ""
Write-Host "Skill is ready for use. Test with:" -ForegroundColor Yellow
Write-Host "  & '$SkillDir\$exeName' --help"
Write-Host ""
