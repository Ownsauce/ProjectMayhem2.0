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
