# Project Mayhem 2.0 — Current Progress and Next Steps

Last updated: 2026-08-25

## Goal

Project Mayhem 2.0 is a server-agnostic custom Anarchy Online client. It is not
specific to AORebirth. The client boundary is intended to support the official
live service, Ithaca, AORebirth, and other compatible private servers through
separate backend adapters. AORebirth Local is the first working adapter and
protocol test target.

## Current Working Path

The following path has been verified end to end against AORebirth Local:

1. Read the user's combined `DimensionServer.url` feed.
2. Parse AORebirth, AORebirth Local, Ithaca, and official dimensions.
3. Connect to AORebirth Local at `127.0.0.1:7500`.
4. Perform AO login-key authentication and receive the character list.
5. Select a character and follow the zone handoff to `127.0.0.1:7501`.
6. Negotiate and decompress the continuous RFC 1950 zlib zone stream.
7. Parse `PlayfieldAnarchyF` into authoritative position/playfield state.
8. Parse character full updates into a persistent neutral entity snapshot.
9. Process movement, health-stat, spawn/update, and despawn packets.
10. Discover static world objects, currently including vending machines.

Credentials remain in the ignored `tmp/` directory, are never logged, and are
not included in source-controlled settings.

## Live Verification

The current probe authenticated five characters and entered the world as:

- Character: `Testinglocal`
- Character identity: `50000:18`
- Playfield: `127`
- Authoritative position: `(155.334, 107.615, 236.312)`

The tested Subway zone-in produced:

- 89 player/NPC entities with identity, name, level, health, and position.
- Six vending machines: Clothes, Basic ICC Special Weapons, Basic Armor, Basic
  Medic Supplies, Basic Tools, and Containers.
- The Containers terminal resolved its transform through its linked Container
  Supplier NPC.

## Implemented Protocol Messages

- Login salt, credentials, character list/select, zone information, and login.
- Compression negotiation (`0x7F00`).
- `PlayfieldAnarchyF` (`0x5F4B1A39`).
- `SimpleCharFullUpdate` (`0x271B3A6B`).
- `Stat` (`0x2B333D6E`) for Life/Health changes.
- `ToClientQuit` / `Despawn` (`0x36510078`).
- `DoorFullUpdate` envelope (`0x365A5071`).
- `DropDynel` (`0x47483633`).
- `CharDCMove` (`0x54111123`).
- `CorpseFullUpdate` envelope (`0x4F474E05`).
- `VendingMachineFullUpdate` (`0x7F544905`).

## Architecture Now in Place

- `IGameServerBackend` keeps presentation code independent of server packets.
- AORebirth wire details remain under `AO.Client/Backends/AORebirth`.
- Shared authentication, character, entity, delta, and object models live under
  `AO.Client`.
- World entities are stored by AO identity and updated in place conceptually.
- One background reader feeds decompressed packets into a channel, allowing
  consumers to request bounded batches without hanging inside zlib.
- Vending objects use direct coordinates or resolve them through a linked NPC.

## Tests and Probes

- Dimension-list parsing and preferred launcher-path discovery.
- Deterministic movement, health, despawn, and vending packet tests.
- Live authentication, character selection, zone handoff, bootstrap, entity,
  object, and bounded-delta probe.
- Current projects build with zero warnings and pass `git diff --check`.

## Known Limitations

- Only AORebirth has a concrete backend adapter. Ithaca and official live still
  need authentication/protocol research and adapters.
- `AO.Client` currently targets .NET 10 for the code-first probe. It must be
  retargeted or bridged for the selected game engine.
- AO's `IsPet` flag is not authoritative; pet ownership needs an owner/master
  relationship message or stat.
- Door and corpse messages are recognized by identity, but their richer
  capture-dependent payloads are not fully translated.
- Static geometry, collision, textures, animation, equipment visuals, and
  playfield asset placement are not connected to network state yet.
- Incremental combat, nanos, inventory, equipment, chat, quests, shop actions,
  loot interaction, and outbound movement are not implemented.
- The visual client should run a long-lived session service that continuously
  consumes delta batches and dispatches them on its main thread.

## Recommended Next Steps

### 1. Create a Minimal Visual World Viewer

- Retarget or bridge `AO.Client` to the selected engine runtime.
- Add dimension, login, and character-selection screens.
- Enter a zone and place a camera at the authoritative character position.
- Represent entities with capsules/labels and objects with boxes.
- Apply movement/upsert/remove deltas on the engine's main thread.

This proves the network-to-visual pipeline before investing in AO assets.

### 2. Add Outbound Player Movement

- Encode client `CharDCMove` messages.
- Separate input intent from AORebirth packet construction.
- Reconcile local prediction with authoritative server updates.
- Add rate limiting and disconnect-safe cancellation.

### 3. Resolve AO World Assets

- Map playfield IDs to terrain or room resources.
- Resolve meshes, textures, materials, scale, heading, and coordinate transforms.
- Start with doors and vendor terminals because their identities already exist.

### 4. Complete World Lifecycles

- Fully decode doors, corpses, chests, terminals, and other dynel updates.
- Decode object states such as open/closed, lootable, or interactive.
- Establish reliable pet ownership.

### 5. Add Gameplay Systems Incrementally

Suggested order: targeting and chat, inventory/equipment, interactions and
shops, combat/health feedback, nanos, loot, then quests.

### 6. Add More Server Backends

Keep the neutral models stable while adding Ithaca and official-live adapters.
Advertise capabilities per backend rather than assuming identical features.

## Unity and Godot on Linux

Both work on Linux.

Unity 6 officially supports its Linux editor on Ubuntu 22.04 and 24.04, x64,
with supported Nvidia or AMD drivers. Documented limitations include
case-sensitive filesystems, limited video importing, and experimental native
Wayland player support. This repository already contains a Unity 6.3 project,
so staying with Unity avoids rewriting its scenes, assets, MonoBehaviours, and
assembly definitions. See the
[Unity 6 system requirements](https://docs.unity3d.com/6000.0/Documentation/Manual/system-requirements.html).

Godot has strong native Linux support and a lighter editor. Its stable docs
support mainstream Linux distributions released after 2018; Vulkan 1.0 is
sufficient for Forward+/Mobile and OpenGL 3.3 for Compatibility. Godot is
likely the smoother Linux-native workflow, but switching would require rebuilding
the existing Unity presentation and asset pipeline. See the
[Godot system requirements](https://docs.godotengine.org/en/stable/about/system_requirements.html).

For this repository, the practical recommendation is to finish one minimal
Unity viewer first because the Unity project already exists. Keep `AO.Client`
engine-neutral. If Unity's Linux workflow or runtime compatibility becomes a
real blocker, the same boundary can feed a Godot C# viewer without rewriting
the AORebirth protocol adapter.
