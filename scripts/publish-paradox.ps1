param(
    [Parameter(Mandatory=$true)]
    [ValidateSet('Prepare', 'FirstPublish', 'NewVersion', 'UpdateMetadata')]
    [string]$Operation,
    [string]$TwitchClientId = $env:CS2TWITCHCITIZENS_CLIENT_ID,
    [string]$RegisteredTwitchClientId = $env:CS2TWITCHCITIZENS_REGISTERED_CLIENT_ID,
    [string]$PreviouslyPublishedVersion
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$configPath = Join-Path $root 'src\CS2TwitchCitizens.Mod\Properties\PublishConfiguration.xml'

if ($Operation -eq 'Prepare') {
    & (Join-Path $PSScriptRoot 'build-release.ps1') -TwitchClientId $TwitchClientId -RegisteredTwitchClientId $RegisteredTwitchClientId
    if ($LASTEXITCODE -ne 0) { throw 'Release preparation failed.' }
    return
}

[xml]$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
$modId = [string]$config.Publish.ModId.Value
$version = [string]$config.Publish.ModVersion.Value
$stage = Join-Path $root "artifacts\release\$version\CS2TwitchCitizens.Mod"
$displayName = [string]$config.Publish.DisplayName.Value
$access = [string]$config.Publish.AccessLevel.Value
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or $displayName -ne 'CS2 Twitch Citizens') { throw 'Unexpected publish metadata version or name.' }
if ($access -notin @('Unlisted', 'Private', 'Public')) { throw 'Unsupported AccessLevel.' }
if ($config.Publish.Tag.Value -ne 'Code Mod' -or
    [string]::IsNullOrWhiteSpace([string]$config.Publish.ShortDescription.Value) -or
    [string]::IsNullOrWhiteSpace([string]$config.Publish.LongDescription) -or
    [string]::IsNullOrWhiteSpace([string]$config.Publish.GameVersion.Value) -or
    $config.Publish.ExternalLink.Type -ne 'github' -or
    $config.Publish.ExternalLink.Url -ne 'https://github.com/lxstjxck/CS2TwitchCitizens') {
    throw 'PublishConfiguration.xml is incomplete or inconsistent.'
}
if ($Operation -eq 'FirstPublish' -and -not [string]::IsNullOrWhiteSpace($modId)) {
    throw 'FirstPublish requires an empty ModId. Use NewVersion or UpdateMetadata for an existing listing.'
}
if ($Operation -ne 'FirstPublish' -and $modId -notmatch '^[0-9]+$') {
    throw 'This operation needs the real numeric ModId from the first successful publication.'
}
if ($Operation -eq 'NewVersion') {
    if ($PreviouslyPublishedVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        throw 'Pass -PreviouslyPublishedVersion with the version currently on the existing Paradox Mods listing.'
    }
    if ([version]$version -le [version]$PreviouslyPublishedVersion) {
        throw 'NewVersion must be newer than the version already published.'
    }
    if ([string]::IsNullOrWhiteSpace([string]$config.Publish.ChangeLog.Value)) {
        throw 'Add a ChangeLog element to PublishConfiguration.xml before NewVersion.'
    }
}

$thumbnail = Join-Path $root 'src\CS2TwitchCitizens.Mod\Properties\Thumbnail.png'
if (-not (Test-Path -LiteralPath $thumbnail -PathType Leaf)) { throw 'Add a real Properties/Thumbnail.png before publishing.' }
Add-Type -AssemblyName System.Drawing
$image = [System.Drawing.Image]::FromFile($thumbnail)
try {
    if ($image.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid -or
        $image.Width -ne $image.Height -or $image.Width -lt 256 -or
        (Get-Item -LiteralPath $thumbnail).Length -gt 2MB) {
        throw 'Thumbnail must be a square PNG of at least 256 px and at most 2 MiB.'
    }
}
finally { $image.Dispose() }

$publisherPath = $env:CSII_MODPUBLISHERPATH
if (Test-Path -LiteralPath $publisherPath -PathType Container) {
    $publisherPath = Join-Path $publisherPath 'ModPublisher.exe'
}
if (-not (Test-Path -LiteralPath $publisherPath -PathType Leaf)) {
    throw 'Installed ModPublisher.exe not found at CSII_MODPUBLISHERPATH.'
}

$commands = @{ FirstPublish = 'Publish'; NewVersion = 'NewVersion'; UpdateMetadata = 'Update' }
$publisherCommand = $commands[$Operation]
$arguments = @($publisherCommand, $configPath)

if ($Operation -ne 'UpdateMetadata') {
    & (Join-Path $PSScriptRoot 'verify-release.ps1') `
        -StageDir $stage -CheckManifest -ForPublishing
}
elseif (-not (Test-Path -LiteralPath $stage -PathType Container)) {
    throw "Release content folder not found: $stage"
}

# ModPublisher requires a content folder for Update too.
$arguments += @('-c', $stage)

Write-Host "Mod: $displayName"
Write-Host "Version: $version"
Write-Host "Access: $access"
Write-Host "ModId: $(if ($modId) { $modId } else { '(new listing)' })"
Write-Host "Publisher command: $publisherCommand"
if ($Operation -ne 'UpdateMetadata') { Write-Host "Upload directory: $stage" }
else { Write-Host 'Metadata update; content folder supplied as required by ModPublisher.' }
Write-Host 'The installed Publisher may ask for Paradox authentication. No credentials are stored by this script.'
$confirmation = Read-Host "Type PUBLISH $Operation to authorize the network operation"
if ($confirmation -cne "PUBLISH $Operation") { throw 'Publication cancelled: explicit confirmation was not given.' }

Push-Location (Split-Path -Parent (Split-Path -Parent $configPath))
try {
    & $publisherPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "ModPublisher $publisherCommand failed with exit code $LASTEXITCODE." }
}
finally { Pop-Location }
Write-Host 'Publisher command completed. Confirm the listing and ModId on Paradox Mods before treating it as published.'
