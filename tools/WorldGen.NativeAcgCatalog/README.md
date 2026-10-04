# Local native ACG baseline

This prepares local collision and metadata for the existing AORebirth native
ACG generator. It reads the user's installed database. The original client reads
its rendering assets from its own installation; this tool exports no client
textures or rendering package. The local server cache contains room metadata,
collision surfaces and grayscale height/occupancy layers. Do not redistribute it.

The current baseline uses configured style 324 and test slot 351. The PF351 static
copy experiment failed gameplay acceptance. Preparing its 81 room surfaces succeeded,
but the existing generator could not close the tested seed 90602 at target sizes
4 or 12. Style324 preparation matched 55 source rooms and 91,484 surface triangles
against the installed DLL reader. Seed90602/target12 produces 20 legal rooms,
with 1,616 placed collision meshes and supported entrance (2.5,5.02,135).
The 151-byte native room-list payload parses and round-trips. The user subsequently entered this baseline in the original client and saw a
mission-style dungeon, as selected by style324. This confirms displayed mission
geometry; it does not establish a subway layout or complete movement/collision
acceptance.

The server package has passed publication and startup validation. Install it from
the ProjectMayhem2.0 checkout in a terminal (the installer needs your sudo password):

```bash
cd /home/cody/Coding/ProjectMayhem2.0
./tools/deploy-local-pf127-rooms.sh
```

Then log in with the original client and run:

```text
.worldgen native-client-acg 90602 12 native-acg-test 351
.worldenter native-acg-test
.worldexit
```

`12` is the existing generator's target size. Entrances, halls and closing rooms
can make the actual count larger. The server reports the actual count. Seed is
an integer in 0..2147483647; target size is 3..40. Only one test slot is currently
reserved. Copies/layouts are in memory; restart to clear the slot for another
test. Characters saved in a missing test instance recover through Respawn.json
(currently PF800). No XP, loot, NPC or quest content is attached to this baseline.

`NativeCopies/native-client-playfields.json` selects `AcgStyle` and
`ReservedPlayfields`. The native command uses the existing generator and doorway
validation, samples floor support/entrance clearance, hashes the source catalog,
and sends its existing ACG payload. Server collision is assembled from exactly
that room list without merging a static test playfield's geometry. Unity high-ID
generation and its PF127 authoring stay available.

Prepare another local style only after its entrance/main-hall mappings exist in
the local GameData/DungeonEntrances.json. Run the existing indoor collision reader
first (it starts our helper, not the game), using the installed source playfield's
tilemap ID and room count:

```bash
WINEDEBUG=-all wine 'Z:\path\to\AOIndoorExtractor.exe' \
  'Z:\path\to\Anarchy Online' 324 310 55 'Z:\tmp\native-style324.aois'
dotnet run --project tools/WorldGen.NativeAcgCatalog/WorldGen.NativeAcgCatalog.csproj -- \
  '/path/to/Anarchy Online' 324 /tmp/native-style324.aois /path/to/local/GameData
```

Preparation refuses to overwrite an existing style folder. Use `--verify` as the
last argument to verify an already prepared folder. Source/catalog preparation
can succeed while candidate generation fails; a created folder alone does not
establish a usable native layout. The utility depends on the sibling AORebirth
source; override the `AORebirthRoot` MSBuild property for another checkout path.

PF127 remains an investigation. Its Unity room poses, irregular dimensions and
measured vertical joins are not encoded by the current ACG adapter. The current
five-tile dimension preflight accepts only one of its 46 rooms, excluding the
entrance. This baseline proves neither that the client supports PF127 rearrangement
nor that every possible native representation is incapable of it. Verify the
supported ACG path first, then investigate native PF127 placement constraints.


## Fresh original-client playfield investigation

The user requests a fresh ID such as90000/900001 instead of adapting an existing
playfield. Distinguish the runtime instance ID from the layout/resource selector:
`NativeClientPlayfieldAdapter` sends a generated-building identity and style324
payload for ACG; the ordinary static path sends Playfield1 with the runtime ID.
A high runtime ID sent as a static resource selector therefore requests a missing
map. Earlier high-ID failures do not by themselves establish a numeric limit for
proper generated instances. A fresh ID alone does not encode PF127 room poses.

Next separate proof points: (1) retain the accepted mission-style layout and
validate a fresh runtime identity through transfer/reconnect, (2) investigate a
client-side virtual playfield definition/room-placement overlay for one subway
room, referring to existing installed geometry/material records and matching
server collision, then (3) multiple transformed rooms and joins. No high-ID ACG
entry or virtual subway resource overlay is implemented/accepted by this note.

The server repository already has a build-specific client-extension host at
`Tools/AOClientRoomSpaceGuard/ProxyDll`; it currently contains login/crash fixes,
not a custom dungeon loader. It supplies an existing extension mechanism to
investigate, not proof that a resource hook is known or sufficient. Our
`AOResourceDatabase` is read-only and provides no installed-database writer.
Leave installed database files unchanged during this investigation. The proposed
runtime overlay would carry generated metadata/placements, with meshes/textures
remaining sourced from each player's own installation. Hook points, native
resource addressing, room assembly and client collision still require evidence.
