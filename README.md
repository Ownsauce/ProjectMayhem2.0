# Project Mayhem 2.0

Project Mayhem 2.0 is an in-development Unity client for Anarchy Online. It is
designed to work with different servers through separate backend adapters while
keeping Unity presentation and local AO asset loading independent of a specific
server implementation.

## Requirements

- Unity `6000.6.0f1` (see `AO.Unity/ProjectSettings/ProjectVersion.txt`)
- A sibling [WorldGen](https://github.com/Ownsauce/WorldGen) checkout containing
  `WorldGen.Core` and `WorldGen.Geometry`
- A local Anarchy Online installation containing a valid executable,
  `version.id`, `ResourceDatabase.dat`, and `ResourceDatabase.idx`

The required AODB runtime assemblies and local Unity packages are included in
the repository. A separate AOGLTF installation or pre-exported visual asset pack
is not required for normal use.

## Getting started

1. Clone ProjectMayhem2.0 and WorldGen beside each other, preserving this layout:

   ```text
   Coding/
     ProjectMayhem2.0/AO.Unity/
     WorldGen/WorldGen.Core/
     WorldGen/WorldGen.Geometry/
   ```

2. Open the `AO.Unity` directory using the Unity version recorded in
   `AO.Unity/ProjectSettings/ProjectVersion.txt`.
3. Allow Unity Package Manager to restore the packages from
   `AO.Unity/Packages/manifest.json`.
4. Open `Assets/Scenes/TestScene.unity` and enter Play mode.
5. Select your local Anarchy Online installation from the connection screen or
   under **F10 > AO Assets**.

For installation-path details and troubleshooting, see [DATA_SETUP.md](DATA_SETUP.md).

## Procedural dungeons: two asset paths

WorldGen supplies shared generation contracts; ProjectMayhem supplies Unity
presentation and AO resource loading. The current gameplay integration is in
AORebirth `ZoneEngine_New`, which owns server collision, saved instances and player
transfers. `AO.Server` is a separate development backend; referencing WorldGen
there does not provide the AORebirth chat commands automatically.

| Path | Where presentation comes from | Authoring tool |
| --- | --- | --- |
| Custom assets / GLB | Project-authored models imported into Unity kits; existing Temple kit also includes Unity meshes | **Tools > WorldGen > Dungeon Authoring** for the Subway kit; GLB preview/import tools for custom models |
| Existing AO rooms | Meshes, textures and static models read from the launch-selected local AO installation | **Tools > WorldGen > Native Dungeon Authoring**, with registered Subway PF127 and Temple PF1931 sources |

For the AO-room path, first build the local readers, prepare and publish matching
room catalogs/collision to client and server, and build/install the integrated
server. These generated packages are Git-ignored and absent from a fresh checkout.
See [native setup and publication](docs/native-dungeon-authoring.md#select-review-and-publish-sources--october-4-2026)
and [reader setup](DATA_SETUP.md#indoor-runtime-reader).
Once configured, use in-game GM chat:

```text
.worldgen native-rooms ao-subway 90602 24 subway-run-01 mixed
.worldenter subway-run-01

.worldgen native-rooms ao-temple 90603 24 temple-run-01 mixed
.worldenter temple-run-01
```

Change the seed and use a new world name for another layout. AO-room counts are
currently 4–24; `mixed` uses the source's available room pools. Source PF127/PF1931
identify the asset families; generated instance playfield IDs are server-owned.
`.worldenter` restores an existing world using its recorded catalog revision.
An editor preview does not create a server instance.

The custom Subway path uses a separate generator and asset kit:

```text
.worldgen 90602 30 custom-subway-01 subway subway procedural
.worldenter custom-subway-01
```

See [custom dungeon authoring](docs/dungeon-authoring.md),
[custom Temple/reference workflow](docs/temple-procedural-design.md),
[AO room authoring and saved recipes](docs/native-dungeon-authoring.md),
[data and DAO boundaries](docs/dungeon-authoring-data.md), and
[integration boundaries and roadmap](docs/worldgen-integration-boundaries.md).
For another Unity client/server, start with WorldGen's
[client/server integration guide](https://github.com/Ownsauce/WorldGen/blob/main/docs/CLIENT_SERVER_SETUP.md).
The complete reusable runtime adapters remain planned; no separately hosted API
is required for the local library integration.

## What has been started

- Server selection, login, character selection, creation, and world entry
- A backend-neutral client layer intended to support different servers
- Direct loading from the local AO resource database
- Outdoor terrain, water, static objects, meshes, textures, and playfield data
- Character meshes, appearance, movement, and CAT animation playback
- Inventory, equipment, wear-window layouts, item icons, and item tooltips
- Runtime resolution of equipped weapons and armor from AO item records
- Programs, nano casting, recharge feedback, and an NCU window
- Character stats, skills, IP, health, nano, and XP display
- Chat, status reporting, movement synchronization, and nearby world entities
- Initial indoor room, surface, and navigation support
- Persistent caching of disposable derived assets

## Current status

This is an active prototype, not a finished replacement client. Backend feature
coverage varies, and some systems still use partial metadata or presentation
fallbacks. Areas needing further work include broader indoor rendering, unusual
equipment calibration, complete combat and nano behavior, additional protocol
coverage, and fresh-clone Unity testing across different machines.

The connected server remains authoritative for character state and gameplay.
Project Mayhem handles presentation, input, backend communication, and visual
resource resolution from the user's local AO installation.

## Repository layout

```text
AO.Unity/       Unity application, UI, input, and world presentation
AO.Client/      Backend adapters and client-domain state
AO.Assets/      AO database readers, decoders, and asset caching
AO.Core/        Shared gameplay models and rules
AO.Server/      Optional local development and test backend
AO.Tools/       Offline diagnostics and conversion tools
docs/           Architecture, progress, and research notes
tests/          Decoder, cache, protocol, and integration tests
```

More detailed status is available in
[PROJECT_MAYHEM_2.md](PROJECT_MAYHEM_2.md) and
[docs/Current-Progress-and-Next-Steps.md](docs/Current-Progress-and-Next-Steps.md).
