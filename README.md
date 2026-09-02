# Project Mayhem 2.0

Project Mayhem 2.0 is an in-development Unity client for Anarchy Online. It is
designed to work with different servers through separate backend adapters while
keeping Unity presentation and local AO asset loading independent of a specific
server implementation.

## Requirements

- Unity `6000.3.8f1`
- A local Anarchy Online installation containing a valid executable,
  `version.id`, `ResourceDatabase.dat`, and `ResourceDatabase.idx`

The required AODB runtime assemblies and local Unity packages are included in
the repository. A separate AOGLTF installation or pre-exported visual asset pack
is not required for normal use.

## Getting started

1. Clone the repository, preserving its directory structure.
2. Open the `AO.Unity` directory as a Unity project with Unity `6000.3.8f1`.
3. Allow Unity Package Manager to restore the packages from
   `AO.Unity/Packages/manifest.json`.
4. Open `Assets/Scenes/TestScene.unity` and enter Play mode.
5. Select your local Anarchy Online installation from the connection screen or
   under **F10 > AO Assets**.

For installation-path details and troubleshooting, see [DATA_SETUP.md](DATA_SETUP.md).

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
