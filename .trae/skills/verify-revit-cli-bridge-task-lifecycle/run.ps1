param(
    [string]$Stamp = (Get-Date -Format "yyyyMMdd-HHmmss"),
    [string]$Out = "",
    [switch]$SkipReplay,
    [switch]$SkipModel
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$tests = Join-Path $repo "bridge\RevitCliBridge.Tests"
$out = if ($Out) { $Out } else { Join-Path $repo "dist\verify-evidence\bridge-task-lifecycle-$Stamp" }

try {
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $replayExit = $null
    $tlcExit = $null

    if (-not $SkipReplay) {
        $before = @(git -C $repo status --porcelain -- bridge)
        $src = Join-Path $PSScriptRoot "replay\TaskLifecycleReplayTests.cs"
        $dst = Join-Path $tests "TaskLifecycleReplayTests.cs"
        $replayLog = Join-Path $out "replay.txt"
        try {
            Copy-Item $src $dst
            Push-Location $repo
            try {
                & dotnet test (Join-Path $tests "RevitCliBridge.Tests.csproj") --filter "FullyQualifiedName~TestVerify_" --logger "console;verbosity=normal" 2>&1 | Tee-Object -FilePath $replayLog
                $replayExit = $LASTEXITCODE
            } finally { Pop-Location }
        } finally {
            Remove-Item $dst -Force -ErrorAction SilentlyContinue
        }
        $after = @(git -C $repo status --porcelain -- bridge)
        $added = @($after | Where-Object { $before -notcontains $_ })
        if ($added) { throw "the replay left the bridge tree dirtier than it found it; remove the copied test by hand: $($added -join '; ')" }
        $text = Get-Content $replayLog -Raw
        if ($text -match "error CS\d+|Build FAILED") { throw "the replay test does not compile against the current bridge code (API drift). Read $replayLog" }
        if ($text -match "INCONCLUSIVE") { throw "the replay could not make the two threads overlap on this machine (one core, or fully loaded); rerun it on one with two or more idle cores. Read $replayLog" }
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
        & node --disable-warning=ExperimentalWarning $statechart (Join-Path $PSScriptRoot "TaskLifecycle.tla") --view (Join-Path $PSScriptRoot "view.ts") --out (Join-Path $out "chart") 2>&1 | Tee-Object -FilePath (Join-Path $out "statechart.txt")
        $tlcExit = $LASTEXITCODE
    }

    ""
    "evidence : $out"
    "replay   : $(if ($null -eq $replayExit) { 'skipped' } elseif ($replayExit -eq 0) { 'PASS (property holds on the real code)' } else { 'FAIL (property broken on the real code, or the sequential half does not hold)' })"
    "model    : $(if ($null -eq $tlcExit) { 'skipped' } elseif ($tlcExit -eq 0) { 'PASS (every rule holds)' } elseif ($tlcExit -eq 1) { 'FAIL (a rule breaks, see chart\report.md)' } else { 'COULD NOT RUN' })"
    if ($tlcExit -eq 2) { exit 2 }
    if ($replayExit -eq 1 -or $tlcExit -eq 1) { exit 1 }
    exit 0
} catch {
    "COULD NOT RUN: $($_.Exception.Message)"
    exit 2
}
