# Body Texture Reset Investigation (Paused)

## Date
- 2026-04-05

## Current Symptoms
- Body armor textures (example: Carbonum) can remain visible after unequip.
- Texture state often resets only after a character mesh swap (example: equip robe then remove robe).
- Unity shows persistent allocation leak warnings (`Leak Detected : Persistent allocates ...`), but stack traces were not informative.

## Confirmed Behaviors
- Equipped item renderers (for example tank armor) are excluded from body-texture repaint logic.
- Weapon hand visuals can be inverted by backend slot mapping (handled in `EquippedItemVisualController`).

## Recent Changes Attempted
- Per-location texture diffing and restore.
- Full body reset + reapply pass on equipment signature change.
- Snapshot capture from `sharedMaterials` to preserve original/base textures.
- Renderer material caching to reduce repeated runtime material instantiation.

## Suspected Remaining Root Cause
- Base snapshot timing and/or body-location-to-material mapping may still be wrong for some imported character mesh variants.
- A subset of body materials may not be mapped to expected AO texture locations, causing restore to miss them.

## Recommended Next Debug Pass
1. Add focused debug logs for each location (0..4) with:
   - mapped materials count
   - texture id resolved
   - restore/apply action taken
2. Dump material names for affected meshes to verify location mapping assumptions.
3. Validate snapshot contents immediately after mesh load and before first armor apply.
4. Re-test with controlled sequence:
   - baseline spawn
   - equip single piece
   - unequip same piece
   - inspect only that location.
