# Original AO client compatibility preflight

The next native entry experiment now uses the existing ACG room-list payload
with matching collision. See [native ACG baseline](../WorldGen.NativeAcgCatalog/README.md).
The static-copy command below remains a failed diagnostic experiment.

**Live status, October 4:** the recognized-ID PF 351 copy experiment failed
gameplay acceptance: the user saw an unexpected room and could not move or turn.
Server logs confirm transfer/reconnect to 351 with source-127 collision, followed
by repeated collision rejection. They do not establish which geometry the client
rendered. The copy commands below document the experiment, not a working native
dungeon workflow. Changing a destination ID has not established layout delivery.

Run from ProjectMayhem2.0:

```bash
dotnet run --project tools/WorldGen.NativeClientProbe/WorldGen.NativeClientProbe.csproj -- \
  '/path/to/Anarchy Online' \
  AO.Unity/Assets/StreamingAssets/NativeCopies/pf127.rooms.json \
  /tmp/pf127-native-client-compatibility.json 90602 12
```

Reads the selected local AO database and verifies its source record against the
native catalog. Generates the normal candidate recipe and reports source/selected
rooms that fail the existing AORebirth ACG adapter's five-tile dimension rule.
This checks a necessary size condition, not all native-client legality rules.
It starts no client, sends no packets and edits no AO installation files.

For the current PF 127 source, only room 13 (`Mini Bridge Exit West`, 5 x 10 tiles)
has whole-block dimensions. Entrance room 8 is 37 x 18 tiles and fails. The tested
12-room seed 90602 contains ten distinct source rooms rejected by that check.
The working Unity recipe cannot be sent unchanged through this ACG adapter.

The isolated original-client **source-copy baseline** is available after installing
the updated local server:

```text
.worldgen native-client-copy 127 90602 pf127-client-copy 351
.worldenter pf127-client-copy
.worldexit
```

Use the original AO executable connected to AORebirth. This uses the installed
source playfield identity in `PlayfieldAnarchyF` and a reserved, client-recognized
test playfield ID, with no Unity manifest envelope or invented ACG payload. It preserves
the original room arrangement; the seed labels the copy. It does not create NPCs,
loot, quests or door interactions. It is an unaccepted client experiment, not
evidence that randomized PF 127 layouts work in the original client.

The existing `.worldgen pf127`, `.worldgen pf127-rooms`, and Unity native tool remain
available. Copy instances are currently in-memory and must be recreated after a
server restart; native room-recipe persistence is a separate path.

Local original-client test IDs are configured in the server's
`NativeCopies/native-client-playfields.json`. The present configuration reserves
351 (`ACD Subway - Ventil`, present in the selected local AO database) for source
127. PF 427 has no playfield record in that installation. The optional last command
argument selects a configured test ID; omitting it chooses an available configured
slot. Creation refuses an ID already loaded or allocated, and the source ID itself
cannot be a test slot. Only one original-client copy can occupy the currently
configured slot. Choose another verified/reserved slot or restart the server for
another copy; exiting alone does not release its allocation.

The original-client path uses 351 in server routing, transfer, runtime identity and
PlayfieldId2; 127 remains PlayfieldId1's source asset selector. Unity generation
retains its high-ID allocation. Entry still requires user live acceptance; database
record presence does not establish complete client transfer compatibility.

Login from a generated instance that is missing after restart returns to the
server's existing `GameData/Respawn.json` destination: presently PF 800 at
(665, 72.6, 570). This includes orphaned high-ID Unity instances and missing
reserved original-client copies. It prevents an orphan from being sent to the
client as a static map. It uses normal character persistence, without direct SQL
edits; saved native room recipes remain enterable through their name.

Next: verify original-client entry/rendering/transfer with this source copy and
inspect actual client/capture evidence. Then determine whether another supported
native format can represent PF 127's sizes and per-room heights. Otherwise arbitrary
subway rearrangement would need client-side support or ACG-compatible source rooms.
The preflight must not be treated as proof that every possible native mechanism
is incapable of loading these assets.

The selected local database scan found no verified blank dungeon host. PF 128,
`Emote Test`, has one defined room. Five decoded indoor definitions have zero room
entries (1424, 2006, 2014, 2024, 4352), but their existing building/shop/market
names and zero room entries do not establish empty geometry or permit arbitrary
room placement. PF 655 is Andromeda and PF 800 is Borealis; both are outdoor
definitions. PF 351 contains 81 room templates. An original-client procedural
path needs a client-supported layout representation such as the existing ACG
generator payload, with matching server collision. The current PF 127 recipe
fails that adapter's size preflight, and the installed server also reports
missing style-324 GameData. Those are separate follow-ups from destination-ID
selection. No client files or running services were changed by this review.
