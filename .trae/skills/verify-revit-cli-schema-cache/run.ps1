param(
    [string]$Stamp = (Get-Date -Format "yyyyMMdd-HHmmss"),
    [string]$Out = "",
    [switch]$SkipReplay,
    [switch]$SkipModel
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$client = Join-Path $repo "client"
$out = if ($Out) { $Out } else { Join-Path $repo "dist\verify-evidence\schema-cache-$Stamp" }

try {
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $replayExit = $null
    $tlcExit = $null

    if (-not $SkipReplay) {
        $src = Join-Path $PSScriptRoot "replay\verify_schema_cache_replay_test.go"
        $dst = Join-Path $client "internal\client\discovery\verify_schema_cache_replay_test.go"
        $replayLog = Join-Path $out "replay.txt"
        try {
            Copy-Item $src $dst
            Push-Location $client
            try {
                & go test ./internal/client/discovery/ -run "^TestVerify_" -count=1 -v 2>&1 | Tee-Object -FilePath $replayLog
                $replayExit = $LASTEXITCODE
            } finally { Pop-Location }
        } finally {
            Remove-Item $dst -Force -ErrorAction SilentlyContinue
        }
        $dirty = git -C $repo status --porcelain -- client
        if ($dirty) { throw "the client tree is not clean after the replay; remove the copied test by hand: $dirty" }
        $text = Get-Content $replayLog -Raw
        if ($text -match "build failed|undefined:|not enough arguments|has no field or method") {
            throw "the replay test does not compile against the current client code (API drift). Read $replayLog"
        }
    }

    if (-not $SkipModel) {
        $toolsJson = Join-Path $env:LOCALAPPDATA "revit-cli-verify\tools\tools.json"
        if (-not ($env:TLA2TOOLS -and $env:JAVA) -and (Test-Path $toolsJson)) {
            $tools = Get-Content $toolsJson -Raw | ConvertFrom-Json
            if ((Test-Path $tools.jar) -and (Test-Path $tools.java)) {
                $env:TLA2TOOLS = $tools.jar
                $env:JAVA = $tools.java
            }
        }
        $statechart = Join-Path $PSScriptRoot "..\statechart\scripts\statechart.ts"
        & node --disable-warning=ExperimentalWarning $statechart (Join-Path $PSScriptRoot "SchemaCache.tla") --view (Join-Path $PSScriptRoot "view.ts") --out (Join-Path $out "chart") 2>&1 | Tee-Object -FilePath (Join-Path $out "statechart.txt")
        $tlcExit = $LASTEXITCODE
    }

    ""
    "evidence : $out"
    "replay   : $(if ($null -eq $replayExit) { 'skipped' } elseif ($replayExit -eq 0) { 'PASS (property holds on the real code)' } else { 'FAIL (property broken on the real code)' })"
    "model    : $(if ($null -eq $tlcExit) { 'skipped' } elseif ($tlcExit -eq 0) { 'PASS (every rule holds)' } elseif ($tlcExit -eq 1) { 'FAIL (a rule breaks, see report.md)' } else { 'COULD NOT RUN' })"
    if ($tlcExit -eq 2) { exit 2 }
    if ($replayExit -eq 1 -or $tlcExit -eq 1) { exit 1 }
    exit 0
} catch {
    "COULD NOT RUN: $($_.Exception.Message)"
    exit 2
}
