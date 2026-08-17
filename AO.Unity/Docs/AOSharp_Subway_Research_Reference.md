# AOSharp Subway Research Reference

This note captures the current state of the AOSharp-based Subway (`127`) extraction work, what was added to AOSharp, what is working, and what is still unresolved.

## Goal

Use AOSharp runtime extraction to recover dungeon structure and, if possible, material/texture binding data for indoor playfields that static parsing does not reconstruct cleanly.

Primary target so far:
- `127` - `Condemned Subway (dng)`

Related secondary targets:
- `655` runtime world objects
- difficult static zones like `4376`

## What Works Today

### 1. Full room-surface geometry export for Subway

AOSharp exports room surface meshes for the entire loaded indoor playfield, not just the currently visible room.

For Subway this gave:
- `46` rooms
- about `11,883` room surface meshes

That geometry is enough to reconstruct the Subway shell/layout in Unity.

### 2. Runtime world-object export

The plugin can export runtime dynel/world-object layers for playfields like `655`, including:
- terminals
- doors
- vending machines
- names
- positions
- rotations
- useful runtime ids

This was enough to add a second world-object layer on top of static playfield data in Unity.

### 3. Indoor room-surface import in Unity

ProjectMayhem now consumes AOSharp room-surface JSON for indoor playfields and builds:
- room meshes
- colliders
- temporary fallback coloring

This is why Subway now looks structurally correct even without real textures.

## AOSharp Changes Made

Repository:
- `<path-to-your-checkout>\aosharp`

Key plugin/project added:
- `AOPlayfieldExtractor`

### Plugin capabilities added

- `/dumpplayfield`
- auto-detect current playfield id/name
- dump playfield metadata
- dump room metadata
- dump room surface meshes
- dump water meshes
- dump live dynels
- dump filtered runtime world objects for service-object reconstruction

### Extra dynel/runtime export details added

The plugin was extended to export more runtime ids and hints such as:
- `Mesh`
- `StaticInstance`
- `TemplateId`
- `TemplateName`
- `YawDegrees`
- `ImportKey`
- lookup/name hints where safe

This was mainly used for `655`.

### Surface-material diagnostic fields added

AOSharp was extended with a separate surface-material diagnostic container instead of trying to overload mesh geometry itself.

Important fields now exported per room surface mesh include:
- `RawHeaderHex`
- `RawHeaderInts`
- `RawHeaderUIntHex`
- `TriangleIndexBytes`
- `VertexBytes`
- `TriangleBlockPreviewHex`
- `VertexBlockPreviewHex`
- `PostVertexPreviewHex`
- `HeaderPointerCandidateHex`
- `HeaderPointerCandidateReadable`
- `HeaderPointerCandidatePreviewHex`
- `HeaderField3Hex`
- `HeaderField4Hex`
- `HeaderField5Hex`
- `HeaderField6Hex`
- `HeaderField7Hex`
- `Header3PointerHex`
- `Header3PointerReadable`
- `Header3PointerPreviewHex`
- `Header7PointerHex`
- `Header7PointerReadable`
- `Header7PointerPreviewHex`

These are diagnostics only. There is still no real material/texture parse yet.

## Files Changed In AOSharp

Main AOSharp files touched during this work:
- `AOSharp.Common/GameData/Mesh.cs`
- `AOSharp.Common/GameData/SurfaceMaterialData.cs`
- `AOSharp.Common/Unmanaged/DbObjects/SurfaceResource.cs`
- `AOSharp.Common/Unmanaged/Imports/Native/Kernel32.cs`
- `AOPlayfieldExtractor/AOPlayfieldExtractorPlugin.cs`

Important behavior:
- stable room geometry export was preserved
- risky live UV/normal parsing was backed out after it caused crashes
- current exporter is on the safe diagnostic path again

## Unity-Side Changes That Depend On AOSharp Dumps

Main ProjectMayhem files involved:
- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWorldBootstrap.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWalkerController.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/CharacterAppearanceController.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/AOStyle/CharacterSettingsWindowView.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/Prototype/PrototypeUiContext.cs`

Key outcomes:
- indoor room-surface import
- runtime world-object import
- Subway fallback colors
- flight mode for testing
- exact XYZ teleport support

## What We Learned About Subway Materials So Far

### Confirmed

- Geometry is available through AOSharp room surfaces.
- Real texture files likely exist separately in exported dungeon texture assets.
- The missing piece is the binding:
  - which surface uses which texture/material
  - and possibly UV/material parameters

### Not working yet

- No usable UV parse
- No usable normal parse
- No direct texture id parse
- No direct material id parse

### Strong findings from diagnostics

- `RawHeaderInts[0]` appears to correlate with triangle count.
- `RawHeaderInts[1]` appears to correlate with vertex count.
- Data after the vertex block is not a simple UV array.
- `Header2` looked promising at first, but later evidence showed it mostly points back toward geometry-adjacent data, not texture binding.
- `Header3` and `Header7` are currently the best remaining suspects.

### Current read on headers

- `Header2`: mostly a dead end for texture binding
- `Header3`: mixed
  - some DOS/PE junk
  - some UTF-16/resource-like tables
  - some compact record tables worth following
- `Header7`: still promising
  - often struct-like or mixed-binary
  - more likely than `Header2` to contain per-surface descriptor data

### Best room focus from the latest analysis

The strongest readable cluster from the latest dedicated `Header3`/`Header7` analysis was:
- `Statue Ramp Connector`

That room currently looks more promising than:
- `Grand Dome`
- `Ticket Checkpoint`
- `Helix Descent`
- `Subway Station`

## Related Analysis Files

Existing notes and tooling:
- `AO.Unity/Docs/AOSharp_Subway_SurfaceMaterial_Analysis.md`
- `AO.Unity/Docs/AOSharp_Subway_Header3_Header7_Analysis.md`
- `AO.Unity/Tools/AnalyzeAoSharpSurfaceHeaders.py`

## Current Status

### Good enough today

- reconstruct Subway layout in Unity
- reconstruct indoor room shell and colliders
- reconstruct runtime service objects for some outdoor playfields like `655`

### Not solved yet

- real Subway materials/textures
- reliable floor completion for every Subway section without heuristics/manual work
- a trustworthy parse of AO room-surface material binding data

## Recommended Next Steps

1. Keep the live exporter stable.
2. Continue material work offline first, not by crashing the client with speculative parsers.
3. Focus on `Header7` and the useful subset of `Header3`.
4. Prioritize `Statue Ramp Connector` for the next AOSharp material-structure pass.
5. Treat Subway floor completion and Subway texturing as separate problems.
