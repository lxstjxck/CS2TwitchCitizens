# CS2 Twitch Citizens

## Paradox Mods preparation

Version 0.9.7 is a candidate update to the existing Paradox Mods listing (ModId 164740); repository scripts do not publish it automatically. [PUBLISHING.md](PUBLISHING.md) covers the Release build, staged package verification, and updates using the same ModId. [TESTING.md](TESTING.md) is the subscriber test checklist. A registered public Twitch Client ID and an original `Properties/Thumbnail.png` are required before publication. The local development build remains available through `scripts/build-official.ps1`.

CS2 Code Mod that binds Twitch viewers to citizens through EventSub chat commands. Version 0.9.7 offers two fixed Channel Points rewards: **Message to the city** and **City announcement**. The streamer can create, enable or disable them and change their point cost from a compact Channel Points screen. Preview and history have separate sections. Messages appear at the upper right for eight seconds; announcements appear at the center for at most 30 seconds. Both can be dismissed early. The user verified `!join`, `!me`, `!find`, and `!history` replies in game on v0.8.2; both rewards were created successfully through live Twitch Helix in v0.9.5; in-game notification display still needs live verification.

## Channel Points setup

On first connection, grant `channel:manage:redemptions` and `channel:read:redemptions`, in addition to the two chat scopes. Open **Interactive → Channel Points**. The mod checks rewards automatically; set a cost and select **Create reward** only for a genuinely missing fixed reward. The two rewards created in the v0.9.5 live test remain linked and must not be recreated. Changing cost or availability updates the same Twitch reward. Enable Interactive events in the section's Settings tab. The mod receives [custom reward redemption add and update events](https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/#channelchannel_points_custom_reward_redemptionadd) over the existing EventSub connection. Reward IDs are compared exactly and both supported rewards require text. Older celebration, alert, and disaster rules no longer execute. Existing Twitch rewards remain in the channel and should be reviewed manually.

The Interactive tab offers local tests that do not redeem points or call Twitch. Messages render viewer input as text and show reward icons when available. If a viewer has an active binding, a message shows the citizen name and a Find citizen button. One event is displayed at a time in a separate game overlay, including when the control panel is closed. The manager lists pending redemptions for fixed rewards. **Fulfill** requires a persisted acknowledgement that the game UI passed a layout and visibility check; **Cancel and refund** requires Twitch to confirm `CANCELED`. Both manual operations require explicit confirmation. Automatic fulfillment is disabled even for older rules because UI layout alone cannot prove that a person saw the notification. Accepted redemption IDs are written by broadcaster under `%LOCALAPPDATA%/CS2TwitchCitizens/redemptions/`; the old `redemptions.json` is read for migration. Interactive settings remain in `interactive.json` and contain no tokens. Uncertain reward creation is recorded by broadcaster under `reward-create/` for safe reconciliation; no POST retry is attempted. Recent event history is held in memory and resets when the city changes.

Open the in-game **Commands** tab to enable commands, set viewer cooldowns and permissions, select reply fields, choose RU/EN replies, and preview a sample. Settings apply immediately and persist globally in `%LOCALAPPDATA%/CS2TwitchCitizens/commands.json`, separate from city saves and encrypted Twitch credentials. See [command fields and limits](COMMAND_FIELDS.md) for the supported field matrix.

## Register the Twitch application

The project owner must [register one Twitch application](https://dev.twitch.tv/docs/authentication/register-app) in the Twitch developer console and set its client type to **Public** for the [Device Code Flow](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/). Enter the application name and category, and a valid redirect URL if the registration form requires one; Device Code Flow itself does not use that callback. Copy the **Client ID** from the application's Manage page. A client secret must never be included in the mod. The client ID is public and should be embedded in release builds by setting `TwitchClientId` as an MSBuild property, for example `dotnet build src/CS2TwitchCitizens.Mod/CS2TwitchCitizens.Mod.csproj -p:TwitchClientId=YOUR_REGISTERED_CLIENT_ID`. Local development can also use the `CS2TWITCHCITIZENS_CLIENT_ID` environment variable. A release without a real registered Client ID shows an error in the panel and cannot authorize.

## Connect in game

Open **Twitch Citizens → Settings → Connect Twitch**. Copy the displayed code and choose **Open Twitch** to authorize in the system browser. After confirmation, the mod validates the user ID and starts EventSub for that account's own channel. Use **Reconnect** to retry EventSub and **Disconnect Twitch** to stop it and remove local credentials. Disconnect does not remove residents, bindings, or life history from city saves. Loading another city keeps the Twitch account connection and restores that city's bindings.

The mod requests `user:read:chat`, `user:write:chat`, `channel:read:redemptions`, and `channel:manage:redemptions`. Existing read-only Channel Points tokens can still receive events but require reauthorization for reward management. The settings panel never receives access tokens, refresh tokens, device codes, or Authorization headers. It shows the one-time user code, login, connection status, chat sending permission, and separate Channel Points read/manage permissions.

## Persistent diagnostic log (0.9.7 candidate)

The mod now writes a low-overhead diagnostic log automatically at
`%LOCALAPPDATA%\CS2TwitchCitizens\logs\CS2TwitchCitizens.log`.
No environment variable is necessary. Entries include mod startup/shutdown,
city save/restore, Twitch/EventSub reconnects, Channel Points and presentation
results, command outcomes, and a one-minute, anonymous HEALTH heartbeat with
queue depth, drops, viewer count, connectivity, and task count.
The active file is limited to 5 MiB with three rotated archives
(`CS2TwitchCitizens.1.log` to `.3.log`). The writer uses a bounded
background queue; disk I/O failures disable only file logging, never gameplay.
The game logger continues to operate.

Sensitive key=value fields (viewer IDs, Twitch logins, tokens, message text,
coordinates, etc.) and Bearer tokens are redacted in the file. The in-game
`CS2TWITCHCITIZENS_DIAGNOSTICS=1` switch still controls high-volume
performance metrics and citizen scans. Before sharing a log, review it for
personal information: redaction is defensive and cannot guarantee that every
future logging format contains no identifiers. These logs are not game saves;
deleting them does not unbind viewers. Official packaging and in-game testing
of this logging addition are still pending.

## Local data and migration

Tokens are encrypted with Windows DPAPI CurrentUser in `%LOCALAPPDATA%\CS2TwitchCitizens\twitch.credentials.dpapi`, separate from city saves. Only the same Windows user can decrypt them. The old `%LOCALAPPDATA%\CS2TwitchCitizens\twitch.config.json` remains untouched and its plaintext token is no longer used. It can supply a Client ID for a local migration build, but a public release should embed the project's registered Client ID. Once the new connection has worked and the old credentials are no longer needed, the user can remove the legacy file manually.

An expired or invalid access token is refreshed without blocking the game thread. The new refresh token is stored after each rotation. A revoked or unusable refresh token requires a new in-game authorization. Network failure leaves encrypted credentials on disk.

## Build and validation

Run the C# checks with `dotnet run --project tests/CS2TwitchCitizens.Commands.Checks`. From `src/CS2TwitchCitizens.UI`, run `npx tsc --noEmit -p tsconfig.json` and `npm test`.

From the repository root, the **official build, DeployWIP, and UI installation** are:

The build script temporarily sets the user value of `CSII_UNITYVERSION` from the validated process value when needed and restores it after the official C# build. It uses the installed Code Mod toolchain for ModPostProcessor, Burst, and DeployWIP.

```powershell
.\scripts\build-official.ps1
```

For a release with the project's registered public Client ID:

```powershell
.\scripts\build-official.ps1 -TwitchClientId YOUR_REGISTERED_CLIENT_ID
```

The script passes only validated local toolchain paths to MSBuild. The project's `Mod.props` and `Mod.targets` still run Entities Source Generators, ModPostProcessor, Burst, and DeployWIP; webpack then writes the UI bundle. `TwitchClientId` accepts letters and digits and is generated into the mod assembly. No Client Secret or OAuth token is accepted by the script or packaged. A build without a Client ID succeeds but cannot connect to Twitch unless one is available through the documented local development setting or legacy config.

The verified installation paths on this workstation are:

- Game assemblies: `D:\Games\Cities.Skylines.2.Ultimate.Edition-InsaneRamZes\Cities2_Data\Managed`.
- Official toolchain and ModPostProcessor: `D:\Games\Cities.Skylines.2.Ultimate.Edition-InsaneRamZes\Cities2_Data\Content\Game\.ModdingToolchain`.
- Entities SourceGenerators: `%LOCALAPPDATA%\Colossal Order\Cities Skylines II\UnityModsProject\Library\PackageCache\com.unity.entities@1.3.10\Unity.Entities\SourceGenerators`.
- Burst package: the same `PackageCache`, under `com.unity.burst@1.8.23`.
- Code Mod deployment: `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\local\CS2TwitchCitizens.Mod`.
- UI bundle: `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\CS2TwitchCitizens.UI`.

If the script reports missing toolchain files, use the game's **Code Modding Toolchain** installer/repair action, then open its Unity Mods Project with the Unity editor version selected by that toolchain and let Unity import the packages. Verify the `CSII_*` paths and the `UnityModsProject\Library\PackageCache` paths before rebuilding. This installation uses Unity `2022.3.62f2`. The official ModPostProcessor reads `CSII_UNITYVERSION` from the user environment. If the script cannot temporarily set it due to registry permissions, run `setx CSII_UNITYVERSION 2022.3.62f2` in a user shell, reopen the shell, and run `./scripts/build-official.ps1`. Game DLLs are referenced from the local installation and are not copied into the deployed mod.
