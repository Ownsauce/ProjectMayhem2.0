> October 4 doorway repair: the user reports that the blockage appears fixed in gameplay. Clipped reused-room collision removes the foreign ramp at PF1931 Hallway14 → Connector_shallow15. Client input now keeps wall tangents; server blocked reports use its existing sweep/slide. New catalog4 snapshots are published for both sources. Corner sliding and broader traversal remain separate acceptance checks. See [doorway details](native-dungeon-authoring.md#doorway-blockage-and-corner-sliding--october-4-2026).

> October 4 implementation: registered AO sources `ao-subway` (PF127) and `ao-temple` (PF1931), local validated publication and retained catalog lookup are built. Unity/server compilation and PF1931 preparation pass; install the local server package and perform Unity gameplay acceptance. Full adapters and subsequent priorities remain backlog.

> October 4, 2026 current direction: focus on Unity; the user declined an original-client extension and further original-client dungeon work. Approved future work: a reusable Unity adapter with local callable methods, configurable room sources (including preparing PF1931 for the existing-room workflow), and a local server integration library. Neither integration API requires a hosted endpoint. The common generation-result contract is implemented and checked; gameplay adoption remains future work. A new standalone example is not planned. See [accepted integration backlog and boundaries](worldgen-integration-boundaries.md).

> October 4, 2026 earlier native experiment: the user entered the native style324 baseline and saw mission-style rooms, as configured. This validates displayed mission geometry, not procedural PF127 subway assembly. Fresh runtime IDs and new static map/resource IDs were separate proposed investigations; earlier high-ID entry failures do not establish a client numeric limit. High-ID native instance routing and a client-side virtual subway room-definition overlay remain unimplemented and are outside the current direction above. See [historical fresh-playfield investigation](../tools/WorldGen.NativeAcgCatalog/README.md#fresh-original-client-playfield-investigation).

> October 4, 2026: A separate original-client ACG baseline is now implemented using the existing native generator payload and collision builder. Configured style324 is prepared locally from the installed database; seed90602/target12 builds20 legal rooms with sampled entrance support/clearance and a round-tripped room-list payload. Native entry uses reserved PF351 with that payload and generated collision, without static-slot geometry or Unity envelopes. PF351's template preparation succeeded but the tested seed/settings did not generate a valid closed graph, so the baseline uses style324. Actual-client acceptance and installation are pending; PF127 rearrangement remains an investigation. See [native ACG baseline and commands](../tools/WorldGen.NativeAcgCatalog/README.md).

> October 4, 2026: The user installed the recognized-ID follow-up, but original-client PF 351 entry failed gameplay acceptance: an unexpected room appeared and movement/turning were blocked. Logs confirm source-127 collision baked on PF 351 and repeated movement rejection; the actual client-rendered geometry is unconfirmed. PF 351 has 81 room templates and is not blank. No verified empty host was found in the local database review. Destination-ID selection alone has not delivered our layout; the next native step needs a supported layout payload and matching collision. PF 800 orphan-login recovery succeeded. See [native experiment status and findings](../tools/WorldGen.NativeClientProbe/README.md).

> October 4, 2026: After the handoff fix was installed, logs confirmed character 37 was loading into orphaned PF 900000 with no procedural geometry after restart. The original-client source-copy path now allocates a configured recognized test slot, presently PF 351, while keeping PF 127 as the asset source. PF 427 is absent from the selected database. Missing generated instances recover on login through the existing safe spawn in PF 800, as authorized by the user. Unity high-ID generation is retained. The user subsequently installed and tested this follow-up; its failed native-entry outcome is recorded above. See [original-client workflow and slot configuration](../tools/WorldGen.NativeClientProbe/README.md).

> October 4, 2026: Original-client login investigation found successful authentication/redirection followed by a rejected zone envelope (size 32, matching character sender, receiver 2). An earlier local Unity integration incorrectly required receiver 1. The server check is restored to its committed receiver-2 contract and the Unity zone-login emitter now uses receiver 2 as well; cookie/account authentication remains intact. The user installed that fix; follow-up logs confirm handoff admission and character hydration now succeed.

> October 4, 2026: Native authoring now saves multiple room capabilities, independent landmark/review flags, themes, notes and supported encounter centers through an authoring DAO with revision/conflict protection. The current provider is the existing project file; SQL/API storage is proposed and no database schema is changed. Annotation/DAO checks confirm normal layouts are unchanged. Original-client preflight finds PF 127's entrance incompatible with the current five-tile ACG adapter (only 1/46 source rooms fits that size rule). A separate unchanged-source copy test command is built; native randomized layouts and original-client acceptance remain pending. See [data/storage design](dungeon-authoring-data.md) and [original-client test workflow](../tools/WorldGen.NativeClientProbe/README.md).

> October 4, 2026: The user accepted native source-room inspection and previews. Native presentation is grouped under `World/NativeDungeons`, and copy/room resource verification is extracted from the network session. Runtime/editor compilation and existing native visual/collision checks pass. Normal generation and recipes are unchanged. An additional boss-route layout profile is proposed, not implemented, with the existing normal mode retained. See [module boundaries and optional dungeon-run design](native-dungeon-authoring.md).

> October 3, 2026: The user accepted raised doorway alignment. Native generator 1.2.0 / catalog 3 now adds walking-region validation, explicit source references and shared transforms, selected-room loading with a bounded RAM cache, native authoring previews/exclusions/pinning, saved resolved recipes, and ceiling fixtures/closure styles with scoped indoor lighting. See [native implementation and live checks](native-dungeon-authoring.md).

> October 3, 2026: The user accepted the corrected entrance spawn, then found a blocked height mismatch in the 24-room layout near U-turn West 11 / Mini to ramp 3x8 connector 1 16. The raised U-turn exit was catalogued about 3.9 m too low. Catalog version 2 measures passage floors, support, clearance and obstructions; generator 1.1.0 aligns rooms to those heights. Captured floor checks pass across all generated joins and the reported connection in all four rotations. Unity compilation and local server publication/startup validation pass. Installation stopped before changes because sudo requires a terminal password; run the local installer and regenerate the instance. See [doorway height details](review/pf127-asset-reuse.md#raised-doorway-alignment--2026-10-03).

> October 3, 2026: The user accepted the generated PF 127 dungeon's appearance but spawned under the entrance landing. Catalog preparation now validates the upper interior floor and capsule clearance, producing spawn (91,115.685,326); the client support ray is scoped to the active playfield. Native collision checks cover the landing and all four rotated stair copies. The user subsequently accepted this spawn correction. See [spawn details](review/pf127-asset-reuse.md#generated-entrance-spawn-floor--2026-10-03).

> October 3, 2026: The user accepted PF 127 floor/stair traversal. A new `native-room-dungeon` generator now composes seeded layouts from 46 source room templates, with shared doorway placement, transformed native collision and sealed unused exits. `.worldgen pf127-rooms 90602 12 pf127-random mixed` / `.worldenter pf127-random` use the existing instance flow. Core/client/server builds and seed checks pass; local server installation awaits terminal sudo authentication, and new layouts await live traversal. See [native room details](review/pf127-asset-reuse.md#seeded-native-room-dungeons--2026-10-03).

> October 3, 2026: A follow-up PF 127 stair fall at AO (97,107,268) exposed a mixed native mesh whose ramp was discarded with its wall faces. Direct native collision now classifies each triangle, retaining the stair ramp at Y 111.1575. Regression checks cover the exact spot, 96 points across the ramp, both landings and all source face counts; the Unity build passes. Live traversal acceptance is pending. See [mixed stair collision](review/pf127-asset-reuse.md#mixed-native-stair-collision--2026-10-03).

> October 3, 2026: The user accepted PF 127 texture coverage, then reported a short entrance drop at AO (91,107,326). The client now adds floor/stair collision from the same tile triangles used for rendering. Seven probes around the reported spot match Y 107.60483, and offline builds pass. Live floor-support review is pending. See [entrance support notes](review/pf127-asset-reuse.md#entrance-floor-support--2026-10-03).

> October 3, 2026: The PF 127 screenshots exposed white collision cages left visible over textured rooms. Runtime now hides diagnostic surface renderers after architecture and static models finish loading, preserving their colliders. A new indoor statel reader loads original models from the selected local installation: 2,244 visible placements, 245 mesh resources and 476 referenced images validated with Unity's ABIFF pipeline. Offline builds pass; the same bathroom/dome/passage locations still need in-game review. Interactive objects and original lighting remain follow-ups. See [white geometry fix](review/pf127-asset-reuse.md#white-collision-cages-and-local-static-models--2026-10-03).

> October 3, 2026: PF 127 now reads textured architecture on demand from the launch-selected AO installation. The runtime no longer needs exported GLBs/PNGs; it prepares geometry in the background and reads original images into memory. The user confirmed the initial textured layout looks good but reported missing textures near the entrance and in several rooms. The omitted room height baseline and collision skins hiding textures are corrected; live DB tests cover all 46 room heights and 104 images. In-game review, remaining presentation coverage, placed objects and original lighting need follow-up. See [runtime details](review/pf127-asset-reuse.md#on-demand-local-database-presentation--2026-10-03).

> October 3, 2026: A separate seeded native PF 127 copy is implemented and compiled. Install the local build, then use `.worldgen pf127 90602 pf127-copy` and `.worldenter pf127-copy`. The user confirmed the copied layout; the original room arrangement is preserved. See [PF 127 copy details](review/pf127-asset-reuse.md#seeded-native-copy-implementation-2026-10-03).

> October 3, 2026: User confirmed Subway dungeon entry/traversal and the TrackTunnel 1 / StationConcourse 5 stair fix. Ceiling-source lighting remains deferred. Entry fixes, file locations, restart behavior and next steps are in [Dungeon authoring](dungeon-authoring.md). A separate local AO-resource path is being studied in [PF 127 asset reuse](review/pf127-asset-reuse.md).

> October 2, 2026: WorldGen 5.13 source fixes shared bathroom placement. A first Subway authoring window, reusable catalog v2, typed surface renderer, and emissive fixture lighting are implemented. Local installation and graphical acceptance are tracked in [Dungeon authoring](dungeon-authoring.md).

> October 2, 2026: The current Subway GLB kit is integrated into procedural presentation. See [Subway GLB kit](subway-glb-kit.md) for preview, refresh, placement, and remaining review work. Extra stair pieces, joins, track transitions, and props are deferred.

> September 30, 2026: The image-to-code pipelines described below are retired. Current content uses optimized GLBs under `WorldGen/Assets` and `WorldGen/GLBCompression`. Procedural Core/Geometry and the existing Unity Temple kit remain active. Older pipeline sections are historical.

# Procedural Generation: Current State and Roadmap

**Last updated:** October 4, 2026
**Status:** Unity dungeon development is the active path; common generation-result
contract implemented; registered PF127/PF1931 sources and local publication/revision
lookup implemented, with PF1931 live acceptance pending. Full reusable Unity/server
adapters remain future work. Original-client extensions and a new standalone example are
outside the current plan. Older pipeline sections below are historical.

The Subway layout has reached a useful procedural checkpoint. Temple now has a fixed,
traversable 30-room PF 1931 reference baseline plus its first randomized topology and
presentation pass. Its room-review workflow, current limitations, and next steps are recorded in
[`temple-procedural-design.md`](temple-procedural-design.md). The procedural module catalog,
Core-owned presentation recipes, shared constructive geometry, collision,
server instance entry, and Unity rendering are active. Fixture refinement, full presentation
migration, traversal QA, gameplay content, and persistence remain incomplete.

This document tracks what exists today and what must still be built. The architectural
rules and long-term design live in [procedural.md](procedural.md).

## Prioritized next work: AO database rooms

The current priority is reusing and rearranging rooms from the installed AO database
in Unity. These stages consolidate the review recommendations and previously approved
integration work. The common result contract, source selection and local publication/revision lookup
are implemented; initial Unity/server result adoption is also complete. PF1931 live
acceptance and the remaining stages are future work. See [detailed priorities, status and acceptance criteria](worldgen-integration-boundaries.md#prioritized-ao-room-backlog).

| Order | Priority | Result |
| --- | --- | --- |
| 1 | Selectable room sources | Implemented for PF127/PF1931; PF1931 gameplay acceptance pending |
| 2 | Publication and retained catalog revisions | Implemented local staging/revision lookup; reference-aware cleanup/team storage pending |
| 3 | Room-library authoring | Search/unreviewed/status implemented; two-room joins/thumbnails/failure details next |
| 4 | Adopt the common result and local adapters | Reuse preview/gameplay loading and server collision integration through local library calls |
| 5 | Data-driven layout profiles | Retain normal generation; add pacing/repetition controls and optional encounter/arena progression |
| 6 | Diagnostics and performance measurement | Reproduce failures and measure costs before increasing dungeon-size limits |

Spawn/collision/doorway safety and matching revisions apply throughout. Reported
regressions take precedence over feature expansion. Lighting and visual polish remain
presentation work; permanent team storage remains a later DAO decision. Future custom
GLB catalogs can use their own provider with these contracts. Original-client extensions
and a new standalone example remain outside the plan.

First milestone is implemented locally: source selection, room review/validation,
publication and generic generation/entry. PF1931 preparation passed; install and
verify it in Unity. Its existing custom Temple/reference layout remains separate.
Next major work is room-library connection inspection, then complete adapter
extraction, layout goals and richer diagnostics. See [commands and verification](native-dungeon-authoring.md).

### Recommended next implementation slice: inspect room joins

Build P3 around the recent doorway repair: select two source rooms and their
sockets, align them with the same transforms used by generation, and preview the
join with collision, floor heights and character clearance visible. Report floor
support, headroom and collision failures across the approach and threshold. Add
room capability/size/doorway filters alongside the existing search; thumbnails can
follow the functional inspector.

Allow socket exclusions and room review changes through the existing authoring
DAO, then use the existing validated publication workflow to activate them. Copy
reproduction details with source/catalog revision, seed, room/socket IDs and
position. Preserve the repaired Hallway14/Connector_shallow15 join as a focused
regression. This slice is proposed; the two-room inspector is not implemented yet.

## Current End-to-End Flow

The following path has been tested successfully:

1. A GM runs `.worldgen <seed> <room-count> <instance-id>`.
2. ZoneEngine_New creates a deterministic dungeon manifest and assigns a runtime
   playfield ID beginning at `900000`.
3. Server and client independently generate the logical layout from the same manifest.
4. The client compares its layout hash with the server hash and refuses to render a
   mismatch.
5. `.worldenter <instance-id>` transfers the player through the normal AO teleport and
   zone-redirection flow.
6. The Unity client reconnects to ZoneEngine_New, receives the new playfield bootstrap,
   clears the old playfield, and renders the verified dungeon.
7. Procedural playfields use the regular ProjectMayhem/ZoneEngine movement path.
   ProjectMayhem predicts movement locally and sends normal position/heading updates;
   ZoneEngine validates them against Bepu geometry and corrects rejected movement.
   There is no procedural-only locomotion or 20 Hz self-snapshot loop.
8. Full door state is synchronized on entry/reconnect. Revisioned changes animate
   client door leaves and update local collision while the server remains authoritative.
9. `.worldexit` returns the player to the recorded source playfield and position.

## Image-to-3D / Native 3D Authoring Path

The offline authoring workspace lives outside the game repository at
`/home/cody/Coding/WorldGen/WorldGen.Authoring`. The cloned img2threejs source is retained beneath
`/home/cody/Coding/WorldGen/upstream/img2threejs` as a read-only behavioral reference. Generated
assets ultimately enter ProjectMayhem through the engine-neutral `WorldGen.Geometry` buffers and
the Unity editor preview; no AI provider or JavaScript runtime is required in a shipped game.

### Option A: upstream workflow with Unity output

The optional path lives in `/home/cody/Coding/WorldGen/WorldGen.OptionA`. It reuses the cloned
img2threejs Python intake, `ObjectSculptSpec`, factory generation, validation, and staged visual
review. It does **not** replace the upstream authoring prompts with prompts that generate Unity
C# source. The spec is the common handoff; a deterministic emitter now generates C# source from
the validated module. Provider-authored code is never compiled into the game. The C# source path
is the intended shippable Option A route; GLB is a visual comparison route.

| Destination | Implemented path | Intended use | Fidelity limit |
| --- | --- | --- | --- |
| C# source and module | `option_a.py import` validates/audits the spec, writes `candidate.module.json` and deterministic `candidate.cs`, and opens the module in **Tools > WorldGen > Option A Preview**. Generated source uses a Unity-compatible, reusable evaluator backed by `WorldGen.Geometry`; a Unity renderer binds semantic material roles. | Ship compact procedural source after review, and compare its geometry with the native authoring evaluator. | Supports an audited subset. Unity compiled the strict skull source, checked JSON parity, packaged six map roles, captured fixed views, and measured LOD/runtime costs. The skull still fails visual and runtime approval; layered materials and wear remain unsupported. |
| Upstream GLB | The browser exporter generates and runs the upstream Three.js factory, waits for material maps, exports the evaluated scene to GLB, and writes browser, texture-free, and albedo-only diagnostic images. Unity glTFast loads it in **Tools > WorldGen > Option A GLB Preview**. | Preserve the actual upstream mesh and PBR texture output for Unity review. | Static GLB omits browser lighting, environment, postprocessing, animation logic, and runtime `userData` behavior. It still needs upstream visual review and Unity-specific lighting/material checks. |

`option_a.py init` creates a separate image session; `next` delegates the staged workflow to
upstream Python. Strict import and export are gated by upstream validation; `--require-complete`
also requires all passes to have accepted review evidence. The browser GLB route currently gives a
closer visual match to img2threejs because its material maps transfer more completely. Closing
that appearance gap is the next source-path milestone. Neither route changes native Production LOD0 automatically. Details and
commands are in `WorldGen.OptionA/README.md`.

**What has been verified:** an early review-only skull-torch blockout produced an 11 MB GLB with
15 meshes, seven materials, and 29 embedded textures. Three.js GLB roundtrip error was 0.0000215
normalized mean RGB at 1024 px. An isolated Unity 6 editor compiled the GLB preview and glTFast
imported that file. The newer strict, pass-specific blockout GLB roundtrip error is 0.0000166.
These results verify transfer of an incomplete model; they do not measure resemblance to the
reference. The live Unity project has not yet received a completed asset review.

**Current quality gap:** the skull-torch session has a structurally valid 38-component,
six-material spec, but its upstream pass state remains `blockout` with zero accepted reviews.
The current map-stripped strict blockout render fails upstream tier-1 diagnostics: silhouette
IoU **0.3554** versus **0.85** required and scale delta **0.6254** versus **0.08** allowed.
Earlier experiments used different framing and are not directly comparable. The plaque profile
is too geometric, the skull reads as a dark disc, and its eye and nose openings are not legible.
Material extraction from small reference crops produces
estimated PBR maps; those maps have not passed neutral-light and reference-light review. The
structural, form, material, surface, lighting, interaction, and optimization passes have not
been accepted or tested end to end on this subject.

**September 26 code and Unity audit:** Accepted photo points, edges, and openings now gate
every candidate against all views. Bounded feature and existing-SDF-opening fit commands can
use verified features to propose review-only part/cavity changes; the real skull has no
accepted opening masks yet. A strict skull candidate generated and compiled C# with 24
packaged maps. An offline 384 px map candidate reduced measured loaded maps from about
256 MiB to 36 MiB without materially changing the already-dark fixed Unity renders.
Its build still takes about 1.1 seconds against a 0.5-second budget, and six material roles
exceed the four-slot budget. Unity fixed-view high/medium/low comparisons pass on skull,
organic, manufactured, and thin/open review fixtures. A hash-pinned release review correctly
rejects the skull on upstream passes, reviewed geometry, runtime budget, and appearance.
These fixtures do not establish full photo-to-asset quality for their categories.

**Next Option A work:**

1. Start with measured front-image anchors: plaque outline and apex, cranial bounds and eye line,
   bracket contact, and torch center/tip. Project the generated component bounds through the same
   review camera, record pixel errors, and adjust the camera and macro dimensions against those
   measurements. All current major components are root-level; the skull cavities are operations
   inside one SDF mesh, not separate child meshes. The reference camera is still unsolved.
2. Isolate the cranium in a neutral render to check the SDF cuts, world-space depth/occlusion,
   winding, and gradient normals. Use a temporary double-sided or normal view only to diagnose a
   rendering defect. Improve the plaque profile and skull/jaw/torch geometry, then rerun the
   map-stripped blockout silhouette, aspect, and scale gates. Do not bypass a failed gate merely
   to unlock later passes.
3. Correct material crop selection and map response, then compare neutral and reference-matched
   browser renders so bone, stone, metal, and char remain distinguishable.
4. Run each remaining upstream pass with a browser comparison sheet, AI visual critique,
   recorded revisions, and accepted review; export the completed GLB only after strict quality
   succeeds.
5. Inspect the completed GLB in the live Unity project, compare its four fixed views with the
   native candidate and source images, and verify materials, LOD, lighting, and runtime cost.

The earlier C# module comparison in `WorldGen.OptionA/workspaces/temple-skull-torch-blockout-a02`
is a separate snapshot: it imported 15/15 authored components and scored IoU 0.755 front, 0.719
side, 0.747 three-quarter, and 0.728 top at 256 px. All four failed the native 0.80 IoU/0.90
coverage gate, and native c12 was ahead in all four IoU views. Its seven material recipes and
scores should not be read as results for the newer 38-component strict spec or GLB route.

### Current skull-torch decision and next work

**Production status: not ready.** The unchanged 32,031-triangle Production LOD0 remains the
baseline. The best measured review candidate is
`workspaces/temple-skull-torch-01/temple-skull-torch-01-assembly-review-c12.module.json` in the
authoring workspace. At the 256×256 gate it scores:

| View | Silhouette IoU | Foreground coverage | Result |
| --- | ---: | ---: | --- |
| Front | 0.818 | 0.902 | Pass |
| Side | 0.741 | 0.858 | Fail |
| Three-quarter | 0.774 | 0.852 | Fail |
| Elevated-top | 0.739 | 0.839 | Fail |

Every required view must reach **0.80 IoU and 0.90 coverage**. These are silhouette checks only;
the earlier a07 Unity LOD captures also show facial-detail and material popping. No c12 Unity
capture or approval has been recorded. The c12 change is a small 5 mm common plaque move, not a
new finished skull or material pass. Its source-pinned part masks remain **proposed**, including
the visually inspected three-quarter and elevated-top outlines. The [evidence overlays](review/skull-torch-evidence-b08/README.md)
show the current masks and matched points.

**Next changes, in order:**

1. Correct the remaining three-quarter and elevated-top part boundaries and internal openings
   at source-pixel resolution; keep occluded ownership unknown. Verify additional matched
   skull, plaque, and shaft landmarks where the same physical point is visible across photos.
2. Give the plaque independent outline, thickness, and depth controls while constraining its
   contact with the skull and shaft. Fit those controls jointly with camera pose using contour,
   part-mask, and confidence-weighted landmark loss. The current common transform improved
   weighted landmark error only **73.8 to 72.6 px**; side and oblique corners still miss by
   roughly 100–150 px.
3. Replace or refine the skull surface around the orbital and nasal openings, jaw, and rear
   profile. Add close-up part and opening gates alongside the whole-object gate, and reject any
   change that regresses a passing view.
4. Once the master shape passes all views, redo part-verified color/material projection and
   feature-aware medium/low LODs. Review fixed views and LOD transitions in Unity, then measure
   runtime screen-size, memory, and frame cost before promotion.

The native authoring CLI compiles, and **53 authoring regression cases pass**. The latest evidence
candidate validates against all four original image hashes; its generated JSON lives under
`/tmp/skull-reference-evidence-review-b08.json` and can be rebuilt from the saved review
recipes. Neither that evidence nor c12 has been promoted into Production LOD0.

### Current pipeline

```text
approved multi-view references
  -> intake/provenance and silhouette evidence
  -> semantic region decomposition
  -> upstream ObjectSculptSpec or bounded native geometry response
  -> fail-closed capability/identity/bounds/budget validation
  -> native C# mesh evaluation
  -> fixed Unity reference/render comparison
  -> provider or human revision
  -> explicitly accepted candidate
  -> cached/exported runtime asset
```

The provider boundary is intentionally replaceable. A response can be prepared manually, through
Codex CLI, through another command-line provider, or through an HTTP adapter. Providers author
declarative specifications; they do not execute at runtime and are not allowed to inject arbitrary
C# into the game.

### Implemented authoring capabilities

- Provider-neutral review, initial-draft, region-segmentation, and SDF geometry-authoring packages.
- Manual and Codex CLI operation, external-command/HTTP review adapters, strict structured-output
  schemas, candidate validation, and explicit apply/discard boundaries.
- Front, side, top, and three-quarter Unity cameras; persistent camera alignment; bounds, socket,
  and clearance overlays; deterministic captures; comparison sheets; and ordered pass reviews.
- Correct front-camera convention in the Unity review window (front now displays the authored
  front rather than the back), role-aware preview materials/lighting, and overlays hidden by
  default so geometry can be judged without diagnostic clutter.
- PNG/JPEG reference intake, deterministic plain-background foreground extraction, component-local
  polygon masks, provenance/confidence tracking, thin-feature supersampling, semantic internal
  features (cavities, ridges, landmarks, and attachments), and multi-view visual-hull carving.
- Front/side/top silhouette scoring using intersection-over-union, coverage, and contour distance,
  plus deterministic silhouette, depth, normal, and cavity diagnostic renders.
- `elevated-top` reference support throughout silhouette extraction, semantic segmentation, and
  geometry authoring. Because the current skull image is oblique rather than a calibrated top
  view, it is mapped to top evidence with confidence capped at **0.55**.
- Portable primitives: box, rounded box, sphere/ellipsoid, cylinder/cone, polygon extrusion,
  lathed profile, torus, tube, curve sweep, and tapered sweep.
- Full component pitch/yaw/roll, endpoint attachment contracts, endpoint-derived tapered geometry,
  and bounded taper/bend/twist deformation stacks.
- Native SDF sphere, ellipsoid, capsule, box, and cone fields; transforms; smooth union,
  subtraction, and intersection; bounded polygonization; and real cavity topology.
- Position welding, Loop subdivision, Taubin smoothing, normal recomputation, triangle budgeting,
  and identical portable/Unity evaluation.
- Fail-closed img2threejs `ObjectSculptSpec` audit/import. The upstream 12-component conformance
  fixture imports and evaluates **12/12** components; the upstream implicit-SDF fixture and the
  3-component `StandProudHead` fixture (using `lathe` and `tapered-sweep`) import and evaluate.
  Supported primitives include box, sphere, ellipsoid, cylinder, cone, capsule, lathe, extrude,
  torus, tube, curve-sweep, and tapered-sweep.
- Correct marching-tetrahedra 2-in/2-out topology, local gradient sampling, and degenerate-triangle
  rejection in the generic SDF mesher.
- A deterministic bounded SDF parameter fitter with source regularization, maximum 4% position
  drift, maximum 10% size drift, and a hard limit of 24 accepted adjustments. The adjustment-cap
  boundary is enforced without the previous one-step overrun.
- Fifty-four authoring tests pass. `WorldGen.Geometry`, the authoring CLI, and the Unity editor
  preview compile successfully.

### Important quality status

Geometry capability parity is not the same as complete visual parity. The first skull-torch
blockout, charred-head smoothing candidate, `skull-sdf-a01.module.json`, and every
`feature-fitted-*` result are rejected evidence only and must not be promoted. The unconstrained
feature fitter reduced numeric mask loss by exploiting the metric and visibly damaged the 3D
shape; this is why bounded drift, source regularization, accepted-edit limits, and fail-closed
feature checks are now mandatory.

The current safe baseline is
`WorldGen.Authoring/workspaces/temple-skull-torch-01/temple-skull-torch-01-production-lod0.module.json`
with **32,031 triangles**. Production LOD0 was not changed by the feature-fit experiments.

The latest experiment is the review-only `approx-top-a05` package. It used front, side,
three-quarter, and reduced-confidence elevated-top images; semantic extraction identified 11
regions and a three-view cranial form with orbital, nasal, ridge, plaque-contact, and shaft-contact
features. Raw A05 failed validation (front IoU 0.65, side IoU 0.59). The bounded fit improved front
IoU to 0.74 and side IoU to 0.61, but still failed the side orbital and brow/cheek checks. No A05
candidate was exported and Production LOD0 remains the version to inspect in the viewer.

This result isolates the main limitation: composing a detailed skull from fitted ellipsoid SDF
primitives cannot reproduce the supplied silhouettes and landmarks precisely enough. More fitting
passes or subdivision would add cost and density without solving the representation problem.

### September 24 native authoring checkpoint

The next-pass plumbing is now available as a **candidate workflow**, with no automatic promotion:

- `new-image` creates a workspace from a PNG/JPEG, records its SHA-256 provenance and approximate
  physical bounds, and labels unseen depth as inferred. One image can be segmented and packaged
  for an initial provider draft; a visual hull still requires multiple compatible views.
- Reference cameras now record perspective/orthographic mode, pose, field of view, crop principal
  point, and scale. `calibrate-cameras` performs a bounded pose/crop search against the current mesh;
  `score-mesh` reports independent per-view IoU and coverage acceptance.
- `mesh-candidate` converts an existing component into a validated explicit mesh without changing
  the source. `calibrate-region-cameras` aligns component-local region masks before `fit-mesh`.
  Fitting preserves triangle connectivity, limits drift to 4% of mesh extent, enforces component
  bounds, and rejects any accepted step that regresses a required view.
- `export-lods` emits portable high/medium/low mesh JSON and copies supplied material maps under
  content-hashed names. Small components retain their original forms; SDF components and explicit
  meshes retaining SDF provenance can re-evaluate that source at lower resolution; other meshes
  use topology-checked reduction. Front/side/top silhouette
  continuity and total triangle budgets gate export. SDF-backed meshes now export padded six-chart
  UVs and explicit smooth normals. `bake-photo-albedo` can project calibrated front/side color into
  those charts while recording source hashes and inferred pixels.
- Unity's module preview accepts explicit meshes. **Tools > WorldGen > Import LOD Asset** creates
  a prefab with a `LODGroup`, mesh assets, semantic-role materials, and supplied albedo, normal,
  roughness/metalness, AO, and emission maps. Unity 6000.6.0f1 batch compilation passed.

Using the unchanged Production LOD0 as input, the export produced review-only meshes of **31,999**
high, **11,248** medium, and **2,632** low triangles. The high artifact drops 32 duplicate or
degenerate triangles during serialization; the 32,031-triangle source module was not edited.
The three review artifacts are saved under
`WorldGen.Authoring/workspaces/temple-skull-torch-01/exports/production-lod0-lod-review-a01/`.
The CPU projection now uses Unity's screen-right basis. The authored front camera is at **+Z**,
and the side camera is at **+X**, placing the projecting torch shaft at screen right as in the
supplied photo. Recalibration under that shared basis gives whole-object IoU **0.611 front**,
**0.710 side**, **0.714 three-quarter**, and **0.696 elevated top**. Coverage is
**0.615 / 0.769 / 0.745 / 0.759**. Every view fails the **0.80 IoU / 0.90 coverage** gate.
Region alignment reached front **0.909** and side **0.862**; two bounded contour steps ended at
front **0.907** and side **0.868**. The fitted mesh is review-only
`WorldGen.Authoring/workspaces/temple-skull-torch-01/temple-skull-torch-01-mesh-contour-review-a06.module.json`.
`score-feature-anchors` now projects named SDF cavity centers against region feature masks.
After correcting image-left/right orbital naming, all six tested projections land inside their
masks. This narrows the next geometry problem to cavity contours, depth, and overall proportions,
not merely misplaced cavity centers. The map and measurements are in the skull workspace
`evidence/` directory.

A separate calibrated two-view coarse hull was tested and rejected: it filled the orbital/nasal
cavities and looked blocky. Under the corrected camera basis, its whole-object scores were front
**0.614**, side **0.699**, three-quarter **0.692**, top **0.700**. The file is
`WorldGen.Authoring/workspaces/temple-skull-torch-01/temple-skull-torch-01-calibrated-hull-rejected-a02.module.json`.
The photo-baked a07 candidate and its atlas are also review-only; the atlas picks up plaque/torch
pixels and misplaces facial detail because silhouette camera alignment and broad region masks do
not give pixel-accurate feature correspondence.

Unity imported the a07 high/medium/low assets as a prefab and captured front, side,
three-quarter, and top at each LOD. The [contact sheet](review/skull-torch-a07/contact-sheet.png)
and [review findings](review/skull-torch-a07/README.md) show visible facial-detail/material
popping, especially from high to medium and from medium to low. The review prefab is
`AO.Unity/Assets/Generated/WorldGenReview/TempleSkullTorchA07.prefab`.
The artifacts remain unapproved; Production LOD0 is unchanged.

The current image-to-3D flow is:

```text
new-image -> extract-silhouettes -> draft-package/provider candidate
          -> calibrate-cameras -> mesh-candidate
          -> calibrate-region-cameras -> fit-mesh
          -> score-mesh and Unity review -> accepted master
          -> export-lods -> Unity LOD prefab
```

### Review the current candidate in Unity

The review files are ready to inspect; they are rejected production candidates.

1. Open **Tools > WorldGen > Module Preview**, choose
   `WorldGen.Authoring/workspaces/temple-skull-torch-01/temple-skull-torch-01-assembly-review-c12.module.json`,
   and compare front, side, top, and three-quarter views with the reference images. Use the
   unchanged `temple-skull-torch-01-production-lod0.module.json` in the same workspace as the
   baseline. The c12 candidate has not yet had a recorded Unity capture; the numerical gate
   already rejects it in side, three-quarter, and top.
2. Open **Tools > WorldGen > Import LOD Asset** and choose
   `WorldGen.Authoring/workspaces/temple-skull-torch-01/exports/photo-review-a07-lods/temple-skull-torch-01-high.mesh.json`.
   The importer finds the matching medium and low JSON files beside it, verifies their shared
   source/master hashes and decreasing triangle counts, then asks for a prefab path under
   Unity `Assets/`. It creates mesh and material assets plus a prefab with an `LODGroup`.
3. Place the prefab in a test scene and move the camera through each LOD transition. Capture
   close and distant views, check for silhouette popping, missing components, flipped faces,
   incorrect materials, and scale/orientation errors. Record failures as review evidence; do
   not promote this candidate based on a successful import alone.

**Next improvement milestone:** build reusable, category-neutral image-to-3D quality stages.
Extract pixel-accurate object and part masks, contours, and named feature correspondences from
each supplied view. Solve camera pose and geometry together, with confidence-weighted evidence,
topology preservation, and explicit handling of unseen surfaces. Project color only from verified
pixels of the matching part, transfer surface detail to lower LODs, and compare rendered appearance
at each LOD transition. Run the same checks on organic, manufactured, thin, and multi-part objects;
the skull-torch is one regression case, not a special rule. The current scorer requires at least
**0.80 silhouette IoU and 0.90 foreground coverage in every view**, but those scores alone do not
establish visual quality.

The remaining quality work is substantial: a stronger mesh representation/solver for organic
identity features, landmark-verified camera calibration for oblique images, accurate UV/color
baking and tangent-space normal transfer, feature-aware LOD detail transfer, and actual in-game
screen-size/performance review. The first automatic photo atlas is review-only and visibly
misregistered. A single image cannot establish
hidden depth or back-surface identity, so those surfaces remain inferred until more evidence or
human approval is supplied.

### Suggested next implementation passes

These passes apply to every asset category. The current skull-torch images provide one test case,
but no thresholds, solver branches, or material rules should depend on that object.

1. **Build a reusable reference-evidence contract.** Store per-view object masks, per-part masks,
   internal openings, contour samples, optional matched landmarks, confidence, camera metadata,
   and source hashes at image resolution. Mark unknown or occluded pixels explicitly. Add a small
   regression set covering an organic solid, a manufactured solid, a thin/open object, and a
   multi-part assembly. **Done when:** the same intake and validation path loads all four, keeps
   part IDs consistent across views, and reports missing evidence without inventing it.
2. **Calibrate cameras with more than silhouette overlap.** Fit pose, projection, crop, and scale
   against contours and matched landmarks; use depth or normal evidence when available. Report
   per-view reprojection error and uncertainty, and reject mutually inconsistent references.
   **Done when:** held-out landmarks and contours improve across the regression set without
   sacrificing any previously accepted view.
3. **Fit coarse shape and part structure before fine detail.** Select a mesh, SDF, shell, or
   parametric representation from object structure; jointly optimize a low-resolution shape
   against all views. Constrain topology, openings, thin features, part interfaces, and source
   shape. Keep unseen surfaces labeled inferred. **Done when:** every required view passes its
   independent contour/coverage checks and close-up feature checks, with no new mesh defects.
4. **Bake appearance from verified correspondences.** Create usable UVs and tangents, test
   visibility and part identity for each projected pixel, reject occluders and source lighting
   where possible, and fill unseen texels separately. Transfer high-resolution surface detail to
   medium/low normal maps. **Done when:** color and feature placement match the references in
   fixed views and the bake report identifies observed versus inferred texels.
5. **Gate LODs by rendered appearance and runtime cost.** Compare each level at its actual screen
   size using silhouette, color, normal, and feature differences; capture transition frames in
   Unity and measure frame cost and memory against configurable budgets. **Done when:** all four
   regression assets meet those gates without visible popping at normal viewing distances.

Pass 1 has a source-resolution contract, review recipes, and some corrected masks, but the
remaining broad or provisional boundaries still limit reliable geometry and texture fitting.
The local browser reviewer in `WorldGen.Authoring/tools/reference_review.py` now overlays the
automatic object and part masks on each source image. It supports source-pixel rectangle/polygon
corrections, explicit unknown pixels, named landmarks, and part approval; all changes produce a
new hash-pinned evidence file through the existing validators. The reviewer does not yet propose
landmarks, edit existing landmarks, or show live mesh/camera score changes.

Pass 1 is underway in `WorldGen.Authoring`: `ReferenceEvidenceSet` defines source-resolution
object and part masks, opening masks, named landmarks, per-view camera/confidence, source hashes,
and explicit unknown pixels. `build-reference-evidence <module.json> <region-proposals.json>
<output.json>` uses the original image pixels and maps existing aligned region proposals back to
them; `validate-reference-evidence <evidence.json>` checks dimensions, identity, provenance, and
mask consistency. Automated intake regression cases now cover organic, manufactured, thin/open,
and connected multi-part shapes in two views. Pixel-accurate part proposals, manually verified
landmarks, real category-diverse reference assets, and consumer integration are still needed
before pass 1 meets its completion criteria.

The first real-image audit used the unchanged skull-torch Production LOD0 module and the
`approx-top-a03` region proposals. At 1024×1024 source resolution, proposed part masks cover
**87.5% front / 52.3% side / 0% three-quarter / 78.8% elevated-top** of extracted foreground.
Pixels claimed by multiple parts are **55.3% / 16.7% / 0% / 36.6%**, respectively. All masks
remain proposed; no landmark is verified. These figures expose broad, overlapping proposals and
missing three-quarter part evidence; they are not quality scores for the 3D mesh.
`audit-reference-evidence <evidence.json> [source-directory]` reports these gaps.
`apply-reference-landmarks <evidence.json> <review.json> <output.json>` accepts explicitly
reviewed source-image coordinates only when their image hash and part pixel agree. It leaves the
source evidence untouched. The next work is to correct part ownership and unknown/occluded pixels
in real references, then verify matched landmarks in more than one view before camera fitting.

`apply-reference-masks <evidence.json> <review.json> <output.json>` now applies source-pinned
pixel-coordinate rectangle or polygon edits. `assign` claims observed foreground for one part and
clears competing claims; `clear` removes a claim; `unknown` records unresolved part identity.
Edits create a separate candidate, downgrade touched masks to proposed, preserve source evidence,
and fail if they invalidate a verified landmark. This correction mechanism is tested, but no
skull-torch mask has been approved or promoted yet. A reproducible review-only front correction
is saved as `WorldGen.Authoring/workspaces/temple-skull-torch-01/evidence/front-mask-review-a01.json`.
It clears two plaque claims from a small area within the visible skull, reducing front overlap
from **55.3% to 50.3%** without changing foreground coverage. The rest of the broad part masks
and the entire three-quarter part view still need review.

### September 25 reviewed-evidence experiment

The review-only `mask-review-a02.json` adds source-pixel annotations for ten visible
three-quarter parts, leaving the obscured right brace unknown. Three-quarter part coverage rose
from **0% to 85.7%** with no overlapping claims in the annotated candidate. These outlines are
provisional and have not been checked pixel by pixel. `landmark-review-a01.json` records three
plaque points matched in front and three-quarter photos; `model-landmark-map-a01.json` identifies
their current mesh anchors. All three recipes are in the skull workspace `evidence/` directory.

`calibrate-reviewed-cameras` now uses the original photo frame rather than recentered silhouette
grids. It raised whole-object IoU to **0.579 front / 0.641 side / 0.709 three-quarter / 0.698 top**.
`refine-reviewed-cameras` uses the matched landmarks to search crop and scale while allowing no
more than 0.005 silhouette-IoU regression. Front IoU rose to **0.718** and mean front plaque
landmark reprojection error fell from **85.9 to 40.0 pixels**. Three-quarter landmark error stayed
at **89.9 pixels**, indicating that crop/scale alone cannot explain its mismatch. The camera-only
candidate is `temple-skull-torch-01-landmark-camera-review-a02.module.json`.

`fit-reviewed-mesh` converts a selected component to an explicit mesh and fits only fully observed
part views. For the skull, six bounded edits improved part-mask IoU from **0.634 to 0.639 front**,
**0.636 to 0.643 side**, and **0.691 to 0.696 top**. The whole-object result remained below every
0.80 IoU / 0.90 coverage gate: **0.716 / 0.641 / 0.710 / 0.698 IoU** and
**0.744 / 0.731 / 0.734 / 0.817 coverage**. The mesh candidate is
`temple-skull-torch-01-landmark-mesh-review-a02.module.json`; Production LOD0 is unchanged.
The mesh candidate must remain rejected until the part masks are pixel-reviewed and a stronger
shape representation resolves the landmark and silhouette mismatch across views.
A separate plaque primitive rotation test improved side IoU from **0.641 to 0.679**, but reduced
front from **0.718 to 0.710** and three-quarter from **0.709 to 0.699** at the same cameras.
It was rejected rather than applied to Production LOD0.

### September 25 explicit-outline and unknown-aware fitting pass

`WorldGen.Geometry` now supports an explicit, simple polygon outline for extruded primitives,
including concave outlines and triangulated caps. The authoring module schema carries normalized
outline points into the shared geometry builder. Geometry and authoring regression tests cover
the new path. This is a category-neutral representation improvement; it does not alter the
Production LOD0 plaque.

Mask review now records other visible parts as known negatives when assigning a pixel.
`reconcile-reference-parts` uses mapped component depth to resolve only unambiguous overlapping
claims and marks the rest unknown. On the review skull evidence, it resolved **198,224 / 227,550**
front conflicts, **12,175 / 33,691** side conflicts, and **92,430 / 99,125** elevated-top conflicts;
the remainder stayed unknown. Resulting foreground coverage is **81.0% front / 41.7% side /
85.7% three-quarter / 76.3% top**, with zero overlapping part claims. These are provisional
model-guided labels, not pixel-verified masks. Unknown pixels now remain unobserved during
part-mask downsampling and mesh fitting instead of becoming false background.

A review-only explicit-outline plaque candidate was generated from the existing front region
proposals. After camera recalibration, its whole-object scores reached **0.822 IoU / 0.907 coverage
front (pass)**, **0.720 / 0.850 side**, **0.757 / 0.813 three-quarter**, and **0.742 / 0.845 top**.
Landmark refinement with independent coverage gating produced a safer camera candidate with
**0.822 / 0.907 front**, **0.720 / 0.850 side**, **0.755 / 0.812 three-quarter**, and
**0.742 / 0.845 top**. It reduced three-quarter plaque landmark error from **125.4 to 113.6 px**,
still far too high for acceptance. The candidate is
`temple-skull-torch-01-explicit-plaque-camera-review-c04.module.json`.

A bounded unknown-aware skull fit improved part scores but failed the independent whole-object
no-regression and quality gates; `fit-reviewed-mesh` now reports those failures and returns a
failure exit status while preserving its review candidate. The rejected mesh is
`temple-skull-torch-01-explicit-plaque-mesh-review-c06.module.json`. A coarse axis-scale search
found no safe skull proportion change. The next geometry work needs a different skull surface and
opening representation, plus pixel-reviewed side and three-quarter part evidence. Production
LOD0 remains unchanged.

`score-reviewed-parts` now compares each mapped component with its observed part pixels. On the
camera-only c04 candidate, the skull part reaches IoU **0.512 front / 0.607 side / 0.724
three-quarter / 0.709 top**, while the outer plaque part is only **0.457 / 0.075 / 0.348 / 0.265**.
Several small parts score near zero. These scores use provisional masks and must not be treated
as a final quality grade, but they show why whole-object front acceptance alone cannot authorize
the asset. Prioritize pixel-reviewed plaque/shaft ownership, more matched landmarks across side
and top, then a part-aware camera and geometry solve with openings and attachment constraints.

The release silhouette check now supports a selectable review grid:
`score-reviewed-mesh <module.json> <evidence.json> 256`. At 256×256, the best review-only c04
candidate scores **0.818 IoU / 0.903 coverage front (pass)**, **0.701 / 0.831 side**,
**0.752 / 0.808 three-quarter**, and **0.738 / 0.841 top**. Its front result survives the finer
check, but the other three views still fail. In particular, side IoU is lower than its 64×64
score of 0.720, so coarse-grid acceptance alone is insufficient for release. The CLI returns
failure when any view misses the 0.80 IoU / 0.90 coverage gate. No production promotion is justified.

Iterative camera calibration now revisits coupled pose, scale, and crop coordinates at each
search scale. On the explicit-plaque review candidate it improved the 256×256 result to **0.818 /
0.903 front**, **0.737 / 0.857 side**, **0.772 / 0.852 three-quarter**, and **0.738 / 0.840 top**.
The review-only camera file is `temple-skull-torch-01-camera-iterative-review-c07.module.json`.
Landmark refinement from c07 reduced three-quarter plaque reprojection error from **110.5 to
102.5 px** on the 64×64 optimization pass, but its 256×256 silhouette fell to **0.765 / 0.847**;
that c08 file remains a diagnostic, not the preferred camera candidate. The remaining error is
too large to treat either camera as calibrated ground truth. The authoring regression suite passes.

The side-photo part overlay exposed a specific evidence error: the provisional outer-plaque mask
claimed a strip of the skull and missed the visible plaque slab. The source-pinned
`evidence/side-mask-review-b04.json` recipe reassigns the visible slab and rear skull patch in a
separate reviewed-evidence candidate. It validates against all four original image hashes. With
the c07 camera, the side outer-plaque diagnostic IoU changes from **0.100 to 0.520**; its coverage
is **0.782** because the corrected mask includes more of the actual plaque. The side skull part
IoU changes from **0.628 to 0.631**. These are approximate manual boundaries and remain proposed,
not pixel-verified ground truth. The part-evidence audit reports side foreground ownership
**41.7% to 81.6%**, with no overlaps; the whole-object mask and its scores are unchanged.

A bounded axis-scale fit of the outer plaque against this corrected evidence found no safe edit:
the accepted scale remained **(1.000, 1.000, 1.000)**. Its `c09` review artifact is rejected.
The plaque needs profile, thickness, and placement changes constrained against the skull and all
four views, rather than another uniform-scale pass. Production LOD0 remains unchanged.

### September 25 cross-view evidence review b08

The [review overlays](review/skull-torch-evidence-b08/README.md) show source-image part masks and
plaque landmarks for side, three-quarter, and elevated-top. The three-quarter major-part proposals
were visually inspected but remain approximate. Small source-pinned mask edits add the missing
elevated-top plaque apex/right corner and front right corner. New matched plaque landmarks cover
**four front, two side, three three-quarter, and three elevated-top** observations. Side edge
midpoints carry confidence 0.45, and elevated-top corners 0.55, reflecting depth ambiguity.

The evidence normalizer now propagates known object background to every part as known negative
pixels without inventing foreground ownership. It preserves unknown foreground and does not
mutate the source evidence. This raises the three-quarter part diagnostic's observed grid from
**31.3% to 95.3%**; under the stricter known-background check, skull part IoU is **0.643** and
outer-plaque part IoU **0.328**. The review evidence validates against all four source hashes,
and the authoring regression suite passes.

On the c07 camera, model reprojection errors include **125.2 px** at the side plaque apex,
**146.5 px** at the three-quarter apex, and **119.9 px** at the elevated-top right corner.
Landmark camera refinement now weights observations by their recorded confidence. All part masks
remain proposed; the photo edges and internal boundaries still need pixel-level review before
fitting fine geometry. Production LOD0 and the whole-object quality result are unchanged.

### September 25 bounded plaque assembly fit

`fit-reviewed-assembly` now searches a small common translation and scale of named connected
components against confidence-weighted landmarks. Every trial must preserve each whole-object
view's IoU and coverage within 0.005 at the working grid; any view already passing must keep
passing. A retained result receives the same checks at 256×256. The first stricter search found
no safe edit. A finer search accepted a **5 mm Z move** of the outer plaque, inner plaque, and
upper rune panel together, reducing weighted plaque landmark error **73.8 to 72.6 px**.

The review-only `temple-skull-torch-01-assembly-review-c12.module.json` scores at 256×256:
**front 0.818 IoU / 0.902 coverage (pass)**, **side 0.741 / 0.858**,
**three-quarter 0.774 / 0.852**, **elevated-top 0.739 / 0.839**. Side and three-quarter IoU
improved slightly from c07, but the three failing views remain well below release thresholds.
One subsequent landmark camera refinement made no further camera changes; c12 is the preferred
review candidate. The authoring tests pass. Production LOD0 is unchanged. These results indicate
that a common affine plaque transform is too limited; the next representation needs independent
profile and thickness control with explicit attachment and visibility constraints.

### Remaining 3D-path work, in priority order

1. Add a representation chooser for closed solids, shells, thin structures, repeated parts, and
   multi-part assemblies. Start from an appropriate coarse mesh, hull, SDF, or parametric template,
   then optimize against all calibrated views while preserving topology, smoothness, feature
   spacing, and attachment relationships. Symmetry is an optional prior, never an assumed property.
2. Improve the first bounded camera search into calibrated perspective-camera solving and automated crop/pose matching. Record intrinsics,
   extrinsics, crop, and subject scale per reference. Treat elevated/three-quarter images as
   oblique cameras rather than pretending they are strict orthographic top views. A true calibrated
   top or rear reference would materially reduce depth ambiguity.
3. Upgrade fitting from whole-region mask loss to a joint, confidence-weighted objective containing
   silhouette IoU, signed contour distance, depth consistency, normal consistency, cavity masks,
   named landmark positions, symmetry, source-shape regularization, and attachment constraints.
   Keep independent per-view acceptance thresholds so a good front score cannot hide a bad side.
4. Add a coarse-to-fine solve: camera and overall mass first, part proportions and interfaces
   second, internal openings and surface features third, and mesh refinement last. Reject a stage
   when it regresses an already accepted view.
5. Add close-up part captures and deterministic multi-signal comparisons. Check contours,
   openings, thin elements, repeated detail, part boundaries, and attachment clearance at useful
   pixel resolution as well as whole-object scale.
6. Port the complete upstream quality contract: pre-spec assessment, detail inventory, landmarks,
   strict-quality checks, attachment/geometry gates, critical-feature acceptance, interior-detail
   comparison, and separate `refine-spec`, `refine-camera`, and `refine-geometry` decisions.
7. Add UV generation/unwrap, tangents, submeshes/material regions, projected-albedo baking, and
   deterministic texture caching with provenance and color-space metadata. Then port layered/PBR
   material recipes for albedo, roughness, metalness, normal/height, AO, and emission.
8. Build a small category-diverse regression set with an organic object, a manufactured object,
   a thin/open structure, and a multi-part object. Run the same intake, fit, texture, LOD, and Unity
   review gates on each. Include the Temple skull-torch against its unchanged Production LOD0.
9. Complete remaining general geometry coverage: extrusions with holes, ground-blade/open-shell
   routes, repetition/instancing systems, relief/displacement, decimation, LODs, collision proxies,
   and deterministic content hashes/mesh diagnostics.
10. Complete production export and catalog ingestion: the current review-only mesh/texture artifacts
   need versioned content hashes, collider payloads, semantic sockets, and pinned ProjectMayhem consumption.
11. Only after the authoring loop is proven, connect approved asset families and controlled
    variation to the procedural dungeon catalog. Runtime generation must select approved assets;
    it must not invoke vision models or regenerate hero meshes while a dungeon is loading.

The detailed authoring roadmap and pipeline contract live in
`/home/cody/Coding/WorldGen/WorldGen.Authoring/docs/ROADMAP.md` and `PIPELINE.md`.

## Implemented

### Shared `WorldGen.Core`

- Pure .NET library shared by ProjectMayhem and ZoneEngine_New.
- Versioned generation manifests and parameter schema.
- Canonical manifest hashing and deterministic layout hashing.
- Deterministic PRNG with named seed derivation.
- Stable world-object identifiers.
- Quantized integer spatial types and bounds.
- Deterministic room graph and grid placement.
- Room roles: entrance, normal, objective, boss, branch, and treasure.
- Corridors and two stable door portals per connection.
- Entrance, exit, and boss spawn points.
- Structural validation for rooms, connections, corridors, portals, and spawns.
- Door-state contract: `Open`, `Closed`, `Locked`, and `Sealed`.
- Door revisions, optional key IDs, and blocking/toggle semantics.
- Determinism and contract tests.
- An engine-neutral presentation contract now publishes stable surface, fixture, and
  light records with owner IDs, semantic material roles, exact quantized bounds, and
  movement-blocking intent.
- Presentation elements and constructive recipes participate in the canonical layout
  hash, so server and clients verify the same authored result rather than independently
  inventing decoration.
- The authoritative collision bake consumes blocking presentation fixtures. Restroom
  partitions, rear infill, toilets, and sinks therefore have the same placement on the
  server and every client.
- Core publishes constructive fixture recipes using portable shapes (`RoundedBox`,
  `Cylinder`, `RoundedRing`, `LathedProfile`, and `Basin`), transforms, dimensions,
  profile points, bevel intent, fixture identity, and semantic material roles.

### Shared `WorldGen.Geometry`

- Unity-free `netstandard2.0` library and local Unity package, now located in the
  shared `WorldGen/WorldGen.Geometry` directory beside `WorldGen.Core`.
- Depends on Core; Core does not depend on Geometry. This preserves the dependency direction:
  Core describes intent, Geometry evaluates it, and engine adapters upload the result.
- Converts constructive recipes into portable vertex, normal, UV, and triangle-index buffers.
- Initial evaluators cover rounded superellipsoid boxes, cylinders, elliptical rounded rings,
  lathed profiles, and open curved basins.
- The first recipes create modern restroom toilets from a lathed porcelain bowl, rounded
  seat, and rounded housing, and sinks from a curved basin plus metal faucet.
- Unity consumes the same generated buffers that a Godot or Unreal adapter can consume;
  it no longer needs to reproduce the fixture modeling rules.

### ZoneEngine_New

- In-memory procedural-instance registry indexed by instance ID and playfield ID.
- Runtime procedural playfield IDs.
- `.worldgen`, `.worldenter`, and `.worldexit` GM commands.
- Manifest delivery to ProjectMayhem through a versioned envelope.
- Manifest resend when reconnecting to a character already inside an instance.
- Normal AO `N3Teleport` and `ZoneRedirection` handoff.
- Recorded return playfield and coordinates.
- Procedural `Playfield` construction without static AO playfield data.
- Authoritative Bepu floors, ceilings, walls, corridor walls, and doorway openings.
- Multiple doorway openings supported on the same room wall.
- Server-side line-of-sight and movement queries use generated geometry.
- Each generated instance owns one initial authoritative door state per portal, giving
  each corridor two independently operated endpoint doors.
- Closed, locked, and sealed procedural doors install one Bepu blocker at their own
  portal opening; reopening removes that blocker on the playfield thread.
- `.worlddoor <index> <state>` is available as a temporary GM collision probe.
- Versioned full door snapshots are sent on entry/reconnect, and revisioned door
  deltas are broadcast to every connected player in the instance.

### ProjectMayhem / Unity Client

- AORebirth zone-redirection decoding and automatic zone reconnection.
- Zone-cookie preservation across playfield transfers.
- New playfield bootstrap handling after reconnect.
- Procedural manifest decoding, compatibility checks, and layout-hash verification.
- Runtime room, corridor, wall, and spawn-marker presentation.
- Multiple doorway openings supported on the same wall.
- Old AO playfield cleanup during procedural entry.
- Session-owned procedural renderer; manually placed debug renderers are unnecessary.
- Removal of orphaned generated roots before rebuilding.
- Procedural geometry is parented to the active playfield for reliable cleanup.
- Door snapshots are validated against the verified layout; unknown, duplicate,
  incomplete, and stale updates are rejected.
- Primitive hinged door leaves and frames render at each portal, animate independently from
  confirmed state, use diagnostic locked/sealed colors, and mirror blocking with
  responsive local colliders.
- Authoritative selected-character movement corrections are applied locally when
  server collision rejects predicted movement.
- Procedural playfields intentionally reuse the ordinary client/server movement
  protocol. The tested procedural-only action/snapshot motor was removed after it
  caused visible input latency, turning oscillation, and periodic rollback.
- Authoritative capsule collision uses bounded sweep-and-slide resolution, preserving
  tangential movement at walls and door frames instead of pinning the player at the
  first contact point.
- Nearby doors highlight when aimed at; `E` and right-click submit a stable-ID,
  expected-revision use request. Open doors retain a nonblocking selection volume.
- Door-use feedback includes both the deterministic door index and stable identity for
  collision diagnosis. Client corridors render side walls matching server blockers.
- Corridor direction is derived from explicit portal facing rather than bounds aspect
  ratio, preventing short, wide corridors from receiving wall end caps across open doors.
- Return to ordinary AO playfields through `.worldexit`.
- Critical paths now make deterministic turns along two axes instead of forming one long
  straight row. The first presentation pass adds variable-height ceilings, role-specific
  palettes, a reusable generated panel texture/material, modular trim/pillar/beam variants,
  and selectively placed room lights that follow room visibility.
- Five deterministic room families are active: rectangle, wide hall, long hall,
  grand chamber, and L-shaped. L-shaped floors, ceilings, outer perimeters, and inner
  notch walls are shared by client and server. Portal facades provide doorway lintels
  and corridor-width shoulder infill, while perimeter trim stops at every doorway.
- Layout profile, visual theme, and asset source are independent manifest choices.
  Facility, subway, temple, and raid profiles have distinct room/corridor dimensions
  and room-family weighting. Industrial, maintenance, alien, subway, and temple themes
  are selectable independently. Procedural, RDB, and hybrid asset modes are transported
  end-to-end; Unity exposes an RDB provider interface and safely falls back to the
  procedural kit until a concrete RDB room-kit adapter is registered.
- Unity now acts as an adapter for Core presentation and `WorldGen.Geometry`: it maps
  semantic material roles to native materials/colors and uploads shared mesh buffers.
  When a constructive recipe exists, Unity suppresses the old placeholder box while
  retaining Core's simplified authoritative collision volume.
- `.worldgen layout` opens a modal top-down inspection view of the last verified layout.
  It labels repeated module kinds deterministically, displays room/corridor outlines and
  door/open-passage markers, supports cursor-centered zoom and drag panning, and tracks
  the controlled player while they are inside the generated playfield.
- The normal `mini_map` binding (Ctrl+6 or Ctrl+Numpad 6 by default) toggles a compact,
  non-modal WorldGen minimap. It reuses the verified topology and live player marker while
  leaving movement, camera, targeting, and interaction enabled.
- WorldGen 5.8 adds six deterministic, non-overlapping subway approach compositions,
  combined with seeded bend direction and the existing approved module catalog. Released
  5.7 manifests retain their original layout behavior.
- WorldGen 5.9 moves the Grand Hall off the fixed train-hub branch. Its location is
  selected from deterministic middle/late side branches, varies by seed, and is never
  connected directly to the Train Chamber. WorldGen 5.8 manifests retain the original
  landmark placement.

### Approved Subway Module Catalog (current checkpoint)

Subway generation is deliberately restricted to the following reviewed pieces. Older
random service, treasure, terminal, and miscellaneous room treatments are not selected:

- Long centered L entrance, with deterministic level and stair-descending variants.
- Clean square circulation rooms with deterministic size variation and no loose gate/
  suitcase-like floor props.
- Train station/platform chamber with recessed track, raised train, wheels, and matching rails.
- Reusable straight track tunnels, side-platform stair access, sealed end treatment, and
  covered tunnel arches.
- Straight-plus-left/right rail junction with actual connected outbound track bays.
- Wide pedestrian arched tunnel and shared stair/passages.
- Flat station-scale grand hall (`44 m × 40 m`, `10 m` high).
- Portal-aware restroom whose stall bank selects a doorway-free wall, reaches the rear
  shell, and supplies stalls, toilets, sinks, and lighting through Core-owned records.

The current modern subway language uses pale large wall panels, graphite floors and track
beds, dark structural metal, blue wayfinding bands, yellow safety accents, and cool-white
public lighting. Core owns placement and semantic roles; clients own native shaders,
material assets, light components, batching, and LOD.

## Verified Checkpoint

Manually verified on September 11, 2026:

- Dungeon generation, layout hash agreement, procedural entry, and return.
- Real-time docked System chat status updates.
- Full door snapshot synchronization after entry/reconnect.
- Revisioned `.worlddoor` deltas for open, closed, locked, and sealed states.
- Door leaf animation, diagnostic state colors, local collision, and authoritative
  server movement blocking/correction.
- Nearby unlocked doors open and close through both `E` and right-click interaction.
- Locked and sealed doors remain closed and return the expected interaction denial.

## Foundation Exists, Runtime Wiring Is Incomplete

### Doors

The shared portal-level door states, per-instance server storage, and
authoritative Bepu blocker toggling exist. Doors initialize open so every generated room
remains reachable. A temporary `.worlddoor` command can exercise state and collision.
Normal player interaction is wired through the development command transport. The
server validates instance, door identity, distance, line of sight, expected revision,
and current state before toggling. Locked doors carry a required AO item-template ID;
the server searches the player's hydrated carried inventory and opens the door only
when that reusable key is present. Sealed doors remain an unconditional denial;
mission-condition behavior is intentionally deferred. Production door assets and
audio also remain outstanding.

## Immediate Next Slice — Player Door Interaction

1. ~~Resolve and highlight an aimed logical door within interaction range.~~
2. ~~Send stable door identity and expected revision via `E` or right-click.~~
3. ~~Validate instance, identity, distance, line of sight, revision, and state.~~
4. ~~Toggle `Open`/`Closed`, deny `Locked`/`Sealed`, and broadcast the delta.~~
5. ~~Add authoritative carried-inventory key lookup for locked doors.~~ Sealed-door
   mission conditions are intentionally deferred.
6. Move the request from development chat transport to the documented production
   extension transport during capability-negotiation work.
7. Keep `.worlddoor` as a GM diagnostic command, not a player interaction mechanism.

**Acceptance:** A nearby player can operate an unlocked door without a GM command;
remote, obstructed, locked, sealed, stale, and cross-instance requests are rejected;
all connected clients converge on the confirmed state.

### Room Visibility and Streaming

The client now identifies the player's logical room or corridor and uses a shared
deterministic room/corridor portal graph to show only spaces reachable through open doors.
An occupied corridor and both endpoint doors remain visible and collidable even when an
endpoint closes. Closed, locked, and sealed doors hide only the blocked side; authoritative
door deltas recalculate the set immediately. The first space across each blocking door is prefetched and
its already-constructed presentation remains ready but inactive. Primitive presentation
construction is cancellable and spread across frames using both a time budget and a
unit-count ceiling. Cube objects are pooled and reused across rebuilds and zone changes.
Needed next:

- Hide nameplates, NPCs, effects, and props in non-visible rooms.
- Extend pooling from primitive building blocks to complete modular room presentations.
- Build only the visible/prefetched presentation for larger layouts instead of eagerly
  constructing the complete prototype dungeon.
- Preserve collision and gameplay authority on the server even when presentation is
  unloaded on a client.

## Known Prototype Limitations

- Instances, return locations, and mutable door states live only in server memory.
- Restarting ZoneEngine_New removes generated instances and their return maps.
- Runtime playfield IDs are not persisted and can be reused after restart.
- Generation is invoked by GM commands rather than missions or physical entrances.
- Dungeon visuals now have a modern procedural material/modular-detail pass. Restroom
  fixtures use first-generation shared constructive meshes, but other authored subway
  details still contain Unity-local procedural decisions that must migrate to Core recipes.
- Door visuals are diagnostic primitives rather than production assets; player use is
  currently carried through a development chat-backed request.
- Entrance, exit, and boss spawns are debug markers rather than gameplay entities.
- No navigation graph or generated NPC pathfinding data is active.
- No encounters, loot, traps, objectives, keys, mission completion, or expiry.
- No multiplayer membership policy or instance ownership/team rules.
- No content download/capability negotiation for a production client.
- Original AO clients cannot render these custom procedural playfields.
- Subway traversal still needs focused validation around train roofs and sides,
  platform/track transitions, escape ramps, ceiling jumps, and the deeper rail tunnel.
  Fixes should change shared collision primitives or the established general movement
  resolver; do not add another procedural-only movement system.
- The first restroom fixture pass is intentionally parked after proving the shared
  constructive pipeline. Its layout and fixtures need substantial visual refinement:
  stall hardware, mirrors, counters, drains, flush controls, plumbing covers, material/
  UV tuning, better proportions, and verified wall-facing orientation.

## Parallel Future Project — AO ACG Mission Toolchain

Plan a separate repository (tentatively `AO.ACG.Toolchain`) for deterministic AO
mission/ACG generation from RDB-derived module data. This is separate from the
ProjectMayhem-only procedural presentation work.

Priorities:

1. Read the current AORebirth mission/ACG implementation and existing extracted RDB
   data without mutating either source.
2. Produce a normalized, versioned catalog of reusable RK mission rooms, corridors,
   doors, and connector sockets.
3. Build exact socket matching and validators for connectivity, overlap, clearance,
   spawn/exit safety, and objective reachability.
4. Generate one reproducible RK mission and emit the legacy server data needed for an
   original AO client to enter and render it using existing RDB resources.
5. Add an optional ProjectMayhem manifest adapter using the same logical layout and
   stable IDs.
6. Add Shadowlands catalogs and generation rules only after the RK vertical slice,
   zoning, reconnect, objectives, and return flow are verified.

The original client cannot consume ProjectMayhem runtime meshes. Its output target
must reference client-known RDB assets and understood ACG/playfield structures unless
a separate, verified client-content distribution mechanism is developed. See the
full design and proposed project boundaries in [procedural.md](procedural.md#separate-ao-acg-mission-toolchain-planned).

## Recommended Delivery Order

### Phase 1 — Authoritative Interactive Doors

1. ~~Give each portal its own logical door so both corridor endpoints operate independently.~~
2. ~~Create player-facing server door interaction using the existing stable identities
   and revisions.~~
3. ~~Bake closed-door Bepu blockers and safely add/remove them on the playfield thread.~~
4. Interaction validation covers instance, distance, line of sight, state, and revision;
   **next:** connect real inventory keys and mission conditions.
5. ~~Define full door snapshots plus revisioned state deltas.~~
6. ~~Render and animate prototype door frames/leaves in Unity only from confirmed
   state.~~ Replace primitives with production presentation later.
7. ~~Resend door snapshots after reconnect and reject stale revisions.~~

**Acceptance:** Two clients observe the same door state; a closed door cannot be crossed
even by a modified client; reconnect preserves the current state.

### Phase 2 — Portal-Based Room Visibility

1. ~~Add room-membership queries and portal-graph traversal.~~
2. ~~Show the current room and rooms reachable through open visible portals.~~
3. ~~Identify a one-room prefetch margin and keep its constructed presentation ready.~~
   Corridor occupancy is also tracked so the floor and both endpoint doors cannot disappear
   around the player when a door closes.
4. Cull remote characters, NPCs, props, nameplates, and effects by visible room set.
5. Primitive object pooling, cancellation, and frame-budgeted construction are active;
   extend them to complete modular room-kit presentations during the visual pass.

**Acceptance:** Closed doors occlude rooms and entities; opening a door reveals the next
space without a large frame spike.

### Phase 3 — Entrances, Exits, and Instance Lifecycle

- Replace `.worldenter` and `.worldexit` with usable server-owned entrance/exit objects.
- Create instances from accepted missions and assign owner/team membership.
- Validate safe entrance and return positions.
- Add completion, abandonment, timeout, cleanup, and reconnect rules.
- Persist manifests, membership, return locations, and mutable overlays.

### Phase 4 — Encounters and Navigation

- Generate walkable cells/portal navigation in the shared core.
- Spawn server-owned encounters from deterministic encounter slots.
- Add room activation, leash boundaries, boss rooms, and encounter completion.
- Keep encounter randomness in a named stream independent of layout generation.

### Phase 5 — Objectives, Loot, and Persistence

- Mission objectives, keys, locked paths, traps, chests, and boss rewards.
- Server-generated loot with stable container identities.
- Persist only the immutable manifest plus mutable overlay/deltas.
- Recovery tests for restart, reconnect, team changes, and expired instances.

### Phase 6 — Production Presentation (active)

- ~~First-pass deterministic turning layouts, role palettes, panel material, modular
  primitive detailing, ceilings, and practical room lighting.~~
- ~~Add wide, long, chamber, and true L-shaped shared room footprints; close doorway
  surrounds and ensure accent trim never crosses a portal.~~
- ~~Add manifest-driven layout profiles, visual themes, and procedural/RDB/hybrid
  provider selection with a safe fallback.~~ Implement the concrete RDB mapping adapter next.
- ~~Add the first profile-specific geometry kit: subway line topology, station
  platforms, concourses, service rooms, track tunnels, route panels, ribs, and lighting.~~
- ~~Add authoritative subway elevation bands and shared stair construction, descending
  from the entrance through station and deep terminal levels; add a stopped train landmark.~~
- ~~Add semantic module contracts for authored custom/RDB kits: entrances, combat rooms,
  junctions, service rooms, stations, train chambers, terminals, and boss arenas.~~
- ~~Add a catalog-backed Unity prefab provider and the first coherent authored subway
  set: recessed track terrain, curved tunnel shells, fitted utilities, enclosed stairs,
  transit doors, and a bent critical route.~~
- ~~Decouple visibility culling from collision so hidden/prefetched rooms never disable
  their floors; add a seam-tolerant grounded probe shared by movement and animation.~~
- ~~Replace subway room chaining with hub-and-hall composition: fewer progression hubs,
  long wide main halls, narrower service passages, and clustered side-room wings.~~
- ~~Add an original WoW-inspired group-dungeon shell: main route with a bend,
  optional wings, wider vaulted halls, landmark objective and finale chambers, and
  a keep-themed modular presentation kit.~~ Encounters, loot, party tuning, and
  authored keep prefabs remain future slices.
- ~~Add a separate cave-based leveling-dungeon variant with S-shifted route,
  variable-width/offset tunnels, rock/earth presentation, and cave chambers.~~
  True curved cave meshes and matching server collision remain future authored work.
- ~~Add engine-neutral presentation records to Core and include them in layout hashing
  and authoritative collision.~~
- ~~Create `WorldGen.Geometry` and prove the shared constructive pipeline with modern
  toilet and sink recipes rendered by the Unity adapter.~~
- **Next:** replace generic fixture records with explicit semantic fixture kinds and
  finish the restroom recipe (stall hardware, mirrors, counters, drains, flush controls,
  plumbing covers, UV/material tuning, and verified wall-facing orientation).
- **Next:** migrate the remaining Unity-authored subway pieces into Core presentation/
  constructive recipes in this order: entrance door and frame; restroom tile surfaces;
  benches/signage; train exterior; track/tunnel trim; ceiling and doorway systems.
- **Next:** define engine-neutral surface recipes for tile size, grout width, pattern,
  orientation, trim layers, roughness class, and color role. Clients still resolve these
  roles to engine-native textures and shaders.
- **Next:** add a minimal second adapter or headless exporter proving that the same Core
  recipes and Geometry buffers can be consumed without Unity; Godot is the smallest
  likely validation target before an Unreal adapter.
- Authored modular room kits, non-rectangular room footprints, door styles, props,
  decals, richer lighting, and audio zones.
- Async construction, pooling, LODs, occlusion, and strict frame budgets.
- Catalog hashes, capability negotiation, content-pack validation, and safe failure UI.

### Phase 7 — Beyond AO Missions

- Buildings and settlements using the same graph and stable-ID contracts.
- Chunked outdoor regions, terrain, roads, vegetation, and resources.
- Persistent custom worlds that no longer depend on AO playfield or RDB content.

## Automated Testing Still Needed

- Golden-layout fixtures for every released generator version.
- Cross-runtime hash tests between Linux server and Unity/Mono/IL2CPP.
- Property/fuzz tests for connectivity, portal bounds, overlap, and safe spawns.
- Server movement tests proving walls and closed doors cannot be bypassed.
- Door concurrency and stale-revision tests.
- Capsule traversal and wall-slide regressions for procedural geometry.
- Reconnect and server-restart recovery tests.
- Two-client instance membership and state-broadcast tests.
- Client frame-time, allocation, pooling, and cancellation tests.
- Long-running instance creation/retirement leak tests.

## Current Developer Commands

```text
.worldgen <seed> <room-count> [instance-id] [profile] [theme] [asset-source]
.worldenter <instance-id>
.worldexit
.worlddoor <index> <open|closed|sealed>
.worlddoor <index> locked <key-item-id>
.worldgen layout
```

Example:

```text
.worldgen 90123 12 playfield-test-1
.worldgen 90201 18 subway-test subway subway procedural
.worldgen 90211 24 subway-geometry-test subway subway procedural
.worldgen 90202 18 temple-test temple temple procedural
.worldgen 90203 24 alien-raid-test raid alien procedural
.worldgen 90301 24 keep-group-test groupdungeon keep procedural
.worldgen 90311 24 cave-group-test cavedungeon cavern procedural
.worldgen 90602 30 subway-geometry-v22 subway subway procedural
.worldgen layout
.worldenter subway-geometry-v22
.worldenter playfield-test-1
.worlddoor 0 closed
.worlddoor 0 open
.worlddoor 0 locked 21601
.worldexit
```

These commands are development tools and are not the intended player-facing workflow.
