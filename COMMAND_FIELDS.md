# Command fields in 0.8.4

Command settings live in `%LOCALAPPDATA%/CS2TwitchCitizens/commands.json` and apply to every city. This versioned file contains no OAuth credentials. A malformed or unsupported file loads defaults. The game UI writes changes immediately. The `Commands` tab selects `!join`, `!me`, `!find`, or `!history`; checked fields alone appear in replies. The sample preview uses example data and can differ from a real citizen.

The classifications below describe what the installed `Game.dll` exposes and what this version actually reads. `Supported` means a command can report a real game or journal value. `Partial` means the source exists but this version cannot reliably present the requested meaning in chat. `Unsupported` fields are absent from the command UI and are never fabricated.

| Requested field | Status | Source / behavior |
| --- | --- | --- |
| Citizen name | Supported | `Game.UI.NameSystem.TryGetCustomName`; the mod assigns the Twitch login on join. |
| Conditional character age | Supported | `Citizen.GetAgeInDays(SimulationSystem.frameIndex, TimeData)` supplies game days. The mod's separate 1:1 display model presents those days as conditional character years; this is not the city's calendar age. `Citizen.GetAge()` is the fallback category. |
| Calendar birthday | Unsupported | `m_BirthDay` can be negative for citizens predating city simulation; no reliable birth calendar date is shown. |
| Alive/deceased/missing | Supported | `CitizenUtils.IsDead` for transitions and `ViewerLifeJournal` for saved status. |
| Household entity | Partial | `HouseholdMember.m_Household` identifies an ECS entity, but there is no useful public label for the household itself. Raw Entity IDs are not shown. |
| Household member count | Supported | `HouseholdMember.m_Household` plus `HouseholdCitizen` buffer length, when present. |
| Family relationships | Unsupported | No reliable kinship projection is implemented from the inspected components. |
| Home building | Supported | `HouseholdMember` → `PropertyRenter.m_Property` → `NameSystem.GetRenderedLabelName`. Missing paths produce no data. |
| Home address and street | Supported when available | `BuildingUtils.GetAddress(EntityManager, building, out street, out number)` and `NameSystem.GetRenderedLabelName(street)` supply the existing `home` field. Without a valid street and number, the address is unknown. A residential district is not inferred. |
| Housing availability | Partial | Absence of the `PropertyRenter` path is not treated as proof of homelessness. |
| Employed status | Supported | Presence of `Game.Citizens.Worker` on the active citizen. |
| Workplace | Partial | `Worker.m_Workplace` resolved through `NameSystem` when its entity exists; a property address is shown only when `BuildingUtils.GetAddress` succeeds. A Worker with no resolved label is still known to be employed. Missing Worker alone does not prove unemployment. |
| Employer or profession | Partial | `Worker.m_Level` and workplace entity are available, but no verified profession or employer name mapping is implemented. |
| School | Supported | `Student.m_School` resolved through `NameSystem` when present. |
| Education level or enrollment label | Partial | `Student` is present, but grade and education level are not translated into a verified public field. |
| Wealth and economic indicators | Unsupported | No reliable per citizen value was verified for a chat reply. |
| Health, happiness, needs | Partial | `Citizen.m_Health` and `m_WellBeing` exist, but this release does not assign semantic labels or expose raw bytes. |
| Current position / coordinates | Supported | `SelectedInfoUISystem.TryGetPosition` on the game thread; coordinates are off by default. No camera action occurs for `!find`. |
| Current building and address | Supported when available | `CurrentBuilding.m_CurrentBuilding` and `NameSystem` label when present; `BuildingUtils.GetAddress` provides that building's address. Neither is replaced by the home building. A citizen outside a building has no asserted current street address. |
| In transport | Supported when available | A valid `CurrentTransport.m_CurrentTransport` reference reports `in transport` in the existing location type field. It does not assert a route, vehicle name, or street. |
| Current district | Unsupported | No verified mapping from the citizen's live position to a district is implemented. |
| At home / at work / other building | Supported | Compares `CurrentBuilding.m_CurrentBuilding` with the home's property entity and the work property's building entity. A valid `CurrentTransport` takes priority over building references; without a confirmed building or transport, location remains unknown. |
| Current activity | Unsupported | No verified per citizen activity label is implemented. |
| Total lives and current life number | Supported | `ViewerLifeJournal` account count. |
| Life start/end dates and previous status | Supported | `ViewerLifeJournal` save records. End date exists only when recorded. |
| Cause of death | Partial | Journal has a cause field, but current death detection records an empty cause; it is omitted unless an actual value exists. |

## Command behavior

- Defaults: all four commands enabled; per viewer cooldowns are 30/15/15/30 seconds for `!join`/`!me`/`!find`/`!history`; shared cooldown is 0 seconds. Cooldown starts when an enabled, permitted command is admitted, whether its game action later succeeds or fails. Disabled and unauthorized commands do not start a cooldown.
- Permissions use EventSub badge IDs plus the broadcaster user ID. Commands from the broadcaster can receive replies.
- Chat replies can be switched off globally or per command. `!join` can separately suppress the already joined reply; the cooldown gate never reassigns an existing binding. Other join selection rules are unchanged.
- Replies are limited to each command's configured maximum, clamped to 1–500 characters (default 450). The bounded outgoing queue defaults to 50 entries; send spacing defaults to 2 seconds. `401` requires renewed authorization, `403` is logged separately, `429` pauses sending, and `is_sent=false` is logged without retrying indefinitely.
- `!history` uses the city journal; its previous life list has a 0–10 item limit. `!find` only reads the current game position/building. Neither command moves the camera.
- Missing values are omitted by default or labeled `no data` / `нет данных` by the global option. Historical snapshots are not presented as current home or work facts after a life ends.
- Loading another city, disconnecting Twitch, or disposing the mod clears queued replies. The EventSub message ID is deduplicated before a command is enqueued; a chat message ID is used for Twitch reply threading when available.

The Twitch request and response fields are based on the [official Send Chat Message API](https://dev.twitch.tv/docs/api/reference). The user token needs `user:read:chat` and `user:write:chat`; an old read-only token must go through Device Code authorization again.
