# Paradox Mods release procedure

Current candidate: **0.9.2**. Nothing in the build or verification scripts uploads a mod. The first upload requires a separate `FirstPublish` command and an interactive confirmation. This procedure uses the `Mod.props`, `Mod.targets`, and `ModPublisher.exe` installed with the game; the latter exposes `Publish`, `NewVersion`, and `Update` on this machine.

## One-time setup

1. Install Cities: Skylines II's Code Mod toolchain from the game's Modding options. In a new PowerShell session, check `CSII_MANAGEDPATH`, `CSII_TOOLPATH`, `CSII_LOCALMODSPATH`, `CSII_USERDATAPATH`, `CSII_UNITYMODPROJECTPATH`, `CSII_MODPOSTPROCESSORPATH`, `CSII_MODPUBLISHERPATH`, `CSII_MSCORLIBPATH`, `CSII_ENTITIESVERSION`, and `CSII_UNITYVERSION`.
2. Install the .NET SDK and Node.js 18 or newer. Run `npm ci` in `src/CS2TwitchCitizens.UI` if `node_modules` is absent.
3. Register one **Public** Twitch application for Device Code authorization. Confirm the public Client ID from its developer-console Manage page. Never use or package a Client Secret. The two parameters below must contain that same registered ID; this is a human assertion, not a remote Twitch validation.
4. Add an original, final, square PNG at `src/CS2TwitchCitizens.Mod/Properties/Thumbnail.png` (at least 256 px, at most 2 MiB, no spaces in the filename). Optional original game screenshots belong in `Properties/Screenshots/`. The release checker blocks publication without a thumbnail. Screenshots are optional until there are tested captures from the game.
5. Check `GameVersion` in `Properties/PublishConfiguration.xml` against the current installed game. `1.6.2f1` was read from this machine's `Player.log` on 24 October 2026. Check that `Unlisted` is suitable for the intended testing account. The installed publisher template recognizes `Public`, `Private`, and `Unlisted`; link subscription still needs a real account test.

## Build and verify locally

From the repository root:

```powershell
./scripts/build-release.ps1 -TwitchClientId 'REGISTERED_PUBLIC_CLIENT_ID' -RegisteredTwitchClientId 'REGISTERED_PUBLIC_CLIENT_ID'
./scripts/verify-release.ps1 -CheckManifest
```

The build uses official Release targets, Source Generators, ModPostProcessor, Burst, and DeployWIP, then runs production webpack into `artifacts/release/0.9.2/CS2TwitchCitizens.Mod`. It copies only the three owned managed assemblies, the platform Burst binaries, UI bundle and stylesheet, and optional original artwork. `artifacts/release/0.9.2/SHA256SUMS.txt` records every staged file. `build-receipt.json` is kept outside the upload directory. The checker prints **Build successful**, **Package verified**, and **Ready for publishing** separately. It can verify a package while still refusing publication because the thumbnail or confirmed Client ID is missing. No command here writes a Paradox listing.

The Code Mod's official DeployWIP output is under `CSII_LOCALMODSPATH/CS2TwitchCitizens.Mod`; the local UI development output is under `CSII_USERDATAPATH/Mods/CS2TwitchCitizens.Mod`. They are development locations. The release upload uses only the staged directory above. For a subscription test, close the game and inspect these folders, then run `./scripts/toggle-local-mod.ps1 -Action Disable`; this moves only the known local code and UI folders (including the older `CS2TwitchCitizens.UI` folder) into `artifacts/local-mod-backup`. Run `./scripts/toggle-local-mod.ps1 -Action Restore` afterward. The helper refuses to overwrite either a backup or a newly created local folder. Preserve `%LOCALAPPDATA%/CS2TwitchCitizens/` credentials, command settings, interactive settings, redemption journals, and all city saves.

## First publication

Check the verified file list and metadata, then use this separate command only when a real upload is intended:

```powershell
./scripts/publish-paradox.ps1 -Operation FirstPublish
```

The script requires an empty `ModId`, verifies the staged package and hashes, shows the name, version, access level and upload path, then requires the exact interactive phrase `PUBLISH FirstPublish`. The installed Publisher may ask for Paradox authentication. Do not pass a password in command arguments. After a successful upload, verify the actual listing and copy its numeric ModId into `Properties/PublishConfiguration.xml` before any further operation; commit that metadata update. If the upload's outcome is unclear, inspect the account's listings before retrying `FirstPublish`, so a second card is not created.

## Later updates

For a new binary version, bump the SemVer in the three `.csproj` files, `UI/mod.json`, and `PublishConfiguration.xml`; add the new `<ChangeLog>` to the XML and update `CHANGELOG.md`. Keep the existing numeric `ModId`. Rebuild and check the stage, then pass the version currently visible on the listing:

```powershell
./scripts/build-release.ps1 -TwitchClientId 'REGISTERED_PUBLIC_CLIENT_ID' -RegisteredTwitchClientId 'REGISTERED_PUBLIC_CLIENT_ID'
./scripts/publish-paradox.ps1 -Operation NewVersion -PreviouslyPublishedVersion '0.9.2'
```

`NewVersion` rejects an empty ModId, missing changelog, and a version that is not greater than the reported published version. The command requires `PUBLISH NewVersion` at the last step. Confirm the listing displays the new version after completion.

For listing text or images without a new binary package:

```powershell
./scripts/publish-paradox.ps1 -Operation UpdateMetadata
```

This maps to Publisher `Update` and does not pass a content folder. It requires `PUBLISH UpdateMetadata` and the existing ModId. Verify the listing afterward. Never use `Publish` for an existing ModId.

## Verification and recovery

Use [TESTING.md](TESTING.md) to check subscription, playset activation, Twitch authorization, commands and rewards on a backup city. Confirm the package is loaded through the Paradox subscription, not the local developer copy. If an upload fails, preserve the stage and `SHA256SUMS.txt`, read the Publisher error, and inspect the listing before retrying. A failed or ambiguous `Publish` may already have created a listing. `Update` changes metadata only and cannot repair a binary package; use `NewVersion` with the same ModId for the next binary version.

The real game UI load, Twitch authentication, reward flow, Paradox subscription, and link visibility remain runtime checks. Local build and package verification do not establish that a Paradox Mods card exists or that the subscribed mod works in game.

## Toolchain and layout evidence

The installed `Mod.targets` performs `RunModPostProcessor` and `DeployWIP`; the installed Publisher's help lists `Publish`, `NewVersion`, and `Update`. The installed Code Mod template supplied the `<Publish>` XML field layout used here. [Paradox's code modding diary](https://www.paradoxinteractive.com/games/cities-skylines-ii/modding/dev-diary-3-code-modding) describes the official build and publisher flow. A [published code/UI mod's release notes](https://github.com/Meapy/signature-logistics) describe co-locating the DLL, `.mjs`, and `.css` in one mod folder and the UI loader's `.mjs` discovery. The [CS2 UI mod tutorial](https://github.com/BrokeAssSoftware/cs2-modding-guide/blob/integration/tutorials/first-ui-mod.md) documents matching the UI module ID to the Code Mod assembly name. These sources support the package structure; the exact 0.9.2 bundle still needs an in-game subscription test.
