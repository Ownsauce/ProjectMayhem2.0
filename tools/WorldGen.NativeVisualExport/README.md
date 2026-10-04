# Native indoor visual export

The game now uses **on-demand local database reads**, rather than this export
package. See [runtime setup](../../DATA_SETUP.md#indoor-runtime-reader). This tool
remains optional for inspection and authoring; its GLBs/PNGs are local development
artifacts and are not required or bundled for dungeon entry.

Exports original AO indoor tile geometry and textures from a locally selected AO
installation. PF 127 is the validated reference. The normal procedural Subway GLB
kit and server collision package are separate paths.

From the ProjectMayhem repository:

```bash
tools/export-native-pf-visuals.sh "/home/cody/AOClientCopy/Anarchy Online" 127 exports/native/pf127
```

Append a room index to export just that room. A partial export is useful for authoring
and inspection; the Unity runtime requires a complete room set.

The script builds the 32-bit helper in a temporary directory, runs the managed
exporter, and validates the output. Requirements: .NET 10, Wine, Python 3, and
`i686-w64-mingw32-g++`. The managed exporter pins AODB 1.0.12 for image decoding.

Output:

- `pf127.visual.aovr`: original AO world coordinates, normals, UVs and material
  triangle groups. No colliders or generated substitute geometry.
- `pf127.visual.json`: source/resource/image hashes, room metadata, original pivots,
  dependencies and limitations. Written last as the package commit marker.
- `room-000.glb` through `room-045.glb`: self-contained textured room architecture.
  Coordinates are relative to the room's `OriginAo`; Z is mirrored for glTF. Original
  room rotation is baked. Use the manifest origin to restore the original placement.
- `textures/`: decoded original images; material flags distinguish wall texture
  type 1010009 from general texture type 1010004.

The native helper uses read-only AO resource materialization and original tile
selection/deformation routines in an isolated process. Private offsets and container
layouts are bound to the verified N3.dll SHA256
`8c019efd72d547879a06585b69147ab1546b9617a2fce090e5863791aec8b0bb`.
A different build is rejected before any private function is called. It starts no AO
client or graphics device. A portable managed decoder is shared by the exporter and
Unity. The runtime invokes the helper on demand (through Wine on Linux); the export
tool invokes it to produce optional inspection artifacts.

The earlier runtime package installation at
`<Unity persistent data>/AOAssetCache/IndoorVisuals/pf127` is superseded. The client
ignores that package and reads original encoded images and room geometry from the
selected installation. The original source database is never edited.

Validation without opening Unity:

```bash
python3 tools/WorldGen.NativeVisualExport/verify-package.py exports/native/pf127
dotnet run --project tests/WorldGen.NativeVisual.Tests.csproj \
  -p:UnityManagedPath=/home/cody/Unity/Hub/Editor/6000.6.0f1/Editor/Data/Managed/UnityEngine \
  -- exports/native/pf127/pf127.visual.aovr
```

Static ABIFF placements are now read separately at runtime from the local indoor
statel file; this exporter does not include them. Interactive doors/dynels, water,
original lightmaps and game population are separate work. This package establishes textured room architecture;
it does not claim complete original-client scene parity.
