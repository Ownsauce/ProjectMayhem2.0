# Project Mayhem 2.0 — Current Progress and Next Steps

Last updated: 2026-09-02

## Current working path

- Unity presents server selection, login, character selection, and world entry.
- `AO.Client` provides neutral session/world models and a working AORebirth
  adapter for authentication, zone handoff, compressed world traffic, nearby
  entities, movement, health, inventory, equipment, and selected world objects.
- The Project Mayhem TCP/JSON backend remains available as a development and
  test backend.
- The user selects an AO installation in the connection screen or under
  **F10 > AO Assets**.
- Bundled `AODB.dll` and `AODB.Common.dll` assemblies read requested resources
  directly from `ResourceDatabase.dat` and `ResourceDatabase.idx`.
- Direct readers cover outdoor terrain, water, statels, ABIFF meshes, CAT
  character meshes and animation, item records, icons, and related textures.
- Equipped items resolve their templates and visual resources from server state
  plus the configured AO database.
- Indoor playfields have room-definition, surface extraction, and navigation
  support, with additional format coverage still in progress.
- Generated results use `Application.persistentDataPath/AOAssetCache` when a
  persistent derived representation is useful.

Detailed feature status is maintained in `PROJECT_MAYHEM_2.md`.

## Verified repository checks

- A clean export of commit `2a11e2c` builds `AO.Assets` and `AO.Client`.
- Installation/cache resolution, dimension discovery, and world-delta tests
  pass from that clean export.
- The repository contains the AODB runtime assemblies, Unity metadata, local
  packages, install configuration, and direct resource readers.

Unity import and play-mode behavior should still be checked after changes to
assembly definitions, package references, scenes, or plugin import settings.

## Known limitations

- Backend capabilities vary; AORebirth currently has the most complete adapter.
- Several packet families and gameplay systems still have partial decoding or
  backend-specific fallbacks.
- Direct AO resource coverage is incremental. Unsupported records must report
  their type/ID and fall back without breaking the whole playfield.
- Indoor material/texture reconstruction and unusual equipment calibration
  need broader validation.
- `AO.Server.csproj` still references a Unity-generated `AO.Core.csproj`, so
  the standalone server build needs a repository-managed Core project reference.
- A fresh-clone Unity smoke test on another machine remains the release-readiness
  check for hidden cache or local-data dependencies.

## Recommended next steps

1. Correct the standalone `AO.Server` project reference and add clean-checkout
   builds to automation.
2. Run Unity import/play-mode verification from a fresh checkout using Unity
   `6000.3.8f1`.
3. Exercise install selection against Windows and Wine layouts.
4. Expand direct decoder tests for terrain, statels, CAT meshes, animations,
   items, icons, and indoor surfaces.
5. Continue moving AODB traversal behind Project Mayhem model boundaries so
   presentation code consumes stable snapshots.
6. Add backend features through `IGameServerBackend` without exposing packet
   structures to Unity views.
