# Procedural generation review — October 2, 2026

Scope: ProjectMayhem Unity procedural presentation and editor importers; sibling
WorldGen.Core generation, presentation, collision, validation, and asset catalog.
This is a review and implementation proposal, not a completed refactor or server deployment.

## Confirmed defects

- Shared DungeonPresentation.BuildRestroom still puts blocking sink bounds near the
  room centre. DungeonCollisionBaker.BakeRoom consumes them. The client now draws
  sinks at the wall, leaving invisible collidable sinks at the old coordinates.
  Correct fixture transforms/bounds in shared Core, consume those transforms in
  Unity, and update both local client/server together. Presentation participates
  in DungeonLayoutHasher, so preserve old generator versions or introduce a new
  version; recreate instances for the new manifest. Do not merely remove client boxes.
- The first bathroom edits modified an unused builder. Active rendering used
  shared Presentation and ConstructiveRecipes, requiring a later routing correction.
- SubwayDungeonKit currently attaches a spotlight to every ceiling panel as well
  as standalone light pieces. This includes the vented panel seen in the authoring
  reference, which has no obvious luminous emitter. A light without a matching
  visible emitter explains illuminated floors beneath apparently unlit ceilings.
- Current shadow selection updates the nearest 16 visible fixtures. Visibility
  toggles mesh renderers and light components together. Visible lamp appearance,
  light influence, and shadow budget need independent policies.

## Keep these foundations

Deterministic manifests and client/server hash verification; engine-neutral Core;
authoritative shared collision; door state synchronization; existing validation,
seed sweeps, portable metadata, and GLB import/preview tooling. Extend these rather
than replace the generator wholesale.

## Priorities

1. One resolved placement record per fixture/module: stable ID, asset ID,
   transform, role, collision proxy, clearance, sockets, and light definition.
   Rendering and collision consume this record. Themes choose presentation;
   they do not independently reposition collision-bearing objects.
2. Replace label substring dispatch with typed slots and registered renderers.
   ProceduralDungeonDebugView is approximately 4,562 lines and mixes shell building,
   theme rules, door interaction, pooling, visibility, materials, lights, and fallbacks.
   Extract services by responsibility and migrate Subway first. Explicitly log
   missing bindings and which fallback ran. Avoid maintaining parallel active builders.
3. Expand the existing portable catalog. AssetCatalog already has IDs, bounds,
   collision, sockets, placement, rotations, and hashes, but its validator currently
   accepts doorway-frame assets only and binds IDs/theme/path to folders. Give IDs
   independence from paths and allow reusable assets and themed compatibility rules.
4. Author native dimensions and connection rules. Uniform tile repeat, approved
   rotations, wall mounts, hinge anchors, door clearances, trim joins, and stair
   rise/run belong in metadata. Restrict permitted stretching; do not infer orientation
   from bounds or rediscover doorway openings from triangles for each placement.
5. Add integration validation of the resolved output: visual/collision agreement,
   open stair stalls, sink wall contact, doorway passage, shell coverage, light
   socket/emitter presence, bounds, material/texture references, and budgets. Exercise
   the actual live render route and shared server collision, not only helper methods.
   Reuse seed sweeps and include a representative bathroom/traversal scene.
6. Define lighting and performance budgets per profile. Use emitter materials,
   explicit fixture sockets and realtime lights; manage influence and shadows
   separately from visual visibility. Track triangle count, renderer count,
   overlapping lights, shadow atlas occupancy, and generation time.

## Authoring tool

Build a Unity EditorWindow on these contracts first. Existing GLB preview/import,
Subway preview generation, and catalogs provide useful starting points. First
version: asset browser, role/theme eligibility, orientation/pivot/clearance editing,
collision and socket overlays, seed/profile controls, regeneration, visible validation
errors, and save/load of versioned definitions. Add hand-authored room templates
and pinned rooms/connections later. Offer procedural, authored, and hybrid layouts.
Do not expose C# label dispatch or filesystem renaming as normal authoring steps.

## Reusable catalog example

An asset can be `industrial-metal-door-01`, stored anywhere, with role `door`, tags
`metal`, `industrial`, and `interior`. Its compatibility can allow subway, facility,
and industrial bunker while excluding ancient temple. Store dimensions, pivot,
hinge socket, collision/clearance, allowed rotations/scaling, matching frame family,
LOD/material/light definitions, approval status, and content revision alongside it.
Apply explicit exclusions and structural compatibility before weighted variation.
Use stable ordering/hashing and a separate decoration random stream.

Start with version-controlled portable JSON plus Unity editor data/views. Compile
validated runtime catalogs. A database is optional for a later collaborative asset
library; a running game or headless server should not depend on editor/database access.
The server needs deterministic semantic/collision data, not GLB meshes.

## Suggested implementation sequence

Fix shared bathroom placement and migrate the client to those transforms; create
actual emitter/light definitions; expand catalog contracts; move Subway bindings
out of DebugView; deliver a small catalog-and-preview editor; then add richer room
layout authoring and additional dungeon themes. Keep each migration reviewable and
preserve generator-version behavior.
