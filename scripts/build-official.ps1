param(
    [string]$TwitchClientId = $env:CS2TWITCHCITIZENS_CLIENT_ID
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\CS2TwitchCitizens.Mod\CS2TwitchCitizens.Mod.csproj'
$ui = Join-Path $root 'src\CS2TwitchCitizens.UI'
$expectedVersion = '0.7.0.0'

function Require-Path([string]$name, [string]$value) {
    if ([string]::IsNullOrWhiteSpace($value) -or -not (Test-Path -LiteralPath $value)) {
        throw "$name is missing: $value. Repair the Cities: Skylines II Code Mod toolchain in the game."
    }
}

$managed = $env:CSII_MANAGEDPATH
$tool = $env:CSII_TOOLPATH
$data = $env:CSII_USERDATAPATH
$localMods = $env:CSII_LOCALMODSPATH
$unityProject = $env:CSII_UNITYMODPROJECTPATH
$processor = $env:CSII_MODPOSTPROCESSORPATH
$mscorlib = $env:CSII_MSCORLIBPATH
$entitiesVersion = $env:CSII_ENTITIESVERSION
$unityVersion = [Environment]::GetEnvironmentVariable('CSII_UNITYVERSION', [EnvironmentVariableTarget]::User)

Require-Path 'CSII_MANAGEDPATH' $managed
Require-Path 'CSII_TOOLPATH' $tool
Require-Path 'Game.dll' (Join-Path $managed 'Game.dll')
Require-Path 'Mod.props' (Join-Path $tool 'Mod.props')
Require-Path 'Mod.targets' (Join-Path $tool 'Mod.targets')
Require-Path 'CSII_USERDATAPATH' $data
Require-Path 'CSII_LOCALMODSPATH' $localMods
Require-Path 'CSII_UNITYMODPROJECTPATH' $unityProject
Require-Path 'CSII_MODPOSTPROCESSORPATH' $processor
Require-Path 'CSII_MSCORLIBPATH' $mscorlib
if ([string]::IsNullOrWhiteSpace($entitiesVersion)) { throw 'CSII_ENTITIESVERSION is missing.' }
$generators = Join-Path $unityProject "Library\PackageCache\com.unity.entities@$entitiesVersion\Unity.Entities\SourceGenerators"
foreach ($generator in @('SystemGenerator.dll', 'SystemGenerator.SystemAPI.dll', 'Unity.Entities.Analyzer.dll')) {
    Require-Path 'Entities SourceGenerators' (Join-Path $generators $generator)
}
$manifest = Get-Content -LiteralPath (Join-Path $unityProject 'Packages\manifest.json') -Raw | ConvertFrom-Json
$burstVersion = $manifest.dependencies.'com.unity.burst'
if ([string]::IsNullOrWhiteSpace($burstVersion)) { throw 'Unity project manifest has no Burst dependency.' }
Require-Path 'Unity Burst' (Join-Path $unityProject "Library\PackageCache\com.unity.burst@$burstVersion")
if ([string]::IsNullOrWhiteSpace($unityVersion)) { throw 'CSII_UNITYVERSION (User) is missing. Set it to the installed Unity editor version.' }
Require-Path 'Unity editor' (Join-Path "C:\Program Files\Unity\Hub\Editor\$unityVersion" 'Editor\Unity.exe')
if ($TwitchClientId -and $TwitchClientId -notmatch '^[a-zA-Z0-9]+$') { throw 'TwitchClientId must contain only letters and digits.' }

$buildArgs = @(
    'build', $project, '-c', 'Debug',
    "-p:ManagedPath=$managed", "-p:CS2ToolPath=$tool", "-p:MSCORLIBPath=$mscorlib",
    "-p:UserDataPath=$data", "-p:LocalModsPath=$localMods",
    "-p:UnityModProjectPath=$unityProject", "-p:ModPostProcessorPath=$processor",
    "-p:EntitiesVersion=$entitiesVersion"
)
if ($TwitchClientId) { $buildArgs += "-p:TwitchClientId=$TwitchClientId" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Official Code Mod build and DeployWIP failed with exit code $LASTEXITCODE." }

$deployedMod = Join-Path $localMods 'CS2TwitchCitizens.Mod\CS2TwitchCitizens.Mod.dll'
Require-Path 'Deployed Code Mod' $deployedMod
$deployedModDir = Split-Path -Parent $deployedMod
foreach ($name in @('CS2TwitchCitizens.Mod.dll', 'CS2TwitchCitizens.Commands.dll', 'CS2TwitchCitizens.Twitch.dll')) {
    $path = Join-Path $deployedModDir $name
    Require-Path "Deployed $name" $path
    $version = [Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString()
    if ($version -ne $expectedVersion) { throw "Deployed $name version is $version, expected $expectedVersion." }
}

Push-Location $ui
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "UI webpack build failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }

$uiDir = Join-Path $data 'Mods\CS2TwitchCitizens.UI'
$uiBundle = Join-Path $uiDir 'CS2TwitchCitizens.UI.mjs'
Require-Path 'Deployed UI bundle' $uiBundle
Require-Path 'Deployed UI stylesheet' (Join-Path $uiDir 'CS2TwitchCitizens.UI.css')
if (-not (Select-String -LiteralPath $uiBundle -Pattern 'Version: 0.7.0' -SimpleMatch -Quiet)) {
    throw 'Deployed UI bundle does not carry the v0.7.0 banner.'
}
$gameAssemblies = Get-ChildItem -LiteralPath $deployedModDir -File |
    Where-Object Name -In @('Game.dll', 'Unity.Entities.dll', 'Unity.Collections.dll', 'Unity.Mathematics.dll')
if ($gameAssemblies) { throw 'Deployed Code Mod contains game assemblies.' }

Write-Host "Code Mod v${expectedVersion}: $deployedModDir"
Write-Host "UI v0.7.0: $uiDir"
