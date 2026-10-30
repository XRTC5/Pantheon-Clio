<#
.SYNOPSIS
    Builds Clio and optionally launches it.

.EXAMPLE
    ./build.ps1 -Run
#>
[CmdletBinding()]
param(
    [switch]$Run,
    [switch]$Clean,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src/Clio/Clio.csproj'
$dist = Join-Path $root 'dist'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet not found. Install it with: winget install --id Microsoft.DotNet.SDK.9'
}

# A running Clio locks its exe. Ask it to save and exit first (--quit), so
# the last second of typing, still inside the save debounce, is not lost;
# kill only what is left after five seconds.
$running = Get-Process Clio -ErrorAction SilentlyContinue
if ($running) {
    Write-Host 'Asking the running Clio to save and exit...' -ForegroundColor DarkYellow
    $exe = Join-Path $dist 'Clio.exe'
    if (Test-Path $exe) { & $exe --quit }
    $running | ForEach-Object { [void]$_.WaitForExit(5000) }
    Get-Process Clio -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

if ($Clean -and (Test-Path $dist)) { Remove-Item $dist -Recurse -Force }

Write-Host "Building Clio ($Configuration)..." -ForegroundColor Cyan

# Framework-dependent: the WindowsDesktop runtime is already here for Argus.
& dotnet publish $proj `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --output $dist `
    --nologo `
    -p:DebugType=none

if ($LASTEXITCODE -ne 0) { throw "build failed with exit code $LASTEXITCODE" }

$exe = Join-Path $dist 'Clio.exe'
if (-not (Test-Path $exe)) { throw "build reported success but $exe is missing" }
Write-Host "Built: $exe" -ForegroundColor Green

if ($Run) {
    Start-Process $exe
    Start-Sleep -Seconds 3
    $p = Get-Process Clio -ErrorAction SilentlyContinue
    if ($p) {
        $mb = [math]::Round($p.WorkingSet64 / 1MB, 1)
        Write-Host "Clio is up. pid $($p.Id), working set $mb MB" -ForegroundColor Green
    }
    else {
        Write-Warning "Clio exited immediately. Check $env:APPDATA\Clio\clio.log"
    }
}
