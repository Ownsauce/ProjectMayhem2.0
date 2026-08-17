# AO.Unity Update Guide

This document describes the intended code structure for `AO.Unity`, current ownership boundaries, and how new systems should be implemented so the codebase stays modular and easy to maintain.

## 1. Folder-Level Structure

`Assets/Scripts/AO.Unity/`

- `AOStyle/`
  - UI view components and reusable UI interaction behaviors.
  - Examples: window views, drag/resize handles, docking behavior, keybinding UI.
- `Prototype/`
  - Prototype orchestration layer that wires world, UI, and runtime systems together.
  - Contains `PrototypeClientUGUI` and related prototype-specific coordinators.
- `World/`
  - Character/world simulation, spawning, transitions, rendering/appearance behavior.
- `Quests/`
  - Quest authoring/runtime quest support.
- `Utils/`
  - Shared utility helpers.

## 2. Modularity Rules

Use these as implementation constraints:

1. One class should own one primary responsibility.
2. `AOStyle` classes should stay UI-focused and avoid world/gameplay authority decisions.
3. `World` classes should not depend on specific UI window implementations.
4. `Prototype` is allowed to orchestrate, but orchestration should call focused helpers/services.
5. Prefer adding focused files/services over extending very large controller files.

## 3. Preferences and Layout Persistence (Current Pattern)

### Source of truth

- One per-character preferences record inside one local preferences file:
  - `AO.Unity/Preferences/local_account/client_prefs.json`

### Window persistence schema

Per character layout now tracks:

- `WindowsById` (keyed by window id) for per-window state:
  - active
  - docked
  - parent scope
  - anchored position
  - size
  - dock sibling index
- `Dock.OrderedWindowIds` for dock order replay.

Legacy `Windows` list is still maintained for backward compatibility during migration.

### Save triggers (event-driven)

Window layout persistence is **event-driven**, not timer-driven:

- Drag/resize end commits (`WindowDragHandle`/`WindowResizeHandle`).
- Dock/undock/drop commits (`WindowDocking`).
- Full capture + flush on close/quit.

This avoids frequent polling saves and reduces unnecessary I/O.

## 4. Docking Rules

Dock replay behavior should be deterministic:

1. Clear stale dock state.
2. Apply undocked window state.
3. Apply docked windows in `Dock.OrderedWindowIds` order.
4. Normalize sibling order in dock content.
5. Rebuild layout/canvas to avoid overlap/stacking artifacts.

If changing docking behavior, preserve this order.

## 5. Authority Boundaries (Client vs Server)

### Client preference ownership

- Window positions/sizes/visibility.
- Dock order.
- Keybind preferences.
- UI-specific preference toggles.
- Chat tab UI preferences (where applicable).

### Server-authoritative gameplay state (target model)

- Character position and authoritative zone.
- Inventory contents and slot assignments.
- Backpack/equipment authoritative item state.
- Active nanos and durations.
- Uploaded nanos (typically server-backed).

Client can cache for display convenience, but gameplay truth should remain server-authoritative.

## 6. Implementation Checklist for New Features

When adding a new window/system:

1. Create a dedicated view/controller class in the appropriate folder.
2. Register a stable window id for persistence.
3. Ensure drag/resize/dock interactions emit commit events.
4. Persist only the affected window block unless a full flush is needed.
5. Add explicit comments for client-owned vs server-owned data.

## 7. Refactor Direction (Next Steps)

Recommended incremental steps:

1. Continue splitting `PrototypeClientUGUI` responsibilities into focused partials/services.
2. Extract shared persistence DTOs/services into dedicated files under `Prototype/Persistence/`.
3. Add small integration tests for:
   - window save/load replay
   - dock order replay
   - non-empty layout capture guardrails

## 8. Current Decomposition Status (2026-04-25)

`PrototypeClientUGUI` has been split into focused partials:

- `PrototypeClientUGUI.cs`
  - bootstrap/startup wiring
  - window creation and registration
  - general runtime orchestration not yet extracted
- `PrototypeClientUGUI.Preferences.cs`
  - window layout capture/apply
  - per-character UI layout schema
  - local prefs file load/save
  - dock order persistence/replay
- `PrototypeClientUGUI.CharacterFlow.cs`
  - character selection/create flow
  - preview actor/camera lifecycle
  - profile apply + playfield transition restore
- `PrototypeClientUGUI.Hotkeys.cs`
  - keybind dispatch (input system + legacy input path)
  - F10 unsaved-changes close flow
  - hotkey-driven window toggles + utility actions (screenshot/copy)

`PrototypeWorldBootstrap` has been split into focused partials:

- `PrototypeWorldBootstrap.TransitionSpawn.cs`
  - playfield transition entry
  - deferred transition spawn state
  - safe surface spawn resolution
- `PrototypeWorldBootstrap.PlayfieldLoading.cs`
  - playfield package/glb folder resolution
  - glb override startup flow
  - glb override coroutine completion/handoff
  - playfield loading overlay lifecycle (`ShowPlayfieldLoadingOverlay` / `HidePlayfieldLoadingOverlay`)
  - runtime animation safety cache/sanitized map path resolution and load
  - deferred spawn handoff when playfield/GLB load is still in progress

`CharacterAppearanceController` has started decomposition:

- `CharacterAppearanceController.WeaponVfx.cs`
  - ranged/melee hit VFX lifecycle
  - weapon-vfx mapping resolution and lookup loading
  - procedural projectile/impact effects and target impact point resolution

`CharacterAppearanceController` jump/action behavior currently includes:

- Explicit one-shot jump action routing for:
  - `Jumping from idle stance`
  - `Jump Forward`
  - `Land after jump`
  - `Land after jump and keep running`
- Input-driven jump gating so slope/hill micro-separations do not trigger jump flow by themselves.
- Preferred clip-name matching and fallback matching for jump/land clips.

`PrototypeUiContext` has started decomposition:

- `PrototypeUiContext.ActivePrograms.cs`
  - active nano program modifier aggregation
  - periodic nano tick processing
  - local health/nano pool synchronization for UI

Planned next split targets:

- `PrototypeClientUGUI.ChatStatus.cs` (chat/status + marker/castbar concerns)
- `PrototypeWorldBootstrap` link/portal and runtime-object fallback slices
- `CharacterAppearanceController` animation-selection and head-preview slices
- `PrototypeUiContext` inventory/equipment and upload/cast flow slices

## 9. Character Flow and Loading Phases (Current)

Current client flow is two-phase at runtime behavior level:

1. Startup phase:
   - Launch logo and shell/UI bootstrap.
   - Character selection/create flow is activated by `PrototypeClientUGUI.CharacterFlow.cs`.
   - No character-specific playfield transition is executed until user confirms Play/Create.
2. Enter-world phase:
   - On Play/Create, profile is applied and target playfield transition is requested.
   - `PrototypeWorldBootstrap` handles playfield load/transition and deferred spawn.
   - Playfield loading overlay remains active until world transition and spawn handoff complete.

## 10. GLB Animation Safety Pipeline (Current)

Runtime relies on a prebuilt safety pipeline for problematic GLBs:

- Offline scanner/editor tool:
  - `Assets/Editor/RuntimeGlbAnimationSafetyScanner.cs`
- Output metadata:
  - `Assets/StreamingAssets/AOData/runtime_glb_animation_safety_cache.json`
  - `Assets/StreamingAssets/AOData/runtime_glb_sanitized_map.json`
- Runtime usage:
  - `PrototypeWorldBootstrap` and `GlbDataUriLoadPathResolver` consume this metadata to avoid repeatedly rebuilding known-bad animation channels and to prefer sanitized copies where available.

---

If a future change conflicts with this guide, update this file in the same PR so structure and behavior stay explicitly documented.
