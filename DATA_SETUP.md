# AO installation setup

Project Mayhem reads presentation resources directly from a local Anarchy
Online installation. A normal source checkout does not require a pre-exported
asset pack, an AOGLTF checkout, or manually created Unity source links.

## Requirements

- Unity `6000.3.8f1`
- An Anarchy Online installation containing:
  - `Anarchy.exe` or `AnarchyOnline.exe`
  - `version.id`
  - `cd_image/data/db/ResourceDatabase.dat`
  - `cd_image/data/db/ResourceDatabase.idx`

The repository includes the managed runtime assemblies used by the direct
reader:

```text
AO.Unity/Assets/Plugins/AODB/AODB.dll
AO.Unity/Assets/Plugins/AODB/AODB.Common.dll
```

## First run

1. Clone the repository and check out the Project Mayhem 2.0 branch.
2. Open `AO.Unity` in Unity `6000.3.8f1`.
3. Allow Unity Package Manager to restore the dependencies declared in
   `AO.Unity/Packages/manifest.json`.
4. Open `Assets/Scenes/TestScene.unity` and enter Play mode.
5. Enter the AO installation folder in the connection screen, or set it under
   **F10 > AO Assets**.

The setting is stored in Unity `PlayerPrefs`. For development and automated
launches, `PROJECTMAYHEM_AO_INSTALL` can provide the initial path. Project
Mayhem also checks common Windows and Wine installation locations.

Validation succeeds when the executable, version file, database, and index are
present and non-empty.

## Runtime data flow

Direct AODB readers load requested records from the configured resource
database. Project Mayhem snapshots records into its runtime models and stores
disposable generated data under:

```text
Application.persistentDataPath/AOAssetCache/
```

The cache can be deleted whenever a clean rebuild is needed. The configured AO
path is not written into the repository.

### Indoor runtime reader

Native PF 127 copies read their textured room architecture and static models from the AO installation
selected at launch. No room GLB, PNG export or downloaded presentation pack is needed.
The local reader prepares geometry on a background worker; Unity decodes original
JPEGs in memory and constructs rooms over multiple frames. A zone change cancels
pending work. Temporary geometry/room-plan files are deleted after reading; visual
assets are released when the world is destroyed. Existing collision caches remain
local and separate from presentation.

Build the Project Mayhem helper executables once from the repository root:

```bash
tools/build-indoor-runtime-helpers.sh
```

This uses `i686-w64-mingw32-g++` and places our reader code under
`AO.Unity/Assets/StreamingAssets/Tools/`, included by Unity builds. The helpers contain
no AO DLLs, textures or meshes; AO DLLs are loaded from the player's own installation.
The current native backend supports Windows and Linux with Wine. It requires the
verified N3.dll build described in the [visual reader notes](tools/WorldGen.NativeVisualExport/README.md).
Other native builds are rejected before private offsets are called; a portable managed
tile assembly implementation remains future work.

The runtime ignores earlier `IndoorVisuals/pf127` exported packages. Local diagnostic
exports under `exports/` are optional and Git-ignored. Indoor static models use the
installation's `cd_image/data/statels/<PF-id>.pf` room blocks and existing ABIFF
mesh/material reader, including texture overrides. Collision preview rendering is
hidden once architecture and models are ready; physics remains intact. Interactive
objects, original lightmaps/lights and water remain follow-ups.

Dungeon recipes should store source resource/room IDs, seed and placement transforms;
each client resolves presentation from its own installation. Current PF 127 seed
copies preserve the source arrangement. Random rearrangement still needs shared
room connections and matching server collision generation.

## Repository packages

Unity consumes these repository packages through relative entries in
`AO.Unity/Packages/manifest.json`:

```text
file:../../AO.Core
file:../../AO.Client
file:../../AO.Assets
```

Keep the repository directory structure intact. Do not create the old
`AO.Unity/Assets/External` symlinks; they are no longer part of setup.

## Optional development data

`AO.Unity/Assets/StreamingAssets/AOData` remains an optional compatibility and
development-data location. The repository includes small Project Mayhem
configuration files such as `servers.json`, but a direct-AODB client does not
require a complete exported world or visual dataset there.

Legacy tools may still use these local paths:

```text
AO.Unity/Assets/StreamingAssets/AOData/
AO.Unity/Assets/Resources/WorldMeshes/
AO.Unity/Assets/Resources/CharacterMeshes/
AO.Unity/Assets/Resources/ItemMeshes/
AO.Core/Data/
AO.Server/Data/
AO.Tools/Data/
```

These paths are ignored except for explicitly committed Project Mayhem files.
They are not part of the standard first-run workflow.

## Server and command-line projects

The standalone `AO.Server` backend can use server-owned gameplay-data
overrides under `AO.Server/Data`. That server dataset is separate from the
Unity client's direct presentation-resource reader.

`AO.Tools` contains offline diagnostics and migration utilities. It is not
required to point the Unity client at an AO installation.

## Troubleshooting

- **Install rejected:** select the directory containing the AO executable and
  `cd_image`, not the `cd_image/data/db` directory itself.
- **Packages fail to resolve:** confirm the repository still contains sibling
  `AO.Core`, `AO.Client`, and `AO.Assets` directories relative to `AO.Unity`.
- **Old generated output appears:** clear
  `Application.persistentDataPath/AOAssetCache` and restart the client.
- **A resource is unsupported:** check the Unity console for its resource type
  and ID. Unsupported records should fall back without invalidating the whole
  playfield load.

## PF 127 room dungeon metadata

Prepared native room packages under `Assets/StreamingAssets/NativeCopies` are local
generated data and are Git-ignored, including catalog snapshots and collision.
Source definitions, authoring code/settings and publication tools remain in Git.
A fresh checkout needs these packages prepared and published from its own AO
installation before native dungeon preview or gameplay. See the
[source preparation tool](tools/WorldGen.NativeRoomCatalog/README.md) and
[catalog publication workflow](docs/native-dungeon-authoring.md#select-review-and-publish-sources--october-4-2026).
Ignoring these packages does not delete the current local copies.

After local preparation, the native room generator reads source room metadata in
`AO.Unity/Assets/StreamingAssets/NativeCopies/pf127.rooms.json`. Rendering meshes,
images and placed models come from the AO installation selected at launch.
The local server uses separately prepared collision triangles from that same
installation. Source/catalog hashes must match before entry. This path needs the
existing PF 127 indoor extraction cache from the working reference copy.

See [native room setup and commands](docs/review/pf127-asset-reuse.md#seeded-native-room-dungeons--2026-10-03).

Native PF 127 rendering performs no network mesh or texture download. It reads
local resources when entering the dungeon and constructs Unity meshes/materials.
Collision uses the local AOIS cache; the indoor helper's temporary decoded geometry
file is deleted after processing. This is runtime local-resource loading, with
local caches and temporary processing files rather than a redistributed render pack.
