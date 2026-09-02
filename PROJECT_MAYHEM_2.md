# Project Mayhem 2.0

## Direction

Project Mayhem 2.0 is a custom Unity client for Anarchy Online servers. It is
not tied to one server implementation. The intended backends include the live
service and private-server implementations such as Ithaca and AORebirth, 

The connected server is authoritative for accounts, characters, inventory,
world entities, combat, quests, chat, zoning, and persistence. Project Mayhem
owns presentation, input, local asset resolution, caching, and communication
through a backend-specific protocol adapter.

Users configure a local AO installation. Required visual resources are read
directly from its database and derived results are placed in a disposable user
cache.

## Runtime Boundary

```text
Supported AO server
├── live service, Ithaca, AORebirth, or another compatible backend
├── authentication and sessions
├── characters and persistence
├── live players, NPCs, mobs, and pets
├── inventory, combat, nanos, quests, and chat
└── authoritative positions and zone transitions
                 |
                 v
Project Mayhem Unity client
├── stable client-domain API
├── selected backend protocol adapter
├── world presentation, UI, input, camera, audio, and VFX
├── interpolation and optional client prediction
├── AO installation asset resolver
└── persistent converted-asset cache
                 |
                 v
User's local Anarchy Online installation
```

## Source Layout

```text
AO.Unity/                 Unity presentation and application shell
AO.Client/                Backend adapters, transport, and client world state
AO.Assets/                AO database reading, decoding, conversion, and caching
AO.Shared/                Small protocol-independent identities and value types
AO.Tools/                 Offline diagnostics and converter development tools
docs/                     Architecture, protocol research, and migration notes
tests/                    Protocol, decoder, cache, and integration tests
placeholders/             Project-owned fallback assets only
```

Folders define code and runtime ownership boundaries. Generated local data and
caches stay outside source-controlled package directories.

## Data Sources

### Obtain locally from the AO installation

- Terrain, water, rooms, and static playfield geometry
- Character, creature, item, and world meshes
- Textures, icons, animations, sounds, and music
- Item, nano, stat, playfield, and resource display metadata where available
- Resource-ID mappings required to render identities received from the server

The current `StreamingAssets/AOData` files are optional migration references,
development overrides, and small Project Mayhem configuration files. A complete
pre-exported visual dataset is not required for the direct-AODB client.

### Receive from the connected server

- Login results and character selection data
- Current character state, inventory, equipment, stats, and progression
- Current playfield, position, and zone transitions
- Live dynel creation, updates, and removal
- Spawns, AI outcomes, combat, nanos, loot, missions, quests, shops, and chat

Files such as `<playfield>_runtime_world_objects.json` must not be authoritative
in the finished client. The connected server supplies the live world state.

## Server Compatibility

Each supported backend gets its own adapter beneath a stable client-facing
interface. Adapters may differ in discovery, authentication, framing,
encryption, opcodes, and feature support, while exposing the same domain events
to Unity. Compatibility must be stated and tested per server and version; a
working adapter for one backend does not imply compatibility with the others.

## Current Implementation Status

Last updated: 2026-09-02.

### Connected world and movement — implemented

- AORebirth login, character selection, zone handoff, playfield bootstrap, and
  ongoing world-delta collection are connected through `AO.Client`.
- Server name, endpoint, and connection state are displayed in the top-left
  server panel. Sent/received counts and the last server tick are retained.
- Forward, backward, strafe, diagonal movement, idle, sitting, jumping, and
  landing resolve AO CAT animations by the active character's breed and sex.
- Forward/backward animation wins when forward/backward is combined with a
  strafe input. Movement stops returning to the stale movement pose after
  landing or releasing input.
- Movement animation timing is read from the AO animation data rather than a
  fixed Unity loop duration.

### Inventory, equipment, and wear window — partially implemented

- The wear window contains the AO weapon/HUD, armor, implant, and social slot
  layouts used by the earlier Windows prototype.
- FullCharacter equipment slots and inventory updates are synchronized from
  AORebirth. The private JSON server retains its authoritative equip/unequip
  request and approval flow.
- Runtime items are resolved from the server-provided low/high template IDs and
  QL through the configured AO ResourceDatabase.
- Equipped meshes can be loaded directly from AO RDB/ABIFF data without a
  pre-exported GLB.
- Breed/sex criteria in item wear spell data are evaluated when choosing a
  visual mesh.
- Backpack attachment is centered on the character's spine.
- Head wear such as glasses uses AO's authored `Attractor01_head`; the tested
  glasses now appear in the correct position and orientation.
- Weapon meshes resolve to hand anchors, including the mirrored CAT hand basis,
  but weapon grip, barrel direction, scale, and per-weapon calibration still
  need additional work.
- Broader armor coverage has not been validated. Robes, tank armor, shoulder
  meshes, and unusual breed-specific equipment remain partial.

### Programs, nano casting, and NCU — implemented with partial metadata fallback

- Nano crystals upload from inventory into the Programs window.
- Uploaded programs are categorized into PSI, Combat, Medical, Protection, or
  Space instead of appearing only under All/Favorites.
- Casting consumes nano, displays cast/recharge progress, and installs
  persistent programs in the NCU window when applicable.
- Nano casting uses AO breed/sex CAT spell animations. Casting is played as an
  overlay so locomotion does not replace it every frame.
- The NCU window shows active programs, capacity usage, duration, and removal.
- Direct nano metadata is used where the current parser exposes it. School,
  duration, and NCU-cost inference remains as a fallback for records whose nano
  formula data is not decoded yet.
- Exact per-nano effects, target-specific casting variants, hostile nano
  execution, and full native server cast-result handling remain partial.

### Skills, IP, and character stats — connected; runtime verification ongoing

- The Windows-era data files and rules remain the source for skill names,
  profession cost factors, breed ability costs, title-level caps, starting
  values, IP progression, derived-stat formulas, and XP thresholds.
- Current values are server authoritative; database formulas do not replace a
  current server value.
- AORebirth FullCharacter `Stats1` and `Stats2` are decoded in protocol order.
  `Stats2` overrides duplicate values from `Stats1`.
- Subsequent local-character AO `Stat` packets merge into the same snapshot, so
  IP, skills, health, nano, XP, and other stats continue updating after login.
- AO stat 53 supplies available IP, stat 54 supplies level, stat 52 supplies
  XP, stat 57 supplies the previous-level threshold, and stat 350 supplies the
  next-level threshold.
- The private JSON server now includes the complete base-stat collection plus
  IP, level, breed, profession, and sex in its local-player world snapshot.
  Unity applies private-server and AORebirth values through the same
  `Character.ApplyAuthoritativeStats` path.
- The Stats window reads exact authoritative health and nano values. It no
  longer takes the larger of a local fallback and the server value.
- XP progress is displayed as `XP - LastXP` out of `NextXP - LastXP` when those
  AO stats are available.
- Initial synchronization writes decoded IP, health, and nano values to the
  System chat tab for diagnosis.
- Remaining verification: reconnect to each target server and confirm the
  reported stat count and IP/HP/Nano values. Native skill-allocation requests
  are implemented for the private JSON server; the live/AORebirth native stat
  allocation packet still needs a backend implementation before live skill
  spending can be claimed complete.

### Chat and status — partially implemented

- The chat/damage window includes Chat, Damage, and read-only System tabs.
- Equipment, inventory, nano, connection, synchronization, and failure status
  messages are routed to System.
- The obsolete separate status UI and the old phase/PF chevron window were
  removed.
- Full live/private chat channel protocol support, channel subscriptions,
  tells, vicinity, groups, organization chat, and persistence remain pending.

### UI and server presentation — partially implemented

- Wear, Programs, NCU, Skills, Stats, inventory, chat/damage/system, and
  character-selection surfaces are present.
- Backpack interaction distinguishes container opening from normal item use.
- The current UI is functional but still contains prototype coupling through
  `PrototypeUiContext` and `AuthoritativeNetworkClient`. These must eventually
  move behind the stable client-domain interfaces described above.

### Validation currently available

- `tests/AO.Client.WorldDelta.Tests.csproj` covers movement, FullCharacter
  equipment/nanos/stats, generic stat updates, health updates, despawn, and
  selected world-object decoding.
- The current protocol test result is:

```text
PASS AO.Client world delta decoding
```

### Immediate next work

1. Reconnect to AORebirth/live and a private JSON server, then record the System
   synchronization line containing stat count, IP, HP, and Nano.
2. Verify all Skills-window rows against known server character values and
   distinguish a genuine zero-IP character from a missing stat.
3. Implement the native AORebirth/live skill-allocation request and its success
   or rejection response; do not mutate live skills locally.
4. Validate Stats-window health, nano, and XP progress while taking damage,
   casting, regenerating, zoning, and leveling.
5. Continue weapon visual calibration using AO attachment/effect metadata,
   followed by broader armor and breed/sex coverage.
6. Replace remaining inferred nano metadata with decoded nano formula records.
7. Continue chat protocol integration and move prototype UI networking behind
   the backend-independent client API.

## Immediate Milestone

Build one vertical slice before performing broad cleanup:

1. Select one initial backend and establish its protocol or source-level packet
   definitions.
2. Validate a configured AO installation.
3. Build and persist a lightweight AO resource index.
4. Authenticate and retrieve the character list through that backend's adapter.
5. Enter one character's playfield and receive its position and nearby dynels.
6. Decode and render the spawn area from the AO installation.
7. Resolve character and dynel visual resource IDs.
8. Measure first-load, cached-load, and frame-stall performance.

Playfield 4582 is a useful initial rendering test, but the selected character's
actual server-selected playfield should drive the end-to-end test. Once the
slice works, validate the same client-domain flow against the other adapters.

## Migration Rules

- Preserve the existing worktree as a reference until the vertical slice works.
- Keep disposable generated resources and caches out of source-controlled
  package directories.
- Keep the current Project Mayhem server available only as a temporary mock or
  test harness; it is not the only target production authority.
- Replace `AuthoritativeNetworkClient` behind a client-facing abstraction rather
  than coupling Unity UI directly to any backend's packet structures.
- Keep AO resource parsing outside Unity presentation classes.
- Keep generated caches under `Application.persistentDataPath`, version their
  contents, and make them safely disposable.
- Perform parsing and conversion away from Unity's main thread.
- Stream optional content and use Project-owned placeholders when resolution
  fails.

## Open Dependencies

The largest unknown is protocol access and compatibility for each intended
backend. Before claiming support, establish its authentication, login/zone
handoff, packet framing, identity, movement, and dynel update formats and
confirm encryption, version, and feature requirements. The live
service, Ithaca, and AORebirth should be tracked as separate compatibility
targets rather than assumed to share an interchangeable protocol.

The resource-reader dependency is now the bundled AODB runtime plus Project
Mayhem's direct decoders. Remaining work is format coverage, performance,
threading, diagnostics, and Unity-player compatibility. Export tools remain
optional behavioral references rather than runtime requirements.
