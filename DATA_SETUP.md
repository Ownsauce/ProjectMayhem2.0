# Local data setup

ProjectMayhem's source repository intentionally does **not** distribute data or
assets extracted from Anarchy Online. This includes item and nano databases,
profession/stat data, playfield exports, textures, icons, meshes, and other
game content.

You must obtain any required content yourself from a copy of Anarchy Online
that you are legally entitled to use. You are responsible for complying with
the game's license, copyright law, and any rules that apply in your region.
Do not upload extracted game content to this repository.

## What a source checkout contains

The repository contains ProjectMayhem source code, Unity project settings,
documentation, and conversion/validation tools. Unity regenerates its
`Library`, `Logs`, `Temp`, `obj`, solution, and project files locally.

Unity Package Manager restores declared dependencies from
`AO.Unity/Packages/manifest.json`. A local embedded copy of
`com.unity.inputsystem` is therefore not committed.

The following local-data paths are ignored by Git:

```text
AO.Unity/Assets/StreamingAssets/AOData/
AO.Unity/Assets/Resources/WorldMeshes/
AO.Unity/Assets/Resources/CharacterMeshes/
AO.Unity/Assets/Resources/ItemMeshes/
AO.Core/Data/
AO.Server/Data/
AO.Tools/Data/
```

## Expected layout

The primary runtime data root is:

```text
AO.Unity/Assets/StreamingAssets/AOData/
├── items.json
├── nanos.json
├── statmap.json
├── breed.json
├── breed_ability_data.json
├── breed_stats.json
├── profession.json
├── profession_skill_cost_factors.json
├── profession_vitals_tl.json
├── title_level_ip_progression.json
├── xp_needed_to_level.json
├── skill_caps_and_color.json
├── skill_trickle_down.json
├── weapon_slots.json
├── armor_slots.json
├── implant_slots.json
├── spell_formats.json
├── Playfields/
├── CharacterMeshes/
├── ItemMeshes/
├── BodyTextures/
├── DungeonTextures/
├── GeneralTextures/
├── GroundTextures/
├── Icons/
└── UI/
```

Not every feature requires every optional asset directory. The core item/stat
systems need the JSON files relevant to that feature. World loading needs
`Playfields`; visual features additionally need their corresponding mesh,
texture, icon, or UI directories.

Generated world meshes used as Unity Resources belong at:

```text
AO.Unity/Assets/Resources/WorldMeshes/
AO.Unity/Assets/Resources/CharacterMeshes/
AO.Unity/Assets/Resources/ItemMeshes/
```

Keep each Unity `.meta` file beside the local asset it describes when moving an
already-imported dataset. If the `.meta` files are absent, Unity will generate
new ones when it imports the assets.

## Obtaining and preparing data

1. Install Anarchy Online from an official source and retain your own local
   installation.
2. Use extraction or conversion software that you are legally permitted to use
   to export the data needed by the feature you are developing.
3. Convert the output to the filenames and directory structure shown above.
4. Place the prepared output under
   `AO.Unity/Assets/StreamingAssets/AOData` and any generated Unity resource
   meshes under `AO.Unity/Assets/Resources/WorldMeshes`.
5. Open `AO.Unity` in the Unity Editor and allow Unity to import the local
   assets and regenerate its cache.

## Linking shared source into Unity

The current Unity project consumes the repository's `AO.Client` and `AO.Core`
source through local links under `AO.Unity/Assets/External`. Absolute links are
machine-specific and are not committed. Recreate them after cloning.

On Linux, from the repository root:

```bash
mkdir -p AO.Unity/Assets/External
ln -s ../../../AO.Client AO.Unity/Assets/External/AO.Client
ln -s ../../../AO.Core AO.Unity/Assets/External/AO.Core
```

On Windows, open Command Prompt with permission to create symbolic links, move
to the repository root, and run:

```bat
mkdir AO.Unity\Assets\External 2>nul
mklink /D AO.Unity\Assets\External\AO.Client ..\..\..\AO.Client
mklink /D AO.Unity\Assets\External\AO.Core ..\..\..\AO.Core
```

Windows Developer Mode commonly permits non-administrator symlink creation.
Unity will create local `.meta` files for the links; those files are ignored.

Some repository utilities under `tools/` validate or build manifests from an
already-prepared dataset. They do not grant rights to, download, or supply the
original game content. Review each tool's parameters before running it; some
older scripts contain Windows-oriented example paths that should be overridden
for your checkout.

### AOStatelParser

AOStatelParser is a separate third-party project and is not included in this
repository. Users who need it can obtain it directly from its upstream project:

- [bitnykk/AOStatelParser](https://github.com/bitnykk/AOStatelParser)

Review and comply with that project's own documentation, dependencies, and
license terms. A local checkout may be placed at
`_external/AOStatelParser/`; that path is ignored by Git.

## Server and command-line tools

By default, `AO.Server` discovers its general AO data at:

```text
AO.Unity/Assets/StreamingAssets/AOData/
```

The server also looks for server-owned overrides in:

```text
AO.Server/Data/AOData/
AO.Server/Data/Playfields/
AO.Server/Data/ipdist.xml
```

When a server-owned AOData file exists, it takes precedence over the Unity
copy. Otherwise, the server falls back to the primary Unity AOData directory.
Server startup also supports explicit data-path arguments; consult
`AO.Server/Program.cs` for the currently accepted options.

`AO.Tools` currently expects these local inputs:

```text
AO.Core/Data/items.json
AO.Core/Data/ipdist.xml
```

They may be copied from your prepared local data or produced directly by your
own conversion process. Because all of these destinations are ignored, doing
so will not accidentally stage them for Git.

## Verifying before a commit

After initializing the repository, always inspect what Git would include:

```bash
git status --short --untracked-files=all
git add --dry-run .
```

None of the ignored data roots above should appear. To diagnose a particular
file, run:

```bash
git check-ignore -v path/to/file
```

Do not use `git add -f` on these paths. If a small test fixture is later added,
it should contain original or clearly redistributable synthetic data, not a
subset copied from the game.
