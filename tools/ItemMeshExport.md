# Item Mesh Batch Export

> Legacy development workflow: the runtime now resolves item meshes directly
> through AODB. Use this batch exporter only for decoder comparison, diagnostics,
> or generating an explicit development override.

This script builds a TinkerParser-compatible mesh manifest for item visuals from `items.json` plus ABIFF/CIR id-name dumps.

## What It Solves

`aogltf` is useful for browsing and one-off exports, but it is too manual for exporting every item mesh one at a time.

This script automates the discovery step by:
- scanning `items.json`
- collecting item visual stat ids
- resolving raw mesh ids against `AbiffNames.json` and `CirNames.json`
- writing `item_mesh_manifest.json` in the same shape as the playfield mesh manifests already used by `TinkerParser --abiff-to-glb`

## Default Visual Stats

- `12` = Mesh
- `38` = BackMesh
- `39` = ShoulderMesh
- `64` = HeadMesh
- `209` = WeaponMesh

## Run

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Build-ItemMeshManifest.ps1 -IncludeUnresolvedReport
```

Outputs:
- `AO.Unity/Assets/StreamingAssets/AOData/item_mesh_manifest.json`
- `AO.Unity/Assets/StreamingAssets/AOData/item_mesh_references.json`
- `AO.Unity/Assets/StreamingAssets/AOData/item_mesh_manifest.unresolved.json` when `-IncludeUnresolvedReport` is used

## Batch Export With TinkerParser

Once the manifest exists, use the same batch conversion pattern you already used for world meshes:

```powershell
$ao  = "C:\Other\PrivateAO"
$out = "C:\Other\codeprojects\ProjectMayhem\AO.Unity\Assets\StreamingAssets\AOData"

.\TinkerParser.exe --aopath="$ao" --prk --abiff-to-glb --mesh-manifest-path="$out\item_mesh_manifest.json" --glb-out="C:\Other\codeprojects\ProjectMayhem\AO.Unity\Assets\Resources\ItemMeshes" --output-dir="$out"
```

## Notes

- The manifest prefers ABIFF names first, because most item/world mesh export currently flows through `--abiff-to-glb`.
- If a mesh id only exists in `CirNames.json`, the manifest still records it with a `.cir` extension so you can identify those separately.
- This solves bulk export discovery. It does not by itself finish the runtime equip-appearance system.
