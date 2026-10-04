# Procedural World and Dungeon Generation Architecture

> Implementation progress and the prioritized backlog are tracked in
> [Procedural Generation: Current State and Roadmap](procedural-status-and-roadmap.md).

## Purpose

Build a deterministic, modular world-generation framework shared by the Unity client and AORebirth server backend. It should support custom AO-style content now and remain usable if the project eventually becomes independent of AO and RDB assets.

The first production target is an instanced mission dungeon. The same foundation can later support buildings, settlements, outdoor playfields, and large streamed worlds.

Three presentation modes must be interchangeable:

- **Procedural:** no RDB assets are required.
- **RDB:** logical content is represented with existing AO assets.
- **Hybrid:** each category independently selects procedural, RDB, or custom authored content.

The server remains authoritative over gameplay. The client constructs visual geometry from compact, versioned generation data rather than receiving large meshes.

## Guiding Principles

1. Generate a logical world before creating Unity objects.
2. Keep deterministic generation in a shared, pure .NET library.
3. Keep generation separate from asset selection and rendering.
4. Give every gameplay-relevant generated object a stable identity.
5. Treat released generator versions as immutable.
6. Store persistent changes as an overlay on a generated base world.
7. Give the server authoritative collision, navigation, and line-of-sight data.
8. Complete one dungeon vertical slice before attempting a procedural planet.
9. Make expensive client work asynchronous, cancellable, cached, and frame-budgeted.
10. Every generator must expose an engine-neutral inspection layout: named spatial
    regions, boundaries, connections/openings, elevation, and stable IDs. Development
    clients must be able to render that contract as a labeled top-down overview.

## Layout Inspection Contract

Layout inspection is a standard WorldGen capability, not dungeon-specific presentation.
The current dungeon adapter displays Core room footprints, corridors, and portal records
through `.worldgen layout`. Repeated semantic module names receive deterministic zero-based
suffixes in logical-index order so a reported problem identifies one exact module.

Future building, settlement, and outdoor/playfield generators must publish equivalent
engine-neutral inspection records. At minimum these records identify named region outlines,
connections and traversable openings, stable IDs, elevation/layer, and relevant semantic
kind. Unity, headless exporters, and future engine adapters may render these records
differently, but must not infer a separate diagnostic topology from presentation meshes.

## System Boundaries

```text
AORebirth
├── Creates instances and generation manifests
├── Runs shared logical generation
├── Owns gameplay, collision, NPCs, loot, and persistence
└── Sends manifests and state deltas
             │
             ▼
AO.WorldGen.Core (pure .NET)
├── Deterministic RNG and seed derivation
├── Versioned manifests and parameters
├── Dungeon/world graphs and spatial layouts
├── Stable object IDs
├── Logical collision/navigation data
└── Validation and content hashing
             │
             ▼
Unity Client
├── Runs compatible shared generation
├── Resolves logical asset requests
├── Builds or loads meshes and materials
├── Streams chunks and LODs
└── Applies authoritative state deltas
```

Unity types such as `GameObject`, `Transform`, `Mesh`, and `Material` must not appear in the shared deterministic core.

Recommended projects:

```text
AO.WorldGen.Core
├── Determinism, contracts, spatial types
├── Dungeons, terrain, and structures
├── Collision and navigation
└── Validation and hashing

AO.WorldGen.Server
├── AORebirth integration and protocol
├── Instance lifecycle
├── Persistence
└── Encounter and spawn state

AO.WorldGen.Unity
├── Mesh builders and asset providers
├── Materials and shaders
├── Streaming, LOD, pooling, and caching
└── Editor and debug tooling
```

## Generation Versus Asset Resolution

A generator decides **what exists**. An asset provider decides **how it looks**.

A generated door might contain:

```text
Stable ID: instance/room-07/door-02
Transform and bounds
Blocking/lock state
Style tags: industrial, security
Gameplay tags: mission-critical
Asset request: architecture/door/industrial/security
```

The dungeon generator must not directly load an RDB mesh or construct a Unity mesh.

```csharp
public interface IAssetProvider
{
    bool CanResolve(AssetRequest request);
    ValueTask<AssetHandle> ResolveAsync(
        AssetRequest request,
        CancellationToken cancellationToken);
}
```

Expected implementations:

- `ProceduralAssetProvider`
- `RdbAssetProvider`
- `CustomAssetProvider`
- `HybridAssetProvider`

`HybridAssetProvider` routes logical requests by category and tags. It does not contain world-generation logic.

Example configuration:

```text
Terrain       = Procedural
Buildings     = Procedural
DungeonShells = Procedural
Doors         = RDB
Props         = RDB
Characters    = RDB
Vegetation    = Procedural
Materials     = Procedural
```

Logical asset IDs and catalogs must be used instead of scattering file paths or RDB IDs throughout generators.

## Determinism Contract

The same manifest and compatible content catalog must produce the same logical world on server and client.

A manifest contains:

```text
Generator ID and semantic version
Parameter schema version
World/instance seed
Region, chunk, or instance ID
Generation parameters and feature flags
Content-pack IDs and hashes
Asset-catalog version
Persistence revision
```

Rules:

- Do not use `UnityEngine.Random` or uncontrolled `System.Random`.
- Implement a documented and tested PRNG in the shared core.
- Never depend on dictionary, hash-set, filesystem, reflection, or thread order.
- Sort candidates by explicit stable keys before selection.
- Quantize gameplay-relevant coordinates and rotations.
- Avoid platform-dependent floating-point decisions that affect topology.
- Use deterministic tie-breaking for graphs, placement, and pathfinding.
- Never silently alter a released generator version.

Derive independent named random streams:

```text
InstanceSeed
├── layout
├── architecture
├── encounters
├── loot-baseline
├── decoration
└── vegetation
```

Adding decoration must not move a boss, change loot, or alter connectivity.

Every gameplay object needs a stable ID derived from logical structure, not generation order or position:

```text
instance-9f31/room-07/door-02
instance-9f31/encounter-04/mob-03
instance-9f31/room-11/chest-01
world/region-12/chunk-08/resource-iron-04
```

## Server Authority

AORebirth owns:

- Manifests, instance lifecycle, and membership
- Player movement validation
- Logical collision and line of sight
- Walkability and NPC navigation
- Doors, lifts, portals, and traps
- NPCs, encounters, loot, resources, and quests
- Destruction and persistent changes
- Player-built structures if supported later

The server must not trust client-generated MeshColliders. Shared generation should produce lightweight authoritative geometry:

- Room/building bounds
- Wall segments and blocking volumes
- Door portals
- Walkable polygons or navigation cells
- Terrain height samples
- Water and hazard volumes
- Spawn-safe regions
- Line-of-sight blockers

Unity creates detailed render and local collision meshes from the same logical description, while gameplay decisions use server geometry.

## AORebirth Protocol

Procedural content requires capability negotiation because original AO clients cannot understand custom generated playfields.

Suggested capabilities:

```text
procedural-manifest-v1
generated-instance-state-v1
custom-content-catalog-v1
```

During zoning, AORebirth sends:

```text
Instance and template/playfield IDs
Generator ID, version, seed, and parameters
Content catalog hash
Persistent-state revision
Validated player spawn
Required content-pack IDs
```

After generation, the server sends state deltas rather than geometry replacements:

- Door state
- NPC state and movement
- Chest/loot state
- Trap activation
- Object destruction or repair
- Quest changes
- Player construction changes

Server and client compare a deterministic logical-layout hash. A mismatch must prevent entry or fail safely rather than allow divergent collision.

## First Vertical Slice: AO-Style Mission Dungeon

Instanced missions fit AO's playfield model and exercise the architecture without requiring an enormous outdoor system.

Lifecycle:

1. A mission is accepted.
2. AORebirth creates an instance ID and immutable manifest.
3. Shared generation creates and validates the logical dungeon.
4. The server initializes collision, navigation, doors, encounters, objectives, and loot.
5. Unity receives the manifest and creates its presentation.
6. Unity acknowledges layout hash and gameplay-critical readiness.
7. AORebirth places the player at a validated entrance.
8. State deltas update the presentation.
9. Completion, expiry, or abandonment retires the instance.

```text
Mission parameters
        ↓
Dungeon graph and progression rules
        ↓
Connectivity validation
        ↓
Room/corridor spatial embedding
        ↓
Overlap and clearance validation
        ↓
Doors, stairs, portals, and vertical links
        ↓
Collision/navigation representation
        ↓
Encounters, objectives, and loot anchors
        ↓
Asset requests and decoration anchors
        ↓
Server state + Unity presentation
```

Support entrance/exit, critical path, branches, dead ends, lock/key gates, side rooms, treasure rooms, objectives, bosses, secrets, multiple floors, route-length constraints, and guaranteed reachability.

## Procedural Geometry

Unity mesh infrastructure should support vertices, indices, normals, tangents, UVs, material regions, bounds, simplified collision, known LOD variants, mesh combination, and instancing metadata.

Reusable primitives include:

- Plane, polygon, box, and beveled box
- Wall, floor, and ceiling
- Cylinder, column, arch, and doorway
- Stairs and ramps
- Spline extrusion
- Road and path strips
- Cliff and rock base forms

Higher-level generators compose primitives. A wall does not know it belongs to a dungeon, and a dungeon does not know how Unity constructs a wall mesh.

Runtime mesh simplification is not required initially. Prefer deterministic known LOD topology or offline-generated and cached LODs.

## Materials and Textures

Start with a small art-directable material system:

- Parameterized shader families
- Tileable base materials
- Trim sheets and atlases
- Deterministic masks and vertex colors
- Decals and detail normals
- Dirt, rust, moss, snow, damage, and wear overlays

Selection can depend on style, material type, biome, elevation, slope, moisture, temperature, age, damage, and seeded variation.

Procedural base-color, normal, roughness, metallic, AO, and height-map generation can be added later. Expensive texture synthesis should be cached or performed offline when practical.

## Buildings and Outdoor Worlds

After the dungeon vertical slice, reuse room and architecture tools for buildings. A logical building can define footprints, floors, rooms, hallways, doors, windows, stairs, lifts, roofs, entrances, decoration anchors, collision, and navigation.

Large interiors can remain separate AO-style instances rather than requiring seamless geometry.

Outdoor generation is a later milestone:

```text
Continentalness and macro shape
        ↓
Mountains, valleys, and basins
        ↓
Domain warping and medium features
        ↓
Hydrology and erosion data
        ↓
Heightfield and water bodies
        ↓
Biomes
        ↓
Roads, settlements, and landmarks
        ↓
Chunk meshes, materials, collision, and decoration
```

The terrain system should allow multi-octave, ridged, and warped noise; moisture; temperature; elevation; slope; flow accumulation; and authored constraints.

Chunk rules:

- Share or identically derive border samples.
- Give cross-chunk features one deterministic owner.
- Plan rivers and roads at region scope before chunk meshing.
- Prevent border decoration duplication.
- Stitch LODs or use skirts without gameplay gaps.
- Design coordinate partitioning or floating origin before huge worlds.

## Water and Environment

Logical generation identifies oceans, lakes, rivers, ponds, shorelines, depth, and gameplay properties. Unity chooses the visual implementation.

Vegetation, rocks, grass, debris, and clutter should use biome, soil, moisture, temperature, elevation, slope, water distance, roads, structures, density masks, exclusion masks, and deterministic spacing rules.

Use GPU instancing or indirect rendering where appropriate. Do not create a heavyweight GameObject per grass blade or distant decoration.

## Persistence

```text
Base world = Manifest + immutable generator + content catalog
Current world = Base world + ordered persistent changes
```

Persistent records contain world/instance ID, generator/schema version, stable object ID, change type, payload, and revision. Examples include doors, looted chests, harvested resources, defeated NPCs, damage, moved objects, quests, and player construction.

Generator migrations must be explicit. Never apply old deltas to a new layout merely because coordinates appear similar. Temporary missions can use short-lived overlays; permanent worlds require durable storage and migration tools.

## Streaming and Performance

```text
World
└── Region
    └── Chunk or instance cell
        ├── Logical data
        ├── Terrain/architecture
        ├── Props/vegetation
        ├── Gameplay objects
        └── Persistent-state overlay
```

Requirements:

- Background pure-data and mesh preparation
- Bounded main-thread Unity queues
- Per-frame time and item budgets
- Cancellation during zoning or movement
- Mesh, material, and asset caching
- Pooling, LOD, occlusion, instancing, and batching
- Distance-based priority and memory eviction
- No recurring full-scene hierarchy scans
- Profiler markers and stage timings

Gameplay-critical geometry should become ready before distant decoration. Client readiness must not wait for every visual detail.

## Validation and Tooling

Required tests and tools:

- Repeated-run determinism tests
- Golden manifest/layout hashes
- Graph connectivity and lock/key validation
- Overlap and clearance checks
- Spawn and exit reachability
- Collision/navigation agreement tests
- Stable-ID uniqueness
- Server/client compatibility fixtures
- Version compatibility and migration tests
- Performance/allocation budgets
- Unity previews for individual generators
- Seed comparison and failure reproduction
- Procedural/RDB/hybrid side-by-side previews

Failures must report manifest, seed, version, stage, and validation reason.

## Runtime Dependency Policy

Runtime generation uses C#, deterministic mathematics, graph/geometry algorithms, and supported Unity APIs. The client and server must not require Python, ML, generative AI, or external generation services at runtime. Those tools may be used offline for research, authoring, baking, catalogs, textures, and validation.

## Development Roadmap

### Phase 1: Contracts and determinism

1. Create `AO.WorldGen.Core`.
2. Implement deterministic RNG and named sub-seeds.
3. Define versioned manifests, parameters, stable IDs, and logical spatial types.
4. Implement canonical serialization and layout hashing.

### Phase 2: Logical dungeon

5. Generate and validate dungeon graphs.
6. Embed rooms and corridors spatially.
7. Produce walls, doors, portals, collision, navigation, and spawn regions.

### Phase 3: Unity vertical slice

8. Build floor, wall, ceiling, doorway, and corridor mesh tools.
9. Add one industrial material family.
10. Render a complete generated dungeon.
11. Add bounded async generation, caching, and cancellation.

### Phase 4: AORebirth integration

12. Add capability negotiation and zoning manifests.
13. Compare server/client layout hashes.
14. Make doors, NPCs, objectives, loot, and transitions authoritative.
15. Persist and replicate instance-state deltas.

### Phase 5: Asset interchange

16. Define asset requests, catalogs, and `IAssetProvider`.
17. Implement procedural, RDB, custom, and hybrid providers.
18. Add versioned custom content packs.

### Phase 6: Expansion

19. Add multi-floor dungeons, lifts, caves, and mission styles.
20. Reuse architecture for buildings and settlements.
21. Add region/chunk contracts and outdoor terrain.
22. Add hydrology, roads, biomes, vegetation, and water.
23. Add durable world persistence, migrations, streaming, and advanced LOD.

## Initial Definition of Done

The first milestone is complete when:

- AORebirth creates a versioned dungeon instance manifest.
- Server and client independently produce the same validated layout hash.
- Unity renders a connected dungeon without RDB assets.
- RDB doors or props can be substituted through an asset provider.
- The server validates movement, doors, line of sight, encounters, loot, and completion.
- Mission state survives disconnect/reconnect for the instance lifetime.
- Generation is cancellable and does not create visible frame spikes.
- Any failed seed is reproducible from logged manifest data.

This vertical slice establishes the foundation for hybrid AO content now and a fully independent custom world later.

## Current Implementation Status

As of September 11, 2026:

- `AO.WorldGen.Core` is a Unity-compatible `netstandard2.0` shared library.
- Seeded named random streams, manifests, stable IDs, quantized spatial types,
  deterministic dungeon graphs, rooms, corridors, door portals, closed-door
  blocking bounds, and entrance/exit/boss spawns are implemented.
- Validation covers connectivity, overlap, references, corridor/portal cardinality,
  and required spawn placement. The canonical hash covers the complete layout.
- `ZoneEngine_New` references the shared project during development and has an
  in-memory authoritative instance service plus the safe
  `.worldgen <seed> <rooms> [instance-id]` GM probe.
- Unity has an opt-in `ProceduralDungeonDebugView`; it is deliberately not attached
  to production scenes and can preview primitive floor, wall, corridor, door, and
  spawn geometry from the same generator.
- A versioned development manifest envelope is transported through the existing
  AO chat response path. ProjectMayhem independently regenerates the layout and
  permits opt-in preview rendering only after its hash matches the server hash.
- Dedicated production transport and explicit capability negotiation are not
  implemented yet. They must use a documented extension path rather than an
  invented AO/N3 packet opcode.
- `ZoneEngine_New` allocates reserved runtime playfield IDs for generated
  instances. `.worldenter <instance-id>` uses the existing playfield transfer
  path and lands at the validated entrance spawn; `.worldexit` returns to the
  recorded source playfield.
- Generated runtime playfields now bake server-authoritative Bepu floors,
  ceilings, split room walls, doorway openings, corridor side walls, and mutable
  closed-door blockers. Each room/corridor portal has its own logical, revisioned
  door state, so the two ends of a corridor operate independently.
- A versioned door snapshot/delta envelope synchronizes current state on entry,
  reconnect, and live changes. Unity rejects unknown doors and stale revisions,
  then animates primitive hinged leaves and mirrors confirmed blocking locally.
- Shared room/corridor-membership and portal-visibility queries derive spaces reachable
  through open doors plus a one-space prefetch margin. An occupied corridor and both of
  its endpoint doors remain presented even when the player closes either endpoint.
- Procedural presentation construction is cancellable and frame-budgeted, and its
  primitive building blocks are pooled across rebuilds and zone changes.
- Prototype corridors render side walls matching the authoritative server corridor
  blockers, and door interaction feedback exposes deterministic indices and stable IDs.
- Server movement resolves procedural wall and door-frame contacts with bounded
  capsule sweep-and-slide so collision cannot pin a player at the first hit point.
- Corridor wall orientation uses the shared portal-facing contract, including corridors
  whose length is shorter than their width; bounds aspect ratio is never used as a
  gameplay or presentation direction guess.
- The first visual-pass slice adds deterministic turning critical paths, variable-height
  enclosed rooms/corridors, role-colored room palettes, a reusable tiled panel material,
  three modular trim/beam/pillar treatments, and selectively placed realtime practical
  lights. These are procedural stand-ins for the later authored modular asset kit.
- Room archetypes now include rectangles, wide halls, long halls, grand chambers, and
  true L-shaped footprints. Shared floor/ceiling and wall-section geometry drives both
  Unity presentation and authoritative server collision. Doorway trim is cut around
  openings; persistent portal facades close corridor shoulders and add textured lintels
  above the 2.8 m visual door opening.
- Manifests independently select a layout profile (`facility`, `subway`, `temple`,
  `raid`, `groupdungeon`, or `cavedungeon`), a presentation theme (`industrial`,
  `maintenance`, `alien`, `subway`, `temple`, `keep`, or `cavern`), and an asset source (`procedural`, `rdb`,
  or `hybrid`). Unity provides an
  optional RDB room/corridor provider seam with procedural fallback; AO.WorldGen.Core
  remains free of Unity and RDB dependencies. Encounter spawning, boss mechanics,
  loot, and outdoor-region profiles remain later shared-layout extensions.
- The subway profile has a dedicated first geometry kit. Its critical route is a
  rail-line spine with alternating side branches. Rooms render as track platforms,
  public concourses, or maintenance/service spaces according to shape and role, while
  corridors render as ribbed track tunnels with rails, route panels, and repeated
  lighting. Decorative geometry is non-colliding; shared floors, walls, and doors
  remain authoritative for movement.
- Subway generation now assigns shared elevation bands: a street-level entrance,
  upper access/service rooms, the station level, deeper post-station rooms, and a
  terminal level. Connections between bands are deterministic stair corridors built
  from the same bounded-rise floor sections on Unity and ZoneEngine. The objective
  station includes a stopped three-car train with shared physical collision.
- Rooms now carry a shared semantic module kind in addition to their footprint and
  gameplay role. Current kinds include entrance, combat room, junction, service,
  treasure, objective, boss arena, station platform, station concourse, train chamber,
  track tunnel, and terminal. Custom prefab and RDB providers can therefore select authored modules
  by purpose instead of guessing from room dimensions. Unity reports the room index,
  module kind, and elevation on entry to aid traversal and missing-floor reports.
- Unity now exposes a `DungeonModuleCatalog` asset and catalog-backed prefab provider.
  Entries select room, corridor, stairwell, or door modules by profile, theme, semantic
  purpose, and priority; supplied prefabs can replace built-in modules and optionally
  fit generated bounds. The first built-in authored subway set uses the same stable IDs.
- The dedicated Subway `TrainChamber` has shared recessed geometry rather than painted
  track lines: two walkable platforms, a 0.6 m track trench, bounded-rise escape steps,
  and rails at the lower elevation. It is 22 m wide by 32 m long, aligned with the
  through-route, with side-room branches on both sides and 4 m crossings beyond both train ends.
  The stopped train is three distinct cars with windows and doors; each car has
  matching client/server collision rather than being a visual-only wall.
  Other station rooms, including the early `StationPlatform`, use continuous full-height
  floors; they no longer expose a small, accidental trench. ZoneEngine bakes the same
  floor sections. Subway passages use
  curved tunnel shells with fitted wall pipes, while stairs use enclosed shells and
  sloped handrails. The route includes deterministic bends instead of one straight row,
  and transit doors use inset panels, windows, and safety markings.
- Presentation visibility now disables renderers and lights only. Room, corridor, and
  door collision remains active while an area is hidden, preventing a one-frame floor
  removal during visibility handoff. Character movement and animation share a short
  downward foot probe to tolerate stair and landing seams where Unity's
  `CharacterController.isGrounded` briefly reports false.
- Subway macro-layouts now reserve roughly one third of the room budget for progression
  hubs instead of treating two thirds as a room chain. The hubs are separated by long,
  5 m-wide main passages; remaining rooms form three side clusters attached to selected
  hubs through narrower service passages. This produces entrance → hall → hub/side rooms
  → hall → major station/terminal pacing. Every doorway center lands on full-height
  platform floor; track-trench escape steps remain outside the main travel line.
- The train chamber has shared client/server collision lips along both recessed-track
  edges. Local fall recovery activates only when the player is more than 1.25 m below
  the server position, so the intended 0.6 m trench cannot trigger repeated snaps.
- Subway stair treads are now solid-supported to 0.75 m below the lower landing,
  using the same depth calculation in Unity and ZoneEngine. There is no open void
  beneath the room-1 descent even if a movement frame misses a tread edge.
  Test with a freshly generated WorldGen 1.9.0 instance.

### Original group-dungeon profile (WorldGen 2.0.0)

Blizzard's [The War Within dungeon overview](https://worldofwarcraft.blizzard.com/en-us/news/24125258/explore-the-zones-and-dungeons-of-the-war-within)
describes dungeons as distinct places with a purpose (a rookery, machine vault,
priory, and more) and named encounters. The [Dungeon Journal preview](https://worldofwarcraft.blizzard.com/en-us/news/2943357/42-dungeon-journal-preview)
shows major encounters as located landmarks on a dungeon map. Our design inference
is a legible entrance-to-finale journey with different-scale spaces, optional wings,
and reserved landmark chambers. These are structural inspirations only; no Blizzard
map, names, art, encounters, or assets are copied.

- `groupdungeon` / `keep` generates a single-level main route that changes direction,
  long 5.5 m-wide vaulted halls, four side-room wings, broad junctions, a 24 m
  objective chamber, and a 24 m finale arena. Branch passages are narrower than the
  main route. Room/corridor floors and walls use the existing shared collision contract.
- The built-in keep kit adds staggered stonework, corner piers, vault ribs, banners,
  warm lanterns, and repeated hall arches. Its stable catalog IDs allow later prefab
  replacements without changing graph semantics.
- The objective and finale are **empty spatial landmarks** for now. No NPCs, boss
  fights, XP, loot, timers, or party scaling are implemented in this slice.
- Test with `.worldgen 90301 24 keep-group-test groupdungeon keep procedural`, then
  `.worldenter keep-group-test` after both client and server use the same current
  WorldGen version.

### Original subway module direction (PF 127 structural study)

A read-only study of the existing PF 127 room-surface export in the Windows
ProjectMayhem checkout found 46 logical room zones and roughly 11,883 surface
meshes spanning about 48.4 m vertically. Its useful design lesson is a sequence
of distinct spatial functions rather than a chain of ordinary rooms: entrance
stairs, public concourses, ticket/checkpoint areas, escalators and ramps, a very
large station volume, transport tunnels, bridges, a helical descent, service
connectors, landmark chambers, and a finale area. For scale reference only, the
exported station zone occupies an approximately 194 m by 62 m plan envelope;
other major envelopes include an approximately 98 m by 42 m arcade and a 30 m
by 78 m helix area.

Historical scope note: the following restriction applied to the original GLB kit.
On October 3, the user requested a **separate** path that reuses locally installed
AO resources. That direction supersedes the reference-only restriction for the new
path; see [PF 127 asset reuse study](review/pf127-asset-reuse.md).

The original procedural implementation was specified not to reproduce PF 127 meshes, coordinates,
textures, room names, or its exact graph. Instead, it should encode an original
reusable spatial grammar with compatible module sockets. The first authored kit
should contain: a street-to-concourse stair module, a wide branching concourse,
a platform-and-track hall with real lowered track beds, straight and curved rail
tunnels, a ramp/escalator transition, a multi-turn vertical descent, small service
rooms, and a large landmark chamber. Variants may mirror, change length and
width, exchange connector types, and reorder branches while preserving exact
socket and elevation contracts. RDB/PF 127 geometry remains an optional reference
and diagnostic source, not content for generated playfields.

WorldGen 5.5.0 begins that kit with an original station-hall module, subsequently
expanded to 44 m long, 40 m wide, and 10 m high. Two full-width platforms flank
a centered track trench recessed 1.2 m below platform level. Broad six-step access
routes connect both platforms to the rail level, while 4 m-deep full-floor bridges
at both longitudinal ends keep the through-route sockets safe. Split retaining
walls frame the two access openings; safety markings are presentation-only.
The stopped three-car test train is centered in the track bed rather than placed
as an obstacle on an ordinary room floor. Existing side connections resolve onto
the two platforms, allowing service-room branches on either side. All floor,
retaining-edge, train-body, and wall collision remains derived from shared Core
geometry and is baked authoritatively by ZoneEngine.

WorldGen 5.6.0 centralizes the station's structural collision in
`DungeonCollisionBaker` inside `AO.WorldGen.Core`. The baker emits stable, typed
box primitives for floors, access steps, ceilings, the continuous foundation,
split retaining walls, room walls, and train bodies. Unity treats station meshes
and props as presentation only and creates invisible colliders from this bake;
ZoneEngine creates Bepu statics from the identical primitive list. Decorative
geometry can therefore no longer accidentally become client-only collision.

WorldGen 5.7.0 centralizes the remaining non-cave collision construction in
`DungeonCollisionBaker`. The primitive IDs, kinds, and bounds participate in the
verified layout hash, and both Unity and ZoneEngine consume the same bake. An
experimental procedural-only server locomotion loop was tested and rejected: its
20 Hz controlled-player snapshots produced visible latency, turning oscillation,
and periodic rollback. Procedural playfields now deliberately use the same
movement path as ordinary playfields. ProjectMayhem predicts normal movement and
sends its normal position/heading updates; ZoneEngine runs the established
`SnapToClientPosition` collision-validation path and sends a correction only when
the requested position is rejected. WorldGen owns collision data, not a second
movement protocol.

Current subway traversal is substantially smoother under the restored movement
path. The station, stairs, recessed track bed, access ramps, and stopped train all
have collision emitted from Core. The remaining work is targeted traversal QA—most
notably jumping onto/off the train, platform edges, train-side track clearance,
ramps back to platform level, ceilings, and tunnel continuation—not another
procedural locomotion implementation.

The track-room slice adds one constrained rail assembly to the subway critical route:
`TrackTunnel` → `TrainChamber` → `TrackTunnel`. The three modules always share one
platform elevation and one shared track-bed elevation, and track-tunnel modules cannot
be scattered elsewhere in the route. Each 28 m-long, 12 m-wide tunnel bay has a recessed
5 m rail bed between two raised side platforms, with aligned rails, sleepers, access
stairs, tunnel ribs, and a continuous ceiling. The access stairs occupy narrow cutouts
aligned with the side doorways: they descend through the platform toward the track rather
than extending as broad steps into the rail bed. Its only longitudinal opening joins the
train chamber; the outside rail end is sealed. Ordinary rooms and service branches can
connect only through perpendicular side openings at platform height, where the internal
stairs provide access to the rail bed. Rail-to-rail connector corridors use the recessed
track elevation, eliminating raised slabs between a tunnel and the station. The train
chamber no longer has full-width end
bridges crossing the trench; its lowered bed, rails, and retaining walls continue to both
matching 5 m tunnel openings. The stopped train remains unique to that larger recessed
station module. Its three car bodies sit 0.65 m above the bed on visible undercarriage
beams and wheel pairs aligned to the same rail gauge used by the station, connectors,
and tunnel bays. The station's track-access stairs are now 7 m-wide notches cut 3 m
back into each platform rather than structures projected into the rail bed. Platform
route and safety markings are split around every station and tunnel stair aperture.
All recessed rail-room boundary walls extend down to track-bed elevation, giving sealed
track ends a complete collision-backed end cap with no visible exterior gap. Each stair
notch now has collision-backed cheek walls from the recessed bed to platform height.
Rail connectors use the 6 m tunnel ceiling rather than inheriting the station's 10 m
ceiling, and the station supplies solid headers above both tunnel portals to close the
remaining exterior-facing openings. The headers now begin at the actual curved-shell
crown (`track floor + 4.75 m`), with double-sided arch-shaped infill filling both portal
corners up to the station ceiling. Curved tunnel-shell triangles are emitted in both
directions so the roof remains visible from inside under back-face culling.
The fitted portal infill owns the visible header surface rather than overlapping a
second rectangular header mesh. Its front and back faces use independent vertices and
normals, and its top/side edges overlap the station wall slightly to prevent lighting
artifacts and sky-visible seams. The rectangular Core header remains collision-only.
The same fitted infill is now generated at every longitudinal rail portal, including
the `TrackTunnel` side of each connector; both ends of an arched passage therefore close
their rectangular wall cut above the shell. Perpendicular sleepers now continue through
connector corridors and the station trench as well as the reusable tunnel rooms.
Subway side passages attached to a `TrackTunnel` are now a separate pedestrian module:
a 12 m-wide long arched hall with a continuous walking floor and no track bed, rails, or
sleepers. Its roof uses independent horizontal and vertical radii, preserving the wide
arch while fitting the generated ceiling height. Stair corridors also add fitted wall
headers from the 2.8 m door crown to the adjoining room ceiling so their endpoint wall
cannot expose the exterior above a door.
Non-rail subway passages now own a complete endpoint-facade contract rather than relying
on the adjoining room wall: both the wide walking tunnel and stairwell emit full corridor-
width headers from the 2.8 m door crown to the passage ceiling, plus side jamb infill down
to floor level. These facade boxes are baked by Core for matching server/client collision.
The curved shell also uses separately duplicated front/back vertices so two-sided normals
remain valid instead of cancelling into a black or apparently open roof.

Immediate next steps for the current custom-client prototype:

1. Re-test a fresh subway instance using the restored ordinary movement path and
   record only reproducible collision failures with coordinates and the attempted
   movement (run, jump, fall, wall-slide, or climb).
2. Fix train/platform defects in `DungeonCollisionBaker` or the general capsule
   sweep-and-slide implementation, then cover each report with shared Core/server
   traversal tests. Avoid coordinate-specific recovery and client-only colliders.
3. Complete the subway route beyond the station: usable clearance beside the train,
   ramps/stairs to both platforms, continued track tunnels, service branches, and
   deeper encounter spaces.
4. Run a visual-quality pass only after the traversal contract is stable: coherent
   materials, fitted lighting, modular detail, occlusion, LOD/pooling, and removal of
   remaining debug presentation.
5. Add shared navigation/encounter anchors and one server-owned subway encounter
   slice before expanding the custom procedural profiles further.
6. Freeze a tested generator version with golden hashes and cross-runtime collision
   fixtures before using it for persistent mission instances.

### Separate AO ACG mission toolchain (planned)

The AO Rubi-Ka/Shadowlands mission generator should become a separate repository,
tentatively `AO.ACG.Toolchain`, rather than another subsystem inside ProjectMayhem.
Its primary goal is to create AO-style ACG missions that the original client can
enter and render using assets it already knows from its RDB. Custom-client support
is an additional output target, not a requirement imposed on the original-client
pipeline.

The toolchain should be split into four layers:

```text
RDB extraction/import
        ↓
Normalized asset and socket catalog
        ↓
Deterministic ACG layout compiler and validator
        ↓
Original-client AORebirth output + optional custom-client manifest
```

Planned responsibilities:

- Read extracted RDB metadata and identify mission rooms, corridors, doors,
  transitions, playfield resources, surface/height data, and relevant template IDs.
- Build a versioned catalog without copying Unity or ZoneEngine types into the
  generator. Each reusable module records bounds, compatible connector/socket types,
  orientation, floor elevation, clearance, semantic tags, and source RDB identities.
- Generate deterministic RK and later SL mission graphs, then choose only modules
  whose socket contracts match exactly. Validate connectivity, overlap, door and
  ceiling clearance, safe spawn/exit positions, objective reachability, and original-
  client resource availability.
- Emit the server-side records/bundle needed by AORebirth's existing mission/ACG
  lifecycle. The exact legacy ACG and RDB-facing output format must be established
  from the current server implementation and captured data before an exporter is
  declared compatible.
- Optionally emit the shared logical manifest/catalog mapping used by ProjectMayhem,
  allowing the custom client to render the same mission with RDB, enhanced, or custom
  presentation while preserving the same graph, sockets, stable IDs, and gameplay.
- Provide a CLI first: import/catalog, inspect, validate, generate, reproduce by seed,
  and compare outputs. A graphical module/socket preview can follow after the format
  and validators are stable.

Original-client compatibility imposes a hard boundary: it can only render resources
and structures represented through formats/assets it understands. Pure runtime Unity
meshes and ProjectMayhem-only procedural cave geometry cannot simply be sent to it.
New visual geometry would require an understood client content/RDB distribution path;
until then, original-client missions must assemble existing compatible RDB modules.

Repository boundaries should be:

- `AO.ACG.Core`: deterministic graph/layout/socket contracts and validation.
- `AO.ACG.Rdb`: extraction adapters and normalized RDB catalog construction.
- `AO.ACG.AORebirth`: legacy mission/ACG export and server integration.
- `AO.ACG.ProjectMayhem`: optional manifest/catalog adapter for the custom client.
- `AO.ACG.Cli` and tests: inspection, generation, reproducibility, fixtures, and QA.

Do not move the current free-form procedural cave/subway renderer into this tool.
Reusable deterministic and socket concepts may be shared deliberately later, but the
first milestone is a read-only RDB catalog plus one reproducible RK mission assembled
from existing original-client-compatible modules. SL should follow only after RK
generation, zoning, return flow, doors, objectives, and reconnect behavior are proven.

### Cave-based leveling-dungeon variant (WorldGen 5.4.2)

Blizzard describes [Ragefire Chasm](https://worldofwarcraft.blizzard.com/en-us/news/20166742)
as a five-player dungeon in volcanic caves. It also [removed a maze section from
Wailing Caverns](https://worldofwarcraft.blizzard.com/en-gb/news/10004620/patch-41-public-test-realm-notes-updated-12-april)
to improve accessibility. Our inference is to preserve readable route progression
while replacing keep geometry with rougher, varied cave spaces. This profile is
original and does not copy those dungeons' maps or assets.

- `cavedungeon` / `cavern` uses a winding, S-shifted main route with side chambers,
  6.7-8 m ordinary main tunnels, narrower offset branch tunnels, L-shaped pockets, and 30 m
  objective/finale caverns. Corridor widths and offsets are shared layout data.
- Cave tunnels render an uneven arch. The entrance uses a dedicated large authored
  chamber; other rooms vary between pockets,
  chambers, and halls. The raised triangle-mesh collision introduced in 2.3.0
  caused doorway snagging and was removed. Unity now renders only very shallow
  rock relief; both client and server use the established flat support slabs for
  collision. The surface meets portal and section edges flush. Cavern arm tips and
  L-shaped notches now leave clearance for the full door aperture plus the player
  capsule; tests check complete openings across 12/24/48-room samples and seeds.
- Cave rooms and corridors now use five explicit opening contracts: `Standard`,
  `Medium`, `Wide`, `Tall`, and `ExtraLarge`. Each owns an exact width, crown
  height, side clearance, and arch resolution. The generator deterministically
  selects a profile for each connection endpoint; S-bends, straight corridors,
  and the long curved descent morph between their independently selected start and
  end profiles. Room walls cut the same rounded profile directly, with no inserted
  connector mesh. If a shaped room wall cannot fit the selected opening, generation
  deterministically chooses the largest compatible profile instead. Tests verify
  endpoint width agreement and full wall-aperture fit across seeds and room counts.
  Every renderer consumes the same canonical 16-segment boundary loop, including
  its center, floor, width, crown height, and semicircular curve. The authored
  entrance chamber owns a narrow 0.6 m organic blend outside each socket width so
  its wall reaches that boundary directly, plus an 18 cm room-owned overlap that
  hides raster seams without inserting a connector. It also builds a room-owned
  shoulder surface from that exact semicircular boundary to the chamber rim, closing
  the upper and side wedges around a rounded corridor without extending a tunnel
  shell into the room. The current authored entrance
  advertises only the 3 m-high Standard socket it can visually support; the other
  four types remain available to compatible rooms and corridor endpoints.
- This is a physics and presentation vertical slice, not yet a finished cave art
  kit. The entrance chamber uses 24 curated control points in
  `DungeonCaveChamber`, interpolated to 96 perimeter samples. Its chamber-specific
  floor, stratified rock walls, and 9 m-high uneven vault use that smoothed outline;
  the matching angled collision walls are shared by Unity and ZoneEngine.
  Door sockets cut openings in the ring, while the flat floor and outer shell
  remain hidden safety supports. Floor undulation is intentionally visual-only and
  shallow after the earlier raised-collision snag; proper walkable terrain needs
  dedicated movement and collision work. The cavern material uses a project-local rock albedo
  texture
  (`AO.Unity/Assets/Resources/Procedural/CaveRockAlbedo.png`) instead of the
  procedural brown-panel texture. The rooms still lack sculpted overhangs and
  more cave module variants. The first corridor now uses `DungeonCaveTunnel`, a
  data-authored S-bend sampled at 33 cross-sections. Its curved floor and rock
  shell are visual, while angled inner wall boxes are shared by Unity and ZoneEngine;
  the original full-width floor, ceiling, and outer corridor walls remain hidden
  safety supports. The first corridor is centered on its door sockets so the full
  door and player capsule fit at both ends. This is a deliberately modest bend within the existing straight
  connection envelope, not yet a 90-degree junction or a freely routed tunnel.
  Passage light stones now use each generated arch crown instead of a fixed
  world-space height. The wide raised-path bend also uses those same crown samples
  for its closing half-vault, making it meet the main tunnel without a crescent-shaped
  opening to the outside world.
  The second critical-path connection is a separate, roughly 468 m-long
  group-scale passage with an 18 m-wide main route and a high rock vault. Its
  centerline bows about 175 m sideways before returning, making a broad half-C
  rather than a stretched straight hall. Both landings use 8 m doorways and
  the main floor descends 3 m on even seeds or 18 m on odd seeds; traversing
  it backward tests ascent. Curved
  floor strips and inner wall collision are shared by Unity and ZoneEngine,
  rather than making the whole large bounding rectangle walkable.
  An 18 m-wide raised secondary route follows one side, matching the lower
  group route's usable width. Its visible floor begins directly at the lower
  route edge so the hidden safety support cannot appear as a pale strip between
  the two surfaces. Curved collision supports compensate for their yaw so their
  world-space coverage reaches the full visible outer wall, and room-facing end
  upper route grows out of the lower route during its initial climb and therefore
  needs no artificial end cap. It also narrows back into the lower route at the
  far landing. ZoneEngine derives the
  outer wall offset from that same shared width instead of retaining the former
  4.5 m ledge boundary. It starts nearly
  level with the main path briefly, then rises more sharply to about 6 m above it
  within the first quarter of the route and remains level and open toward the main
  floor for jumping down. The bend has an 18 m-high collision envelope and its
  visible vault swells upward through the interior while retaining compatible
  socket heights at its room connections. After its long plateau, the elevated
  route descends and narrows back into the lower route before the far room. This
  prevents the smaller room socket wall from appearing as an obstruction across
  the widened shelf. Six narrow collision lanes follow the upper curve instead of
  one broad rotated box; this prevents collider corners from intruding into the
  middle of the lower walkway.
  Its collision uses short, yaw-aligned supports following the curve on both
  Unity and ZoneEngine; axis-aligned overlapping support boxes caused snap-back
  on the ledge and were removed. A continuous visible
  rock mass covers the ramp, inner drop face, outer face, and underside. A
  matching vault closes the open space above the ledge so the outside world
  is not visible through that side of the cave. Its default side is right
  relative to travel from room 1 to room 2;
  manifest parameter `caveLedgeSide=left` mirrors it. This setting is currently
  available to manifest authors, not as an additional `.worldgen` argument.
  At sharp turns the rotated collision supports use the ledge's true
  perpendicular width, with an additional 0.5 m allowance along the inner
  drop edge. The wider collision boxes protruded into the lower path. Server
  collision tests now cover both reported landings near `(109.19, -2.17, 17.48)`
  and `(124.71, -2.87, 30.94)`, plus all intermediate curve sections at
  multiple offsets from the ledge.
  At the outside wall near `(194.20, -2.33, 105.10)`, server collision tests
  confirm that the capsule can move away and slide along the wall. Unity now
  applies server wall corrections through the `CharacterController` safely
  instead of assigning its live Transform directly; this needs Play-mode
  confirmation. No new WorldGen layout is required for this client-only fix.
  Stronger cave materials, authored rock detail, and in-game movement tuning
  remain follow-up work. The longer level entry fixes a measured snag near
  `(62, -0.34, -7.67)`, where the previous ledge had already risen about 0.5 m.
  Jump input now has a brief press buffer and ground grace period; the local
  controller no longer resets an upward jump while its foot probe still sees
  the floor. Test jumps while stationary, walking, running, on the main path,
  and on the raised path. The current collision geometry is WorldGen 5.2.0; use
  `.worldgen 90421 24 cave-ledge-drop-v2 cavedungeon cavern procedural`
  followed by `.worldenter cave-ledge-drop-v2`.
  Test the entrance, first tunnel, wide descent, door
  transitions, and floor seams before replacing the fallback slab.
- After redeploying the server and restarting the client, test the new chamber with
  `.worldgen 90371 24 cave-bend-test cavedungeon cavern procedural`, then
  `.worldenter cave-bend-test`. The previous cave worlds will not show this
  WorldGen 5.2.0 tunnel modules. To test ledge entry and jump-down clearance,
  generate a fresh world with `.worldgen 90421 24 cave-ledge-drop-v2 cavedungeon cavern procedural`
  and enter with `.worldenter cave-ledge-drop-v2`. For the gentle 3 m variant,
  use seed `90402` and a different world ID. Redeploy and restart
  ZoneEngine_New, and restart the Unity client before testing.
- `.worlddoor <index> <open|closed|sealed>` and
  `.worlddoor <index> locked <key-item-id>` are the current GM test probes.
  Players can aim at a nearby highlighted door and use `E` or right-click. The server
  validates instance, identity, distance, line of sight, revision, and state before
  toggling. Locked doors require a matching reusable item in the player's carried
  inventory. Sealed doors remain unconditional denials; mission-condition behavior is
  intentionally deferred.
- Runtime instances and return locations are currently memory-resident. Players
  must use `.worldexit` before a development server restart; durable instance
  persistence and crash recovery remain required before production use.
