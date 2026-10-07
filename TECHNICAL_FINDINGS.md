# Technical findings — CS2 Twitch Citizens 0.1.0

## Environment

- Investigated local assemblies in the installed game's `Cities2_Data/Managed` directory. No game DLL is stored in this repository. `Game.dll` SHA-256: `AAEE15C4FA41C130ABAA1183840667E4FAAE531D67E8A9FA7536618EFFA2F86A`; `Unity.Entities.dll` SHA-256: `07D9CB32BF935A88ECD5FE0BAFF31D2BC7E828F6DEA31619300A50DD9E2C2871`.
- Metadata was read with `Colossal.Mono.Cecil.dll` from the same installation. The examined assemblies have CLR runtime marker `v4.0.30319`; `Game.dll` references `mscorlib 4.0.0.0` and `netstandard 2.1.0.0`. Their assembly versions are `0.0.0.0`, so the Unity Entities package SemVer and exact game release are **Not confirmed** by this metadata.
- `CS2TwitchCitizens.Mod` targets `net48`, matching the installed official `Mod.props` template. This compiles here; loading in the game is **not runtime verified**. `CS2TwitchCitizens.Commands` remains an independent `net8.0` project and is not referenced by the mod project.
- Verified game entry point: `Game.Modding.IMod` (`OnLoad(Game.UpdateSystem)`, `OnDispose()`). Verified registration method: `Game.UpdateSystem.UpdateAt<TSystem>(Game.SystemUpdatePhase)` where `TSystem : Unity.Entities.ComponentSystemBase`. `Game.SystemUpdatePhase.GameSimulation` exists. `Mod.OnLoad` uses this API to register `CitizenDiscoverySystem`.
- Verified logging API: `Colossal.Logging.LogManager.GetLogger(string)` returns `Colossal.Logging.ILog`, which has `Info(object)`. The mod logs with that API. Settings API/location remains **Not confirmed** and is unused.
- The installed game contains `Cities2_Data/Content/Game/.ModdingToolchain/ColossalOrder.ModTemplate.1.0.0.nupkg`, `Mod.props`, `Mod.targets`, `ModPostProcessor.exe`, and `UnityModsProject.zip`. The files are present, but the toolchain is **not configured or complete** on this machine. Colossal Order [describes the official build/post-processing workflow](https://www.paradoxinteractive.com/games/cities-skylines-ii/modding/dev-diary-3-code-modding).

## Citizen ECS

All `Game.*` types below were found in **Game.dll**. “Component” means the metadata explicitly lists `Unity.Entities.IComponentData`; “buffer” means `IBufferElementData`. Field names are copied from this build's metadata. “Used by” names concrete methods/systems found in `Game.dll`, not inferred use.

| Purpose | Actual CS2 type and namespace | Kind | Important fields / members and verified use |
| --- | --- | --- | --- |
| Citizen | `Game.Citizens.Citizen` | Component, `ISerializable` | `m_State`, `m_Health`, `m_WellBeing`, `m_BirthDay`, `m_PseudoRandom`; `GetAge()`, `GetAgeInDays(...)`. Queried by `Game.Serialization.CitizenSystem.OnCreate` using `ComponentType.ReadOnly<Citizen>()`. No name or GUID field. |
| Age | `Game.Citizens.CitizenAge`, `Game.Citizens.CitizenFlags` | Enums | `CitizenAge`: `Child`, `Teen`, `Adult`, `Elderly`; `Citizen.GetAge()` reads age bits from `m_State`. `GetAgeInDays` uses `m_BirthDay` and `Game.Common.TimeData`. `Game.UI.InGame.CitizenUIUtils.GetAge` calls `GetAge()`. |
| Household | `Game.Citizens.HouseholdMember` | Component, `ISerializable` | On Citizen: `m_Household : Entity`. `Game.UI.InGame.CitizenSection.OnProcess` and `CitizenUIUtils.GetResidenceEntity` traverse it. |
| Household data | `Game.Citizens.Household` | Component, `ISerializable` | `m_Flags`, resources and income fields. `Game.UI.InGame.CitizenUIUtils.GetStateKey` reads it. |
| Household members | `Game.Citizens.HouseholdCitizen` | Buffer, `IEmptySerializable` | On household: `m_Citizen : Entity`; handled by `Game.Serialization.HouseholdCitizenSystem`. |
| Home/property | `Game.Buildings.PropertyRenter` | Component, `ISerializable` | On household: `m_Property : Entity`. `CitizenUIUtils.GetResidenceEntity` follows `HouseholdMember.m_Household` → `PropertyRenter.m_Property` when available. |
| Alternate home | `Game.Citizens.TouristHousehold`, `Game.Citizens.HomelessHousehold` | Components, `ISerializable` | `m_Hotel`, `m_TempHome`. `CitizenUIUtils.GetResidenceEntity` checks these paths; a `PropertyRenter` is not universal. |
| Residential property | `Game.Buildings.ResidentialProperty` | Component, `IEmptySerializable` | Tag on a property; existence verified, but not required for the diagnostic query. |
| Work | `Game.Citizens.Worker` | Component, `ISerializable` | On Citizen: `m_Workplace : Entity`, `m_LastCommuteTime`, `m_Level`, `m_Shift`. `CitizenUIUtils.GetWorkplaceEntity` checks Worker and may follow `PropertyRenter.m_Property` from the workplace. |
| Student | `Game.Citizens.Student` | Component, `ISerializable` | `m_School : Entity`, `m_LastCommuteTime`, `m_Level`. `CitizenUIUtils.GetSchoolEntity` is called by `CitizenSection.OnProcess`. |
| Health/death | `Game.Citizens.HealthProblem`, `Game.Citizens.HealthProblemFlags`, `Game.Citizens.CitizenUtils` | Component, enum, ordinary static helper | `HealthProblem.m_Flags` includes `Dead`. `CitizenUtils.IsDead(EntityManager, Entity)` and `IsDead(HealthProblem)` exist; the latter checks the `Dead` bit. `CitizenUIUtils.GetStateKey` calls `IsDead`. `Citizen.m_Health` is a separate byte. |
| Current building | `Game.Citizens.CurrentBuilding` | Component, `ISerializable` | `m_CurrentBuilding : Entity`. Optional on Citizen. |
| Transport / rendered person | `Game.Citizens.CurrentTransport`, `Game.Creatures.Resident`, `Game.Creatures.CurrentVehicle` | Components, `ISerializable` | `CurrentTransport.m_CurrentTransport : Entity`; rendered `Resident.m_Citizen : Entity` points back to Citizen; `CurrentVehicle.m_Vehicle : Entity` is on a creature. `Game.Serialization.ResidentSystem` queries `CurrentTransport`; `CitizenUIUtils.GetStateKey` uses it. |
| Position | `Game.Objects.Transform` | Component, `ISerializable` | `m_Position : Unity.Mathematics.float3`, `m_Rotation`. `Game.Simulation.ResidentAISystem.ResidentTickJob` has lookups for `Game.Objects.Transform` and `CurrentTransport`. A `Transform` on the Citizen entity itself is **not confirmed**; location may require resolving the active creature/transport. |
| Name | `Game.UI.NameSystem`, `Game.UI.CustomName`, `Game.Common.RandomLocalizationIndex` | System, component, buffer | `NameSystem` stores `Dictionary<Entity,string> m_Names`; `TryGetCustomName`, `SetCustomName`, `GetName(Entity,bool)`. `CustomName` is a marker component. `RandomLocalizationIndex.m_Index` participates in generated names; see Naming below. |
| Persistent Citizen identity | Not confirmed | — | No GUID/string/64-bit ID field or dedicated ID type found in `Game.Citizens` metadata. `Citizen.m_PseudoRandom` is only `UInt16` and is not a unique key. See Persistence. |

### Query and relationships

`Game.Serialization.CitizenSystem.OnCreate` constructs a query with `ReadOnly<Game.Citizens.Citizen>()`, `ReadOnly<Game.Prefabs.PrefabRef>()`, and `Exclude<Game.Prefabs.RandomLocalization>()`. The read-only mod diagnostic uses `GetEntityQuery(ReadOnly<Citizen>(), Exclude<Game.Common.Deleted>())`. `Game.Common.Deleted` is a verified tag component. The diagnostic uses `CitizenUtils.IsDead(EntityManager, entity)` to skip dead Citizens and logs at most five living entities every 1,024 `OnUpdate` calls. It reads optional household, property, workplace and current building references only after component/existence checks. Its `home` field is a simple `PropertyRenter` path; tourists, homeless households, and other exceptional cases can legitimately log `Entity.Null`.

The core traversals verified from `Game.UI.InGame.CitizenUIUtils` are:

```text
Citizen entity --HouseholdMember.m_Household--> household
household --PropertyRenter.m_Property--> home property (when present)
Citizen entity --Worker.m_Workplace--> workplace/company
workplace --PropertyRenter.m_Property--> work property (when present)
Citizen entity --Student.m_School--> school
Citizen entity --CurrentTransport.m_CurrentTransport--> active transport/creature
creature --Resident.m_Citizen--> Citizen entity
```

`CurrentBuilding.m_CurrentBuilding` is a separate optional current-building reference. `Transform.m_Position` exists for world objects; a universal direct Citizen position lookup is **Not confirmed**.

## Naming

`Citizen` has no name field. `Game.UI.NameSystem.GetName(Entity,bool)` first checks `TryGetCustomName`; when a Citizen is selected it calls `GetCitizenName(Entity)`. `GetCitizenName` builds a localized `Assets.CITIZEN_NAME_FORMAT` from a first-name ID and a household-derived, gendered last-name ID. `Game.Common.RandomLocalizationIndex` is used by `NameSystem.GetId` and `GetGenderedLastNameId`. `NameSystem.SetCustomName(Entity,string)` exists and updates its dictionary plus the `CustomName` marker through `Game.EndFrameBarrier`. `NameSystem.Serialize/Deserialize` writes and reads the dictionary as `Entity`/string pairs. This confirms a game-supported custom-name path, but safe use by this mod and save/load behavior in a real city are untested; no Citizen is renamed by this project.

## Persistence

`Game.Citizens.Citizen.Serialize` writes state, health, birth day, pseudo-random value and other simulation fields; it writes no dedicated Citizen ID. `HouseholdMember.Serialize` writes its `Entity` reference via `Colossal.Serialization.Entities.IWriter.Write(Entity)`. The game's `Game.Serialization.SerializerSystem` uses `Colossal.Serialization.Entities.EntitySerializer` and `EntityDeserializer`.

Inspection of **Colossal.Core.dll** shows `BinaryWriter.Write(Entity)` looks up the entity in `m_EntityTable`, verifies its version, and writes the table index; `BinaryReader.Read(ref Entity)` resolves that index through its own `m_EntityTable` or returns `Entity.Null`. Thus the game's save format remaps entity references. Raw `Unity.Entities.Entity.Index`/`Version` is **not established as a stable cross-load key**. The working `NameSystem` serialization demonstrates that an Entity reference can be preserved *within the game's serialization context*, but this does not prove a standalone file containing raw Index/Version will resolve after reload.

No independent, globally unique persistent Citizen identifier was confirmed in the examined `Game.Citizens` types. Binding persistence remains unimplemented. A future approach may use the game's serializer to store a Citizen `Entity` reference in the same save context, subject to verification that a mod can register a serializable system/component and restore it safely. A save → exit → load test with actual game code is still required. `CitizenBinding<TKey>` makes no choice of key yet.

## Mod build and checks

The game-facing project uses references through `CS2ManagedDir`, with `<Private>false>` for each game DLL. This machine's path is stored in the ignored `src/CS2TwitchCitizens.Mod/CS2TwitchCitizens.Mod.local.props`; the shared project has no machine-specific path. Another developer can copy `CS2TwitchCitizens.Mod.local.props.example` to the `.local.props` name and set their own directory, or pass `-p:CS2ManagedDir=...`. Example from the repository root:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet-home'
dotnet build src/CS2TwitchCitizens.Mod/CS2TwitchCitizens.Mod.csproj
dotnet run --project tests/CS2TwitchCitizens.Commands.Checks/CS2TwitchCitizens.Commands.Checks.csproj
```

The official mod build now imports the installed `Mod.props` and `Mod.targets`. It uses the official Entities source generators, IL post-processing and Burst compilation. The completed build has zero warnings/errors; the earlier independent command check prints `PASS: parser, DEV command handoff, concurrent queue`.

The processed mod has been installed locally but **not launched in the game**. Expected log strings are `[CS2TwitchCitizens] Mod OnLoad`, `[CS2TwitchCitizens] CitizenDiscoverySystem created`, and `[CS2TwitchCitizens] Citizen entity=`. Their appearance in a real game log remains unverified. The command queue is still independent and has not been wired into this game-facing project.

## Local Code Mod deployment investigation (7 October 2026)

- The installed game's `Player.log` reports version `1.6.2f1 (767.21d1)` and `Modding runtime: Builtin`. `Logs/Modding.log` reports no active playset or enabled mods at the last run. These observations describe the current local installation, not a successful mod load.
- The generated `.cache/Mods/mod_directory.json` lists `pdx_mods` and `local` source roots. The local root resolves to `%USERPROFILE%/AppData/LocalLow/Colossal Order/Cities Skylines II/.cache/Mods/local`. The official `Mod.targets` would deploy to `$(LocalModsPath)/$(TargetName)`, where `LocalModsPath` comes from the user environment variable `CSII_LOCALMODSPATH`.
- The official `.nupkg` template imports `Mod.props` and `Mod.targets`. `Mod.props` specifies `TargetFramework=net48` and `LangVersion=9.0`; both now come from that file in our project. Game references retain `<Private>false</Private>`.
- Official `Mod.targets` runs `ModPostProcessor.exe PostProcess` on the built assembly with `-u $(UnityModProjectPath)`, game references, and Windows/macOS/Linux platforms. Its `DeployWIP` target then copies the resulting output directory to `$(LocalModsPath)/$(TargetName)`. The project now imports these official targets.
- The template contains `Properties/PublishConfiguration.xml` for **Paradox Mods publishing** and publish profiles. `DeployWIP` copies build output and does not copy this XML from the template. The successful local development package has no separate manifest or publishing XML.
- Unity `2022.3.62f2`, Burst `1.8.23`, Collections `2.5.7`, Entities `1.3.10`, the Entities source generators, and the populated Unity Mods project are present. Builds from this agent run as `CodexSandboxOffline`, whose User environment does not inherit `danil`'s `CSII_*` values. The official post-processor reads `CSII_UNITYVERSION` from the build account's User environment. Its confirmed value `2022.3.62f2` was set temporarily for the successful build and restored afterward; all other toolchain paths were passed as explicit MSBuild properties. No post-processing error was skipped.
- The first official compilation exposed a source-generator error because `CitizenDiscoverySystem` was not `partial`. Adding the `partial` modifier changed no game logic. The subsequent compiler, `RunModPostProcessor`, Burst builds for all three platforms, and `DeployWIP` all passed with **0 warnings, 0 errors**.
- Installed to `C:\Users\danil\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods\local\CS2TwitchCitizens.Mod`. Exact files: `CS2TwitchCitizens.Mod.dll`, `CS2TwitchCitizens.Mod.pdb`, `CS2TwitchCitizens.Mod_win_x86_64.dll`, `CS2TwitchCitizens.Mod_win_x86_64.pdb`, `CS2TwitchCitizens.Mod_linux_x86_64.so`, and `CS2TwitchCitizens.Mod_mac_x86_64.bundle`. No `Game.dll`, Unity DLL, or other game assembly is in that directory. The `mod_directory.json` local source points to the parent directory. The game has not been restarted since deployment, so runtime discovery is **not yet verified**.

### In-game verification after deployment

1. Launch Cities: Skylines II normally. In the main menu's **Paradox Mods → Playsets**, add the local mod to an active playset; the [official Paradox Mods description](https://www.paradoxinteractive.com/games/cities-skylines-ii/modding/dev-diary-1-paradox-mods) confirms local mods can be added to playsets. Restart if the game requests it.
2. Load an existing city with several living citizens and let simulation run. Check `%USERPROFILE%/AppData/LocalLow/Colossal Order/Cities Skylines II/Player.log` for the three `[CS2TwitchCitizens]` strings above. `Logs/Modding.log` in the same user-data directory shows mod registration/playset status. The `Colossal.Logging.UnityLogger` used by this project forwards messages to Unity's logger, so `Player.log` is the primary log to inspect.

### Sharing and distribution

Shareable source should include the tracked project and `.local.props.example`, while omitting the ignored `.local.props`, `bin/`, `obj/`, `Library/`, game DLLs, credentials and local SDK artifacts. Game DLL references are compile-time references and have `<Private>false>`; players must use their own compatible game installation. The current local deployment is still an untested diagnostic mod. Before an internet release, verify game loading and version compatibility in a real city. Release packaging should include only the mod's own required artifacts and instructions, not the game's managed assemblies.

## Risks

- Official compilation and deployment do not prove loader recognition or safe execution in the installed game. An in-game smoke test is still needed.
- Entity relationships can be missing or point to an entity that no longer exists; use component and existence checks. `home` and `currentBuilding` describe different concepts.
- Names are generated or stored in `NameSystem`, not in `Citizen`; a name must not be used as identity.
- The game remaps `Entity` references during its own save/load process. Persisting raw `Index`/`Version` outside that process has no verified safety.
- `NuGet.Config` clears package sources for this offline, no-package workspace because the machine's global fallback path is broken. Reassess it when the official template adds dependencies.
