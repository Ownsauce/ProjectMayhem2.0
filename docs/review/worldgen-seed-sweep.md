# WorldGen 5.9.0 seed sweep

The CSV files in this directory record deterministic Core layout results for seeds 1–30
across all seven profiles. The sweep runs `DungeonValidator`, hashes each layout with
`DungeonLayoutHasher`, and measures graph distance from the entrance, degree, dead-end
ratio, and independent loop count (`connections - rooms + 1`). A failed seed retains
its exception in the CSV. `TempleReference` always uses its required 30 rooms.

Reproduce from the ProjectMayhem root:

```bash
dotnet run --project tools/WorldGen.SeedSweep -- 5.9.0 1 30 30 > docs/review/worldgen-5.9.0-seeds-1-30.csv
dotnet run --project tools/WorldGen.SeedSweep -- 5.9.0 1 30 24 > docs/review/worldgen-5.9.0-seeds-1-30-rooms-24.csv
```

At 30 rooms, all 210 layouts generate and validate. All six seeded profiles have
zero loops; the fixed TempleReference graph has three. At 24 rooms, Subway fails
for all 30 seeds with `No subway junction outlet cell was available`; the other
180 layouts validate. The fixed boss path length within each profile confirms
that current critical path distance is largely set by room count rather than seed.

These are Core graph and validator results. Unity frame time, draw calls, object
count, mesh and texture memory, and visual traversal still need an editor run.
The 30-seed sample is a starting baseline, not a quality threshold or an exhaustive
failure rate estimate. Preserve these hashes for version 5.9.0 when changing
placement or room roles in a new generator version.

## Subway placement recovery in 5.10.0

Version 5.10.0 retains the six seeded Subway approach templates and selects a free
junction location with room for two outlets before placing its first optional room.
It then searches a bounded set of adjacent cells for each branch. The old 5.8.0 and
5.9.0 placement path remains in place. The client manifest reader accepts 5.10.0.

The two `worldgen-5.10.0` CSV files use the same seeds and room counts as the 5.9.0
baseline. All 210 layouts validate at 24 rooms and all 210 validate at 30 rooms.
An additional local sweep of seeds 1–1000 found zero Subway failures at either
room count. Regenerating the saved 5.9.0 30-room baseline after the change produced
the same canonical hashes for every row. These checks do not replace Unity
traversal and presentation review for the changed Subway branch shapes.

## Subway route and doorway correction in 5.11.0

A seed 1, 30-room playthrough reported a jump between the rail tunnel and an
optional junction, plus snagging at the connector from Station Concourse 8 to
Station Concourse 11. In 5.10.0, room 8 (Track Tunnel) connected directly to
room 10 (Track Junction). Their rail floor levels differed by 1.2 m without
stair treads on that rail join. Version 5.11.0 attaches the optional branch to
room 9 (Station Concourse) and uses pedestrian concourse rooms for that branch.
Only the two Track Tunnels adjacent to the Train Chamber remain. The existing
Track Tunnel to side-room descent uses 240 mm maximum stair rises.

Optional concourse-to-concourse connectors in 5.11.0 have a 4.0 m corridor and
3.6 m doorway. The reported Concourse 8 to 11 connector had been 3.0 m and
2.6 m. A focused N3Lite surface query through that doorway at torso height
found a collision 1.6 m off center in 5.10.0 and a clear line in 5.11.0.
That measures added clearance; a player traversal check is still needed.

The saved 5.11.0 CSVs cover seeds 1–30 at 24 and 30 rooms: all 420 layouts
validate. A further local sweep of seeds 1–1000 found zero Subway generation or
validation failures at either room count. Regenerated 5.10.0 hashes match the
saved 30-room baseline. AORebirth emits 5.11.0 manifests, and Unity accepts them.

## Subway rail-side and train-aisle correction in 5.12.0

The 30-room, seed-1 5.11.0 layout still attached Concourse 13 to the end of
Track Tunnel 6. The rail side branches now use only the two sides perpendicular
to the train line; if both are occupied, they attach to a pedestrian branch
instead. Shorter critical paths also use a side approach and departure. Older
manifests continue to generate with their original geometry.

The train chamber grows from 44 × 40 m to 46 × 42 m in 5.12.0. Its track bed
widens from 5 m to 7 m, moving platform retaining walls and access stairs away
from the train. The clear aisle between a car and the platform wall is now at
least 2.2 m, versus about 1.2 m in 5.11.0.

The WorldGen Core regression generated seeds 1–1000 at both 24 and 30 rooms,
validated each layout, asserted every pedestrian-to-rail connection is on a
rail side, and checked the train-side aisle. Core and geometry checks pass;
AORebirth's ZoneEngine_New project builds with no errors. Live client movement
beside the train remains to be checked in-game.

Create: `.worldgen 1 30 subway-512-seed1 subway subway procedural`

Enter: `.worldenter subway-512-seed1`
