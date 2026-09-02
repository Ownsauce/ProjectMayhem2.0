# AO.Server Reference

## Purpose

`AO.Server` is Project Mayhem's existing authoritative runtime. It owns
validation, accepted state changes, and simulation when this backend is in use.
In the 2.0 multi-backend client architecture it also serves as a controllable
mock/test server; it is not assumed to be the authority when connected to the
live service, Ithaca, or AORebirth.

## Owns

- Authoritative loading of gameplay data from AO data files.
- Delegate/bootstrap wiring for `AO.Core`.
- Connected player/session state.
- Character creation and identity assignment.
- Server-side action validation for movement, stat spending, equip/unequip, XP gain, and future combat/nano actions.
- World tick orchestration.
- Zone lifecycle orchestration (active/warm/sleep) and idle shutdown policy.
- Area-of-interest and runtime spawn streaming policy.
- Persistence and networking once implemented.

## Data It Should Treat As Authoritative

- `statmap.json`
- `items.json`
- `nanos.json`
- `profession_skill_cost_factors.json`
- `title_level_ip_progression.json`
- `xp_needed_to_level.json`
- `breed_ability_data.json`
- `breed_stats.json`
- `skill_caps_and_color.json`
- `skill_trickle_down.json`
- `weapon_slots.json`
- `armor_slots.json`
- `implant_slots.json`
- `breed.json`
- `profession.json`
- `AO.Core/Data/ipdist.xml`
- World/playfield files under `AO.Unity/Assets/StreamingAssets/AOData/Playfields`

## Intent Model

Clients should send intents such as:

- Move
- StopMove
- EquipItem
- UnequipSlot
- IncreaseStat
- RequestZoneTransition
- CastNano
- UseItem

The server should:

1. Validate the request against authoritative state and rules.
2. Mutate authoritative state only if valid.
3. Tick world simulation.
4. Broadcast snapshots or deltas back to clients.

## World/Zone Runtime Expectations

The authoritative server runtime should provide:

- Zone activation on demand when a character logs in or teleports into the zone.
- Zone warm-cache TTL after last character leaves (configurable).
- Sleep/unload after TTL expiry to release resources.
- Chunk/section-based streaming of NPC/runtime objects with interest management.
- Deterministic target lists for tab-targeting and nearby selection queries.

## Current Server Implementation

The new `AO.Server` project contains:

- `AuthoritativeGameData`: server-owned loading and bootstrap of gameplay data.
- `AuthoritativeGameServer`: in-memory authoritative session/world manager.
- `Contracts/ClientAction.cs`: intent DTOs that represent client requests.
- `Program.cs`: server bootstrap entry point.

## Current Gap

Some world bootstrap and content-loading behavior is still client-heavy in `AO.Unity`. The long-term plan remains to migrate zone lifecycle and authoritative world streaming decisions into `AO.Server`.

## AI Guidance

- New validation and orchestration specific to the Project Mayhem backend should land here unless it is a pure shared gameplay rule.
- Keep transport-agnostic logic separate from actual sockets or RPC libraries when possible.
- Treat the Unity client as untrusted for final gameplay outcomes.
- Do not make Unity or `AO.Client` depend on this backend's JSON messages; expose
  them through the same stable client-domain interface used by other adapters.
