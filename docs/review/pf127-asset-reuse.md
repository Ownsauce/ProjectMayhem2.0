# PF 127: separate local AO-resource dungeon path

Inspected October 3, 2026 at the user's request. This is a read-only study and
initial inspection tool, not an implemented random dungeon generator.

## Confirmed source and inventory

Unity's `ProjectMayhem.AOInstallPath.v1` preference selects
`/home/cody/AOClientCopy/Anarchy Online`. The inspector opened that installation's
`cd_image/data/db/ResourceDatabase.idx` / segmented `.dat` database directly using
our existing `AOResourceDatabase` reader. It did not use the server database as
its source of room geometry.

- The installation contains 460,193 resource records.
- Playfield resource `1000001:127`: **Condemned Subway (dng)**.
- Indoor tilemap resource `1000009:126`; 46 room definitions.
- Existing local `127_v2.aois` extraction: 46 rooms, 11,883 surface meshes,
  198,132 triangles; tilemap 280 × 280, tile size 2, height scale 0.2.
- Surface resource identity is `1000013:((127 << 16) | roomIndex)`. The AOIS
  room `Instance` is the room index, not the complete database resource identity.
- The JSON report records resource presence, definitions, rotation, tile rectangles,
  raw geometry bounds, counts, candidate material fields and source hashes.

The surface counts come from an existing cached extraction, rather than a fresh
native extraction in this task. Matching room count and tilemap IDs do not prove
cache freshness. Material candidate fields are not confirmed texture references.

See [resource inventory](pf127-resource-inventory.json). Example room candidates:

| Room index | Room | Meshes | Triangles | Initial use |
|---|---|---:|---:|---|
| 0 | Ladies' Room | 64 | 1,162 | Small enclosed-room experiment |
| 8 | Entrance Stairs | 623 | 8,724 | Elevation connector after collision review |
| 27 | Subway Station | 1,134 | 19,661 | Large landmark chamber, later milestone |
| 38 | Cave Bridge | 515 | 7,932 | Transition family, later milestone |

The generated server placement export `AORebirth/docs/generated/playfields/placements/pf_127.json`
also describes four named districts: Abandoned Mall, Condemned Subway, Fractured
Sewers and Sewer Nucleus. That export is supplemental context; its spawn/district
records are not interchangeable with room surface meshes or reusable prop identities.

## Existing implementation to reuse

- `AO.Unity/Assets/Scripts/AO.Unity/Assets/AOInstallConfiguration.cs`: launch-selected
  installation validation and local asset-cache root.
- `AO.Assets/ResourceDatabase/AOResourceDatabase.cs`: direct resource lookup.
- `AO.Assets/Decoders/AOPlayfieldDefinitionDecoder.cs`: room definitions, tilemap ID,
  placement and quarter-turn rotation.
- `tools/AOIndoorExtractor/AOIndoorExtractor.cpp`: native materialization through
  the selected installation's 32-bit `DatabaseController.dll`, via Windows/Wine.
- `AO.Assets/Decoders/AOIndoorSurfaceStreamDecoder.cs`: extracted room surfaces and
  dungeon tilemap heights/collision.
- `AO.Assets/Navigation/AOIndoorDungeonTerrainBuilder.cs`: terrain/collision triangles
  from tilemap cells; room surface meshes alone are not the full walkable geometry.
- `tools/WorldGen.PFInspect`: new read-only inventory command, using existing readers.

Run from ProjectMayhem2.0:

```bash
dotnet run --project tools/WorldGen.PFInspect -- \
  '/home/cody/AOClientCopy/Anarchy Online' 127 \
  /tmp/pf127-inventory.json \
  '/home/cody/.config/unity3d/DefaultCompany/AO.Unity/AOAssetCache/IndoorSurfaces/40000000-8DDC1B5B51DB230-900000-8DDC1B5B51DB230/127_v2.aois'
```

The final argument is optional; without it the tool inventories room definitions.
It writes only the requested report, never modifies the AO installation, and does
not launch Unity or the AO client. This command ran successfully against PF 127.

## Recommended separate path

Start with **curated room templates**, not random individual surface meshes. Each
AO room comprises many surfaces, with baked material and spatial relationships.
Treating all 11,883 meshes as independent building blocks would lose that structure.

Create an AO-local content pack alongside the current Subway GLB pack. Store resource
references and authored metadata: stable template ID, content source/install revision,
room resource index, local origin, bounds, role, compatible themes, and optional props.
Resolve native resources from the same installation selected at launch. Keep server
layout generation independent of Unity and native DLLs; publish reviewed template
bounds, connectors and collision metadata to shared Core.

Author connector sockets explicitly: local position, facing, width, clearance,
floor elevation, connector family and allowed quarter-turns. Two room templates
can join only when these contracts agree. Translation and rotation come first;
arbitrary non-uniform scaling of baked rooms is unsuitable for the initial path.
Do not infer all doorway connectivity from room names or axis-aligned bounds.

For mixing with GLBs, place authored props in validated free space and use reviewed
transition modules between connector families. Keep AO room architecture/materials
intact initially. Replacing walls/floors within baked rooms requires selecting and
masking original surfaces, otherwise overlapping geometry and collision remain.

Extract both surface and tilemap collision into room-local coordinates, apply the
same placement transform to visuals and collision, and record portal openings.
Validate capsule travel across every join and elevation change. Do not assume
visual triangles or an enclosing box are sufficient navigation collision.

## Next implementation steps

1. Fresh extraction from the configured installation; verify material resolution,
   room transforms, and matching surface + tilemap collision in a separate preview.
2. Choose three to five small rooms/connectors; curate their sockets and clearances
   in a sidecar JSON catalog. Preview one room at a time before composing them.
3. Build a fixed two-room join and walk both directions. Validate stairs, portal
   clearance, opaque enclosure, spawn support and consistent client/server collision.
4. Generate a deterministic short chain from those approved templates. Add GLB props
   and one compatible GLB transition after the native-only chain works.
5. Extend Dungeon Authoring with content-pack selection and AO-template previews;
   include resource availability, budgets and connector validation.
6. Persist generated instance manifests/content revisions or restore them on login.
   The current in-memory instances disappear on zone-server restart, as observed.

The existing procedural Subway path remains available. The older reference-only
restriction in `docs/procedural.md` was scoped to that original kit; the user now
explicitly requests this separate resource-reuse path. Broader material replacement,
large station variants, and per-surface modularization follow the small-room prototype.

## Quick original-map baseline

The original PF 127 can use the existing normal-playfield path immediately; no
procedural seed or clone is required. The local server already has
`GameData/PlayfieldContent/127`, and `.tp` calls `PlayfieldManager.GetOrCreate`.
Unity's normal playfield loader falls back to the launch-selected AO installation,
decodes its PF definition, and loads the cached/native room surfaces and tilemap.

```text
.tp 184 108 252 127
```

This specifies x/y/z/playfield and targets the decoded Men's Room centre near
its 107.6-metre floor as an initial test location, rather than assuming an entrance
spawn. Clear a selected player target first: `.tp` can act on the selected player.
The command and load path were inspected; this particular Unity transfer has not
been accepted in-game yet. PF 127's current renderer includes diagnostic material
substitutions, so this is the original geometry baseline, not a claim of identical
original-client lighting/material rendering.

This enters actual PF 127; it is not a seeded isolated instance. A later template
pack/clone command should have its own identity and content source rather than
feeding the original map into the procedural-layout hash protocol. Test this
existing path first, then add a convenience creation/entry wrapper if needed.

## Seeded native copy implementation (2026-10-03)

The direct `.tp` attempt returned unknown PF 127 because the running server does
not have the required normal-playfield content directory. At the user's request,
a separate seeded copy path was implemented using the instance registry instead.
It does not require entering or registering normal PF 127.

```text
.worldgen pf127 90602 pf127-copy
.worldenter pf127-copy
```

This creates a distinct synthetic playfield ID and records the seed. It preserves
all 46 source rooms in their original arrangement; different seeds currently do
not shuffle rooms. The copy has no source NPC population or scripted original
zoning triggers. Room rearrangement and hybrid props are later milestones.

Before using those commands, install the prepared **local** server build:

```bash
bash /home/cody/Coding/ProjectMayhem2.0/tools/deploy-local-pf127-copy.sh
```

Then stop Play, Assets → Refresh, restart Play and reconnect. Recreate instances
after server restarts. The authoring preview is not required for this path.

Implementation locations:

- `tools/WorldGen.PFInspect/Program.cs`: optional fifth argument exports native
  collision plus metadata; combines room surface triangles and tilemap terrain.
- Sibling server `Core/WorldGeneration/NativeCopyPackage.cs`: loads the local
  package from `NativeCopies/pf127.json` / `.collision` in the published directory.
- Sibling server `Core/WorldGeneration/ProceduralInstanceService.cs`: native-copy
  manifest, seeded identity, instance registry and source spawn.
- Sibling server `Core/Commands/WorldGenCommand.cs`: native PF 127 command branch;
  the existing `.worldenter` command handles transfer and re-entry.
- Sibling server `Core/WorldSimulation/PlayfieldWorldSimulation.cs`: native triangle
  collision surface instead of procedural room-box collision.
- `AO.Unity/Assets/Scripts/AO.Unity/World/AOGameServerSession.cs`: distinct native
  manifest verification and entry path. Verifies source record and cached-surface
  hashes against the server metadata before loading, then checks spawn support.
- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWorldBootstrap.TransitionSpawn.cs`:
  load original PF 127 from the selected installation in AO world coordinates,
  while retaining the synthetic playfield identity for the session.
- `AO.Unity/Assets/Scripts/AO.Unity/Prototype/PrototypeClientUGUI.CharacterFlow.cs`:
  preserve an already loaded native copy during initial world-entry completion.

The native envelope uses generator `native-playfield-copy` version `1.0.0`, with
source/surface/content hashes. Its empty procedural room collections are transport
metadata, not an invented procedural reconstruction of PF 127. Actual rendering
and collision come from the source resources. Native content version mismatch
blocks entry rather than substituting generated rooms.

Validation: export produced 215,912 collision triangles, including terrain;
package length/header checked; a vertical intersection at the chosen spawn confirms
support at Y=107.6048. Local server publish/startup validation and Unity compilation
pass. The user subsequently confirmed the copied layout; textured presentation is tracked below.
Original-client materials/lighting parity is not claimed: the existing PF 127 renderer
has diagnostic material substitutions that can be improved once this baseline works.

## Texture investigation after native-copy acceptance

The user confirmed the copied room layout is correct but reported missing textures.
The current geometry baseline does **not** constitute a complete textured asset export.

Repository inspection identified three gaps:

1. `AOIndoorExtractor.cpp` exports triangle indices, XYZ positions and 11 opaque
   fields from each native surface mesh. It exports no UV coordinates or resolved
   visual-material bindings. It was originally designed for collision/navigation.
2. `AOIndoorSurfaceStreamDecoder` preserves those positions/indices/opaque fields;
   `ConvertDirectIndoorMesh` then converts only geometry into the Unity surface DTO.
3. `BuildIndoorRoomSurfaces` merges surfaces by floor/wall/ceiling classification and
   forces simple diagnostic materials for PF 127. It loses per-surface material
   separation rather than binding source textures.

A direct database audit across all 11 opaque field slots in all 11,883 extracted
meshes found zero matching identities in general texture type 1010004 or wall texture
1010009. These fields must not be used as texture IDs without understanding their
native layout. This does not prove PF 127 has no textures; it shows the collision
stream is insufficient to establish the visual resource dependency graph.

Next implementation priority is a **visual-resource extraction path**: resolve original
render mesh/material dependencies from the PF room records, retain UV coordinates and
material boundaries, extract referenced texture bytes using existing RDB readers,
and bind textured materials in a separate native-copy renderer. Confirm on one room
before exporting all 46. Merely disabling diagnostic colours or applying arbitrary
textures to the existing position-only meshes would not reproduce the original map.

The texture database reader/decoder already exists under
`AO.Unity/Assets/Scripts/AO.Unity/World/DirectOutdoor/AoImageTextureCache.cs`; resolving
PF room visual dependencies and texture mapping remains open. That initial collision-only investigation did not produce a textured room. The visual
export implementation below supersedes that limitation.


## Original visual export and Unity integration — 2026-10-03

This section records the initial export-based milestone. The runtime package path is
superseded by the on-demand database reader below. The user confirmed its appearance
was promising but reported missing floors/walls at the entrance and across many rooms.

The user confirmed the seeded copy layout, then requested original textures and our
own implementation using the source data. LostEden's indoor layout is a stub; its
image cache does not assemble PF 127. AODB 1.0.12 exposes the required visual-tile
resources, original UVs/normals and indoor material channels.

Implemented a distinct visual pipeline:

- `tools/AOIndoorVisualExtractor/AOIndoorVisualExtractor.cpp`: isolated, read-only
  native extraction. Reuses original tile selection, rotation, floor fallback,
  wall overlays and height deformation. Private native layouts/functions are
  guarded by the verified N3.dll hash; another build is refused before calling them.
- `AO.Assets/Decoders/AOIndoorVisualStreamDecoder.cs`: engine-independent validated
  AOVR decoder shared by the managed exporter and Unity. Preserves material groups,
  original UVs/normals, source room identity and visual dependencies.
- `tools/WorldGen.NativeVisualExport/`: image extraction, self-contained GLB export,
  dependency/image/geometry hashes and portable package verification.
- `tools/export-native-pf-visuals.sh`: repeatable local export command; no AO client,
  Unity editor launch, server deployment or cloud write is required.
- `AO.Unity/Assets/Scripts/AO.Unity/World/NativeIndoorVisuals.cs`: verifies a complete
  package against the currently selected AO installation, then builds textured URP
  meshes. Converts Direct3D UV V for Unity and owns generated meshes/materials/images.
- `NativeSurfaceCoverage.cs`: removes overlapping diagnostic render triangles while
  retaining uncovered geometry. Existing MeshCollider meshes and authoritative
  server surfaces are not replaced. Larger legacy helper-floor triangles are
  matched by their centres and horizontal normals so they cannot cover the source
  textured floor merely because their corners extend outside the authored room.
- `PrototypeWorldBootstrap.cs`, direct indoor-loading branch: attempts the prepared
  visual package after loading the existing collision/navigation baseline. A missing,
  partial or incompatible package preserves the diagnostic presentation.

Validated export: **46 rooms, 8,890 visual cells, 113,176 triangles, 104 original
textures and 101 visual-resource dependencies**. The first Ladies' Room contains
43 visual cells, 682 triangles and five textures. A standalone Blender import/render
confirmed source tile/wall textures; preview lighting was added only for inspection.
No new textures were painted or inferred from collision fields.

Generated assets are local and ignored by Git:
`exports/native/pf127/`. Individual GLBs use room-relative pivots from `OriginAo`
and glTF handedness; the AOVR stream retains original AO world coordinates for entry.
The complete package is installed at
`~/.config/unity3d/DefaultCompany/AO.Unity/AOAssetCache/IndoorVisuals/pf127/`.

Recreate/export with:

```bash
tools/export-native-pf-visuals.sh "/home/cody/AOClientCopy/Anarchy Online" 127 exports/native/pf127
```

Stop Play, Assets → Refresh, then start Play/reconnect and enter the existing copy:

```text
.worldenter pf127-copy
```

If the server restarted and forgot the instance, first recreate it:

```text
.worldgen pf127 90602 pf127-copy
```

The generation/entry commands and collision content hash are unchanged. This turn
changes local asset extraction and client presentation; there is no new server build
or cloud deployment.

Checks: managed exporter build; offline Unity AO.Assets and runtime compilation;
portable GLB/PNG/stream/hash validation; source floor alignment at the accepted
spawn `(184, 107.60483, 252)`; decoder corruption/truncation/index rejection; surface
matching checks covering raised props, wall-adjacent horizontal fixtures, gaps,
different levels and degenerates. In-game visual acceptance remains pending user
refresh/re-entry.

Next work:

1. Inspect texture orientation, wall coverage and stairs in the running native copy.
   Uncovered source geometry can still use diagnostic materials; it is not evidence
   that every original placed prop has been reconstructed.
2. Resolve original placed-object templates/ABIFF dependencies separately. AODB
   decodes 60 PF 127 dynel records (including door templates and placements); no
   original interactive door behaviour or prop population is enabled by this export.
3. Decode/apply original room lightmaps or supply authored fixture/emission lighting.
   Current exported native vertex colours are not treated as recovered lighting.
4. Add source-backed room entries to the reusable asset catalog with room origins,
   door connections/sockets, dimensions, compatible dungeon tags and source hashes.
5. Build rearrangement/hybrid generation on those room definitions. Current seeded
   copies still preserve the original room arrangement.

See [export tool instructions](../../tools/WorldGen.NativeVisualExport/README.md).

## On-demand local database presentation — 2026-10-03

The user requested runtime reads from each player's AO installation rather than
exporting/downloading or redistributing meshes and textures. This is now the indoor
client's presentation path. LostEden's outdoor texture cache follows the same direct
DB principle; its indoor layout implementation remains a stub.

- `AO.Assets/Conversion/AOIndoorVisualExtractor.cs` reads playfield definitions from
  the selected local DB, invokes our isolated native geometry reader on a background
  worker, validates the complete room stream and reads original image records into
  memory. Temporary plans/geometry are cleaned after success, cancellation or failure.
  Both helper output pipes drain concurrently; a five-minute limit and cancellation
  stop the helper. No geometry/image asset package is written or downloaded.
- `AO.Assets/Decoders/AOTexturePayloadDecoder.cs` validates general/wall texture record
  identity and returns the original encoded JPEG/PNG body. No AODB upgrade, converted
  PNGs or redistributed AO DLLs are needed in Unity.
- `NativeIndoorVisuals.BeginLoad` starts cancellable local work, then creates textures
  and rooms over successive Unity frames. Zone destruction releases owned assets.
  Diagnostic rendering stays visible until all source visuals are ready. Source
  colliders and the accepted server entry/movement path are retained.
- `tools/build-indoor-runtime-helpers.sh` builds **our own code** into StreamingAssets
  for both collision and visual readers. Unity builds include those helpers; they
  load AO DLLs from the player's selected installation. Current support is Windows
  and Linux/Wine with the verified N3.dll hash; other builds are explicitly rejected.
- The old `AOAssetCache/IndoorVisuals/pf127` package is ignored. Local GLB/PNG exports
  remain optional diagnostics under Git-ignored `exports/`, never required for entry.

The reported broad texture gaps also exposed a positioning defect in the initial
reader: it passed Y offset zero to the tile emitter. AO's room builder subtracts
the minimum occupied-cell heightmap elevation. Omitting that step put several rooms
8–20 units above the existing collision presentation. The native helper now applies
the same baseline and ignores type-zero cells when finding it. This preserves the
first bathroom/entrance levels and corrects bridges, tunnels, lower rooms and ramps.

The diagnostic surface matcher initially allowed 0.25 AO units, accounting for authored
collision skins approximately 0.2 units in front of source walls/floors. It retains
raised/uncovered geometry and tests normal direction for horizontal matching. The
combined changes match 124,986 of 198,132 captured source collision triangles,
compared with 48,653 in the initial implementation. Those are geometry-overlap
counts, not evidence of complete placed-prop or lighting reconstruction.

Validation: direct live DB read independently resolves 46 rooms, 113,176 triangles,
104 decodable original images and 101 visual dependencies. All room minima remain
near their authored Y placement; corruption/identity rejection, collision shell
matching and temporary-file cleanup checks pass. Unity runtime/AO.Assets compile
offline without opening another editor. The corrected runtime needs in-game review
after Stop Play → Assets Refresh → Play/reconnect → `.worldenter pf127-copy`.

For future hybrid/random dungeons, store resource IDs, source room IDs, seed and
placement transforms in a recipe. Clients resolve them through this local DB reader;
the server must derive matching authoritative collision/layout from the same recipe.
The current seeded PF 127 copy still preserves the original arrangement. Room sockets,
rearrangement, interactive dynels and original lighting remain next steps. Static ABIFF
placements are now supported by the change below.

## White collision cages and local static models — 2026-10-03

The user's three screenshots showed large white/gray surfaces in the spawn bathroom,
nearby dome and passage. These were diagnostic collision meshes with flat materials,
not source surfaces whose textures failed to decode. The overlap filter retained
too much of that collision geometry. It is superseded for runtime presentation:
after complete architecture/model construction succeeds, **all diagnostic room-surface
MeshRenderers are disabled**. Their MeshColliders and meshes stay intact. A failed
presentation build still leaves the existing diagnostic world available.

The selected installation also contains `cd_image/data/statels/127.pf`. Its indoor
format differs from outdoor placement files: a version and one absolute offset per
room, opaque compressed lightmaps, short records, two full model lists, and trailing
light/sound/fog data. We decode the full model lists, preserving position, packed
rotation/scale/shear and texture overrides. Short records and the trailing lighting
records are not reconstructed by this change.

- `AO.Assets/Decoders/AOIndoorStatelPlacementDecoder.cs` reads and validates the
  indoor placement blocks, bounded by each room offset.
- `DirectOutdoor/Statel/StatelParser.cs` adds the indoor path to the existing ABIFF
  mesh/material pipeline. Room-local positions and rotations receive the source
  room's quarter-turn and origin; the runtime parent applies coordinate centering
  and scale. Occlusion/collision-only model names remain excluded.
- `NativeIndoorVisuals.cs` builds those static models alongside the tiled architecture
  and hides collision rendering only once both are ready. Nested coroutine failures
  use the same rollback path. World destruction releases model meshes, textures,
  materials and the model database reader.

Validation against the actual local placement file: **2,247 records / 246 unique mesh
IDs**, of which **2,244 placements / 245 mesh IDs** survive the occlusion filter.
Every visible mesh resource decodes with the same AODB plugin assemblies used by
Unity; all triangle indices are valid. All **476 unique referenced images**, including
texture overrides and emission references, decode. The spawn bathroom has 37 model
placements (including its original toilet booths, sinks, pipes, mirrors and fixtures);
the nearby Grand Dome has 78. Synthetic tests reject bad versions, offsets, room
counts, truncated overrides and nonfinite positions. AO.Assets and the Unity runtime
compile offline; no second Unity editor was opened.

```bash
dotnet run --project tests/WorldGen.NativeVisual.Tests.csproj \
  -p:UnityManagedPath=/home/cody/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Managed/UnityEngine \
  -- --statels '/home/cody/AOClientCopy/Anarchy Online'
```

This uses the player's local file/database directly and creates presentation in memory;
no original asset package is downloaded, installed into the repo or redistributed.
The optional tile GLB exporter still exports architecture only. No server rebuild or
collision regeneration is required for this presentation fix.

Next review: Stop Play → Assets Refresh → Play/reconnect → `.worldenter pf127-copy`.
If the local server was restarted, first recreate with `.worldgen pf127 90602 pf127-copy`.
Check the same three locations for source model orientation/height, texture coverage
and traversal. These changes have not yet been accepted in-game. Remaining work is
interactive doors/dynels, lightmap/light reconstruction, water, source-reference room
catalogs/sockets and deterministic room rearrangement with matching server collision.

## Entrance floor support — 2026-10-03

The user confirmed the updated textures look almost perfect, with no missing textures
observed, then reported a short fall near the entrance at AO **(91, 107, 326)**.
This falls in Entrance Stairs, room 8. Offline probes find a source tile floor at
Y **107.60483**; the exported server terrain also contains support at that elevation.
The live Unity failure has not been reproduced here, so the existing imports' exact
failure mechanism is not established.

The client now supplements imported collision with upward floor/stair triangles from
the **same tile geometry used for rendering**, through
`AO.Assets/Navigation/AOIndoorVisualFloorBuilder.cs`. It excludes ceilings, walls,
degenerate faces and faces below the ground probe's normal cutoff. Each room gets
an owned `FloorSupport` mesh collider under the presentation root, using the same
centering/scale as the visible geometry. These colliders activate with presentation;
`Physics.SyncTransforms()` makes them available immediately. Existing collision and
the server package are retained. There is no coordinate-specific patch or new floor
level, and static prop collision is still separate.

Verification: the source creates **33,499** support triangles across the reference
map. Point probes at (91,326), (90,326), (92,326), (91,325), (91,327), (89,324) and
(93,328) all find the expected 107.60483 elevation. Synthetic floor/ramp elevation
and ceiling/wall/degenerate/steep-face exclusion checks pass, as do offline AO.Assets
and Unity runtime builds. No server changes or restart are required.

Next live check: Stop Play → Assets Refresh → Play/reconnect → `.worldenter pf127-copy`.
Walk over the reported spot and around the adjacent entrance tiles, then check stairs
and landings for unintended height changes. The user subsequently reported falling
through the stairs at (97,107,268); tile support alone was insufficient, as below.

## Mixed native stair collision — 2026-10-03

The follow-up coordinate **(97,107,268)** exposed the exact import defect. Entrance
Stairs room 8, native surface mesh 486, contains both vertical faces and an upward
ramp. `ClassifyIndoorSurface` averaged the first eight triangle normals and labeled
the whole mesh `Wall`. Direct indoor loading disables wall colliders, so it also
discarded the ramp. The server's full native collision package already retains it.
At X=97/Z=268 the ramp is at **Y=111.1575**, above the tile floor at **107.60483**.
The previous floor-support change protected that lower floor, not the stair ramp.

`NativeIndoorSurfaceTriangles.cs` now splits each direct native surface into floor,
wall and ceiling faces individually, using the existing 0.65 normal threshold.
`BuildIndoorRoomSurfaces` uses that split for its direct/native horizontal-collider
path. The ordinary JSON import retains its previous classification. Horizontal
faces inside mixed meshes now reach the floor/ceiling colliders, while vertical
faces remain in the wall group. No coordinates or model names are patched into
runtime behavior. Textured presentation and the server package are unchanged.
The surface-load log reports `perTriangleClassification=True` for this path.

Regression checks reproduce the old stair mesh's wall classification, then confirm
the ramp survives at five reported/nearby points. **96 probes** across its full
six-unit width and eight-unit run match the original collision surface heights.
Combined tile/native support also covers both landings, at Z=260.5 (Y≈107.605) and
Z=269.5 (Y≈111.605). Across all captured rooms, partitioning retains all 198,132
source faces: 31,744 floor, 131,475 wall and 34,913 ceiling triangles. Synthetic
mixed floor/ramp/wall/ceiling checks and the offline Unity runtime build pass.

```bash
dotnet run --project tests/WorldGen.NativeVisual.Tests.csproj \
  -p:UnityManagedPath=/home/cody/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Managed/UnityEngine \
  -- --native-collision \
  '/home/cody/.config/unity3d/DefaultCompany/AO.Unity/AOAssetCache/IndoorSurfaces/40000000-8DDC1B5B51DB230-900000-8DDC1B5B51DB230/127_v2.aois' \
  exports/native/pf127/pf127.visual.aovr
```

Next: Stop Play → Assets Refresh → Play/reconnect → `.worldenter pf127-copy`, then
walk the stairs up and down near X=97/Z=268 and through both landings. This is a
local client fix; no server restart/regeneration is needed. The user subsequently
confirmed traversal without falling through, accepting this collision baseline.

## Seeded native room dungeons — 2026-10-03

The user accepted the mixed stair collision fix: entrance floors and stairs now
traverse without falling through. Textured PF 127 is the baseline for this separate
room-rearrangement path. The original `.worldgen pf127` copy still preserves the
source arrangement; the new command actually assembles a different room graph.

From **ProjectMayhem2.0**, install the compiled local server build:

```bash
./tools/deploy-local-pf127-rooms.sh
```

This uses the existing local release/configuration mechanism, validates startup,
restarts only `ao-rebirth-zoneengine-new`, and restores the previous release if the
new one exits. It clears in-memory generated instances. No cloud deployment or
Git push occurs. The agent's installation attempt stopped at sudo authentication
before making changes; the local terminal must supply the sudo password. The
published build is ready under AORebirth's existing LinuxBuild artifacts directory.

Then Stop Play → **Assets > Refresh** → Play/reconnect, and run these in game chat:

```text
.worldgen pf127-rooms 90602 12 pf127-random mixed
.worldenter pf127-random
```

Another seed and ID produce another layout:

```text
.worldgen pf127-rooms 90603 12 pf127-random-b mall
.worldenter pf127-random-b
```

Syntax: `.worldgen pf127-rooms <seed> <4–24 rooms> <instance-id> [mixed|mall|depths]`.
`mixed` is the default. `mall` selects source rooms 0–27; `depths` selects source
rooms 28–45. Every layout starts with Entrance Stairs, source room 8, and reuses
rooms as necessary. Use a new instance ID for a new generation, and `.worldexit`
to return to the entry playfield. The authoring window is not required.

Implementation:

- Shared `WorldGen.Core/Dungeons/NativeRoomDungeon.cs` uses the existing deterministic
  random generator, manifest, layout and instance interfaces. It joins equally wide
  native door sockets with opposite facing, aligns their floor heights, rotates
  rooms by quarter turns, rejects overlapping nominal room footprints and retries
  bounded attempts. The result is a connected tree. It seals every unused doorway,
  including external exits, with a visible solid panel and matching server collision.
- `AOPlayfieldDefinitionDecoder` now retains the source door records. The catalog
  tool decodes tile position/facing, groups contiguous doorway cells, samples native
  floor heights, and records source origins, footprints, pools and spawn offsets.
  The first catalog has 46 templates and 115 sockets.
- `AO.Unity/Assets/StreamingAssets/NativeCopies/pf127.rooms.json` contains room
  metadata and source hashes. It contains no texture or rendering mesh pack.
  `NativeRoomDungeonRuntime.cs` places borrowed room meshes using the recipe.
  `NativeIndoorVisuals.cs` continues reading architecture, images and static models
  from the launch-selected local AO installation; `StatelParser` groups indoor
  model placements by source room so props receive the same transform as floors,
  stairs and walls. Original source objects remain inactive while their shared
  meshes/materials are owned by the runtime loader.
- `PrototypeWorldBootstrap` composes collision before spawn, retaining the accepted
  per-face stair classification. The new recipe path normalizes tile height baselines
  and omits SharpNav helper colliders. Original-copy behavior is retained.
  Rendering-derived floor support is also placed with its corresponding room.
- `AOGameServerSession` verifies the local catalog, selected PF definition and
  captured source collision hashes, regenerates the shared recipe and checks its
  layout hash before entry. Initial entry and subsequent transfers recognize the
  native room generator and retain its presentation instead of drawing generic rooms.
- Local AORebirth `ProceduralInstanceService`, `WorldGenCommand`,
  `NativeRoomPackage` and `PlayfieldWorldSimulation` register the new generator,
  transform per-room native/normalized terrain triangles and add the same seals.
  Its local `NativeCopies/pf127.rooms.collision` is physics data; the client reads
  its own local rendering resources. Existing `.worldenter`/`.worldexit` commands
  and the synthetic playfield registry are reused.

Validation: AO.Assets, shared Core and offline Unity runtime compilation pass;
AORebirth publishes and reports `ZONEENGINE_NEW_STARTUP_VALIDATION_OK`. There are
100 distinct 12-room layouts for each of the three pools across seeds 1–100.
Additional 4/12/24-room checks pass for seeds 1, 2, 90602 and 90603 in all pools,
including layout construction and repeat-recipe hashes. All non-exterior sockets
have floor support at the inward doorway probe. Catalog bytes match between Unity
and the server (SHA-256 `9b51476109e9e9dd02a00348cdba797e5739e1703700e1627a71fb4f329aefde`).
These checks establish recipe/placement invariants, not full live traversal of every
new room combination. The client was not launched by the agent.

Next live acceptance: generate the mixed 12-room example, walk the entrance stairs,
all room joins and dead ends, then try mall/depths and a different seed. Report
instance ID/seed and AO coordinates for any clipping or fall. Nominal footprint
checks do not prove that every decorative overhang or prop clears every possible
neighbor. Large recipes may reject a seed after bounded retries; try another seed
or fewer rooms if the command reports that no layout fits.

Next content work: curated entrance/branch/boss room roles and weights; validated
room-connection compatibility and capsule clearance; objectives, encounters and
loot; per-instance switches such as branch count, room repetition and difficulty;
loops and authored layouts; then hybrid placement of our own models. Original AO
lights, interactive dynels and water remain deferred. Ceiling fixture lighting is
still a separate follow-up. Loading currently prepares the original source room
set before placing selected instances; selected-room loading/cache optimization
can follow after live acceptance of this first composition path.

## Generated entrance spawn floor — 2026-10-03

The user accepted the appearance of the new room dungeon, but reported spawning
under the entrance floor. Source probes expose three upward surfaces at the authored
entrance X/Z (91,326): terrain at Y≈107.60483, the upper entrance landing at
Y≈115.605, and roof caps at Y≈123.705. The initial catalog used `SpawnY=80` relative
to room origin Y≈107.605, choosing the buried terrain instead of the landing.
This is a spawn-layer defect; the upper landing already exists in retained native
collision (room 8, source mesh 19).

Catalog preparation now derives entrance Y from source triangles. At the authored
X/Z it selects the highest supported interior landing, verifies nine footprint
probes for the 0.5 m radius capsule, and requires room for the 1.8 m character below
the first downward ceiling face. The unroofed roof caps are rejected. The resulting
spawn is **AO (91,115.685,326)**, with the existing server handoff clearance placing
the initial transfer at Y≈115.835. Both Unity and the server use the updated catalog
(SHA-256 `2ea6cb446d8316888427d3fa74f64bcd191da311e3ffd93fe531046f16b8b235`).
The client entry support ray also now accepts only colliders under the active
playfield root, excluding old-world collision pending destruction.

Reused rooms retain the earlier per-face native floor/ramp classification and the
rendering-derived tile support; both receive the room's rotation and translation.
Captured-source checks confirm the upper landing across the capsule footprint and
landing/ramp support after all four quarter turns plus a height/position offset.
The existing full stair/landing checks pass. Catalog generation still passes all
three pools' 100-seed checks and the additional 4/12/24-room recipe checks. The
Unity runtime compiles; the local server publishes and passes startup validation.
No AO client was launched by the agent.

Install the updated published server from ProjectMayhem2.0 with
`./tools/deploy-local-pf127-rooms.sh`, then Stop Play → Assets Refresh → Play/reconnect
and regenerate/enter the dungeon. The agent's installation attempt again stopped
at sudo authentication before changing the service. Generated instances are
in-memory and must be recreated after the restart. Live spawn acceptance is pending.

Runtime resource clarification: on entry, `NativeIndoorVisuals` calls the local
`AOIndoorVisualExtractor`, which reads the selected AO installation's PF definition,
architecture and images, then constructs Unity meshes/materials. `StatelParser`
reads local `cd_image/data/statels/127.pf`, meshes and texture references. There is
**no network mesh/texture download** for this path and no exported rendering asset
pack is required. Local source collision is cached as AOIS; the helper briefly
writes a decoded geometry stream in a unique temporary folder and deletes the
folder after reading it. The backend uses prepared local collision data. Seed and
catalog metadata are shared separately from rendering resources. Lost Eden's
`ResourceDatabase` initializes `RdbController` with the local installation path,
and its outdoor `TerrainParser` reads tilemaps/textures from that controller;
this is the same general local-resource rendering approach, with a different
indoor decoder implementation.

## Raised doorway alignment — 2026-10-03

The user accepted the entrance spawn correction, then reported an impassable drop
in the 24-room layout near generated U-turn West 11 and Mini to ramp 3x8 connector
1 16. The source U-turn West template (room 16) has a raised exit at Y≈106.705,
above terrain at Y≈102.8064. The old floor probe only searched near the terrain
baseline and recorded socket 0 at relative Y=0 instead of **3899 mm**. Neighboring
rooms were therefore placed about 3.9 m too low: their ceiling obstructed the
passage and their floor created the drop. Other exits in that same template are
lower; moving the whole source template to a single floor level would be wrong.

`tools/WorldGen.NativeRoomCatalog/NativeDoorwayAnalyzer.cs` now resolves each
socket from captured source triangles. It checks floor support across the capsule
footprint inside the opening, at least 1.9 m of headroom where a ceiling exists,
and crossing segments at three body heights. An interior roof is required for the
candidate floor, while a short unroofed doorway seam is allowed. Crossing checks
reject upward-facing lintel surfaces that otherwise resemble an upper landing.
The socket height is measured just inside the threshold. `Clearance` and `Blocked`
are retained in **catalog version 2**, and the shared generator excludes blocked
sockets. All 115 current sockets were resolved; one is exterior and none needed
to be blocked. Existing width/facing matching and native collision transformations
then align neighboring room floors using the corrected offsets.

The shared implementation is in
`../WorldGen/WorldGen.Core/Dungeons/NativeRoomDungeon.cs`, with a reviewable copy in
`tools/patches/native-room-dungeons/NativeRoomDungeon.cs`. **Generator version
1.1.0** is used consistently by the client and local server. The client catalog
and published server catalog share SHA-256
`cfed11ce6cea9511fc634e780df808a47437c44d872d33e2d4a946c7df788482`.
The earlier entrance spawn and per-face native stair collision fixes remain in use.

Validation compares captured floor heights 0.05 m inside each side of every join,
requiring agreement within 0.025 m. This passes the 100-seed checks for each of
the three pools, plus 4/12/24-room recipes at seeds 1, 2, 90602 and 90603. Repeated
recipes remain deterministic. The reported raised U-turn/socket-to-connector
connection passes all four quarter turns at joint Y≈111.339, and every usable
doorway has inward support. Unity runtime compilation passes; the local server
publishes and passes startup validation. In-game traversal of this correction
still needs user verification.

The agent attempted the local installer, but sudo required a terminal password
and stopped before any installation or service change. From **ProjectMayhem2.0**:

```bash
./tools/deploy-local-pf127-rooms.sh
```

Then Stop Play → Assets Refresh → Play/reconnect and create a fresh instance:

```text
.worldgen pf127-rooms 90602 24 pf127-24-fix mixed
.worldenter pf127-24-fix
```

The local service restart clears in-memory instances. Existing generated rooms
must be regenerated with the updated catalog; refreshing Unity alone cannot move
the old server recipe to the corrected doorway heights. No cloud deployment or
Git push was performed.

## Traversal, authoring and saved recipes — 2026-10-03

The user accepted the raised doorway correction and authorized improvements 1, 4,
5, 6, 7 and 8. Generator 1.2.0 uses catalog version 3. The implementation, source
responsibilities, saved-recipe storage, native preview/pinning workflow, lighting
and remaining live checks are documented in [native dungeon authoring](../native-dungeon-authoring.md).

Changes live in the shared `WorldGen.Core/Dungeons/NativeRoom*.cs`, the PM catalog
tool and `AOIndoorVisualCache`, Unity native assembly/visibility/lighting components
and the new editor window. Local server changes are `NativeRoomPackage`,
`NativeRoomArchive` and the native methods in `ProceduralInstanceService`; reviewed
source copies/integration helper remain under `tools/patches/native-room-dungeons`.
The source preparation checks actual geometry, shared checks exercise corrupted
recipes and pinned growth, and local reader checks exercise selected-room cache
reuse. No backend fixture suite or AO client is started by the agent.

The final catalog includes 94,736 retained walking samples and 1,185 fixture
anchors across all 46 source templates; generation loads only its selected
templates and rendering uses light/shadow budgets. Matching client/published
server catalog SHA-256 is `99ab46806f56e373db98103b6b1355b8b62451d611d657e67edf2a7f4fd98328`.
Runtime/editor compilation, shared checks and server publish/startup validation
pass. Local installation stopped at sudo authentication before service changes;
terminal installation and live acceptance remain pending.
