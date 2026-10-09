# Changelog

## 0.8.3 — Commands tab UX redesign (game UI verification pending)

- Reorganized the Commands tab into compact global settings, four command tabs, basic command controls, and one advanced section at a time. All v1 command settings and saved values remain supported; resets now ask for confirmation in the advanced sections.
- Added RU/EN hover and click help based on actual cooldown, reply queue, missing-data, permission, and field-source behavior. Field categories now use short localized names and show only fields supported by each command.
- Added an explicitly labeled demonstration reply that follows selected fields, reply language, missing-data behavior, and length limits. No sample message is sent to Twitch.
- Added UI regression checks for old and incomplete settings, command views, advanced sections, help, field selection, preview, RU/EN, and persistence shape. The user confirmed Twitch replies and bindings with v0.8.2 in game; this UI layout is not yet verified in game.

## 0.8.2 — Twitch chat permission recovery (game verification pending)

- Validate saved and refreshed tokens for `user:read:chat` and report `user:write:chat` separately. A read-only token can keep EventSub connected while chat replies are blocked with an explicit permission state.
- Show chat sending permission in Twitch settings and add a separate Device Code action to renew both scopes. Existing credentials are replaced through DPAPI only after validation, and reauthorization does not clear city bindings, life history, or command settings.
- Log safe reasons for skipped and rejected replies without message text or tokens. Added read-only scope, write-scope rejection, UI binding, and Helix HTTP/`is_sent` checks. In-game authorization and chat delivery remain to be verified.
- Allow commands sent by the connected broadcaster to receive replies. The previous silent self-message guard discarded every reply to the broadcaster's own `!me`, `!find`, and `!history` commands, matching the local session log. Added a queue regression check for broadcaster and viewer replies and missing-scope logging.

## 0.8.1 — Commands tab HUD crash fix (game verification pending)

- Replaced the Commands tab's native `<select>`, `<option>`, `<details>`, `<summary>`, number and checkbox inputs with game `Button` controls and a plain text numeric input. React DOM reads `node.options.length` while mounting `<select>`; the game's UI DOM does not provide that collection, matching the reported `undefined.length` crash.
- Added a strict UI data boundary for command settings and field catalogs. Missing, null, or wrongly typed `selectedFields` become safe arrays; valid settings survive normalization and round trips. The tab shows an in-panel recovery notice for incomplete configuration or a corrupt settings file, a save error message, and a local render error fallback with a reset action.
- Added render and normalization checks for empty/missing JSON, incomplete commands, invalid field arrays/catalogs, RU/EN, all command views, repeated tab sequences, and preserved custom values. Game runtime confirmation remains pending.

## 0.8.0 — Configurable Twitch commands (integration pending)

- Added global, versioned command settings with in-game controls for enablement, per viewer and shared cooldowns, permissions, selected fields, reply language, missing-data behavior, length and outgoing queue limits, and reset actions. Corrupt settings fall back to defaults without touching city saves or credentials.
- Added game-thread snapshots for name, age group, status, household size, home/work/school labels, current building/location type/position, and journal history. Chat replies use only selected supported fields. `!find` leaves the camera alone; join eligibility and Entity-aware save format remain intact.
- Added Device Code `user:write:chat` scope, reply threading with the chat message ID, and bounded asynchronous Helix chat sending. Old read-only credentials require reauthorization. Disconnect and city changes clear queued replies; self replies are suppressed.
- C# command/OAuth/mock HTTP checks, TypeScript typecheck, UI checks, and official Code Mod/UI builds passed. Webpack reports the existing Sass warning. Runtime behavior and visual layout remain unverified until Cities: Skylines II is run.

## 0.7.0 — Twitch OAuth and in-game connection (integration pending)

- Added Twitch public-client Device Code authorization in C#, with a single cancellable session, `user:read:chat`, code expiry, Twitch polling interval, denial and rate-limit handling, token validation, refresh rotation, and restart recovery.
- Added Windows DPAPI CurrentUser credential storage outside city saves. The old `twitch.config.json` is detected but its plaintext token is neither used nor deleted. The public Client ID can be embedded at build time; no Client Secret is included.
- Connected UI actions for authorization, browser activation, cancellation, reconnect and disconnect, with RU/EN status text. EventSub still receives `channel.chat.message` through the existing command queue. Disconnect leaves city bindings and history intact.
- OAuth mock HTTP and DPAPI checks, previous C# checks, UI checks, and typecheck passed. The official Code Mod build now passes Entities generation, ModPostProcessor, Burst for Windows/macOS/Linux, and DeployWIP with zero C# warnings/errors; webpack installed the UI with the existing Sass deprecation warning. The missing `CSII_UNITYVERSION` user variable was restored to the installed Unity `2022.3.62f2`, and `scripts/build-official.ps1` validates paths and deployed versions. No real Client ID was supplied for this build, and in-game behavior remains unverified.

## 0.6.4.1 — History date formatting compatibility

- Replaced `Intl.DateTimeFormat` in the UI history date formatter with static Russian and English month names. The game UI runtime lacks `Intl`, which caused `ReferenceError: Intl is not defined` when opening life history. Calendar validation and the localized No data fallback remain in place.
- Added checks for one and multiple lives, invalid dates, RU/EN output, and rendering logic with `Intl` unavailable. The UI bundle was rebuilt and installed locally; confirmation that the interface remains visible requires a running-game test.

## 0.6.4 — Viewer life history UI

- Added a current-life DTO to the existing city journal projection. The panel now shows current and completed lives newest first inside the selected viewer card, with saved dates, age, home, workplace, and a verified death cause when available. Missing stays distinct from death. The city-save format remains version 1.
- Added RU/EN life history labels, a compact scrollable history view, and an information tab that returns to the existing camera actions. Empty fields show localized No data. The existing local Cyrillic font rules remain in place.
- Added DTO and UI checks for current-life JSON, life ordering, statuses, missing fields, date formatting, and navigation. TypeScript typecheck, C# checks, UI tests, webpack, official Code Mod postprocessing and Burst, and both local deployments passed. Webpack reports the existing Sass legacy API warning; the game save/load and visual appearance still require in-game verification.

## 0.6.3.2 — Control text and duplicate close correction

- In-game screenshot confirmed the previous font change on the panel container did not fix Cyrillic inside native `Button` controls or the search `input`. The panel's ordinary text renders correctly, while these controls still show missing glyph boxes. Their local CSS now selects the game's `Noto Sans` directly when the mod UI is Russian; English controls use the game's `--fontFamily`. This removes the controls' `font-family: inherit` rule without changing global game styles.
- The screenshot also showed two working close controls. Removed the extra button added in 0.6.3.1, leaving the game's visible cross and the panel's existing `onClose` handler. The earlier `closeIcon` theme override remains removed.
- TypeScript typecheck, UI tests, webpack build, and local deployment passed. The only build warning remains the Sass legacy API deprecation. The game was running during deployment, so a full restart is required before checking the corrected UI visually.

## 0.6.3.1 — Cyrillic font and close icon hotfix

- Fixed the v0.6.3 font regression in the UI controls: the mod's RU/EN switch is independent of the game's locale, while the new controls inherited the game's current font. The installed game selects `Noto Sans` first for `locale-ru-RU` and `Overpass` first by default. The mod now applies the same `Noto Sans` family locally when its Russian UI is selected and uses the game's `--fontFamily` otherwise. Source and deployed bundle contain intact UTF-8 Russian strings.
- Removed the `Panel.theme.closeIcon` override, which supplied a CSS Module class where the game expects an image URL. `UI.log` showed failed requests for `assetdb://gameui/closeIcon_Bq8`. A local 28×28 close button now draws two crossing CSS lines and closes the panel through the existing local open state; the panel's `onClose` remains wired.
- TypeScript typecheck, UI tests, and webpack build passed. The build reported only the existing Sass legacy API deprecation warning and deployed the UI module to `CSII_USERDATAPATH/Mods/CS2TwitchCitizens.UI`. In-game visual confirmation remains pending.

## 0.6.3 — UI cleanup and consistent controls

- Removed literal circle glyphs from viewer rows and the card, replaced the header glyph with a text badge, and removed the standalone `#` from the life number. A localized helper now renders `Взрослый · Жизнь №1` / `Adult · Life 1` without dangling separators when age or life count is missing. Unknown age values display `Нет данных` / `No data` instead of raw technical strings.
- Applied scoped dark-blue control styling to the top-right button, tabs, viewer rows, language buttons, and camera actions. The native `Panel.theme.closeButton`/`closeIcon` slots style the close control without a global reset. Hover, active, focus-visible, selected, and disabled states are explicit; the search field gets a matching focus state. The camera actions, bindings, DTOs, and registration hooks are unchanged.
- Added UI checks for localized life numbers, empty metadata, long-name access, absence of stray glyphs and raw placeholders, and scoped button states. Runtime appearance still requires an in-game check.
- TypeScript typecheck and UI tests passed. The official webpack UI build deployed v0.6.3 locally; its only warning is the existing Sass legacy API deprecation. No C# files or Code Mod package were rebuilt for this visual-only change.

## 0.6.2 — In-game UI redesign

- Reworked the existing `GameTopRight` button and `Game` panel using the installed UI template's native `Button`, `Panel`, and `Scrollable` components. The panel now has a compact connection summary, bound-viewer/life totals, search, Residents and Settings tabs, a scrollable viewer list, and one selected viewer card. Camera triggers and C# bindings are unchanged.
- Added RU/EN interface dictionaries with system-language initial choice and an in-panel manual switch. UI labels, statuses, feedback, and Citizen age categories are localized without changing DTO values or Twitch names. Search now includes the Citizen name. The card uses `Нет данных`/`No data` for absent home, work, and age values.
- Scoped CSS Modules keep the dark blue panel at 420 px (380 px at 1280-wide screens), bound its height to the viewport, wrap long values and two-column facts, and cap the viewer list. Static UI tests cover 0/1/10/100 viewers, search, selection, long/missing values, RU/EN, camera states, and layout guards. Visual appearance at 1280×720, 1920×1080 and 2560×1440 still requires an in-game check.
- TypeScript typecheck, UI tests, and official webpack build passed. Webpack reported only the existing Sass legacy API deprecation warning. The v0.6.2 UI bundle was installed through the template's `CSII_USERDATAPATH/Mods` output path. C# projects were unchanged for this milestone, so the Code Mod remains at v0.6.1.

## 0.6.1 — Citizen eligibility for new joins

- Replaced first-free-Citizen selection with `CitizenEligibilityService`. A new `!join` requires a living, unclaimed Citizen whose household exists, is neither tourist nor commuter, is not moving away, and rents an existing residential building. It also rejects invalid optional ECS links, outside-connection targets, and unavailable positions. Work is optional; adults are preferred. No map-center radius is used.
- Selection runs only for a new `!join` and logs `[CS2TwitchCitizens] JOIN eligibility` with viewed count, rejection counts, and selected Entity. Command consumption is capped at one per simulation update so a chat burst cannot trigger many full candidate scans in one update. Previously restored bindings are checked once for diagnostics and retained even if they fail the new rule; save format, life history, Twitch delivery, and camera behavior are unchanged.
- Added policy checks for resident/unemployed eligibility, tourists, temporary visitors, outside connections, broken household/home references, claimed/dead Citizens, moving away, absent positions, no candidates, and a later life. Official game build, Entities generators, `ModPostProcessor`, Burst, and `DeployWIP` passed with zero warnings/errors. In-game selection behavior still requires a runtime test.

## 0.6.0 — Persistent Viewer Bindings & Life History

- Added city-save serialization to `CitizenBindingSystem` through the installed game's `IDefaultSerializable` system serializer. Format 1 writes Twitch user ID, login/display name, active Citizen through `IWriter.Write(Entity)`, and a versioned life history. OAuth credentials remain in local configuration and are never written to the save. Restored Entity references are validated after game deserialization; a missing old save section starts with an empty journal. Unknown or corrupt format blocks mod commands and saves rather than silently replacing history.
- Added `ViewerLifeJournal` with unique LifeIds, one active Citizen per viewer, completed life snapshots, death/missing states, duplicate Citizen protection, and explicit new life creation on a later `!join`. A confirmed `HealthProblemFlags.Dead` ends a life; unresolved Entity loss becomes `Missing` and does not permit automatic reassignment. The check runs only for bound Citizens every ten seconds. City preload clears runtime bindings and queued commands; completion of restore also discards commands received during loading.
- Added `!history` through the existing command queue and log, and projected life counts/status/history through plain UI DTOs without ECS entities. The panel shows a compact life count and status. Full history UI remains outside this version.
- Pure life-state tests cover death, another life, Missing, duplicate claims, two viewers, model roundtrip, city isolation, version rejection, and `!history`; previous command/UI checks and TypeScript typecheck pass. The game binary save/load cycle and death detection still require a runtime test before persistence can be considered verified.

## 0.5.2 — Camera Focus Fix

- Split the camera actions. `Найти` now requests a one-time move through the game's `CameraController.pivot` and `zoom`, returns to the gameplay controller, and keeps the Citizen selected. `Следить` uses the installed game's orbit controller `Mode.Follow`; `Остановить слежение` restores the gameplay controller. Twitch command delivery and `!find` are unchanged.
- Replaced the unconditional `Focused` result with `CitizenNotFound`, `PositionUnavailable`, `CameraUnavailable`, `FocusRequested`, `FocusConfirmed`, `FocusFailed`, `FollowRequested`, and `FollowStopped`. The UI never claims continuous following from a one-time request. A later camera position check reports a movement confirmation or failure; visual correctness still requires an in-game test.
- Added camera diagnostics for Citizen position, controller, camera position before/after, target, zoom, and result. Offline command checks, UI search check, TypeScript typecheck, official Code Mod build with `ModPostProcessor`/Burst/`DeployWIP`, and UI webpack build passed. The UI build retains the existing Sass loader warning.
- The official postprocessor initially failed because the local user environment lacked `CSII_UNITYVERSION`. The installed `2022.3.62f2` editor was verified, that one local user variable was restored, and the unmodified official workflow then passed. Both local mod outputs were updated. Camera movement and follow behavior await a game restart and runtime inspection.

## 0.5.1 — UI layout fix

- Fixed the regression where clicking the top-right button showed no panel. The button and native `Panel` had both been appended to the compact `GameTopRight` hook; after v0.5.1 constrained the panel size, its nested `position: fixed` placement was unreliable in that UI context. The button remains in `GameTopRight`, while the panel now mounts at the game's `Game` hook with local absolute positioning. A shared local binding keeps their open state synchronized.
- Added temporary UI console markers for button toggles and panel mounting. The panel still has a fixed 400 px width (360 px on narrower viewports) and a bounded viewer list, so the former unconstrained horizontal strips cannot recur from this layout. The changed UI passed its search check, TypeScript typecheck and official webpack build, and was redeployed locally. Visual behavior awaits a new game run.
- Fixed the panel and viewer-list sizing. The v0.5.0 stylesheet used CSS `min()` for panel widths and list max-height; the installed Gameface runtime rejected those declarations in `Logs/UI.log`, leaving the native panel and scroll area unconstrained. The log then reported elements expanding to its 10,000,000-unit limit, matching the full-width dark strips observed in game.
- Replaced those declarations with explicit 400 px panel width (360 px below 1500 px), bounded height and scroll area, plus local overflow and box sizing. The existing CSS Module still scopes every rule; no game-wide selectors or C#/Twitch behavior changed.
- This patch changes only the UI module (`0.5.1`); the deployed Code Mod assemblies remain at `0.5.0` and do not require a rebuild.
- UI search tests, TypeScript typecheck, existing C# checks and webpack build passed; the local UI module was redeployed. The build retains one Sass loader deprecation warning. The game must be restarted for a visual check of the new bundle.

## 0.5.0 — In-Game UI

- Added an official-template React/TypeScript UI module registered at the `GameTopRight` hook. Its compact panel has an open/close button, Twitch service status, channel ID, bound-viewer count, searchable scrollable list, selected Citizen card, and read-only connection settings.
- Added `ViewerPanelUISystem` at `SystemUpdatePhase.UIUpdate`. It sends a plain JSON snapshot through `Colossal.UI.Binding.ValueBinding<string>` at most once per second and receives explicit `focusCitizen` triggers containing only a Twitch user ID. The existing `CitizenBindingSystem.FocusCitizen` performs validation and camera control. Chat `!find` still never moves the camera.
- Exposed thread-safe Twitch connection states from the existing EventSub service: Connecting, Connected, Reconnecting, AuthenticationError, and Disconnected; Disabled is used when the service is not enabled. The UI receives the configured broadcaster ID, never the token or the whole config.
- Added UI projection/serialization and safe action tests, plus a frontend search check. Existing command/Twitch tests still pass. The official Code Mod build, Entities generation, `ModPostProcessor`, Burst, and `DeployWIP` passed with 0 warnings/errors. The official UI webpack build passed with one Sass loader deprecation warning. Both outputs are installed locally; v0.5.0 UI appearance and camera action still require a game runtime test.

## 0.4.0 — Citizen Identity, Find & Camera

- Added `!find` to the Twitch command path. On the game thread it resolves the existing viewer binding, checks whether the Citizen still exists, and uses the game's position resolver. It logs `not joined`, `stale binding`, `position unavailable`, or the current Citizen and position. It never moves the camera or reassigns a binding.
- After a successful `!join`, the mod captures the prior custom name or rendered label for diagnostics and calls `Game.UI.NameSystem.SetCustomName` once with the Twitch login. The Twitch user ID remains the binding key. The game serializes custom names independently of this mod's session-only registry, so a renamed Citizen can retain that name after a save/load even when the Twitch binding is gone.
- Added an explicit game-thread `FocusCitizen(viewerId)` action using the public orbit-camera controller API, plus `GetAllBindings`, `GetViewerInfo`, and `FindCitizen` for a future UI. UI snapshots contain plain values and use Twitch user ID as their action key; they expose no ECS object.
- Added offline checks for `!find`, missing/stale bindings, position failures, repeated finds, duplicate Citizen prevention, and DTO isolation. Official Entities generation, `ModPostProcessor`, Burst and `DeployWIP` completed with zero warnings/errors. The v0.4.0 name, find, and camera paths still require an in-game runtime test.

## 0.3.0 — real Twitch chat command source

- Added a framework-independent EventSub WebSocket client for one `channel.chat.message` subscription, using `ClientWebSocket`, `HttpClient`, and built-in JSON serialization. It validates a user access token, creates the subscription, handles Twitch reconnect and ordinary disconnect, deduplicates relevant EventSub message IDs, and uses bounded reconnect delays.
- Added ignored local Twitch configuration and a credential-free example. The service starts only with enabled, complete config and stops through cancellation when the mod is disposed. Network code sends parsed `!join` and `!me` into the existing concurrent queue; `CitizenBindingSystem` retains all ECS and binding work.
- Preserved the Debug DEV injector for disabled Twitch config and retained the existing command and binding checks. Added offline checks for Twitch payload parsing, identity fields, queue handoff, deduplication, and reconnect URL/delay rules.
- Official Code Mod build, Entities post-processing, Burst and local deployment passed with zero warnings/errors. Real Twitch connectivity and command delivery await a user runtime test with credentials.

## 0.2.0 — DEV Viewer → Citizen Binding

- Built `CS2TwitchCitizens.Commands` for both `net48` and `net8.0` and referenced its `net48` assembly from the game-facing mod; parser, receiver, and concurrent queue are shared rather than duplicated.
- Added a session-only `ViewerBindingRegistry<TKey>` that prevents duplicate viewer bindings and shared Citizen keys, and reports missing or stale bindings without automatic reassignment.
- Added `CitizenBindingSystem` in `GameSimulation`. Its one-time Debug-only DEV `!join → !me → !join` sequence uses the existing injector, parser and queue; commands are consumed on the ECS update thread. It selects an existing living Adult Citizen when available, without requiring home or work or modifying the entity.
- Added command-layer checks for join rules, stale detection and bounded queue processing. The existing parser/DEV/concurrent-queue checks still pass.
- Official Code Mod build, `ModPostProcessor`, Burst and `DeployWIP` passed with zero warnings/errors. The deployed package includes the mod and its Commands dependency, with no game assemblies. The later in-game test confirmed DEV `!join`, `!me`, and repeated join against a real Citizen.

## 0.1.0 — foundation in progress

- Added the independent Twitch command model, parser, concurrent queue, bounded receiver, DEV `!join` injector, and local checks.
- Inspected the installed game's `Game.dll`, `Colossal.Core.dll`, Unity ECS and logging assemblies; documented verified Citizen types, relationships, naming and entity-reference serialization.
- Added a separately buildable `CS2TwitchCitizens.Mod` with a verified `IMod` entry point and a read-only, rate-limited `CitizenDiscoverySystem`.
- Kept game DLL references external through `CS2ManagedDir`; no game DLL is copied into the repository.
- Moved the machine-specific `CS2ManagedDir` value to an ignored local `.props` file and added a shareable example, keeping IDE builds working without embedding a contributor's game path in the shared project.
- Identified the installed official Code Mod template, confirmed `net48`, the local mods source, and mandatory post-processing. An earlier toolchain probe failed, and no unprocessed DLL was installed.
- Imported the official Code Mod build targets, enabled the Entities source-generator pipeline, made `CitizenDiscoverySystem` partial for generated code, and completed official post-processing plus local deployment without bundling game assemblies.
- Confirmed from the game's named mod log that the previous build loaded, created `CitizenDiscoverySystem`, read real Citizen entities in a city, and disposed cleanly; corrected the documented log location.
- Added one-time load, registration, creation, and first-update diagnostic markers plus a rate-limited Citizen query count. Rebuilt with official post-processing and redeployed locally with zero warnings/errors. The new markers await a game run.
- Save/load identity behavior remains untested. No Twitch networking, Citizen mutation, or binding persistence has been added.
