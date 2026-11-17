[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers2\Game2',
    [string]$PluginPath = 'C:\SE2Mods\BuildPlanner\BuildPlanner.dll',
    [switch]$Install,
    [string]$Rollback
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-GameClosed {
    if (Get-Process -Name 'SpaceEngineers2' -ErrorAction SilentlyContinue) {
        throw 'Close Space Engineers 2 before installing or rolling back. No process will be stopped automatically.'
    }
}
function Confirm-Replace([string]$Source) {
    Assert-GameClosed
    Write-Host "Source: $Source"
    Write-Host "Destination: $PluginPath"
    Write-Host "SHA256: $((Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash)"
    if ((Read-Host 'Type INSTALL to approve replacing the installed plugin') -cne 'INSTALL') {
        throw 'Installation cancelled; installed plugin unchanged.'
    }
    Assert-GameClosed
}

$root = $PSScriptRoot
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$backup = Join-Path $root "patch-backups\update-$stamp"

if ($Rollback) {
    if ($Install) { throw 'Use either -Install or -Rollback.' }
    $candidate = (Resolve-Path -LiteralPath $Rollback).Path
    Confirm-Replace $candidate
} else {
    # Source snapshot excludes generated outputs and the backup tree itself.
    New-Item -ItemType Directory -Path (Join-Path $backup 'source') -Force | Out-Null
    foreach ($folder in @('BuildPlanner', 'BuildPlanner.Tests', 'notes', 'packaging', 'tools')) {
        $base = Join-Path $root $folder
        foreach ($file in Get-ChildItem -LiteralPath $base -File -Recurse) {
            $relative = $file.FullName.Substring($root.Length + 1)
            if ($relative -match '(^|[\\/])(bin|obj)([\\/]|$)') { continue }
            $copy = Join-Path (Join-Path $backup 'source') $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $copy) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $copy
        }
    }
    Get-ChildItem -LiteralPath $root -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $backup 'source')
    }
    Push-Location $root
    try {
        & dotnet restore BuildPlanner.sln
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
        & dotnet build BuildPlanner\BuildPlanner.csproj --no-restore -c Release "-p:GameDir=$GameDir"
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
        & dotnet test BuildPlanner.Tests\BuildPlanner.Tests.csproj --no-restore -c Release "-p:GameDir=$GameDir"
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    } finally { Pop-Location }
    $candidate = Join-Path $root 'BuildPlanner\bin\BuildPlanner.dll'
    if (Test-Path -LiteralPath $PluginPath) {
        Copy-Item -LiteralPath $PluginPath -Destination (Join-Path $backup 'BuildPlanner.previous.dll')
    }
    Copy-Item -LiteralPath $candidate -Destination (Join-Path $backup 'BuildPlanner.candidate.dll')
    Write-Host "Build and tests passed. Snapshot: $backup"
    if (-not $Install) { return }
    Confirm-Replace $candidate
}

New-Item -ItemType Directory -Path $backup -Force | Out-Null
$previous = Join-Path $backup 'BuildPlanner.before-replacement.dll'
if (-not (Test-Path -LiteralPath $PluginPath)) { throw 'Installed plugin missing; verify -PluginPath.' }
Copy-Item -LiteralPath $PluginPath -Destination $previous
try {
    Assert-GameClosed
    Copy-Item -LiteralPath $candidate -Destination $PluginPath
    if ((Get-FileHash -LiteralPath $candidate).Hash -ne (Get-FileHash -LiteralPath $PluginPath).Hash) {
        throw 'Installed DLL hash verification failed.'
    }
} catch {
    Copy-Item -LiteralPath $previous -Destination $PluginPath
    throw
}
Write-Host "Installed and hash verified. Previous DLL: $previous"
Write-Host 'In-game verification is still required. Use a disposable save with nonvaluable items.'
