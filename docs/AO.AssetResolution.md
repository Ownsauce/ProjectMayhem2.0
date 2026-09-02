# AO Install Asset Resolution

## Purpose

Project Mayhem resolves presentation resources on demand from the configured
AO installation. Direct AODB reads are the primary Unity runtime path. Derived
results may be stored in a disposable local cache, while Project Mayhem
placeholders cover missing or unsupported records.

## Lookup flow

```text
Project Mayhem requests a resource
        |
        v
Check the user-local derived cache
        |
        +-- Found ------> Load the cached result
        |
        +-- Missing ----> Read the configured AO database
                                |
                                v
                       Decode or snapshot the record
                                |
                                +-- Success --> Render and optionally cache
                                |
                                +-- Failure --> Use a placeholder and log ID/type
```

`StreamingAssets/AOData` remains an optional development and compatibility
source. It is not required to contain a complete pre-exported visual dataset.

## Responsibility boundaries

The local AO database supplies presentation resources and metadata such as:

- Character, creature, item, and world meshes
- Terrain, water, indoor rooms, and static playfield records
- Textures, icons, animations, and supported audio
- Resource names and resource-ID mappings

The connected server supplies live and authoritative state such as:

- Characters, inventory, equipment, and stats
- Current playfield and position
- Players, NPCs, mobs, pets, and interactive objects
- Combat, nanos, loot, quests, shops, chat, and persistence

Local resource records describe how an identity looks; they do not replace the
server state describing what currently exists or what actions are accepted.

## Runtime components

- `AOInstallConfiguration` stores the selected path and exposes the cache root.
- `AOInstallLocator` discovers and validates installation candidates.
- `AOResourceDatabase` and `AOResourceCatalog` provide engine-neutral indexed
  raw access.
- Bundled AODB assemblies provide the typed records used by the current Unity
  playfield, mesh, texture, item, and animation readers.
- `AOInstallAssetResolver` and `AOAssetCache` support asynchronous converted
  outputs where a persistent file is useful.
- `AOCompositeAssetResolver` supports ordered overrides and fallbacks.

Unity presentation code should request Project Mayhem models or resolved
assets. Keep database parsing and AODB object traversal in the asset/world
reader layer, and snapshot mutable record data before background processing.

## Configuration

The user can select the AO root from the connection screen or **F10 > AO
Assets**. The root is valid when it contains:

```text
Anarchy.exe or AnarchyOnline.exe
version.id
cd_image/data/db/ResourceDatabase.dat
cd_image/data/db/ResourceDatabase.idx
```

`PROJECTMAYHEM_AO_INSTALL` can seed the path. Common Windows and Wine paths are
also probed. The selected path is stored locally in `PlayerPrefs`.

## Cache design

Generated data belongs under:

```text
Application.persistentDataPath/
└── AOAssetCache/
```

Cache identity should include the resource type and ID, database fingerprint,
decoder/converter version, output format, and relevant settings. Writers use a
temporary file and publish only complete output. Concurrent requests for the
same destination share one operation.

The cache is an optimization and must remain safely disposable. A missing or
stale cache entry should trigger another database read, not break the client.

## Performance and diagnostics

- Keep database I/O and expensive decoding away from Unity's main thread when
  the AODB object has been safely snapshotted.
- Load required spawn-area resources during the playfield loading phase.
- Stream optional and distant resources incrementally.
- Deduplicate concurrent requests for identical resource IDs.
- Log the resource type, ID, database fingerprint, cache result, duration, and
  fallback reason when diagnosis is useful.
- Treat an unsupported or malformed record as a local failure rather than
  invalidating an entire playfield.

## Current direction

Continue expanding direct decoders in independently testable slices. Use
AOGLTF, AOSharp exports, and older `StreamingAssets` outputs only as behavioral
references or optional developer overrides. The shipped runtime architecture
should depend on the configured AO database, the bundled AODB assemblies, and
Project Mayhem's own cache and model boundaries.
