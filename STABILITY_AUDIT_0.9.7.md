# Stability audit — 0.9.7 source candidate

Scope: local source audit and deterministic checks on 25 October 2026. No real Twitch load and no running-game performance or stability claim. No user credentials, saves, reward IDs, or redemption journals were deleted. The Paradox Mods listing (164740) was not changed.

## Confirmed defects and root causes

| Priority | Area | Evidence and consequence | Change |
| --- | --- | --- | --- |
| P1 | Twitch commands | `TwitchCommandQueue` used an unbounded `ConcurrentQueue`; a burst could retain thousands of old `!join` requests. | Capacity 512, two-minute age limit, rejected and expired counters. The simulation thread still drains at most one command per update, so a full citizen search cannot be triggered more than once per update. |
| P1 | RedemptionLedger | `TryReserve`, `Finish`, and `MarkExternalStatus` changed `_states` before atomic disk write. A failed write left memory ahead of disk. | Persist the proposed broadcaster state first and commit memory only after success. A deterministic write-failure check confirms rollback. |
| P2 | EventSub reconnect | The consecutive failure count was not reset after the socket reached Connected. Later interruptions could immediately receive maximum backoff. | Reset at successful session establishment; test the retry state across simulated short and long outages. |
| P2 | EventSub directed reconnect | `ConnectAsync` assigned the replacement socket to `_socket` before the replacement Welcome was validated; failed handoff left disposal targeting the wrong socket. | Activate the replacement only after Welcome and cancellation validation. |
| P2 | Normal logging | `CitizenDiscoverySystem` allocated a full citizen entity array every 1,024 updates and logged five detailed rows; every accepted chat command was also logged. | Require `CS2TWITCHCITIZENS_DIAGNOSTICS=1` for those detailed logs and discovery scans. |
| P2 | Cooldown bookkeeping | `CommandGate` retained an entry for every admitted viewer and verb indefinitely. | Prune entries older than the maximum one-hour configured cooldown every 256 admissions. |
| P2 | Manual reconnect | `Reconnect` reused the old generation while pending restore or authorization tasks might still complete. | Advance generation, cancel authorization, clear old queues and replies, and reject stale work. |

Reward management also checks the Twitch session generation after asynchronous Helix reads and before applying local state. A request already sent to Twitch cannot be recalled; the existing uncertain-create journal remains the recovery mechanism for that case.

## Potential risks and limits

| Priority | Area | Why not confirmed |
| --- | --- | --- |
| P1 | Full recovery of missed redemptions | The new Helix sweep records unknown unfulfilled redemptions for manual review, but no end-to-end socket outage and recovery test exercises the game controller. A running-game test is required before release. The sweep does not auto-fulfill any item. |
| P1 | Official release | C# projects and local checks compile; the installed ModPostProcessor exits -1 after compilation. No verified official 0.9.7 package exists. |
| P2 | Game performance | Local 300,000-candidate policy model does not include ECS iteration, game memory pressure, or Unity UI. Diagnostic counters were added; collect a real streamer trace before optimizing more. |
| P2 | Long-running tasks | OAuth/revocation fire-and-forget tasks and timer behavior were reviewed but not all have isolated shutdown tests. Tracked background task count excludes those short-lived tasks. |
| P2 | Journal throughput | Each redemption transition serializes its full per-broadcaster JSON dictionary. A local 100-write check is a small sample and does not establish throughput for thousands of entries. |
| P2 | Queue loss on transient disconnect | `CityEventSystem` clears its bounded redemption queue when Twitch disconnects. The Helix sweep quarantines missed unfulfilled records, but unknown in-flight events still require manual review. |
| P3 | JSON durability | Writes use a temporary file and replace/move, protecting against a partial target file during ordinary failures. The code does not force storage hardware flush. A disk-power-loss guarantee is unverified. |

## Behavior and data preservation

- Twitch user ID remains the command and binding identity. Game save format is unchanged; v0.9.x life history remains in the game's Entity-aware serialization path.
- Existing reward IDs remain in `interactive.json`. The deterministic restart check reloads an ID unchanged.
- Corrupt legacy and per-channel redemption journals set `LoadFailed` and refuse new reservations; the damaged files are left intact. No automatic migration or reset is performed.
- Reward creation still records uncertain outcomes and does not repeat POST. Reconciliation only issues Helix GET requests for configured rewards returned as manageable for this account.
- Missing unfulfilled redemptions discovered on reconnect become `ManualReview`, with atomic ledger persistence. Existing claimed IDs are not overwritten. A broadcaster must inspect them and make an explicit decision in Twitch/the panel.

## Local test matrix

Run `dotnet run --project tests/CS2TwitchCitizens.Commands.Checks/CS2TwitchCitizens.Commands.Checks.csproj -c Release`. All checks passed locally.

| Case | Automated expected result | Scope |
| --- | --- | --- |
| 1,000 commands quickly / overflow | Depth 128 in test, 872 rejected; default runtime capacity 512. | In-process generator |
| Stale commands | All queued commands older than two minutes expire; a fresh command still passes. | In-process clock |
| 30-second and five-minute outage | First retry after a successful session is two seconds in both simulated retry histories. | Retry policy model, no socket |
| Duplicate notifications | Repeated message ID is rejected; ledger rejects a repeated redemption ID. | In-process deduplication |
| Manual restart | Session generation advances and old command queue empties; empty client ID prevents network access in this check. | Local controller check |
| JSON write failure | Existing journal and in-memory state remain at `Received`. | Local deterministic filesystem failure |
| Partial legacy or channel journal | `LoadFailed` prevents reservations and preserves source bytes. | Local filesystem |
| Reopen city / history | 500 viewer records, including 250 previous lives, survive a model roundtrip. | Plain model, not game serializer |
| Reward ID restart | Saved reward ID reloads unchanged. | Local settings store |
| Population 300,000 | Policy evaluates all generated candidate facts. | No ECS or real game |
| Sequential reward operations | Existing mock Helix and interactive tests pass; no 0.9.7 live sequence has run. | Mock API |
| Restart during create | Existing recovery file logic is audited, but a crash-injection test of the controller is still missing. | Release gap |

Example local timings from one run on this machine: 100 ledger reservations 191 ms, 300,000 plain candidate policies 2 ms, and a 500-viewer panel JSON serialization 3 ms / 214,355 characters. These values cannot be extrapolated to the game. With `CS2TWITCHCITIZENS_DIAGNOSTICS=1`, one `[Stability]` line per minute reports call count and total milliseconds for `CitizenBindingSystem`, NPC search, and `ViewerPanelUISystem`; JSON count/bytes; ledger write count/time; queue depth/peak/rejections/expiry; and tracked background tasks. Leave this environment variable unset for ordinary Release play.

## Changed files

- Commands: `TwitchCommandQueue.cs`, `CommandGate.cs`, `InteractiveEvents.cs`, `ViewerPanelSnapshot.cs`, `ViewerFocusFeedback.cs`, `CommandSettings.cs`, and new `StabilityMetrics.cs`.
- Twitch: `TwitchEventSubService.cs`, `EventSubRetryPolicy.cs`, `TwitchConnectionController.cs`.
- Game and UI bridge: `CitizenBindingSystem.cs`, `CitizenEligibilityService.cs`, `CitizenDiscoverySystem.cs`, `ViewerPanelUISystem.cs`, `RewardManagementController.cs`.
- Tests and release metadata: `StabilityChecks.cs`, `Program.cs`, three C# project files, `UI/mod.json`, `PublishConfiguration.xml`, `build-official.ps1`, `verify-release.ps1`, and `CHANGELOG.md`.

## Build result and release gate

- `dotnet run ...Checks.csproj -c Release`: pass.
- Official C# `Compile` target with the locally installed game assemblies: pass, 0 warnings and 0 errors.
- UI `npm run build`: pass, with the existing Sass legacy API deprecation warning. UI `npm test`: pass.
- Full official Release target: **blocked after compilation** by the installed `ModPostProcessor.exe` returning -1. The first direct invocation without explicit toolchain properties also reported missing path properties; the second provided them and compiled before the processor failed. This is a packaging blocker; do not stage or publish 0.9.7 yet.
- No 0.9.7 Paradox Mods package was produced, and no upload was attempted.

## Long streamer test before release

1. Back up the city and `%LOCALAPPDATA%/CS2TwitchCitizens/`. Repair/validate the official toolchain, then build and verify 0.9.7 locally. Confirm the three DLL versions match and launch only one copy of the mod.
2. Enable `CS2TWITCHCITIZENS_DIAGNOSTICS=1` for one controlled test session. Record the game version, hardware, city population, frame time, and one `[Stability]` line every minute. Keep credentials, viewer IDs, codes, and message text out of shared logs.
3. Run at least two hours with a local command generator or consenting test viewers; do not use a real channel for load generation. Include 1,000 queued commands, several hundred bindings, and a city near 300,000 population. Watch queue depth, rejected/expired counts, update times, JSON bytes, ledger write time, task count, and memory use.
4. Test manual disconnect, reconnect, a 30-second offline interval, and a five-minute offline interval. Verify no old commands execute afterward, task count returns to baseline, and new commands work. Save, switch to a second city, reopen the first, and restart the mod/game. Confirm life history and reward IDs survive.
5. With only the two existing rewards, perform one message and one announcement redemption at a time. Inspect any `ManualReview` item before manually fulfilling or canceling it. Interrupt a session with an unfulfilled redemption, reconnect, and confirm the Helix sweep does not create a duplicate display or issue automatic fulfillment. Do not repeat a reward-create POST after an ambiguous result; sync and inspect Twitch first.
6. Disable diagnostics after collecting data. Release only after the official package verifies and the live test shows no blocking regression.
