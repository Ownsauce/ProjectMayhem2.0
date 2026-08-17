# AO.Unity Reference

## Purpose

`AO.Unity` is the Unity client and prototype runtime. It owns presentation, scene bootstrap, input, camera control, and UI.

## Owns

- World spawning and scene composition.
- Camera behavior and local movement controls.
- HUD, inventory UI, wear windows, stats windows, and drag/drop UX.
- Character appearance and animation components.
- Reading assets from Unity `StreamingAssets`.

## Key Areas

- `AO.Unity/Assets/Scripts/AO.Unity/World`
- `AO.Unity/Assets/Scripts/AO.Unity/Prototype`
- `AO.Unity/Assets/Scripts/AO.Unity/AOStyle`
- `AO.Unity/Assets/Scripts/AO.Data.Unity`

## Important Classes

- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWorldBootstrap.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWorldBootstrap.PlayfieldLoading.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWorldBootstrap.TransitionSpawn.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/PrototypeWalkerController.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/CharacterAppearanceController.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/World/CharacterRuntimeBridge.cs`
- `AO.Unity/Assets/Scripts/AO.Unity/Prototype/PrototypeClientUGUI.CharacterFlow.cs`
- `AO.Unity/Assets/Scripts/AO.Data.Unity/AODataManager.cs`
- `AO.Unity/Assets/Editor/RuntimeGlbAnimationSafetyScanner.cs`

## Current Runtime Notes

- Character select/create flow is handled in `PrototypeClientUGUI.CharacterFlow.cs`.
- Entering world from character flow transitions through `PrototypeWorldBootstrap.TransitionToPlayfield(...)`.
- Playfield-specific loading UI is handled by `ShowPlayfieldLoadingOverlay()` and `HidePlayfieldLoadingOverlay()`.
- Runtime GLB safety metadata is read from:
  - `Assets/StreamingAssets/AOData/runtime_glb_animation_safety_cache.json`
  - `Assets/StreamingAssets/AOData/runtime_glb_sanitized_map.json`
- Offline generation of those files is done by:
  - `Assets/Editor/RuntimeGlbAnimationSafetyScanner.cs`

## Current Architectural Caveat

`AODataManager` currently loads and wires gameplay data for Unity, but the long-term authoritative owner of that bootstrap should be `AO.Server`.

## AI Guidance

- Keep Unity-side code focused on presentation and player interaction.
- Do not make the client authoritative for equipment, movement outcome, or progression.
- If a feature needs validation, route the decision to `AO.Server` and keep only display/prediction here.

## Loading and Zone Lifecycle Handoff

Unity should keep a two-stage client flow:

1. Boot/account/character flow readiness.
2. Enter-world load only after character Play/Create confirmation.

Unity owns presentation for those stages (logo, character flow UI, world loading overlays), but authoritative zone lifecycle policy should live in `AO.Server`.

For long-term behavior and ownership details, see:

- `docs/AO.Server.md` (zone active/warm/sleep lifecycle and interest/streaming ownership)
- `docs/ProjectMayhem.Architecture.md` (cross-project loading lifecycle and authority boundaries)
