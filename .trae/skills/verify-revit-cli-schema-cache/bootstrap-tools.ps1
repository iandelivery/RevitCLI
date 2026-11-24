param(
    [string]$ToolsDir = (Join-Path $env:LOCALAPPDATA "revit-cli-verify\tools")
)
$ErrorActionPreference = "Stop"

$JarVersion = "1.7.4"
$JarSha256 = "936a262061c914694dfd669a543be24573c45d5aa0ff20a8b96b23d01e050e88"
$JarUrl = "https://github.com/tlaplus/tlaplus/releases/download/v$JarVersion/tla2tools.jar"
$JdkVersion = "25.0.5.1"
$JdkUrl = "https://aka.ms/download-jdk/microsoft-jdk-25-windows-x64.zip"

function Resolve-Proxy {
    if ($env:VERIFY_PROXY) { return $env:VERIFY_PROXY }
    $cfg = Join-Path $PSScriptRoot "..\proxy-guide\config.local.json"
    if (Test-Path $cfg) {
        $p = (Get-Content $cfg -Raw | ConvertFrom-Json).proxy
        if ($p) { return $p }
    }
    if ($env:HTTPS_PROXY) { return $env:HTTPS_PROXY }
    return $null
}

function Get-File([string]$url, [string]$dest) {
    if ((Test-Path $dest) -and ((Get-Item $dest).Length -gt 0)) { return }
    $tmp = "$dest.part"
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    Write-Host "downloading $url"
    & curl.exe -fsSL --connect-timeout 20 --max-time 900 -o $tmp $url
    if ($LASTEXITCODE -ne 0) {
        $proxy = Resolve-Proxy
        if (-not $proxy) { throw "direct download failed ($LASTEXITCODE) and no proxy is configured (VERIFY_PROXY, proxy-guide config.local.json, HTTPS_PROXY)" }
        Write-Host "direct download failed; retrying via $proxy"
        & curl.exe -fsSL --retry 2 --connect-timeout 20 --max-time 900 -x $proxy -o $tmp $url
        if ($LASTEXITCODE -ne 0) { throw "download via proxy failed: $url" }
    }
    Move-Item $tmp $dest -Force
}

New-Item -ItemType Directory -Force -Path $ToolsDir | Out-Null
$jar = Join-Path $ToolsDir "tla2tools-$JarVersion.jar"
Get-File $JarUrl $jar
$hash = (Get-FileHash -Algorithm SHA256 $jar).Hash.ToLower()
if ($hash -ne $JarSha256) { throw "tla2tools.jar sha256 $hash does not match the pinned $JarSha256" }

$jdkDir = Join-Path $ToolsDir "jdk-$JdkVersion"
if (-not (Test-Path $jdkDir)) {
    $zip = Join-Path $ToolsDir "microsoft-jdk-$JdkVersion-windows-x64.zip"
    Get-File $JdkUrl $zip
    $extract = Join-Path $ToolsDir "jdk-extract"
    Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -Path $zip -DestinationPath $extract -Force
    $top = Get-ChildItem $extract -Directory | Select-Object -First 1
    Move-Item $top.FullName $jdkDir
    Remove-Item $extract -Recurse -Force
    Remove-Item $zip -Force
}
$java = Get-ChildItem $jdkDir -Recurse -Filter java.exe | Where-Object { $_.DirectoryName -like "*\bin" } | Select-Object -First 1
if (-not $java) { throw "java.exe not found under $jdkDir" }

@{ jar = $jar; java = $java.FullName } | ConvertTo-Json | Set-Content (Join-Path $ToolsDir "tools.json")
Write-Host "tools ready: $ToolsDir"
Write-Host "  jar : $jar"
Write-Host "  java: $($java.FullName)"
