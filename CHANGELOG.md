# Changelog

## 0.2.0 — DEV Viewer → Citizen Binding

- Built `CS2TwitchCitizens.Commands` for both `net48` and `net8.0` and referenced its `net48` assembly from the game-facing mod; parser, receiver, and concurrent queue are shared rather than duplicated.
- Added a session-only `ViewerBindingRegistry<TKey>` that prevents duplicate viewer bindings and shared Citizen keys, and reports missing or stale bindings without automatic reassignment.
- Added `CitizenBindingSystem` in `GameSimulation`. Its one-time Debug-only DEV `!join → !me → !join` sequence uses the existing injector, parser and queue; commands are consumed on the ECS update thread. It selects an existing living Adult Citizen when available, without requiring home or work or modifying the entity.
- Added command-layer checks for join rules, stale detection and bounded queue processing. The existing parser/DEV/concurrent-queue checks still pass.
- Official Code Mod build, `ModPostProcessor`, Burst and `DeployWIP` passed with zero warnings/errors. The deployed package includes the mod and its Commands dependency, with no game assemblies. The new binding path still awaits a real in-game runtime test.

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
