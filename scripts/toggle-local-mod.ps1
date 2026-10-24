param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Disable', 'Restore')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$backupRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts\local-mod-backup'))
$artifactsDir = Join-Path $root 'artifacts'
if ((Test-Path -LiteralPath $artifactsDir) -and
    ((Get-Item -LiteralPath $artifactsDir).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'artifacts is a reparse point; refusing to move local mods outside the workspace.'
}
$localMods = $env:CSII_LOCALMODSPATH
$userData = $env:CSII_USERDATAPATH
if ([string]::IsNullOrWhiteSpace($localMods) -or [string]::IsNullOrWhiteSpace($userData)) {
    throw 'CSII_LOCALMODSPATH and CSII_USERDATAPATH are required.'
}
if (Get-Process -Name 'Cities2' -ErrorAction SilentlyContinue) { throw 'Close Cities: Skylines II before changing local mod folders.' }

$folders = @(
    @{ Key = 'code'; Path = (Join-Path $localMods 'CS2TwitchCitizens.Mod') },
    @{ Key = 'ui-combined'; Path = (Join-Path $userData 'Mods\CS2TwitchCitizens.Mod') },
    @{ Key = 'ui-legacy'; Path = (Join-Path $userData 'Mods\CS2TwitchCitizens.UI') }
)
$localModsRoot = [IO.Path]::GetFullPath($localMods).TrimEnd('\')
$userModsRoot = [IO.Path]::GetFullPath((Join-Path $userData 'Mods')).TrimEnd('\')
$moves = @()
foreach ($entry in $folders) {
    $original = [IO.Path]::GetFullPath($entry.Path)
    $backup = [IO.Path]::GetFullPath((Join-Path $backupRoot $entry.Key))
    $approvedRoot = if ($entry.Key -eq 'code') { $localModsRoot } else { $userModsRoot }
    if (-not $original.StartsWith($approvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe local mod path.'
    }
    if (-not $backup.StartsWith($backupRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe backup path.'
    }
    if ($Action -eq 'Disable') {
        if (-not (Test-Path -LiteralPath $original -PathType Container)) { continue }
        if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
        $moves += @{ From = $original; To = $backup; Label = 'Temporarily disabled' }
    }
    else {
        if (-not (Test-Path -LiteralPath $backup -PathType Container)) { continue }
        if (Test-Path -LiteralPath $original) { throw "Local mod folder already exists: $original. Inspect it before restoring." }
        $moves += @{ From = $backup; To = $original; Label = 'Restored' }
    }
}
if ($Action -eq 'Disable' -and $moves.Count) { New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null }
foreach ($move in $moves) {
    Move-Item -LiteralPath $move.From -Destination $move.To
    Write-Host "$($move.Label): $($move.From) -> $($move.To)"
}
Write-Host 'No credentials, command settings, redemption journals or city saves were moved.'
