# Dungeon authoring — first Subway milestone

Open **Tools > WorldGen > Dungeon Authoring** after Unity finishes compiling.
This first editor supports the Subway presentation kit; it does not yet author
room graphs, pin rooms, or replace shared gameplay/collision definitions.

## PF 127 native room layouts

Native generator **1.2.0** adds walking-region validation, selected-room loading,
saved recipes and **Tools > WorldGen > Native Dungeon Authoring**. The window
supports room inspection, textured previews, exclusions, pinned preview growth,
lighting/closure settings, seed reports and recipe export/import. See
[native authoring and saved recipes](native-dungeon-authoring.md) for the workflow.

A separate generator now reuses the textured rooms from the selected local AO
installation. After installing the local server build with
`./tools/deploy-local-pf127-rooms.sh` from ProjectMayhem2.0, refresh Unity assets and
reconnect. Use game chat:

```text
.worldgen pf127-rooms 90602 12 pf127-random mixed
.worldenter pf127-random
```

Change the seed and instance ID for another layout; choose `mixed`, `mall` or
`depths`, with 4–24 rooms. The authoring window is not required. The original
`.worldgen pf127 <seed> <instance-id>` still creates the reference arrangement.
Native rendering assets continue to load from the player's local AO database.
See [native room implementation and next steps](review/pf127-asset-reuse.md#seeded-native-room-dungeons--2026-10-03).

## Asset workflow

1. Stop Play mode and use **Assets > Refresh**. The legacy kit is upgraded to portable
   metadata on editor reload. If needed, click **Refresh source models** in the window.
2. Select an asset to inspect its model and edit its stable ID, typed role, tags,
   allowed/excluded themes, lighting, approval status, and notes.
3. To register another model, enter a unique lowercase asset ID, choose a role, and
   click **Add GLB asset**. Custom sources must already use the intended Y-up
   orientation; they are centred and normalized for the current prototype placements.
4. Click **Validate and save catalog**. This validates portable metadata, checks GLB
   file hashes, saves Unity bindings, and exports `Assets/Resources/SubwayKit/asset-catalog.json`.
5. Enter seed and room count; **Generate / rebuild Subway preview** creates a separate
   scene object at the existing standalone preview offset. Enable the collision
   overlay to inspect the authoritative primitives in Scene view. **Remove preview**
   clears it. Save the scene yourself if you want to keep the preview.

Excluded themes override allowed themes. IDs are independent of model paths.
Compatible variants are sorted by stable ID and chosen with a stable placement hash,
without consuming layout randomness. Refresh preserves authored metadata and custom
registered entries. Model bindings, eligibility and light settings affect current
Subway placements. Reuse metadata for other themes is supported by the resolver,
but those theme renderers have not yet migrated to this kit.

Measured dimensions, pivots, fitting, rotations, collision and sockets are displayed
read-only in this first window. Collision-bearing placements remain shared Core data;
editing a Unity prefab must not independently change server traversal. Current fitting
is the prototype's normalized stretching. Floors and walls still use the kit's baked
surface materials rather than per-placement material variants. Inspect model fit before
marking an asset approved. The `prototype` approval label is descriptive, not a runtime gate.

## Bathroom collision and versioning

Generator **5.13.0** resolves toilets and sinks into typed shared records with bounds
and yaw. Sinks are against walls, with doorway clearance and separation from other
sinks. Their visuals and collision consume the same records. Old centre-room sink
bounds and old constructive fixture meshes are absent from new bathrooms. Stall doors
are omitted. Earlier generator versions preserve their original layouts and hashes.

The client accepts 5.13.0; the local server source creates that version. Updating client
scripts alone does not update the running server. The local publish completed and
startup validation passed. Install from any working directory:

```bash
bash /home/cody/Coding/ProjectMayhem2.0/tools/deploy-local-worldgen-513.sh
```

The script uses sudo in your terminal, preserves the active Config.xml, validates the
new release before stopping the zone service, and restores the prior release if the
new service fails to start. It restarts only ZoneEngine_New. No Git or cloud push is
performed. Stop Play mode before installation; recreate in-memory dungeon instances
after the service restart:

```text
.worldgen 90602 30 subway-v513 subway subway procedural
.worldenter subway-v513
```

## Lighting

Only actual light-role fixtures get realtime lights and visible emissive diffusers.
Plain vented ceiling panels no longer emit invisible light. Fixtures and diffusers
remain attached to the same placement; visibility hides them together. The closest
16 visible fixtures within 30 m receive soft shadows, leaving other visible lights
illuminating surfaces. The authoring preview updates this shadow budget as the Scene
camera moves. The active PC pipeline supports additional-light shadows.

Lighting metadata controls enabled state, source position relative to placement centre
in metres, colour, intensity, range, cone angle and diffuser emission. Bloom is optional
and was not enabled globally by this change. Physical fixtures are not switched on as
a gameplay event when entering a room; visibility and shadow budgets control rendering.

## Verification and remaining work

- Shared Core builds without warnings/errors.
- Focused checks pass across 33 seeds: 33 bathrooms, 99 sinks, matching collision,
  wall mounting, doorway clearance, no old centre sink proxies, deterministic generation,
  unchanged legacy fixture behavior, theme exclusion precedence, independent IDs/paths,
  and rejection of unsafe catalog paths.
- Unity runtime and editor assemblies compile using Unity's references.
- Local ZoneEngine_New publish and startup validation pass.
- The authoring preview runs integration checks against placed GLB bounds, shared
  colliders, open stalls, luminous diffusers and downward light orientation.

The user confirmed on October 3 that the dungeon can be entered and traversed, and
the TrackTunnel 1 / StationConcourse 5 stair issue is fixed. Ceiling-source lighting
still needs visual improvement; emission implementation alone has not met that goal. Further stages
include pinned rooms/connections, room template authoring, catalog-driven structural
constraints and matching families, adoption by other dungeon themes, content-pack
revision binding, full separation of doors/visibility/pooling from DebugView,
and automated budgets for renderer/triangle/light counts. The typed Subway surface
renderer and lighting controller are the first extractions; the remaining DebugView responsibilities still need to move into focused components.

## Stair recovery and ceiling coverage (2026-10-03)

Client floor recovery after a server correction now expires after 0.2 seconds or
0.2 metres of horizontal travel. Previously it retained an arbitrary support height
while walking, which could hold the player above descending stairs. Physical
collision and gravity resume when that temporary recovery expires.

Subway rooms now have ceiling fixture grids at approximately 6-metre spacing;
train chambers use 4-metre spacing with twice the fixture intensity. Track rooms
use one ceiling envelope rather than spawning lights over every stair tread.
Other rooms retain their actual floor footprints, including the entrance L.
Lamp diffusers cover more of each fixture underside and have at least 8 emission
intensity, so the luminous source is visible separately from the lit floor.

No new model is required for this implementation. For an authored replacement,
provide a Y-up ceiling lamp with an opaque housing and a separate downward-facing
diffuser mesh/material for emission. Airborne light shafts require a fog/scattering
effect; an emissive model alone does not generate them.

Runtime and editor compilation passed. On October 3, the user confirmed the reported
stairs work and a dungeon run succeeds. Lighting remains an open visual issue.


## Entry recovery and confirmed run (2026-10-03)

The user confirmed successful dungeon entry and traversal after the following local
client/server fixes. This is a movement acceptance checkpoint, not acceptance of all
visuals. The ceiling illumination still does not convincingly originate at the lamps;
work on that is deferred by the user.

Where the changes live:

- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWalkerController.cs`: temporary
  authoritative floor recovery expires instead of holding players over descending stairs.
- `AO.Unity/Assets/Scripts/AO.Unity/World/AOGameServerSession.cs`: process manifests
  before same-batch transfers, retain pending procedural bootstraps, wait for constructed
  collision, validate actual floor support, and pause movement reporting during entry.
- `AO.Unity/Assets/Scripts/AO.Unity/Prototype/PrototypeClientUGUI.CharacterFlow.cs`:
  reconnect to procedural playfields using the procedural loader and zero world offset.
- `AO.Client/Backends/AORebirth/AORebirthBackend.cs`: preserve server messages received
  during bootstrap/entity collection, including the initial dungeon manifest.
- `AO.Unity/Assets/Scripts/AO.Unity/World/Procedural/ProceduralDungeonDebugView.cs`:
  expose presentation readiness, add ceiling fixture coverage, and brighten train chambers.
- `AO.Unity/Assets/Scripts/AO.Unity/World/Procedural/SubwayFixtureLighting.cs`:
  larger, brighter lamp diffusers; further visual tuning is still needed.
- Sibling `AORebirth/AORebirth/Server/ZoneEngine_New/Core/Commands/WorldEnterCommand.cs`:
  entering the current dungeon warps the authoritative motor to its entrance and resets
  fall velocity, instead of rejecting a same-playfield transfer; preserves return location.
- `tools/deploy-local-subway-reentry.sh`: installs that local server build. No cloud changes.

The failure sequence included a missing normal-playfield JSON load, late manifests,
movement starting before collision existed, and a server-saved position far below the
floor undoing client recovery. Fixing only client position was insufficient.

Procedural instances currently live in server memory. Restarting the local zone server
clears them; recreate before entering. A persisted character playfield ID alone does
not recreate its dungeon. The authoring window is optional and is not needed for entry.

```text
.worldgen 90602 30 subway-v513 subway subway procedural
.worldenter subway-v513
```

Next steps: validate the separate PF 127 textured native-room path (see
[PF 127 asset reuse study](review/pf127-asset-reuse.md)); preserve this working GLB
path; later tune fixture source placement, emission, shadowing and train-chamber
coverage in-game. Instance persistence/recovery across server restarts is also needed.


### PF 127 original visual tiles — October 3, 2026

The separate seeded native copy now reads original textured architecture directly
from the AO installation selected at launch: 46 rooms and 104 textures. Runtime
preparation runs on a background worker, creates Unity assets in memory and deletes
temporary geometry. Exported asset packages are no longer used for entry. The native
helper applies the occupied-cell room height baseline; the initial extraction had
omitted it, raising lower rooms by 8–20 units. The user's later screenshots exposed
white collision cages left visible by the overlap filter. Runtime now disables all
diagnostic surface renderers after architecture and static models are ready, keeping
their colliders intact. Indoor model placements come directly from local
`cd_image/data/statels/127.pf`, with room transforms and original texture overrides.
Validation resolves 2,244 visible placements, 245 mesh resources and 476 referenced
images through Unity's existing ABIFF reader. The user confirmed textures look almost
perfect, then reported an entrance drop at AO (91,107,326). The client now adds
floor/stair support directly from the rendered tile triangles; seven point probes
around that spot match Y 107.60483. The follow-up stair drop at (97,107,268) exposed
a mixed ramp mesh classified as a wall, which caused its collider to be skipped.
Direct native loading now classifies individual faces; the ramp at Y 111.1575 is
retained. Checks cover 96 points across the ramp and both landings; live review is
pending. See [native stair collision](review/pf127-asset-reuse.md#mixed-native-stair-collision--2026-10-03).
Interactive objects and original lighting remain follow-ups. Stop Play, refresh, then
use `.worldenter pf127-copy`; regenerate after a server restart as documented in the
[PF 127 implementation notes](review/pf127-asset-reuse.md#on-demand-local-database-presentation--2026-10-03).

The export command and room GLB/pivot format are documented in
[WorldGen.NativeVisualExport](../tools/WorldGen.NativeVisualExport/README.md).
These optional local room exports provide inspection material for later catalog/socket
authoring. Runtime dungeon recipes should contain source room/resource IDs and
placement transforms, resolved from each player's local DB. Current PF 127 seeds
retain the original arrangement; rearrangement must also update server collision.
See [runtime helper setup](../DATA_SETUP.md#indoor-runtime-reader).
