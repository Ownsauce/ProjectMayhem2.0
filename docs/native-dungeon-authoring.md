# Native dungeon traversal, authoring and saved recipes

October 3, 2026. Implements the requested improvements 1, 4, 5, 6, 7 and 8 for
the PF 127 room path. Generator **1.2.0** reads retained catalog3 and new clipped **catalog4** snapshots.

October 4 backlog update: selectable sources, catalog publication/revisions, room-library
authoring, common-result adoption, layout profiles and diagnostics are prioritized in
[the roadmap](procedural-status-and-roadmap.md#prioritized-next-work-ao-database-rooms).
[Detailed scope and completion criteria](worldgen-integration-boundaries.md#prioritized-ao-room-backlog)
distinguish implemented foundations from future work. PF127 and PF1931 are now prepared, registered database-room sources. Source selection,
local publication and retained catalog revision lookup are implemented; PF1931 live
Unity acceptance remains pending.

## Doorway blockage and corner sliding — October 4, 2026

User report: seed 90602's PF1931 Hallway14 → Connector_shallow15 looked open but
blocked movement. Collision inspection reproduced source room0's ramp extending
about 2m beyond its room footprint into Hallway14. It created an extra raised floor
on the approach. Center threshold heights alone did not detect that overhang.

New preparation clips source collision triangles to each room's declared footprint,
using `AO.Assets/Navigation/AOIndoorRoomCollisionClipper.cs`. Unity applies that same
clipping only for recipes whose catalog declares `CollisionGeometryVersion=1`.
Prepared server triangles and client collision therefore match. New catalogs use
version4; retained version3/default-policy recipes preserve their original collision.
Generator1.2.0 and the original-copy path remain compatible. Original textures and
presentation meshes still load from the selected local installation.

`PrototypeWalkerController` now records actual wall contact normals and projects
movement onto the contact tangent. It preserves retreat, stops direct head-on
input, and stops in closed corners rather than suppressing an entire movement
intent after any side contact. Server `CharacterMotor` resolves blocked reported
moves with its existing N3Lite `SetRelPos` sweep/slide, preserving steering instead
of discarding the whole displacement and halting. Bounds/root gates remain active.
No third-party vehicle implementation was changed.

Both sources were re-prepared and published with corrected collision. Use the
local installer below, Stop Play/refresh/reconnect, and generate a **fresh world**:

```text
.worldgen native-rooms ao-temple 90602 24 temple-doorfix-02 mixed
.worldenter temple-doorfix-02
```

Saved worlds pin their original catalog: entering an older world does not switch
it to the corrected collision package. Shared checks passed tangential/retreat
input, closed/head-on contacts, all source-room footprint bounds, and floor/body
clearance across Hallway14/Connector_shallow15. Both preparation passes checked
100 seeds per pool and 4/12/24-room layouts, including the prior PF127 stair join.
Unity compile and local server publish/startup validation pass. Service installation
and live corner/doorway acceptance were left to the user; no client was launched.

October 4 gameplay follow-up: the user reports that the doorway blockage appears
fixed. Record Hallway14 → Connector_shallow15 as passing that targeted blockage
retest. Corner sliding and broader traversal across other seeds remain separate
acceptance checks.

## Install and play

From **ProjectMayhem2.0**, install the published local server, supplying sudo
authentication in your terminal:

```bash
./tools/deploy-local-pf127-rooms.sh
```

Stop Play, refresh Unity assets, then Play/reconnect. Create a fresh instance:

```text
.worldgen pf127-rooms 90602 24 pf127-v12 mixed
.worldenter pf127-v12
```

The source-independent command selects a registered source. PF127 remains the default;
the older `pf127-rooms` command selects the source registered to PF127.

```text
.worldgen native-rooms ao-subway 90602 24 subway-source-test mixed
.worldenter subway-source-test

.worldgen native-rooms ao-temple 90602 24 temple-source-test mixed
.worldenter temple-source-test
```

`ao-subway` and `ao-temple` are stable source IDs; `90602` is the seed. Each world
name must be new when generating. `.worldenter` restores an existing saved world
using its recorded catalog revision. Runtime instance PF IDs remain server-owned.

## Select, review and publish sources — October 4, 2026

Open **Tools > WorldGen > Native Dungeon Authoring**. Select Subway (PF127) or
Temple of Three Winds (PF1931), search rooms or filter unreviewed tags, inspect
source/full-layout previews and run the seed validator. Authoring settings and the
published gameplay catalog are separate; the window shows unsaved changes, saved
settings revision and published settings/catalog revision. Identical catalog bytes
reuse their existing immutable publication, even after a settings-only revision bump.

**Validate and publish catalog to client / server** saves changed settings through
the existing optimistic file DAO, runs preparation/geometry/traversal checks in a
background process, then stages matching immutable metadata/collision packages in
both local repositories before changing either source registry. Configure the
server's `NativeCopies` folder and `dotnet` executable in the window if needed.
Detailed failures go to `native-catalog-publication.log` in Unity's temporary cache.
Publication requires the local AO installation and its prepared surface cache;
this workspace's PF1931 cache was prepared from that installation. Publishing does
not rebuild, install or restart the server. Build/package the server, then run the
local installer above to activate published changes.

Registry: `AO.Unity/Assets/StreamingAssets/NativeCopies/sources.json`; server uses
its own `NativeCopies/sources.json`. Immutable revisions live at
`RoomCatalogs/<source-id>/<catalog-SHA256>/` with `catalog.json`, `collision.bin`
and `publication.json`. The original PF127 catalog bytes/hash are retained.
Archives store source identity and exact catalog hash; version1 PF127 saves remain
compatible. Exported editor recipes now include a source ID and can import a retained
catalog revision. Do not delete revisions still referenced by saved worlds or recipes.
Automatic reference-aware cleanup and permanent team database storage remain future work.

Preparation definitions remain editable data in
`tools/WorldGen.NativeRoomCatalog/pf127.source.json` and `pf1931.source.json`.
The latter specifies entrance room7, validated spawn X/Z and its temple pool.
This is separate from the existing custom Temple/reference-layout pipeline.
New AO sources require their own validated definition and publication; changing an
ID alone does not establish that another playfield is safe. Mixed-source assembly
is not implemented.

CLI publication of an already prepared source:

```bash
/home/cody/.dotnet/dotnet run --project tools/WorldGen.PublishRoomCatalog -- \
  tools/WorldGen.NativeRoomCatalog/pf1931.source.json /path/to/prepared \
  AO.Unity/Assets/StreamingAssets/NativeCopies \
  ../AORebirth/AORebirth/Server/ZoneEngine_New/NativeCopies
```

Shared implementation: sibling `WorldGen.Core/Dungeons/NativeRoomCatalogStore.cs`.
Unity resolves sources/revisions in `NativeDungeons/NativeRoomCatalogs.cs`; server
selection/collision stays in `NativeRoomPackage`. Both hosts now use the common
`DungeonGenerationResult` for verified native layout/hash/source references.
The full reusable Unity/server lifecycle adapter extraction remains future work.

Verification: PF1931 preparation passed 100 seeds per pool, 4/12/24-room probes,
spawn/headroom/threshold checks and recipe/hash round trips. Shared publication checks
passed source scoping, matching host packages, old revision lookup, rollback and
corrupt/mismatched data rejection. Unity runtime/editor compile and local server
publish/startup validation passed. No client was launched or service installed/restarted.
PF1931 textured rendering and traversal still require user gameplay acceptance.

Meshes/textures still load from the launch-selected local AO installation. No
network rendering asset download or exported GLB/PNG pack is required. The server
uses prepared local collision. The original copy and Subway paths remain separate.

## Module boundaries — October 4, 2026

The user accepted the native authoring window: source-room inspection and textured
previews work well. The database-backed path shares common contracts and AO readers
with other world features, but has its own generation and presentation code.

| Responsibility | Implementation | Boundary |
| --- | --- | --- |
| Local AO resources, geometry/image decoding and RAM cache | `AO.Assets/Conversion/AOIndoorVisual*`, `AO.Assets/Decoders`, resource database | AO data access; independent of Unity dungeon presentation |
| Registered sources, immutable catalog/collision publication and revision lookup | sibling `WorldGen.Core/Dungeons/NativeRoomCatalogStore.cs`; host JSON codecs | Local metadata/collision only; separate from editable DAO and AO decoding |
| Native room placement, traversal validation and recipe snapshots | sibling `WorldGen.Core/Dungeons/NativeRoom*` | Engine-independent; no Unity, GLB importer or database reader dependency |
| Native Unity rendering, collision assembly, lights and visibility | `AO.Unity/Assets/Scripts/AO.Unity/World/NativeDungeons/` | Native presentation module; consumes room recipes and AO reader output |
| Source selection and manifest resource/hash verification | `NativeDungeons/NativeRoomCatalogs.cs`, `NativeDungeonManifestVerifier.cs` | Resolves recorded revisions; returns verified recipe/common result; no network-session or player state |
| Transfers and session state | `AOGameServerSession`, `PrototypeWorldBootstrap` | Dispatch to native or GLB paths and own the transfer lifecycle |
| Native authoring | `AO.Unity.Editor/WorldGen/NativeDungeonAuthoringWindow.cs` | Uses the same native loader as gameplay |
| Server physics and layout persistence | AORebirth `Core/WorldGeneration/NativeRoomPackage.cs`, `NativeRoomArchive.cs` | Consumes shared native recipes; no Unity renderer |

The native presentation files now live together, preserving their Unity `.meta`
GUIDs, public types and namespace. Resource/hash verification for both PF 127 copies
and generated room layouts was extracted from the network session. Existing AO
resource readers, static-model parsing and material factories remain shared; they
are not duplicated into a second asset pipeline.

The native room settings now use an engine-independent authoring DAO and typed
annotations. The original-client experiment has its own server adapter/command
classes and offline compatibility tool. See [data/storage design](dungeon-authoring-data.md)
and [original-client test workflow](../tools/WorldGen.NativeClientProbe/README.md).

This is code/module separation, not a separate Unity assembly or independent
package. The existing `AO.Unity` assembly still contains presentation modules;
native static-model rendering reuses its `StatelParser` and material factories.
An assembly split would first require extracting those common rendering helpers
to avoid circular dependencies. Normal generation behavior, catalog/hash formats,
saved recipes and commands are unchanged by this refactor. Runtime and editor
compilation and the existing native visual/collision checks passed after the move.
The Unity editor was not launched; no server rebuild or deployment is needed.

## Additional dungeon-run layout profile — proposed, not implemented

October 4 update: the room capability UI, independent landmark/review flags, theme
tags, notes, supported encounter-center picker and authoring DAO are implemented.
Save them with **Save room tags / settings**. See
[authoring data and storage boundaries](dungeon-authoring-data.md). The actual
dungeon-run planner below remains proposed; normal generation ignores role tags.

The user explicitly wants to retain the current normal generation mode and add an
optional dungeon-run profile using these assets. This is a layout feature; XP,
loot and combat implementation are outside the current proposal. Current generation
grows a connected tree, attaches one new room per join, and closes remaining ports.
It has no boss roles, progression order, route-length targets or loop-closing pass.

Generate an encounter/progression graph first, fit physical room templates to it
second, then reuse the existing traversal and doorway alignment validation. The
graph planner should not read an AO database or depend on a particular renderer.
Native rooms and authored GLB rooms can each provide suitable templates through
their adapters; a shared plan does not mean their geometric joins are interchangeable.

An initial 24-room profile could allocate 18 rooms to the required route and six
rooms to short optional branches. Use three boss arenas, with alternating encounter
spaces and connecting passages between them. Counts are configuration targets,
not claims about the suitability of every current source room.

```text
Entrance → encounters → Boss 1 → encounters → Boss 2 → encounters → Final boss → exit
                └→ short side area                  └→ short side area
```

Implementation order:

1. Author room capabilities: connector, encounter space, arena, hub, landmark and
   theme. Arena eligibility must consider supported usable floor area, headroom,
   connected walking regions and space for the intended party/encounter.
2. Add a separate layout-profile setting, defaulting to normal. Keep its planner
   separate from current `NativeRoomDungeon.Generate`; record profile, resolved
   room roles and progression edges in recipe/hash data. Version the new format
   explicitly, preserving existing normal recipes through their current path.
3. Build the required boss route with limits on consecutive connectors, repeated
   templates and walking distance. Reject layouts that cannot fit the plan safely;
   do not silently fall back to a plan without required arenas.
4. Add small optional branches with recognizable destinations. Validate that
   mandatory boss order cannot be bypassed by an optional connection.
5. Show route colors, numbered arena placeholders, branches and route metrics in
   the native authoring window. Placeholders mark future encounters; they do not
   spawn functional bosses or introduce an XP/loot system.
6. Add shortcuts/checkpoints separately. Physical loops require a new matching-port
   connection pass and new overlap/traversal checks; they are not supported by the
   current tree-growth algorithm. An explicit return portal is a simpler initial
   exit option. Progression gates need later server-authoritative encounter state.

Seed changes should vary eligible rooms and branches while the selected profile
preserves the three-stage progression. Test required boss reachability/order,
branch depth, route length, supported arena markers and repeat limits in addition
to the existing collision checks. The first usable profile need not contain NPCs:
walking a marked route in the preview is enough to judge layout pacing initially.

WoW reference: Blizzard's [Eye of Azshara / Neltharion's Lair preview](https://worldofwarcraft.blizzard.com/en-us/news/20237350/legion-dungeon-previews-eye-of-azshara-and-neltharion-s-lair)
describes boss encounters and choices in boss order. The three-boss subway plan
above is our proposed design, rather than a reproduction of a specific WoW dungeon.

## Walking routes and runtime organization

Catalog preparation indexes source triangles spatially and builds a 0.5 m walking
grid. Samples check a 0.4 m footprint, floor support, headroom and body probes.
Neighbor edges check intermediate support, height changes and crossing probes.
Flood filling records bidirectional walking regions. Doorways receive region IDs,
and the entrance spawn uses its accepted upper landing. Sampling chooses supported
ramps above overlapping buried terrain.

Generation permits outgoing doorways only in the region reachable from the room's
entry. Entrance exits must connect to its spawn region. Shared validation checks
the entire graph, internal regions, socket reuse, matching positions/facing/widths
and restored recipes. These are conservative source-architecture walking checks,
not an NPC navmesh or continuous player-physics simulation. Narrow valid routes
may be excluded; dynamic obstacles and future interactive props need additional
traversal data. The region overlay exposes splits for review.

`NativeRoomSourcePart` explicitly identifies collision, architecture and model
groups, replacing runtime name matching. `NativeRoomTransform.SourcePoint` supplies
the shared client-placement/server-collision transform with float vertex precision.
`NativeRoomTraversal` validates routes; `NativeRoomRecipeData` captures/restores
placements and reconstructs used sockets. Vertical bounds are measured from source
geometry instead of a generic 30 m envelope. The preparation tool validates before
publishing metadata/collision files.

## Loading and visibility

The extractor requests only distinct selected source rooms. Static-model loading
uses the same selection. Each template is built once and its placed copies share
meshes/materials. `AOIndoorVisualCache` retains decoded geometry/image bytes in RAM
under a 96 MiB budget, evicting least recently used room entries. Keys include
installation path, database fingerprint and playfield. Unity assets belong to the
active presentation and are disposed on exit; previews use immediate disposal.
Server catalog and source collision are cached by file revision.

Visibility retains nearby neighbors and conservatively follows doorways in the
camera frustum, allowing long visible passages across multiple graph edges. It
changes renderers/lights while floor-support colliders remain active. Load logs
report elapsed time, cached room count, memory accounting and hits.

## Native authoring window

Open **Tools > WorldGen > Native Dungeon Authoring**, also linked from the existing
Dungeon Authoring window. Work outside Play mode:

1. Browse source rooms and **Preview selected source room** to inspect actual local
   meshes/textures, doorway heights and walking regions.
2. Set seed/count/pool and **Generate textured native preview**. Region colors show
   connected walking areas. Collision overlays show bounds, not exact wireframes.
3. Exclude rooms/sockets, adjust fixture intensity/range/emission, and choose metal
   panels or black backing for closed passages. Source safety restrictions remain
   enforced; exclusions cannot make an unsafe port usable.
   Mark allowed uses with multiple selections (arena, encounter, connector, hub),
   set landmark/reviewed flags, comma-separated theme tags and notes. In a source
   preview, enable **Pick encounter center in Scene view**, then click a floor;
   it snaps within 0.75 m to an accessible validated walking sample. Escape
   cancels. Yellow markers and labels show these settings on placed copies.
4. **Validate 100 seeds** checks the selected configuration and writes a report in
   Unity's temporary cache directory.
5. Generate 12 rooms and **Pin current preview rooms**, increase to 24, then generate
   again. Existing placements/joins remain fixed while new rooms grow from unused
   safe sockets. Pinning retains the complete preview, rather than isolated rooms
   disconnected from their entrance route. Count cannot be smaller than the pinned
   layout. Clear pins for unrestricted generation.
6. Export/import resolved preview recipes to retain a favorite layout and overrides.
   These files are separate from backend archives.

The preview drives the gameplay loader through editor updates and checks configured
definition/collision hashes first. Objects are temporary and removed when the window
closes. It does not create a server instance or modify the outdoor environment.

## Apply preview settings to gameplay

**Save room tags / settings** writes through `INativeRoomAuthoringDao` to
`tools/WorldGen.NativeRoomCatalog/pf127.authoring.json`. Preview changes are local
until included in a prepared catalog. Saves are atomic and reject a stale authoring
revision; reload before resolving a conflict. The current provider is a local file
DAO, not a SQL database. From ProjectMayhem2.0:

```bash
dotnet run --project tools/WorldGen.NativeRoomCatalog/WorldGen.NativeRoomCatalog.csproj -- \
  '/path/to/Anarchy Online' '/path/to/127_v2.aois' '/tmp/pf127-native-rooms' \
  tools/WorldGen.NativeRoomCatalog/pf127.authoring.json
```

Copy the resulting JSON to Unity's `Assets/StreamingAssets/NativeCopies` and both
prepared files to the local server's `NativeCopies`, then publish/install with the
existing workflow. The local integration helper
`tools/patches/native-room-dungeons/apply-server.py` copies the server files from
`/tmp/pf127-native-rooms`. JSON byte hashes must match between client and server.
Changing settings changes the content revision; create a new named instance after
installation. Pinned layouts adjust the applicable count/seed validation cases.

## Saved backend recipes

Creating a native dungeon atomically saves seed, pool/count, generator version,
catalog hash, resolved placements/joins and layout hash. `.worldenter <name>` lazily
restores it after restart with a new runtime playfield ID. The manifest carries the
resolved recipe, so client and server validate/assemble the saved placements. Saved
names cannot be silently overwritten.

Storage uses `AO_REBIRTH_DUNGEON_STORE` when supplied; otherwise it uses
`STATE_DIRECTORY/NativeDungeons` on systemd or the user's local application-data
AORebirth directory. The current service uses `/var/lib/ao-rebirth/NativeDungeons`.
Writes flush a temporary file and atomically move it into place. Hashed filenames
keep instance names out of paths. Loading validates size, revisions, room references,
walking routes and layout hash. A different generator/catalog revision produces an
explicit rejection while retaining the file. Automatic migration and old-catalog
retention are not implemented. NPC/loot/quest state and AO rendering assets are not
stored in these layout files.

## Lighting and enclosure

Catalog preparation derives ceiling anchors above walking regions. Each fixture has
a housing, emissive diffuser and matching downward spotlight. Metadata controls
intensity/range/emission. Runtime budgets 64 illuminating lights and 12 nearest
eligible soft-shadow lights within 30 m. Emissive fixtures remain visible with their
room. Architecture uses two-sided shadow casting for thin native shells.

Unused ports retain shared physical seals; optional metal trim/black backing adds no
decorative obstacles. A scoped indoor environment disables directional lights,
removes the skybox, uses black camera background and reduces ambient/reflection
light, restoring prior settings on exit and between instances. The scene does not
assign `RenderSettings.sun`, so directional components must be handled explicitly.
Remaining openings reveal black void; this does not manufacture watertight source
geometry. Fixtures are generated fallback geometry, not recovered original light
records or authored GLB lamps. Bloom and light shafts are separate work.

## Verification and live acceptance

Catalog checks cover 100 seeds per pool plus 4/12/24-room recipes at seeds 1, 2,
90602 and 90603, actual floor agreement across joins, the raised U-turn connection
in four rotations, determinism and snapshot round trips. Focused shared tests cover
corrupt doorways, disconnected internal regions, exclusions, idempotent lighting,
four float transforms and pinned 12-to-24 growth. Local reader checks confirm room
selection, shared cached geometry, cache hits and reading only a missing template.

Build checks are Unity runtime/editor compilation and server publication/startup
validation. Backend fixture/regression suites and the game client are not launched
by the agent. Live acceptance remains: walk a fresh 24-room layout, inspect lamps
and previews, verify outdoor lighting restoration, and enter a saved name after a
local service restart. No cloud deployment or Git push occurs.

Final local build result: runtime/editor compilation passed, shared checks passed,
and server publication/startup validation passed. Client/published server catalog
SHA-256 is `99ab46806f56e373db98103b6b1355b8b62451d611d657e67edf2a7f4fd98328`.
The installation attempt stopped at terminal sudo authentication before changing
the service; the new build still needs the install command above.
