
# CS2 Twitch Citizens

Cities: Skylines II Code Mod integrating Twitch viewers
with real in-game citizens.

## Stack

- C#
- Cities: Skylines II official Code Mod SDK
- Unity ECS
- Twitch EventSub WebSocket
- Twitch Helix API

## Architecture

Twitch must NEVER modify ECS entities directly.

Network callbacks:
Twitch -> ConcurrentQueue

Game systems:
ConcurrentQueue -> ECS

## Rules

- Use Twitch user ID as viewer identity.
- Never use username as primary key.
- Never hardcode OAuth tokens.
- Never commit credentials.
- Avoid Harmony unless official ECS/system access cannot solve the problem.
- Do not invent CS2 APIs.
- Inspect available game assemblies/types before implementing unknown APIs.
- Keep Twitch networking independent from CS2 citizen logic.
- All Twitch connections must support reconnect.
- Deduplicate Twitch EventSub message IDs.
- Do not block the simulation thread with network requests.
- Persist city bindings through the game's Entity-aware serializer; never use raw Entity Index/Version as cross-load identity.

## Versioning

Use SemVer.

Current target:
0.6.0

Update CHANGELOG.md for every completed feature.

## MVP

1. Load mod.
2. Connect Twitch.
3. Receive channel.chat.message.
4. Parse !join.
5. Queue JoinCommand.
6. Find valid citizen.
7. Bind Twitch user ID to citizen.
8. Persist binding.
9. Restore binding after loading save.

Do not implement future features until MVP works.
