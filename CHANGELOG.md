# Changelog

## 0.1.0 — foundation in progress

- Added the independent Twitch command model, parser, concurrent queue, bounded receiver, DEV `!join` injector, and local checks.
- Inspected the installed game's `Game.dll`, `Colossal.Core.dll`, Unity ECS and logging assemblies; documented verified Citizen types, relationships, naming and entity-reference serialization.
- Added a separately buildable `CS2TwitchCitizens.Mod` with a verified `IMod` entry point and a read-only, rate-limited `CitizenDiscoverySystem`.
- Kept game DLL references external through `CS2ManagedDir`; no game DLL is copied into the repository.
- Moved the machine-specific `CS2ManagedDir` value to an ignored local `.props` file and added a shareable example, keeping IDE builds working without embedding a contributor's game path in the shared project.
- Identified the installed official Code Mod template, confirmed `net48`, the local mods source, and mandatory post-processing. An earlier toolchain probe failed, and no unprocessed DLL was installed.
- Imported the official Code Mod build targets, enabled the Entities source-generator pipeline, made `CitizenDiscoverySystem` partial for generated code, and completed official post-processing plus local deployment without bundling game assemblies.
- Game loading and save/load identity behavior remain untested. No Twitch networking, Citizen mutation, or binding persistence has been added.
