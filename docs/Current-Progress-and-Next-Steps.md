# Project Mayhem 2.0 — Current Progress and Next Steps

Last updated: 2026-09-10

## September 10 appearance continuation

- Replaced the heuristic `SimpleCharFullUpdate` appearance-tail search with the
  exact AOSharp serializer order, including byte-sized player/organization names,
  extended textures, active nanos, waypoints, owner data, and special attacks.
- Selected-character appearance now feeds the same authoritative texture/mesh
  renderer as nearby players. A failed selected-character decode emits the full
  appearance-only SCFU hex needed for a regression fixture.
- Body slots match CAT renderer, mesh, or material names and support both URP and
  HDRP base-texture properties. Appearance logs now identify missing naked skins,
  attachment sockets, mesh resources, and the final applied counts.
- `AO.Client` tests and a Unity `6000.6.0f1` batch import/compile pass. Live visual
  validation remains: log into Rubi-Ka, inspect the `[AO Appearance]` lines, and
  exercise equip/unequip, dual wield, shoulders/back, and zoning.

## September 9 continuation

- Server texture/mesh snapshots now feed local and nearby character appearance,
  including skin/armor compositing, attachment sockets, and visibility flags.
  Newly received outfits reset failed-load retry delays; movement alone does not.
- World-delta fixtures pass, including appearance truncation/empty-outfit cases
  and distinguishing canceled socket reads from genuine I/O failures.
- Unity `6000.6.0f1` isolated batch import/compilation succeeds with warnings
  (`/tmp/ao-appearance-resume.log`).
- The recorded Rubi-Ka attempt authenticated, selected the character, connected
  to the zone, and received compression negotiation, then failed while reading
  the zlib header. Its original I/O exception was discarded by session error
  handling. Errors now preserve that cause, and timeout-driven socket closure
  reports cancellation. The live login failure is not yet resolved; retry in
  Unity and inspect the project-relative `AO.Unity/Logs/Editor.log`.
- In-world visual validation (equip/unequip, breed variants, zoning, and nearby
  changes) remains pending successful world entry.
- Follow-up: the live zone-login packet used header sender `0`. Lost Eden's
  `NetworkSession.Send` uses its selected character ID for this header. Our
  zone login now does the same while retaining the body identity and cookies;
  a synthetic byte-level regression fixture passes. This corrects a concrete
  packet mismatch but still needs a live retry to establish world-entry success.
  EOF errors now say "remote AO server" instead of incorrectly naming AORebirth.

## Current working path

- Unity presents server selection, login, character selection, and world entry.
- `AO.Client` provides neutral session/world models and a working AORebirth
  adapter for authentication, zone handoff, compressed world traffic, nearby
  entities, movement, health, inventory, equipment, and selected world objects.
- AORebirth Local is the primary development server. Live, Ithaca, and AORebirth
  are the intended targets; maintaining AO.Server is outside the current scope.
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

- Native inventory changes pass world-delta and dimension-discovery fixtures and
  Unity `6000.3.8f1` batch import/compilation (2026-09-06).

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
- Native main-inventory moves, equip/unequip, and equipment swaps now send requests
  and apply ContainerAddItem confirmations. Protocol fixtures and Unity batch
  compilation pass; AORebirth Local play-mode/reconnect validation is still required.
- Native backpack transfers, stacking, deletion, item use, and nano uploading
  remain pending. Unsupported item interactions must not mutate local inventory.
- A fresh-clone Unity smoke test on another machine remains the release-readiness
  check for hidden cache or local-data dependencies.

## Recommended next steps

1. Verify native equipment/inventory actions on AORebirth Local, including
   rejected requests, occupied-slot swaps, and reconnect persistence. Then connect
   backpack contents/transfers and the remaining item actions.
2. Run Unity import/play-mode verification from a fresh checkout using Unity
   `6000.3.8f1`.
3. Exercise install selection against Windows and Wine layouts.
4. Expand direct decoder tests for terrain, statels, CAT meshes, animations,
   items, icons, and indoor surfaces.
5. Continue moving AODB traversal behind Project Mayhem model boundaries so
   presentation code consumes stable snapshots.
6. Add backend features through `IGameServerBackend` without exposing packet
   structures to Unity views.
