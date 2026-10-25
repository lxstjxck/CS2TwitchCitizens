# Subscriber test: CS2 Twitch Citizens 0.9.5

Use a backup of the city. The mod is experimental. Real fires, floods and other Twitch-triggered disasters are not implemented.

1. From a separate test account, subscribe to the exact Paradox Mods listing link supplied by the author. In the game, open the mod manager and enable **CS2 Twitch Citizens** in the active playset. Restart if requested. If an Unlisted link is unavailable or unsubscribable, report that result; the access mode is not yet verified.
2. Check the installed mod's page and game log for version **0.9.5**. Confirm the code mod and UI load as one listing. On the developer machine, close the game and use `./scripts/toggle-local-mod.ps1 -Action Disable` before testing the subscription, then `-Action Restore` afterward. Inspect local folders first and never delete settings or saves.
3. Open a copy of a city, then open **Twitch Citizens → Settings → Connect Twitch**. Enter the Device Code at Twitch's authorization page and grant the requested chat and Channel Points scopes. Confirm that connection succeeds and can reconnect after a network interruption. Do not send codes, tokens, or credential files in bug reports.
4. In the connected channel, test `!join`, `!me`, `!find`, and `!history` with a viewer account. Confirm a valid citizen is bound, tracking and replies work, and the same binding returns after saving and reloading the city. Check the command settings tab in both English and Russian.
5. In **Interactive → Channel Points**, wait for the automatic check, then create the two fixed rewards. Test **Message to the city** and **City announcement** separately with actual Channel Points redemptions. Verify the upper-right and centered presentations, text, dismissal, and an enabled/disabled transition and cost change. Use Preview for local tests without spending points. Use History for events, queue counts, pending redemptions, fulfillment and cancellation/refund. Local preview does not prove live Twitch delivery.
6. If an error occurs, record the game version, mod version, active playset, reproduction steps, which command or reward was used, and whether it occurs after a restart. Attach relevant excerpts from `CSII_USERDATAPATH/Logs/Modding.log`, `CSII_USERDATAPATH/Logs/CS2TwitchCitizens.log`, and `Player.log`. Review and redact personal data, tokens, authorization codes, and chat text before sharing.
7. To disable the mod, remove it from the playset or unsubscribe, then restart the game. Keep the city save and `%LOCALAPPDATA%/CS2TwitchCitizens/` user data for troubleshooting unless you intentionally want to erase your own settings.

Please report whether the UI module and bindings actually loaded from the subscribed package. Local Release build and hash verification are separate checks and cannot establish this result.

## 0.9.5 Channel Points and UI check

With a broadcaster account that has unrelated rewards but no mod-managed rewards, open Interactive → Channel Points. Confirm both cards say Not created, then create one reward and check that Twitch received one creation. Check cost saving, disable and re-enable, Preview actions, and History redemptions. Repeat at 1280×720, 1920×1080 and 2560×1440 in Russian and English. COHTML layout and live Helix behavior are not covered by the local tests.

## 0.9.4 Channel Points diagnostic check

With the broadcaster account connected, refresh Channel Points once. In `CS2TwitchCitizens.log`, check for `operation=ListAll`, `operation=ListManageable`, HTTP status, and `rewards=0` if the channel has no rewards. Try creating one fixed reward only after a successful empty sync. Check Twitch before repeating a failed create; the mod preserves its uncertain-create recovery record. Share only redacted log excerpts. A local mock test or build does not confirm live Helix access.
