# Native AO room catalog preparation

Builds room metadata and a **local server physics** package from the selected AO
installation and a validated indoor collision cache for the selected source. The Unity runtime
continues reading its own meshes/textures from the selected installation.
PF127 is the default source; PF1931 has its own `pf1931.source.json` definition.
The cache, prepared outputs and published packages are local generated data and
are Git-ignored. A fresh checkout must prepare them locally.

```bash
dotnet run --project tools/WorldGen.NativeRoomCatalog/WorldGen.NativeRoomCatalog.csproj -- \
  '/path/to/Anarchy Online' '/path/to/127_v2.aois' '/tmp/pf127-native-rooms'
```

Outputs `pf<source>.rooms.json` and `pf<source>.rooms.collision`. Publish these
through `WorldGen.PublishRoomCatalog` to stage matching retained revisions for both
hosts, then rebuild/install the server. See
[publication and source selection](../../docs/native-dungeon-authoring.md#select-review-and-publish-sources--october-4-2026).
Directly copying the old PF127 filenames is the legacy workflow; it does not
activate a registered source revision.

The tool checks seed variation, deterministic recipes and doorway floor support.
Current catalog version4 / collision policy1 clips collision to the source-room
footprint and measures each doorway's actual passage floor, capsule headroom and body
obstructions. Unsupported or blocked sockets are excluded from generation. This
distinguishes raised exits from buried terrain, roofs and lintel surfaces. Checks
compare captured floor heights on both sides of every generated join, including
4/12/24-room recipes and the raised U-turn/connector regression in all four rotations.
Generator version 1.2.0 requires this updated catalog on both client and server;
recreate instances after installing the local server build. Entrance
height is derived from an interior floor with capsule support/headroom, excluding
buried terrain and roof caps. This is
specific source-catalog preparation; gameplay mechanics consume the exported data.

See [implementation and live commands](../../docs/review/pf127-asset-reuse.md#seeded-native-room-dungeons--2026-10-03).

Walking-region preparation now checks routes inside rooms as well as doorway
thresholds. An optional fourth argument supplies exported authoring overrides for
room/socket exclusions, pinned layouts, closures and light settings. The tool
validates before publishing outputs. See [native authoring](../../docs/native-dungeon-authoring.md).

Authoring overrides also carry data-driven allowed room uses, landmark/review flags,
theme tags, notes and an optional supported encounter center. The native editor
loads/saves these through an authoring DAO. Roles do not change normal room
selection; the optional dungeon-run planner is future work. The fourth argument
applies the saved tags to the prepared catalog. Publishing changes its content hash
and requires matching client/server catalogs. See [data and DAO design](../../docs/dungeon-authoring-data.md).

Source playfield, entrance/spawn X/Z, pool memberships and source-specific doorway
check cases are editable in `pf127.source.json`, copied beside the built tool.
To select another definition explicitly, use a fifth argument; use `-` in the
fourth position when no room overrides are needed. Available pool names come from
catalog data. Existing PF 127 assignments are preserved. Source preparation is
configurable; client/server source-package registration remains a separate concern.
