# AOGLTF Reference Assessment

AOGLTF was evaluated as an early route for turning AO resources into GLB/glTF.
Project Mayhem now uses bundled AODB assemblies and direct, targeted database
reads as its primary runtime architecture.

## Useful reference behavior

AOGLTF can still help developers compare:

- ABIFF and CIR mesh interpretation
- Playfield terrain, statel, collision, and water output
- Axis conversion and mirroring
- Terrain texture-atlas generation
- GLB/glTF output from known resource IDs

## Why it is not the runtime pipeline

- Its normal workflow is an interactive exporter rather than an unattended
  in-process resource service.
- Exporting through intermediate GLB files adds startup work, storage, and
  another failure boundary.
- It targets a different runtime/platform configuration from the Unity client.
- Several export paths reduce errors to a Boolean result, limiting diagnostics.
- Direct AODB access allows Project Mayhem to request only the records needed
  for the current scene and retain control of caching and threading.

## Current decision

Project Mayhem bundles `AODB.dll` and `AODB.Common.dll` under
`AO.Unity/Assets/Plugins/AODB`. Direct database reading is the supported runtime
path. AOGLTF is optional reference tooling for debugging decoder behavior and
does not need to be installed by users or contributors.

Keep exporter-specific behavior outside presentation classes. Comparisons with
AOGLTF output should be captured as tests or decoder notes so the direct reader
remains independently maintainable.
