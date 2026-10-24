# CS2 Twitch Citizens

CS2 Code Mod that binds Twitch viewers to citizens through EventSub chat commands. Version 0.9.1 adds a Twitch Channel Points reward manager to the in-game Interactive panel. It can create, sync, edit, pause, enable, delete, and bind rewards, and manually confirm or refund eligible redemptions. Viewer messages, city announcements, celebrations, and emergency alerts remain UI presentation events without simulation changes. The user verified `!join`, `!me`, `!find`, and `!history` replies in game on v0.8.2; the reward manager and newer UI still need a live Twitch and in-game check.

## Channel Points setup

Authorize the broadcaster again in **Settings** to grant `channel:manage:redemptions` and `channel:read:redemptions`, in addition to the two chat scopes. Open **Interactive → Twitch rewards**, select **Sync with Twitch**, then create a reward or link an existing one to a mod action. Enable Interactive events in the section's Settings tab. The mod receives [custom reward redemption add and update events](https://dev.twitch.tv/docs/eventsub/eventsub-subscription-types/#channelchannel_points_custom_reward_redemptionadd) over the existing EventSub connection. Reward IDs are compared exactly. ShowMessage and CityAnnouncement require text; CityCelebration and EmergencyAlert can run with empty text. `GameDisaster` remains unavailable; see [installed DLL research](DISASTER_RESEARCH.md).

The Interactive tab offers local tests that do not redeem points or call Twitch. Notifications render viewer input as text, show reward icons when available, have configurable length and display limits, and can be dismissed in game. If a viewer has an active binding, a message shows the citizen name and a Find citizen button. The city announcement, celebration, and alert actions are presentation events only. The manager lists pending redemptions for rewards created by this app. **Fulfill** requires a persisted acknowledgement that the game UI displayed the event; **Cancel and refund** requires Twitch to confirm `CANCELED`. Both manual operations require explicit confirmation. Automatic fulfillment is an opt-in per reward and stays off by default until live validation; it only sends `FULFILLED` after UI acknowledgement and a fresh `UNFULFILLED` status check. A failed automatic attempt stays for manual review. Rewards configured to skip Twitch's request queue may already be fulfilled, so status and refund controls are hidden for them. A redemption received while no city is ready is dropped. Accepted redemption IDs are written to separate files under `%LOCALAPPDATA%/CS2TwitchCitizens/redemptions/` by broadcaster; the old `redemptions.json` is read for migration. A process crash before display leaves a redemption for manual review. Interactive settings remain in `interactive.json` and contain no tokens. Uncertain reward creation is recorded by broadcaster under `reward-create/` for recovery by Reward ID or title on sync; no POST retry is attempted.

Open the in-game **Commands** tab to enable commands, set viewer cooldowns and permissions, select reply fields, choose RU/EN replies, and preview a sample. Settings apply immediately and persist globally in `%LOCALAPPDATA%/CS2TwitchCitizens/commands.json`, separate from city saves and encrypted Twitch credentials. See [command fields and limits](COMMAND_FIELDS.md) for the supported field matrix.

## Register the Twitch application

The project owner must [register one Twitch application](https://dev.twitch.tv/docs/authentication/register-app) in the Twitch developer console and set its client type to **Public** for the [Device Code Flow](https://dev.twitch.tv/docs/authentication/getting-tokens-oauth/). Enter the application name and category, and a valid redirect URL if the registration form requires one; Device Code Flow itself does not use that callback. Copy the **Client ID** from the application's Manage page. A client secret must never be included in the mod. The client ID is public and should be embedded in release builds by setting `TwitchClientId` as an MSBuild property, for example `dotnet build src/CS2TwitchCitizens.Mod/CS2TwitchCitizens.Mod.csproj -p:TwitchClientId=YOUR_REGISTERED_CLIENT_ID`. Local development can also use the `CS2TWITCHCITIZENS_CLIENT_ID` environment variable. A release without a real registered Client ID shows an error in the panel and cannot authorize.

## Connect in game

Open **Twitch Citizens → Settings → Connect Twitch**. Copy the displayed code and choose **Open Twitch** to authorize in the system browser. After confirmation, the mod validates the user ID and starts EventSub for that account's own channel. Use **Reconnect** to retry EventSub and **Disconnect Twitch** to stop it and remove local credentials. Disconnect does not remove residents, bindings, or life history from city saves. Loading another city keeps the Twitch account connection and restores that city's bindings.

The mod requests `user:read:chat`, `user:write:chat`, `channel:read:redemptions`, and `channel:manage:redemptions`. Existing read-only Channel Points tokens can still receive events but require reauthorization for reward management. The settings panel never receives access tokens, refresh tokens, device codes, or Authorization headers. It shows the one-time user code, login, connection status, chat sending permission, and separate Channel Points read/manage permissions.

## Local data and migration

Tokens are encrypted with Windows DPAPI CurrentUser in `%LOCALAPPDATA%\CS2TwitchCitizens\twitch.credentials.dpapi`, separate from city saves. Only the same Windows user can decrypt them. The old `%LOCALAPPDATA%\CS2TwitchCitizens\twitch.config.json` remains untouched and its plaintext token is no longer used. It can supply a Client ID for a local migration build, but a public release should embed the project's registered Client ID. Once the new connection has worked and the old credentials are no longer needed, the user can remove the legacy file manually.

An expired or invalid access token is refreshed without blocking the game thread. The new refresh token is stored after each rotation. A revoked or unusable refresh token requires a new in-game authorization. Network failure leaves encrypted credentials on disk.

## Build and validation

Run the C# checks with `dotnet run --project tests/CS2TwitchCitizens.Commands.Checks`. From `src/CS2TwitchCitizens.UI`, run `npx tsc --noEmit -p tsconfig.json` and `npm test`.

From the repository root, the **official build, DeployWIP, and UI installation** are:

The v0.9.1 official build completed ModPostProcessor, Burst, DeployWIP, and UI deployment on 2026-10-24. The installed toolchain was intact; `ModPostProcessor` failed when `CSII_UNITYVERSION` was missing from the user environment. The build script temporarily sets the user value from the validated process value and restores it after the official C# build. This requires permission to write the user environment on this machine.

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
