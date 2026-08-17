# CellAO Overview Reference

This note is a practical overview of `CellAO`, what it contains, what looks useful for ProjectMayhem, and which server-side areas appear broader than what ProjectMayhem currently has.

Repository inspected:
- `<path-to-your-checkout>\CellAO`

## What CellAO Is

CellAO is an older open-source Anarchy Online emulator project written in C#.

From the repository layout and README, it is a multi-process/server emulator with:
- `LoginEngine`
- `ChatEngine`
- `ZoneEngine`
- `WebEngine`

It also has:
- shared libraries
- extracted/packaged AO game data
- XML and database-backed world data
- tools for extraction, documentation, packet work, and launching

It is clearly server-focused. It is not a client rendering project like ProjectMayhem.

## High-Level Architecture

### Services

CellAO is split into separate engines:
- `LoginEngine`
- `ChatEngine`
- `ZoneEngine`
- `WebEngine`

Config is centralized in:
- `CellAO/Config/Config.xml`

Important config areas include:
- listen IP / ports
- chat / zone / login / comm ports
- SQL backend selection
- MOTD
- IRC relay settings
- web host settings

### Shared Data / Libraries

CellAO includes a substantial shared library layer under:
- `CellAO/Libraries/Source/...`

Notable dependencies/components visible from project references:
- `Cell.Core`
- `CellAO.Core`
- `CellAO.Database`
- `CellAO.Enums`
- `CellAO.Stats`
- `SmokeLounge.AOtomation.Messaging`
- `PlayfieldLoader`
- `Translations`
- `Utility`

This suggests the project was designed around:
- shared entity/core models
- database-backed persistence
- network message serialization/deserialization
- reusable playfield/world loading

## What The Engines Do

### LoginEngine

What it appears to handle:
- account login
- character list
- character create/delete/select
- random-name requests
- server startup/console commands
- admin-style console account creation

Interesting signs:
- `Program.cs` includes console-based user creation
- login data is DB-backed
- support for expansion flags and GM levels is explicit

### ChatEngine

What it appears to handle:
- buddy list
- player name lookup
- tells
- private groups
- organization channels
- raid channels
- vicinity/system/private-group messages
- optional IRC relay

This is a much fuller AO social/chat stack than ProjectMayhem currently exposes.

### ZoneEngine

This is the main gameplay/world service. It includes:
- playfield loading
- client connection/bootstrap
- packet/message handlers
- NPC/mob spawn logic
- vendors
- static dynels/statels
- script hooks
- weather command hooks
- trade skills
- teleport/debug/admin chat commands
- playfield district metadata

This is where most of the useful server-side AO behavior lives.

### WebEngine

This appears to be an administrative/community web server wrapper with:
- HTTP serving
- PHP/webcore checks
- configurable web root
- error handlers

Useful mostly as a reference for old infrastructure, not a direct gameplay target.

## Data / Content Sources In CellAO

### Extracted datafiles

Repository includes:
- `CellAO/Datafiles/items.dat`
- `CellAO/Datafiles/nanos.dat`
- `CellAO/Datafiles/playfields.dat`
- `CellAO/Datafiles/itemrelations.txt`

### XML data

ZoneEngine ships XML content for:
- `Stats.xml`
- `Playfields.xml`
- `Perks.xml`
- `Districts/*.xml`

Example:
- `Districts/127.xml` contains district names and suppression gas data for Subway

### Documentation

CellAO has a built-in documentation set for AO enums and data concepts:
- stats
- move modes
- inventory errors
- can flags
- action/event/function types
- identity types
- vending machine ids
- network message ids

Useful files:
- `CellAO/Documentation/Index.md`
- `CellAO/Documentation/Stats.md`
- `CellAO/Documentation/N3MessageIDs.md`
- `CellAO/Documentation/VendingmachineIds.md`

## Specific Useful Systems Found

### 1. Playfield metadata and districts

Files:
- `ZoneEngine/XML Data/Playfields.xml`
- `ZoneEngine/XML Data/Districts/*.xml`

This gives CellAO:
- playfield names
- base map offsets/scales
- expansion flags
- district names
- district suppression gas

This is likely useful as a reference for:
- PF naming
- district naming
- gas/suppression rules
- some old server-side world metadata

### 2. Static dynels/statels

In `Playfield.cs`, CellAO loads:
- statels
- vendors
- mob spawns
- static dynels

That is useful because it confirms a server-side model that combines:
- extracted playfield statics
- DB-driven static entities
- DB-driven spawn content

### 3. Mob/NPC spawning

`Playfield.cs` references:
- `LoadMobSpawns(...)`
- `NonPlayerCharacterHandler`
- `NPCController`

So CellAO had a meaningful NPC/mob spawn pipeline.

### 4. Vendor handling

`Playfield.cs` references:
- `LoadVendors(...)`
- `VendorHandler`
- vending machine identities

This is useful for:
- shop service-object behavior
- server-side vending-machine logic

### 5. Trade skills

Files:
- `ZoneEngine/Core/TradeSkill.cs`
- `TradeSkillEntry.cs`
- `TradeSkillSkill.cs`
- `Core/PacketHandlers/TradeSkillReceiver.cs`

This is one of the clearest bigger-than-current-ProjectMayhem systems in the repo.

CellAO caches:
- item names
- DB trade-skill entries
- source/target process counts

This likely gives a good server-side reference for AO tradeskill processes.

### 6. Script system

Files:
- `ZoneEngine/Script/ScriptCompiler.cs`
- `ZoneEngine/Script/IAOScript.cs`
- `ZoneEngine/Scripts/*.cs`

Visible script examples:
- `InfoBot.cs`
- `KnuBotFlappy.cs`
- `KnuBotItemGiver.cs`

Zone connect flow calls:
- `ScriptCompiler.Instance.CallMethod("OnConnect", ...)`

So CellAO had a real script-extension layer for gameplay/server hooks.

### 7. Chat/admin command surface

Files in `ZoneEngine/ChatCommands` include:
- teleport
- teleport dynel
- show statel
- spawn
- make shop
- insta grid
- NPC
- playfield list
- weather
- walking test

This is useful for:
- GM/debug tooling ideas
- testing workflows
- server-side helper/admin command design

### 8. Packet/message bootstrap reference

`ClientConnected.cs` is especially useful as a historical AO zone bootstrap reference.

It shows the sequence of:
- chat server info
- playfield info
- vending machine updates
- player SCFUs
- animation/stance
- game time
- full character/inventory update
- specials
- appearance update

This is very valuable as a reference for what the old emulator considered necessary to get a client in-world.

## What Looks Broader Than ProjectMayhem Today

This section is intentionally cautious. It is based on the current ProjectMayhem repo shape and quick inspection, not a full implementation audit.

ProjectMayhem clearly already has:
- authoritative game server basics
- item/nano/stats data loading
- zone transition handling
- playfield/world import work

But CellAO appears to go further in several server-side areas that are not obvious in current ProjectMayhem yet:

### Likely broader in CellAO

- dedicated `LoginEngine`
- dedicated `ChatEngine`
- buddy / private-group / org / raid chat systems
- IRC relay
- DB-backed account and character admin flows
- scriptable server hooks
- vendor/shop systems
- mob/NPC spawn systems
- richer static dynel handling
- trade-skill process system
- playfield district / suppression-gas metadata usage
- broader GM/debug chat command set
- weather hooks
- legacy AO packet/bootstrap sequencing

### Areas where ProjectMayhem already overlaps

- nanos data loading
- playfield ids / world transitions
- world/playfield content pipeline
- item/stat-oriented gameplay foundation

## Best Places To Mine For Ideas

If the goal is find good server-side AO ideas, the most valuable CellAO areas are probably:

1. `ZoneEngine/Core/Playfields/`
- playfield lifecycle
- statels/static dynels/vendors/mobs

2. `ZoneEngine/Core/PacketHandlers/`
- old AO message flow
- useful connect/bootstrap sequencing

3. `ZoneEngine/Core/TradeSkill*.cs`
- tradeskill process model

4. `ZoneEngine/ChatCommands/`
- admin/debug command ideas

5. `ZoneEngine/Script/` and `ZoneEngine/Scripts/`
- extensibility model

6. `Documentation/`
- AO enums / ids / message ids / vending ids

7. `XML Data/Playfields.xml` and `Districts/*.xml`
- district naming
- suppression gas
- legacy playfield metadata

## Caveats

- CellAO is older emulator code, so not every design decision should be copied directly.
- It targets a different architecture than ProjectMayhem.
- Some code is clearly rough, historical, or partially hardcoded.
- Use it as a reference mine, not as a drop-in blueprint.

## Practical Summary

If someone asks what CellAO is useful for in this project, the short answer is:

- a historical AO server emulator reference
- especially useful for server-side world logic, packet/bootstrap flow, tradeskills, vendors/NPCs, district metadata, chat systems, and GM tooling
- less useful as a client/world-rendering solution

## Suggested Follow-Up Targets

If we want to pull more value from CellAO later, the next best deep dives are:

1. `ZoneEngine/Core/Playfields/Playfield.cs`
2. `ZoneEngine/Core/PacketHandlers/ClientConnected.cs`
3. `ZoneEngine/Core/TradeSkill.cs`
4. `ZoneEngine/ChatCommands/*`
5. `Documentation/N3MessageIDs.md`
6. `Documentation/VendingmachineIds.md`
7. `XML Data/Districts/*.xml`
