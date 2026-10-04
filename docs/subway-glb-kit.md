# Subway GLB kit — October 2, 2026

The existing procedural Subway layout now uses prepared GLB visuals for floors,
walls, ceilings, platform safety strips, station lights, straight rails, platform
columns, and the train. Source assets remain in the sibling `WorldGen/Assets/subway`
workspace. The imported kit is bundled under `AO.Unity/Assets/Resources/SubwayKit`;
a built client does not need the authoring checkout.

## Preview

Open `AO.Unity/Assets/Generated/Subway/SubwayPrototype.unity` in Unity. This is a
separate generated preview and does not replace TestScene. For another layout,
change Seed and Room Count on Subway Dungeon Prototype, then use its component
context menu **Rebuild Subway Dungeon**. It uses generator version 5.12.0.

**Tools > WorldGen > Build Subway Prototype Dungeon** refreshes the kit and builds
an isolated preview object in the current scene. **Tools > WorldGen > Refresh
Subway Kit** imports changed source `game.glb` files. Existing runtime Subway
instances load the same kit automatically when their presentations are rebuilt.

Server development commands remain:

```text
.worldgen 90602 30 subway-glb-test subway subway procedural
.worldenter subway-glb-test
.worldexit
```

The server determines the manifest version; client and server still verify the
shared layout hash. The preview does not create or persist a server instance.

## Placement and collision

The dedicated Subway importer creates visual-only, centered unit-bounds prefabs.
It aligns source principal axes before normalization. Floor surfaces now repeat at 0.5 m (about one eighth of the original spacing),
and wall tiles are 0.375 m wide by 0.25 m high, staggered by half a tile on alternate rows. The importer projects the floor
top and the lighter wall face from the imported GLBs into repeating textures.
Every wall surface faces inward, so the darker back never alternates into view.
These surfaces use two triangles each rather than duplicating detailed meshes
for every small tile. Ceilings retain the original spacing; station details fit
existing presentation bounds.
This is a prototype fit, not a claim that every model's proportions are approved.

DungeonCollisionBaker remains authoritative. Imported GLBs contain no colliders;
client collision comes from the existing shared primitive bake. Layout RNG, hashes,
portal openings, server door state, and movement behavior are unchanged.

Stairs/handrails, curved tunnels, track junction transitions, signs, and doorframes
continue using existing procedural treatments. Complete stair models, custom join
pieces, track transition GLBs, props, and authored doorway clearance metadata are
later work. This kit does not extend the portable doorway-only catalog schema.

## Verification

Unity batch compilation/import and generation succeeded for the initial 12-room
seed-123 preview, placing 2,053 authored GLB objects. The batch verification method
also checks centered unit bounds on saved prefabs, absence of visual colliders,
placement of the surface/station roles, and presence of shared collision primitives.
All eight roles were present in the saved scene. The second batch completed these
checks and saved the scene, then exited with signal 139 during Unity shutdown; the
first batch exited normally. A clean second-process exit is not claimed.

Appearance, source-facing orientation, player traversal, and performance must be
reviewed in the graphical client. This pass supplies dungeon presentation; encounters,
objectives, loot, and persistent player instance lifecycle remain separate roadmap work.

## Entrance retreat fix — October 2, 2026

A reported ricochet at the large locked entrance exposed movement contract gaps.
The client previously labeled all changing positions with `0x05`; the current
ZoneEngine movement enum treats that byte as StrafeRightStart. AOGameServerSession
now sends explicit forward/backward/strafe start/stop transitions and `0x16`
position updates. Direction changes stop obsolete flags first; authoritative
corrections reset the outgoing action state before the next move.

The prediction capsule now uses the current server BodyRadius of 0.5 m rather
than 0.4 m. This leaves clearance beyond the server's 0.4 m wall-crossing probe
offsets even with Unity skin width. A collision suppresses only travel into the
rejected direction; retreat and strafing stay available. Retained velocity into
a wall is cleared when the player changes direction, and horizontal corrections
clear that velocity immediately.

Verification: the movement action tests cover all 81 planar direction transitions,
idle updates and correction resets against server action semantics. Existing world
delta tests pass. Unity compilation and the seed-90602, 30-room entrance regression
pass, including retreat from 14 generated wall contacts. Unity exited normally.
Live server/client traversal remains a manual check; no claim is made that every
possible corner, jump, or collision is covered by this regression.

Reload the client after script compilation (restart Play mode in the editor), then
exit/re-enter the existing instance. These fixes change client movement, so they do
not require regenerating the layout or deploying a server change. The exterior
entrance remains sealed; approaching it should allow retreat without ricochet.

## Follow-up: live entrance snag and missing GLBs

The capsule-only regression above did not cover the server wall-crossing gate.
The live entrance snag persisted. The server's two-sided box triangles reject
inside-to-outside retreat; the local collision patch removes reversed faces while
preserving every exterior face. Source is patched, local publish/startup validation passed, and the user installed
the local release. ZoneEngine_New restarted successfully at 15:44:14 MDT.
Recreate the generated instance after this restart. See [entrance wall escape review](review/subway-entrance-wall-escape.md).

The missing GLBs coincided with Unity Data Store errors after an earlier batch
validation opened the project concurrently with the user's Hub-launched editor.
After the user saved/closed Unity, the kit was force-reimported with one editor
process. All eight roles and 2,053 preview pieces resolved. The importer now refuses
Play-mode writes; runtime validation and placement counts make missing assets
visible in the Console and System status. Reopen Unity after the batch has exited.

## Smaller tiles and consistent wall fronts

The open Unity editor compiled and baked both surface assets after Refresh.
The wall front measured 0.742 average albedo luminance, versus 0.318 on its back;
the lighter front was selected. Re-enter the dungeon after restarting Play mode
to rebuild its surfaces. This is a local client presentation change.

## Platform safety strips and closed stairs

Safety-strip GLBs are restricted to the platform-top line and placed in approximately
one-metre modules. They no longer replace and stretch across retaining-wall boxes.
The deliberate interruption at each staircase remains. See
[subway safety strip asset specification](subway-safety-strip-asset-spec.md) for replacement
model instructions; the current source mesh still has authored edge irregularities.

The importer also includes `stairs/stationary` (a single tread), `stairs/straight-hand-rail`,
and `stairs/hand-rail-post`. Track-access and elevation-changing corridor stairs use
the tread model. Handrails follow each flight, with repeated rail modules and posts.
Dark closed solids behind each tread cover risers and undersides. These are visual
changes; shared movement/collision geometry is preserved. Stop Play mode, Refresh,
and restart Play mode to import and rebuild the kit.

## Train height, staggered walls, ceiling backing, metal corners

The complete train GLB is placed with its lower bound at the rail tops (track floor
plus 0.11 m), lowering it 0.54 m from the old body-only clearance. Wall textures
repeat two rows with a half-width offset, using 0.375 m-wide, 0.25 m-high tiles.
A versioned surface bake refreshes the old square texture automatically in Edit mode.
Every tiled room ceiling has a continuous black backing slab, overlapping adjacent
sections slightly to hide daylight through irregular model seams.

`architecture/wall-edge/2/game.glb` is imported as `wall-corner`, upright along its
long axis. Metal trim is placed where perpendicular room wall sections meet,
including room shapes represented by multiple sections. Shared layout and collision
remain unchanged. Runtime and editor assemblies compile successfully; visual fit
still needs review in the graphical client after Refresh and re-entering the dungeon.

## Wall backing, indoor lighting, bathroom fixtures, metal doors

Tiled room and corridor walls have opaque black boxes behind their visible faces;
slight overlap closes corner seams without filling portal openings. In Play mode,
Subway presentation disables existing directional lights, uses flat cool ambient
lighting, reduces environment reflections, and disables outdoor fog. Light enabled
states and RenderSettings are restored when the dungeon clears or the view disables.

The kit imports `props/toilet`, `props/sink`, `props/door`, and
`architecture/doorways/2`. Bathroom stalls retain dividers and rear infill but no
stall doors. Toilets sit on the floor; sinks mount on the opposite wall. These props
retain native Y-up orientation before normalization.

Subway portal frames prefer the new metal frame over the older catalog. Frame
scaling measures the imported mesh opening at its midlines to fit the existing
clearance. Metal leaves are parented to the existing moving collision leaf; its
placeholder renderer is suppressed. Hinge animation, interaction, authoritative
door states, and collision remain on the existing leaf. The locked exterior
entrance also uses the metal door/frame visuals.

Runtime and editor assemblies compile. Import and graphical placement should be
checked after Assets > Refresh in Edit mode and restarting Play mode.

## Active ceiling fixture lighting

Every placed ceiling panel and standalone light GLB now receives a downward-facing
Unity spotlight, positioned 0.12 m below the mesh to prevent self-shadowing. The
lights use cool white illumination, a 24 m range, and a broad cone. They belong to
the fixture hierarchy, so existing room/corridor visibility turns them on and off.

The nearest 16 visible fixture lights within 30 m cast soft shadows, refreshed
every quarter second as the player moves. Other visible fixtures still illuminate
the room. This bounds shadow-atlas use when many ceiling panels are present. The
active PC URP asset already enables additional-light shadows; Mobile quality does
not. Runtime compilation passed. Restart Play mode and re-enter to rebuild lights.

## Bathroom presentation routing correction

The earlier fixture swap changed an unused bathroom builder. Live Subway bathrooms
rendered shared `Presentation` elements and constructive toilet/sink recipes instead.
The active route now skips the old porcelain/partition presentations and toilet/sink
constructive meshes, then calls the updated GLB bathroom builder once. Other shared
presentation, including room lights, remains; shared light fixtures also use the
active light GLBs. The stall-door presentation elements are skipped. Runtime
compilation passes. Refresh and rebuild the live dungeon presentation to apply.
