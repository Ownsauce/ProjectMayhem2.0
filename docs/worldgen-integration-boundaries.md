# WorldGen integration changes explained

October 4, 2026. Unity remains the gameplay target. No original-client extension
is part of this work.

User decisions: reusable local integration, configurable sources and the shared
result are approved. The user subsequently authorized implementation. Source
selection, publication/revision lookup and initial host result adoption are now
implemented locally. Remaining stages below are still backlog. A standalone example
and original-client extensions are not planned.

## Prioritized AO-room backlog

The subsequent code/documentation review adds publication, authoring usability,
layout goals and diagnostics to the existing integration backlog. The user requested
recording and prioritizing this work, then authorized building it. Status below
distinguishes implemented local work from remaining tasks. Priority IDs below are independent of
the original numbered proposals that follow.

| Priority | Work | Current state | Completion evidence |
| --- | --- | --- | --- |
| P1 | Selectable, registered room sources | Implemented for registered PF127/PF1931; compile/preparation checks pass; PF1931 live acceptance pending | PF127 and a prepared PF1931 source use the same selection, generation, preview and entry workflow |
| P2 | Catalog publication and retained revisions | Local staged publication and exact saved-revision lookup implemented; cleanup/team storage remain pending | One publish operation stages matching client/server data; saved recipes resolve their recorded revision |
| P3 | Room-library authoring and connection inspection | Added search, unreviewed filter and saved/unsaved status; seam tools/thumbnails remain | Search/filter/status tools, seam previews and room/socket failure explanations support authoring |
| P4 | Common-result adoption and local Unity/server adapters | Unity verification/server registration consume shared results; lifecycle extraction remains | Preview/gameplay reuse loading and cleanup; server integration consumes the same result for collision |
| P5 | Data-driven layout goals and optional dungeon-run profile | Current tree growth ignores authored role tags | Authored pacing, repetition and branch targets plus optional arena progression pass placement/traversal checks |
| P6 | Rich diagnostics and measured loading budgets | Walking validation, seed checks, selected-room loading and RAM cache exist | Reproducible failures and generation/loading metrics guide repairs and larger-dungeon limits |

Use this order for major stages. Adopt common result/source references incrementally
during P1/P2; P4 completes reusable integration. Collision, spawn support, doorway
safety and matching revisions are acceptance requirements throughout every stage.
P6 enhances diagnostics/performance measurement; safety checks continue from P1.

### P1: Selectable room sources

Register a stable source ID, playfield, catalog/revision, provider, entrance settings
and room pools. Carry the selection through preparation, editor browsing, verification,
runtime assets and server collision. Scope room identity to its source: room8 in
PF127 and room8 in PF1931 must remain distinct. Extend existing source-definition
and resource-reference contracts.

PF1931 now has a prepared and validated database-room catalog, separate from its
existing Temple reference layout. One source is selected per dungeon. Cross-source joins need later compatibility validation. The
benefit is adding another prepared source through data while reusing the workflow.

### P2: Publication and catalog revisions

Add Validate and publish catalog around existing preparation/validators. Show
source/authoring revisions, changes and validation results, then stage matching
metadata and collision for both hosts under one revision. Retain earlier revisions
referenced by saved recipes and resolve the recorded revision rather than only the
active catalog. Historical entry still requires matching local source resources.

The DAO remains the editable authority; generation consumes published snapshots.
Rendering meshes/textures come from the player's read-only installation. Publication
handles metadata and local server collision preparation. Benefits are fewer manual
copy mismatches and reproducible saved worlds. Define retention/cleanup by references.

### P3: Authoring usability

Extend the native window with searchable thumbnails, source/role/size/doorway filters
and review statuses. Explain unusable sockets through headroom, floor-support and
walking-region failures. Add two-room connection previews with threshold/elevation
and collision/clearance overlays, alongside existing source/full-layout previews.

Distinguish unsaved edits, saved settings and published gameplay revisions. Copy
reproduction details should include seed, generator/source revision, room names,
socket IDs and position, without rendering assets. This makes repairs accessible
through the existing editor.

### P4: Common result and integration boundaries

Adopt the implemented result for source references, transforms, openings and closures.
Extract local Unity operations for loading, visibility, door state, cancellation,
cleanup and unloading; keep player/session/UI responsibilities in the game. Editor
previews and gameplay should reuse construction. Expose local server-library operations
for generation/restoration, collision registration and preparing versioned client data.
AORebirth retains instance IDs, authentication, transfers, storage and gameplay state.

No hosted endpoint is required. Explicit loading stages make collision readiness
and supported spawn confirmation prerequisites for entry. Extract incrementally,
preserving accepted generation/hash/recipe behavior, before independent packaging.

### P5: Intentional layout variety

Author targets for route length, branch depth, repetitions, consecutive connectors,
room weights, landmarks and encounter spaces. Retain normal generation and add the
optional dungeon-run profile from [native authoring](native-dungeon-authoring.md#additional-dungeon-run-layout-profile--proposed-not-implemented).
Template capabilities and assigned placed-room roles remain distinct.

Plan required progression, fit physical rooms, then validate walking/connections.
Use bounded backtracking and actionable failure reports when placement needs it.
Version generation/selection changes to preserve existing recipes. Benefits are
intentional pacing and variety. XP, loot, NPC behavior and gates remain gameplay work.

### P6: Validation and performance evidence

Include failed room/socket pairs, attempts, repetitions, route metrics and revisions
in seed reports. Preserve bad seeds as focused regressions. Validate assembled seams
and spawn support for each new source, supplementing conservative walking samples
with appropriate client/server physics checks.

Measure database reads, decoding, Unity construction, collision, RAM/GPU usage and
visibility/light costs separately. Build on selected-room loading, caching and
frame-budgeted construction. Optimize shared resources or duplicate in-flight loads
when measurements justify it. Raising the room-count constant alone is insufficient.

### Other priorities and boundaries

- Fix reported spawn, collision, missing-resource and join regressions before content expansion.
- Ceiling-source lighting, closure appearance and remaining visual gaps remain presentation priorities.
- Keep room capability review/data-driven authoring active. Permanent team storage is a later DAO choice; this update selects no SQL schema or hosted authoring service.
- Keep AO decoding in AO.Assets, deterministic/source-independent generation in WorldGen, rendering in the Unity adapter and gameplay authority in AORebirth.
- Custom GLB preparation remains the other asset pipeline. Later its provider can reuse catalogs/contracts; cross-source joins need validation.
- Original-client extensions and a new standalone example remain outside the plan.

### First implementation milestone

Select PF127 or a prepared PF1931 source in the existing Unity tool, inspect/validate
rooms, publish matching catalog data, then generate and enter through Unity/AORebirth.
Implemented locally with validated PF1931 preparation and selectable runtime loading.
Install the local server package and verify PF1931 gameplay in Unity. Commands and
publication details are in [native authoring](native-dungeon-authoring.md#select-review-and-publish-sources--october-4-2026).

## 1. Complete Unity adapter — approved future work

An adapter turns a shared dungeon result into Unity objects. A small public API
would let the game load a dungeon, unload it, update its visibility and apply door
state without directly managing individual surfaces, materials and colliders.
These are proposed operations, not methods already shipped.

"Public API" means callable C# methods/classes in the local Unity library. No
web endpoint, separately hosted service, public website or public access is
required. The game and editor call this code in their own process.

Move existing presentation code behind that boundary rather than rewrite it.
Session login, player spawning and game UI remain the host game's responsibilities.
This makes another Unity project able to reuse the renderer and makes rendering
fixes apply to both gameplay and editor previews. The cost is careful extraction
of code currently coupled to the debug view, AO loaders and shared Unity helpers.
It does not make the same Unity renderer usable in a different engine.

## 2. Configurable source selection — AO path implemented

Select a content source through provider/catalog/revision settings instead of a
fixed `pf127.rooms.json` path or a condition that requires PF127. An AO provider
could reference subway source127 or another prepared source. A custom-content
provider could reference imported GLBs. The generation algorithm consumes room
metadata and never opens either database or model file itself.

The former PF127 workflow used fixed client/server paths and source checks.
Registered source/revision loading now replaces those assumptions. The change
is to reuse that workflow for another prepared source by selecting its catalog and
provider in settings, without duplicating the PF127 loader or adding source-specific
conditions throughout runtime code. PF1931 is the intended example: prepare and
validate its room catalog/collision, register it as a source, then select it for
generation and rendering. Its existing Temple reference-layout path is separate;
this work makes the database-backed room workflow reusable across sources.

Editable source selection and fingerprints allow more dungeon families without
copying loaders, and allow asset eligibility to remain authored data. A registered
provider must validate resource availability and its revision. Different sources
still require preparation, compatible doorways and matching collision; selecting
a source does not establish that arbitrary mixed rooms can connect safely.

Existing asset metadata and room authoring DAO are the starting points. The current
DAO uses a project file; SQL/API storage is not implemented by this change. Choose
one authoring authority and publish a matching catalog to client and server.

## 3. Common generation result — implemented

Shared WorldGen now exposes `DungeonGenerationResult` and
`DungeonGenerationResults.GenerateModular/GenerateRooms`, plus adapters for existing
layouts and recipes. Both paths describe logical rooms/connections/spawns, source
references, placement transforms and connected openings through the same contract.
Source-room static closures are also represented. Existing `DungeonLayout`, hash
formats and algorithms are reused.

This simplifies common inspection, future renderer/server integrations and later
progression planning. It does not force the two room algorithms or collision sources
to become identical. The source-room transform now uses the common transform,
preserving its previous float arithmetic exactly. Unity manifest verification and server native registration now consume the shared
result for layout/hash/source identity. Room rendering/collision construction still
uses the established recipe paths; lifecycle extraction remains.

See sibling WorldGen `docs/DUNGEON_RESULTS.md` for the exact public API and limits.

## 4. Small server integration API — approved future work

This means local C# library methods inside the server process. It requires no HTTP
endpoint, separately hosted API or public service. Another compatible server would
reference the library and connect it to its existing physics, transport and lifecycle.
The host would ask WorldGen to generate/restore a result, obtain the appropriate
collision representation, and prepare versioned generation data for its client.
The host keeps ownership of instance IDs, authentication, player transfers, gameplay
state and storage connections. Existing `ProceduralInstanceService` provides the
AORebirth-specific lifecycle to build around.

A reusable boundary reduces the AORebirth code another server must copy and gives
generation one place for validation. It still needs a host implementation for that
server's physics and transport. A library wrapper should not become a second server
framework or duplicate the existing lifecycle. The common result is a foundation;
this wider integration API is proposed.

## 5. Standalone example — not planned

The user does not want this work at this point. Do not schedule or implement a new
standalone client/server example. Existing examples remain available.

## Current verification

Shared catalog/result checks: PASS. PF1931 preparation: PASS, including 100 seeds
per pool, 4/12/24 rooms, supported spawn and matching doorway thresholds. Unity
runtime/editor compile: PASS. AORebirth local publish/startup validation: PASS.
Publication/revision checks cover source-scoped references, matching packages,
historical lookup, rollback and corrupt/mismatched resource rejection. The original
PF127 catalog is retained. Corrected catalog4 collision snapshots now clip source
overhangs to room footprints; shared doorway/tangent checks pass. No client launch, service deployment or cloud/Git
publication; PF1931 live rendering/traversal acceptance remains pending.
