# ProjectMayhem vs CellAO Overlap Matrix

This note compares the parts of ProjectMayhem that already overlap with CellAO in concept, shows how they currently differ, and identifies practical next steps if we want to move closer to CellAO-style depth without copying its architecture blindly.

## How To Read This

- `ProjectMayhem now`: what exists today in broad terms
- `CellAO`: what the older emulator appears to cover in the same area
- `Main difference`: the most important architectural or behavior gap
- `Good next step`: the smallest useful upgrade path for ProjectMayhem

## Matrix

| System | ProjectMayhem now | CellAO | Main difference | Good next step |
|---|---|---|---|---|
| Login / session start | Lightweight server connection and player session flow | Dedicated `LoginEngine` with account, character list, create/delete/select | ProjectMayhem is simpler and more direct; CellAO is a full login service | Add a real account/session boundary and character selection flow in the server layer |
| Chat / social | Minimal or local-only compared to MMO chat expectations | Dedicated `ChatEngine` with buddies, tells, private groups, org/raid channels, IRC relay | ProjectMayhem does not yet treat chat/social as a standalone server subsystem | Define a server-side chat/social service with tells, vicinity, and group chat as the first slice |
| Zone/bootstrap flow | Authoritative movement and zone transitions, but relatively lean bootstrap | Explicit zone bootstrap with playfield info, appearance, SCFUs, vendors, character full update, specials | ProjectMayhem relies more on Unity-side reconstruction and simpler payloads | Add a richer server-authored zone bootstrap snapshot/message bundle |
| Playfield ownership | Playfield data is imported and rendered strongly on the client side | `Playfield` runtime object on the server owns statels, static dynels, vendors, spawns, scripts | ProjectMayhem has strong world import, but thinner server-side playfield runtime behavior | Introduce `PlayfieldRuntime` / `PlayfieldService` on the server |
| Static world objects | Imported statels, room surfaces, runtime object overlays from extracted data | Server loads statels/static dynels/vendors and treats them as runtime world content | ProjectMayhem is still more asset/data oriented than entity-service oriented here | Model service objects as authoritative runtime entities, not just imported scene objects |
| Runtime world entities | Players exist authoritatively; some runtime objects are being layered in | Players, vendors, mobs, static dynels, scripts all live inside server playfields | ProjectMayhem has not yet broadened runtime entity ownership enough | Add authoritative categories for terminals, doors, vendors, NPCs |
| NPC / mob spawning | Very limited compared to classic AO server expectations | DB-backed mob spawns and NPC controller pipeline | ProjectMayhem is not yet using world/NPC spawn groups as a primary server feature | Add spawn definitions and one NPC spawn/controller path |
| Vendors / shops | Runtime object rendering is improving; server-side shop behavior is still thin | Explicit vendor handling in playfield load and bootstrap | ProjectMayhem has the visuals emerging before the gameplay service | Add authoritative shop service behavior for vending machines / terminals |
| Tradeskills | Data exists or can exist, but behavior is not a mature server subsystem yet | Cached trade skill entries, packet handling, process counting, DB-backed recipes | CellAO treats tradeskills as a first-class server system | Add a first authoritative tradeskill recipe/process service |
| Items / equip rules | Strong modern item/equip validation path | Older, broader ecosystem around items and interactions | ProjectMayhem is cleaner here, but narrower in downstream systems | Keep the current core, add more systems that consume it: vendors, tradeskills, interactables |
| Nanos / perks / derived systems | Nanos data loading exists; gameplay coverage still incomplete | Nanos/perks/stats integrated into broader server model | ProjectMayhem has data, but less gameplay surface tied to it | Add incremental server systems that apply nanos/perks in real gameplay loops |
| Scripts / extensibility | Some hardcoded game flow; not yet a broad server script model | Script compiler plus hook calls like `OnConnect` | CellAO exposes more extension points to gameplay logic | Add event hooks for connect, zone enter, interact, spawn, and item use |
| GM / debug commands | Strong Unity-side debug/testing workflow | Broad server-side chat commands: teleport, spawn, show statel, make shop, weather, etc. | ProjectMayhem is more client-debug oriented right now | Add server-side GM/debug commands for live world testing |
| Districts / suppression gas | Playfield ids and movement exist; district metadata not yet central | District XML with names and suppression gas | CellAO uses explicit world-rule metadata | Add district metadata and gas/safety rule evaluation to server playfields |
| Packet/message reference | Modern custom transport, cleaner for current needs | Legacy AO packet/message handling across login/chat/zone services | CellAO is useful as protocol/bootstrap reference more than architecture target | Keep ProjectMayhem transport, borrow sequencing and world-state ideas only |
| Web/admin | Minimal compared to a classic emulator stack | `WebEngine` for web/admin hosting | Not a near-term gameplay need for ProjectMayhem | Ignore for now unless account/admin tooling becomes a priority |

## Best Move Closer To CellAO Steps

If we want the highest-value overlap improvements first, these are probably the best sequence:

1. Add `PlayfieldRuntime` / `PlayfieldService` on the server.
2. Add authoritative runtime entity types:
   - doors
   - terminals
   - vendors
   - NPCs
3. Add richer zone bootstrap snapshots.
4. Add district / suppression-gas metadata handling.
5. Add server-side interact/service-object logic.
6. Add a first tradeskill subsystem.
7. Add script/event hooks.
8. Add server-side GM/debug commands.

## What Not To Copy Blindly

CellAO is very useful as a reference, but some parts should be used selectively:

- Do not copy its old multi-engine structure unless ProjectMayhem truly needs it.
- Do not copy old hardcoded packet behavior just because it worked historically.
- Do not replace ProjectMayhem’s cleaner item/stat core with older DB-heavy patterns.

The best use of CellAO is:
- server behavior inspiration
- world-system coverage reference
- old AO sequencing reference
- content/metadata reference

Not:
- literal architecture blueprint

## Short Summary

ProjectMayhem already overlaps CellAO in the broad ideas of:
- authoritative server
- playfields
- items/stats/nanos
- world transitions
- imported AO world data

But CellAO goes wider and deeper in:
- playfield-owned runtime systems
- chat/social
- vendors/NPCs/spawns
- tradeskills
- district metadata
- scripts
- GM tooling

So the right direction is not become CellAO. It is:
- keep ProjectMayhem’s cleaner modern core
- add the missing runtime world/service layers that CellAO already proved are useful
