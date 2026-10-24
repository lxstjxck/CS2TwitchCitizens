param(
    [string]$TwitchClientId = $env:CS2TWITCHCITIZENS_CLIENT_ID,
    [string]$RegisteredTwitchClientId = $env:CS2TWITCHCITIZENS_REGISTERED_CLIENT_ID
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\CS2TwitchCitizens.Mod\CS2TwitchCitizens.Mod.csproj'
$ui = Join-Path $root 'src\CS2TwitchCitizens.UI'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'Code Mod version must be SemVer major.minor.patch.' }
$stage = Join-Path $root "artifacts\release\$version\CS2TwitchCitizens.Mod"
$releaseRoot = Split-Path -Parent $stage

function Require-File([string]$label, [string]$file) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "$label is missing: $file" }
}

if ($TwitchClientId -notmatch '^[A-Za-z0-9]+$' -or
    [string]::IsNullOrWhiteSpace($RegisteredTwitchClientId) -or
    $TwitchClientId -cne $RegisteredTwitchClientId) {
    throw 'Set TwitchClientId and RegisteredTwitchClientId to the same real, registered public Twitch application Client ID. The script cannot verify Twitch registration remotely.'
}

$managed = $env:CSII_MANAGEDPATH
$tool = $env:CSII_TOOLPATH
$userData = $env:CSII_USERDATAPATH
$localMods = $env:CSII_LOCALMODSPATH
$unityProject = $env:CSII_UNITYMODPROJECTPATH
$processor = $env:CSII_MODPOSTPROCESSORPATH
$mscorlib = $env:CSII_MSCORLIBPATH
$entitiesVersion = $env:CSII_ENTITIESVERSION
$originalUnityVersion = [Environment]::GetEnvironmentVariable('CSII_UNITYVERSION', [EnvironmentVariableTarget]::User)
$unityVersion = $originalUnityVersion
if ([string]::IsNullOrWhiteSpace($unityVersion)) { $unityVersion = $env:CSII_UNITYVERSION }
foreach ($required in @(
    @('Game.dll', (Join-Path $managed 'Game.dll')),
    @('Mod.props', (Join-Path $tool 'Mod.props')),
    @('Mod.targets', (Join-Path $tool 'Mod.targets')),
    @('ModPostProcessor', $processor),
    @('mscorlib', $mscorlib)
)) { Require-File $required[0] $required[1] }
if (-not (Test-Path -LiteralPath $localMods -PathType Container)) { throw 'CSII_LOCALMODSPATH is missing.' }
if (-not (Test-Path -LiteralPath $userData -PathType Container)) { throw 'CSII_USERDATAPATH is missing.' }
if (-not (Test-Path -LiteralPath $unityProject -PathType Container)) { throw 'CSII_UNITYMODPROJECTPATH is missing.' }
if ([string]::IsNullOrWhiteSpace($entitiesVersion)) { throw 'CSII_ENTITIESVERSION is missing.' }
$generators = Join-Path $unityProject "Library\PackageCache\com.unity.entities@$entitiesVersion\Unity.Entities\SourceGenerators"
foreach ($name in @('SystemGenerator.dll', 'SystemGenerator.SystemAPI.dll', 'Unity.Entities.Analyzer.dll')) {
    Require-File 'Entities source generator' (Join-Path $generators $name)
}
$manifest = Get-Content -LiteralPath (Join-Path $unityProject 'Packages\manifest.json') -Raw | ConvertFrom-Json
$burstVersion = $manifest.dependencies.'com.unity.burst'
if ([string]::IsNullOrWhiteSpace($burstVersion) -or
    -not (Test-Path -LiteralPath (Join-Path $unityProject "Library\PackageCache\com.unity.burst@$burstVersion") -PathType Container)) {
    throw 'The installed Unity project Burst package is missing.'
}
if ([string]::IsNullOrWhiteSpace($unityVersion)) { throw 'CSII_UNITYVERSION is missing.' }
Require-File 'Installed Unity editor' (Join-Path "C:\Program Files\Unity\Hub\Editor\$unityVersion" 'Editor\Unity.exe')

# A failed rebuild must not leave a previous verification receipt for this version.
foreach ($name in @('build-receipt.json', 'SHA256SUMS.txt')) {
    $oldRecord = Join-Path $releaseRoot $name
    if (Test-Path -LiteralPath $oldRecord -PathType Leaf) { Remove-Item -LiteralPath $oldRecord -Force }
}

$buildArgs = @(
    'build', $project, '-c', 'Release',
    "-p:ManagedPath=$managed", "-p:CS2ToolPath=$tool", "-p:MSCORLIBPath=$mscorlib",
    "-p:UserDataPath=$userData", "-p:LocalModsPath=$localMods",
    "-p:UnityModProjectPath=$unityProject", "-p:ModPostProcessorPath=$processor",
    "-p:EntitiesVersion=$entitiesVersion", "-p:TwitchClientId=$TwitchClientId"
)
$temporaryUnityVersion = [string]::IsNullOrWhiteSpace($originalUnityVersion)
if ($temporaryUnityVersion) {
    # ModPostProcessor.PathSet reads the User value, rather than the child process value.
    [Environment]::SetEnvironmentVariable('CSII_UNITYVERSION', $unityVersion, [EnvironmentVariableTarget]::User)
}
try {
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "Official Release build, ModPostProcessor, Burst or DeployWIP failed: $LASTEXITCODE" }
}
finally {
    if ($temporaryUnityVersion) {
        [Environment]::SetEnvironmentVariable('CSII_UNITYVERSION', $originalUnityVersion, [EnvironmentVariableTarget]::User)
    }
}
Write-Host 'Build successful: official Release, ModPostProcessor, Burst, DeployWIP.'

# Keep staging inside the workspace. Never remove a game installation or local mod directory.
$expectedStage = [IO.Path]::GetFullPath((Join-Path $root "artifacts\release\$version\CS2TwitchCitizens.Mod"))
if ([IO.Path]::GetFullPath($stage) -cne $expectedStage) { throw 'Unsafe release staging path.' }
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$previousUiOutput = $env:CS2TC_UI_OUTPUT_DIR
$env:CS2TC_UI_OUTPUT_DIR = $stage
Push-Location $ui
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "Production UI build failed: $LASTEXITCODE" }
}
finally {
    Pop-Location
    $env:CS2TC_UI_OUTPUT_DIR = $previousUiOutput
}

$deployed = Join-Path $localMods 'CS2TwitchCitizens.Mod'
$requiredFiles = @(
    'CS2TwitchCitizens.Mod.dll', 'CS2TwitchCitizens.Commands.dll', 'CS2TwitchCitizens.Twitch.dll',
    'CS2TwitchCitizens.Mod_win_x86_64.dll', 'CS2TwitchCitizens.Mod_linux_x86_64.so',
    'CS2TwitchCitizens.Mod_mac_x86_64.bundle'
)
foreach ($name in $requiredFiles) {
    $source = Join-Path $deployed $name
    Require-File "DeployWIP output $name" $source
    Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name)
}

$thumbnail = Join-Path $root 'src\CS2TwitchCitizens.Mod\Properties\Thumbnail.png'
if (Test-Path -LiteralPath $thumbnail -PathType Leaf) {
    $properties = Join-Path $stage 'Properties'
    New-Item -ItemType Directory -Path $properties -Force | Out-Null
    Copy-Item -LiteralPath $thumbnail -Destination (Join-Path $properties 'Thumbnail.png')
}
$screenshots = Join-Path $root 'src\CS2TwitchCitizens.Mod\Properties\Screenshots'
if (Test-Path -LiteralPath $screenshots -PathType Container) {
    $images = @(Get-ChildItem -LiteralPath $screenshots -File | Where-Object Extension -In @('.png', '.jpg', '.jpeg'))
    if ($images.Count) {
        $target = Join-Path $stage 'Properties\Screenshots'
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        foreach ($file in $images) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $target $file.Name) }
    }
}

$receipt = [ordered]@{
    version = $version
    configuration = 'Release'
    confirmedClientId = $true
    publicClientId = $TwitchClientId
    source = 'Official Mod.targets DeployWIP and production webpack'
    createdUtc = [DateTime]::UtcNow.ToString('o')
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'build-receipt.json') -Encoding UTF8
& (Join-Path $PSScriptRoot 'verify-release.ps1') -StageDir $stage
if ($LASTEXITCODE -ne 0) { throw 'Release package verification failed.' }
Write-Host "Release staging: $stage"
