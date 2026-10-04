# Procedural Temple Design

**Status:** PF 1931 reference topology/traversal checkpoint approved at
`temple-reference-v5`; detailed room art and procedural module extraction remain.

## Current Checkpoint — TempleReference v5

The reference dungeon is now the stable room-review baseline:

- Fixed 30-room layout with the PF 1931-derived room identities.
- Thirty-two axis-aligned connections with two door portals per connection.
- Paired entrance routes reconnect through the Past/Future connector rooms and into
  Great Hall without corridor shells passing through unrelated rooms.
- Past, Present, and all three Future routes are logically connected from the entry.
- Crypt Chamber is a traversable through-room; Crypt Hall has an unobstructed entry.
- Future Chambers I exposes four distinct sockets: its entry and all three split paths.
- The layout and minimap display specific room names and the live player marker.
- Temple doors are automatic sliding stone slabs with manual toggling disabled and a
  larger closing safety radius than opening radius.
- Automated validation rejects disconnected graphs, diagonal reference connections,
  missing branch sockets, and corridors intersecting unrelated rooms.
- WorldGen Core tests pass, and ZoneEngine_New builds successfully with zero errors.

Use a fresh instance ID if this reference layout changes again; previously generated
instances may retain an older snapshot.

## PF 1931 Reference Reconstruction

The `TempleReference` layout profile provides a fixed, 30-room review world based on
the official PF 1931 room catalog and connection graph. Its two upper approach halls
terminate at the Past/Future connector rooms before entering Great Hall, keeping the
connector wing exits free of overlapping corridor walls. It includes the paired entry
routes, Entrance Hub, Great Hall, Past/Present/Future wings, crypt route, guardian
area, and their original resource-derived room identities. It deliberately renders
them with our procedural geometry rather than copying the original meshes. This lets
us walk every room, approve or reject its proportions and visual treatment, and then
promote approved modules into the randomized `Temple` profile.

```text
.worldgen 1931 30 temple-reference-v5 templereference temple procedural
.worldenter temple-reference-v5
.worldgen layout
```

The reference profile always requires exactly 30 rooms. Its seed is retained in the
manifest but does not randomize the topology.

Temple and TempleReference doors are proximity-operated sliding stone slabs. They
cannot be manually toggled. A door opens inside the near radius and does not begin
closing until the player has cleared a larger safety radius, preventing a closing
leaf from lifting or trapping the character.

## Goal

Create a replayable temple dungeon with the memorable progression of an occupied
cult sanctuary without reproducing Anarchy Online playfield 1931 room-for-room.
The player should move from a restrained public entrance into increasingly sacred,
dangerous, and visually imposing spaces.

## Reference Findings

The official PF 1931 resource capture contains 30 authored room records arranged
around an Entry Hall, an early hub, a Great Hall, and several strongly themed wings.
Those wings include crypt spaces, chamber/concourse chains, guardian spaces, and
larger destination rooms. This creates a useful rhythm: readable entry, choice at a
landmark, dangerous side routes, and major encounters in rooms that feel earned.

Community guides consistently describe an initially safe corridor, an early room
where cultists begin, long corridors capable of accumulating enemy pressure, many
minibosses, and a difficult final encounter. We should preserve that gameplay rhythm,
not the exact PF 1931 topology or named encounters.

## Original Layout Grammar

Every generated Temple should contain this critical-path progression:

1. **Pilgrim Entry** — safe or nearly safe orientation room.
2. **Processional Hall** — long ceremonial approach with the first cultists.
3. **Nave Hub** — a large, recognizable chamber with two or three visible routes.
4. **Inner Gate** — guardian encounter or locked threshold around 35–55% depth.
5. **Exarch Gallery** — later combat space that signals the elite section.
6. **High Sanctuary** — objective room around 65–85% depth.
7. **Profane Sanctum** — final boss arena at maximum critical-path depth.

Optional branches should come from the Nave Hub, the middle path, or the Exarch
Gallery rather than only from the entrance or final room:

- **Reliquary** — treasure branch with a small guard encounter.
- **Sunken Crypt** — darker low-ceiling branch with undead/corrupted inhabitants.
- **Scriptorium** — lore/objective branch with shelves, lecterns, and ritual props.
- **Ritual Chapel** — Reverend or named-miniboss branch.
- **Dormitory / Vestry** — compact ordinary combat branch.
- **Collapsed Cloister** — loop or shortcut that reconnects to the main route.

## Topology Rules

- Use 18–34 rooms for the first Temple implementation.
- Reserve 55–70% of rooms for the critical route and the remainder for branches.
- Give the Nave Hub degree 3 or 4 and make it an early visual anchor.
- Place the boss at the greatest graph distance from the entrance.
- Put the main objective in the latter half, but not automatically adjacent to the boss.
- Require at least two substantial side branches when room count permits.
- Prefer one reconnecting loop; avoid a pure corridor tree when enough rooms exist.
- Keep the entry leg readable and mostly linear, then broaden choice after the hub.
- Alternate narrow processional passages with large halls and chapels.
- Prevent consecutive Grand Chambers except for a deliberate sanctuary-to-boss finale.
- Use locked/sealed doors sparingly at guardian thresholds and treasure branches.

## Seed Variation

The seed should vary more than room dimensions. It should select among several
recognizable macro patterns:

- **Trident:** central nave with three deep wings; one contains the final sanctum.
- **Processional:** long main spine with chapels on both sides and a late split.
- **Twin Cloisters:** two routes around a central sacred space that reconnect later.
- **Broken Cross:** asymmetric transepts with a displaced sanctuary and crypt branch.
- **Spiral Descent:** the route curls around the hub and gradually descends inward.

Role ordering and validity remain fixed while branch side, rotation, depth, loop
placement, and optional room selection are seed-driven.

## Visual and Encounter Language

- Tall stone walls, heavy pillars, arches, red/dark-purple cloth, bronze trim,
  braziers, censers, altars, and restrained blue-white energy accents.
- Strong axial symmetry near the entrance; increasing damage, corruption, and
  asymmetry deeper in the dungeon.
- Cultists populate entry and middle routes; Reverends lead chapels or ritual rooms;
  Exarch-style elites guard late halls; named guardians occupy threshold rooms.
- Large rooms need columns, platforms, or altar blocks so they do not become empty
  rectangular boxes and so line-of-sight combat remains controllable.
- Encounter identities are semantic anchors. Exact AO character names, layouts, and
  asset arrangements are reference material rather than generation requirements.

## Implementation Order

1. **Complete:** Temple-specific macro topology selection and semantic room names.
2. **Complete:** Initial deterministic validation for layout variety and landmarks.
3. **Complete:** Layout/minimap labels, player marker, zoom, and pan.
4. **Current:** Review the fixed reference dungeon room-by-room and define approved
   entry, hallway, hub, chapel, crypt, gallery, sanctuary, and boss-arena modules.
5. **Next:** Promote approved reference-room treatments into reusable procedural
   geometry and fixture recipes.
6. **Later:** Populate cultist, Reverend, Exarch, guardian, and boss encounter anchors.

## Next Steps

### 1. Room-by-room visual review

Walk `temple-reference-v5` and record feedback using the exact layout label. Review in
this order so foundational pieces stabilize before destination rooms:

1. Entry Hall and Entrance Hub.
2. Lower/Upper Hallways and Past/Future Connectors.
3. Great Hall.
4. Wing of the Past, Crypt Chamber, and Crypt Hall.
5. Present Concourse, Present Chambers, and Present Temple.
6. Future Chambers I–III, three Concourses, three Hubs, Guardian Hall, and Future Temple.

For each room decide: footprint, ceiling height, doorway positions, columns/arches,
lighting, banners/altars/props, encounter space, and whether the room is reusable,
reference-only, or rejected.

### 2. Geometry and door refinement

- Replace placeholder box architecture with approved Temple wall, arch, column,
  ceiling, floor, altar, and stair recipes.
- Give crypt rooms their own socket-safe alcove geometry instead of restoring the
  obstructing generic L-shaped notch.
- In-game test every automatic stone door from both directions, including standing
  inside its trigger and approaching several doors in quick succession.
- Confirm the three Future split paths remain physically traversable, not merely
  logically connected on the layout.

### 3. Procedural extraction

- Assign socket contracts and allowed rotations to every approved module.
- Teach the randomized `Temple` profile to select those modules by semantic role.
- Preserve the five seeded macro families while adding reference-derived room rhythm.
- Add generation checks preventing corridors, room shells, and door blockers from
  intersecting unrelated modules.

### 4. Gameplay population

- Define encounter anchors only after geometry and traversal are approved.
- Populate early/middle routes with Cultists, chapels with Reverends, late routes with
  Exarch-style elites, and threshold rooms with named guardians.
- Add objectives, treasure/reliquary rewards, boss staging, respawn behavior, and
  encounter-specific leash boundaries in later passes.

## First Acceptance Check

Generate at least six consecutive seeds at 24 rooms. Each layout must be connected,
must visibly differ at the macro level, must include the complete critical-path role
sequence, and must make the entrance, central hub, side branch, objective, and boss
room immediately identifiable in `.worldgen layout`.

The automated contract currently exercises 30 consecutive 24-room Temple seeds,
requires all five macro families to appear, and verifies the Pilgrim Entry,
Processional Hall, Nave Hub, Inner Gate, Exarch Gallery, High Sanctuary, Profane
Sanctum, Sunken Crypt, and Ritual Chapel progression. The reference topology has
reached its traversal checkpoint; detailed visual review and reusable module approval
are now the active acceptance work.
