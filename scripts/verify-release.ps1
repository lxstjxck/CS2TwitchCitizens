param(
    [string]$StageDir,
    [switch]$ForPublishing,
    [switch]$CheckManifest
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$configPath = Join-Path $root 'src\CS2TwitchCitizens.Mod\Properties\PublishConfiguration.xml'
[xml]$config = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8
$version = [string]$config.Publish.ModVersion.Value
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'PublishConfiguration ModVersion must be SemVer major.minor.patch.' }
if ([string]::IsNullOrWhiteSpace($StageDir)) {
    $StageDir = Join-Path $root "artifacts\release\$version\CS2TwitchCitizens.Mod"
}
$stage = [IO.Path]::GetFullPath($StageDir)
$expectedStage = [IO.Path]::GetFullPath((Join-Path $root "artifacts\release\$version\CS2TwitchCitizens.Mod"))
if ($stage -cne $expectedStage) { throw "Unexpected staging directory: $stage" }
if (-not (Test-Path -LiteralPath $stage -PathType Container)) { throw "Build has not produced a release package: $stage" }

$required = @(
    'CS2TwitchCitizens.Mod.dll', 'CS2TwitchCitizens.Commands.dll', 'CS2TwitchCitizens.Twitch.dll',
    'CS2TwitchCitizens.UI.mjs', 'CS2TwitchCitizens.UI.css',
    'CS2TwitchCitizens.Mod_win_x86_64.dll', 'CS2TwitchCitizens.Mod_linux_x86_64.so',
    'CS2TwitchCitizens.Mod_mac_x86_64.bundle'
)
$allowed = $required + @('CS2TwitchCitizens.UI.mjs.LICENSE.txt', 'Properties/Thumbnail.png')
$errors = New-Object System.Collections.Generic.List[string]
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
$relative = @($files | ForEach-Object { $_.FullName.Substring($stage.Length + 1).Replace('\', '/') })
foreach ($name in $required) {
    if ($relative -cnotcontains $name) { $errors.Add("Missing required file: $name") }
}
foreach ($name in $relative) {
    if ($name -notin $allowed -and $name -notmatch '^Properties/Screenshots/[^/]+\.(png|jpg|jpeg)$') {
        $errors.Add("Unexpected package file: $name")
    }
    if ($name -match '(^|/)(node_modules|bin|obj|Logs?|Saves?)(/|$)' -or
        $name -match '(?i)(credential|token|secret|\.dpapi|\.pdb$|\.map$|\.cs$|\.tsx?$|\.props$|\.targets$|\.json$)') {
        $errors.Add("Forbidden package path: $name")
    }
}
if (@($relative | Sort-Object -Unique).Count -ne $relative.Count) { $errors.Add('Duplicate paths in package.') }
foreach ($name in @('CS2TwitchCitizens.Mod.dll', 'CS2TwitchCitizens.Commands.dll', 'CS2TwitchCitizens.Twitch.dll')) {
    $path = Join-Path $stage $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        try {
            $actual = [Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString()
            if ($actual -ne "$version.0") { $errors.Add("$name has version $actual, expected $version.0") }
        }
        catch { $errors.Add("Cannot read assembly version: $name") }
    }
}

try {
    if ($version -ne '0.9.3') { Write-Warning "Verifying a later release: $version. Confirm compatibility with the installed game." }
    [xml]$codeProject = Get-Content -LiteralPath (Join-Path $root 'src\CS2TwitchCitizens.Mod\CS2TwitchCitizens.Mod.csproj') -Raw -Encoding UTF8
    [xml]$commandsProject = Get-Content -LiteralPath (Join-Path $root 'src\CS2TwitchCitizens.Commands\CS2TwitchCitizens.Commands.csproj') -Raw -Encoding UTF8
    [xml]$twitchProject = Get-Content -LiteralPath (Join-Path $root 'src\CS2TwitchCitizens.Twitch\CS2TwitchCitizens.Twitch.csproj') -Raw -Encoding UTF8
    $uiMetadata = Get-Content -LiteralPath (Join-Path $root 'src\CS2TwitchCitizens.UI\mod.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($item in @($codeProject.Project.PropertyGroup.Version, $commandsProject.Project.PropertyGroup.Version,
        $twitchProject.Project.PropertyGroup.Version, $uiMetadata.version)) {
        if ([string]$item -ne $version) { $errors.Add('Code Mod, helper assembly, UI and publish versions do not match.') }
    }
    if ($uiMetadata.id -ne 'CS2TwitchCitizens.Mod') { $errors.Add('UI module ID must match the Code Mod.') }
    if ($config.Publish.DisplayName.Value -ne 'CS2 Twitch Citizens') { $errors.Add('PublishConfiguration DisplayName is incorrect.') }
    if ($config.Publish.Tag.Value -ne 'Code Mod') { $errors.Add('PublishConfiguration tag must be Code Mod.') }
    if ($config.Publish.AccessLevel.Value -notin @('Unlisted', 'Private', 'Public')) { $errors.Add('Unsupported AccessLevel.') }
    if ([string]::IsNullOrWhiteSpace($config.Publish.GameVersion.Value)) { $errors.Add('GameVersion is missing.') }
    if ($config.Publish.Thumbnail.Value -ne 'Properties/Thumbnail.png') { $errors.Add('Thumbnail path does not match package layout.') }
    $playerLog = Join-Path $env:CSII_USERDATAPATH 'Player.log'
    if (Test-Path -LiteralPath $playerLog -PathType Leaf) {
        $gameLine = Select-String -LiteralPath $playerLog -Pattern '^Game version:\s*(\S+)' | Select-Object -First 1
        if ($gameLine -and $config.Publish.GameVersion.Value -ne $gameLine.Matches[0].Groups[1].Value) {
            $errors.Add('GameVersion differs from the most recent local Player.log.')
        }
    }
}
catch { $errors.Add("PublishConfiguration.xml is invalid: $_") }

$ui = Join-Path $stage 'CS2TwitchCitizens.UI.mjs'
if (Test-Path -LiteralPath $ui -PathType Leaf) {
    $header = [IO.File]::ReadAllText($ui).Substring(0, [Math]::Min(600, (Get-Item -LiteralPath $ui).Length))
    if ($header -notmatch 'Id: CS2TwitchCitizens\.Mod' -or $header -notmatch ('Version: ' + [regex]::Escape($version))) {
        $errors.Add('UI banner ID/version does not match the Code Mod.')
    }
}
foreach ($file in $files) {
    if ($file.Length -eq 0) { $errors.Add("Empty package file: $($file.Name)") }
    if ($file.Extension -in @('.mjs', '.css', '.txt')) {
        $content = [IO.File]::ReadAllText($file.FullName)
        if ($content -match '(?i)oauth:[a-z0-9]{20,}|"(access_token|refresh_token|client_secret)"\s*:\s*"[^"\s]{12,}"') {
            $errors.Add("Possible embedded credential in $($file.Name)")
        }
    }
    if ($file.Name -in @('CS2TwitchCitizens.Mod.dll', 'CS2TwitchCitizens.Commands.dll', 'CS2TwitchCitizens.Twitch.dll')) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        $ascii = [Text.Encoding]::ASCII.GetString($bytes)
        if ($ascii -match '(?i)oauth:[a-z0-9]{20,}|"(access_token|refresh_token|client_secret)"\s*:\s*"[^"\s]{12,}"') {
            $errors.Add("Possible embedded credential in $($file.Name)")
        }
    }
}

$thumbnail = Join-Path $stage 'Properties\Thumbnail.png'
$hasThumbnail = Test-Path -LiteralPath $thumbnail -PathType Leaf
if ($hasThumbnail) {
    try {
        Add-Type -AssemblyName System.Drawing
        $bitmap = [System.Drawing.Image]::FromFile($thumbnail)
        try {
            if ($bitmap.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid -or
                $bitmap.Width -ne $bitmap.Height -or $bitmap.Width -lt 256 -or
                (Get-Item -LiteralPath $thumbnail).Length -gt 2MB) {
                $errors.Add('Thumbnail must be a square PNG of at least 256 px and at most 2 MiB.')
            }
        }
        finally { $bitmap.Dispose() }
    }
    catch { $errors.Add("Cannot inspect Thumbnail.png: $_") }
}
foreach ($file in $files | Where-Object { $_.FullName.StartsWith((Join-Path $stage 'Properties\Screenshots') + '\', [StringComparison]::OrdinalIgnoreCase) }) {
    if ($file.Name -match '\s' -or $file.Length -gt 2MB) { $errors.Add("Screenshot name or size is invalid: $($file.Name)") }
    try {
        Add-Type -AssemblyName System.Drawing
        $image = [System.Drawing.Image]::FromFile($file.FullName)
        try {
            if ($image.Width -lt 256 -or $image.Height -lt 256) { $errors.Add("Screenshot is too small: $($file.Name)") }
        }
        finally { $image.Dispose() }
    }
    catch { $errors.Add("Cannot inspect screenshot $($file.Name): $_") }
}

$releaseRoot = Split-Path -Parent $stage
$receiptPath = Join-Path $releaseRoot 'build-receipt.json'
$confirmed = $false
if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
    try {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $confirmed = $receipt.version -eq $version -and $receipt.configuration -eq 'Release' -and
            $receipt.confirmedClientId -eq $true -and $receipt.publicClientId -match '^[A-Za-z0-9]+$'
        if ($confirmed) {
            $binary = [IO.File]::ReadAllBytes((Join-Path $stage 'CS2TwitchCitizens.Mod.dll'))
            $pattern = [Text.Encoding]::Unicode.GetBytes([string]$receipt.publicClientId)
            $found = $false
            for ($index = 0; $index -le $binary.Length - $pattern.Length -and -not $found; $index++) {
                if ($binary[$index] -ne $pattern[0]) { continue }
                $match = $true
                for ($part = 1; $part -lt $pattern.Length; $part++) {
                    if ($binary[$index + $part] -ne $pattern[$part]) { $match = $false; break }
                }
                if ($match) { $found = $true }
            }
            if (-not $found) {
                $confirmed = $false
                $errors.Add('Confirmed public Twitch Client ID is not embedded in the release Code Mod DLL.')
            }
        }
    }
    catch { $errors.Add('Build receipt is invalid.') }
}

$hashes = @($files | Sort-Object FullName | ForEach-Object {
    $name = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $name
})
$manifestPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
if ($CheckManifest) {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { $errors.Add('SHA256 manifest is missing.') }
    else {
        $savedHashes = @(Get-Content -LiteralPath $manifestPath -Encoding UTF8)
        if ($savedHashes.Count -ne $hashes.Count -or
            [string]::Join("`n", $savedHashes) -cne [string]::Join("`n", $hashes)) {
            $errors.Add('Release package differs from SHA256 manifest.')
        }
    }
}
elseif ($errors.Count -eq 0) {
    $hashes | Set-Content -LiteralPath $manifestPath -Encoding ASCII
}

if ($errors.Count) {
    foreach ($problem in $errors) { Write-Error $problem }
    throw 'Package verification failed.'
}
Write-Host "Package verified: $stage ($($files.Count) files)."
Write-Host "SHA-256 manifest: $manifestPath"
$ready = $hasThumbnail -and $confirmed
if ($ready) { Write-Host 'Ready for publishing: local prerequisites satisfied; game subscription test remains pending.' }
else {
    if (-not $hasThumbnail) { Write-Warning 'Ready for publishing: NO. Add a real Properties/Thumbnail.png.' }
    if (-not $confirmed) { Write-Warning 'Ready for publishing: NO. Build with a confirmed registered Twitch Client ID.' }
}
if ($ForPublishing -and -not $ready) { throw 'Publication prerequisites are incomplete.' }
