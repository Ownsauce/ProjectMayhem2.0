# Gameplay capture compatibility — October 12, 2026

The stock client session `raw-20261013-012807-pid816` recorded 371 packets from
01:28:07 to 01:29:42 UTC, October 13 (October 12 locally), on ICC Shuttleport,
playfield 4582. Recording began after login and ended at logout. The separate
Wine raw sink contains the evidence; the comprehensive session's decoded logs
are mostly empty. Original files remain in AORebirth-Contribute under
`.local/linux-dev/ao-capture/bin/captures/`.

The new session covers eight equipment acknowledgements and appearance updates,
twelve regular/special hits, three misses, four special lock/unlock updates,
three cast announcements with completion acknowledgements, three nano-duration
updates, two explicit removals, movement/turning requests, and the caster's
Pronouncement of Greatness transformation. The leet buff remains active at logout;
this session does not demonstrate canceling that buff or initial world loading.

## Changes

- Nano cast and cancel requests now include NanoProgram identity type 53019 in
  Parameter1. Self casts support the captured implicit target `(0, 0)` and the
  explicit caster target seen in earlier live captures. Other recipients retain
  their actual identity.
- Cast announcements (`25314D6D`), completion (`CharacterAction 6B`), and
  interruption (`6C`) reach Unity as lifecycle events. Cast start/finish can play
  the installed nano animation semantics; the UI reports server cast status.
  Buff duration and removal remain separate server-authoritative updates.
- MonsterData (stat 359) and MonsterScale (360) now produce appearance deltas.
  The captured caster update selects MonsterData **17655**. The renderer resolves
  the body and animation set from the installed resource database, rather than
  inferring a model from a nano name.
- Controlled and nearby players switch to the server's monster body, preserve
  their equipment state, and restore the humanoid body when MonsterData clears.
  Humanoid body textures, head/armor/weapon attachments, and breed scaling are
  suppressed for the morphed player. Async remote loads reject obsolete body
  requests and reload the newest model. Session cleanup clears the local morph.

Movement, equipment acknowledgements, attack state, ranged/melee hits and misses,
special requests, cooldowns, nano durations, and NCU removals already had client
handlers. Capture replays exercise those paths alongside the new behavior.

## Verification and limits

`tests/Fixtures/Captures/` contains selected N3 gameplay bodies from all four
local raw sessions: October 7 at 16:25:55, 18:11:36, and 18:57:18 UTC, and this
session. Fixtures retain direction and original packet ordinal; transport
headers and unrelated packet types are omitted. They contain no login exchange.
The source `raw-packets.csv` remains intact in AORebirth-Contribute.

`AO.Client.WorldDelta.Tests` checks actual outbound special/cast/cancel bodies,
decodes captured appearances, combat, buffs and cast lifecycle, and replays the
new session through the backend. Equipment replay starts from explicitly
synthetic item identities in the necessary occupied slots because initial
inventory was not captured. All eight acknowledged transitions survive stat
updates. Tests also verify morph preservation across movement/equipment/damage,
NCU durations/removal, and Unity event delivery. Morph clearing is a synthetic
server stat regression, not a captured cancellation.

`AO.Client.MovementActions.Tests` covers all 81 direction transitions.
`AO.Client.GameplayMetadata.Tests` checks installed nano icons/NCU/effects, weapon
animation metadata, and resolution of MonsterData 17655 to a CAT body and
animation set. The Unity runtime assembly is compiled with its generated
project. These checks do not establish visual correctness in a running scene;
the next client playtest should exercise moving as a leet, canceling the morph,
switching ranged/melee weapons, using specials, and applying/removing buffs.

## Follow-up: self targeting, relog NCU, and combat presentation

The self nameplate used the first child renderer to calculate its height. If that
renderer was the plate itself, each refresh included the previous plate position
and raised it again. It now uses the character body bounds, excludes text and VFX
renderers from fallback measurements, and positions the plate in world space.

The full-character spawn decoder previously skipped all twenty-byte running-nano
entries. It now reads NanoProgram identity, active instance, and remaining timers.
The selected character's list is delivered as an authoritative NCU snapshot,
separate from later duration/removal events. The UI clears stale entries and
restores the returned programs with their remaining time. An empty snapshot clears
NCU; another player's list cannot replace the local list. Regression tests cover
initial restoration, reduced time on relog, empty state, identity filtering,
metadata preservation, and one-time delivery. These login packets are generated
from the verified AORebirth SCFU layout; the October 12 capture began after login.

Native combat had bypassed the routine responsible for attack/recharge bars.
A separate presentation timer now updates those bars, pauses ranged progress on
movement, and reconciles regular weapon recharge with server hit/miss messages.
It never generates damage or speculative local hit events. Native attack start
uses the resolved CAT weapon stance instead of a legacy animation-rule one-shot.

Animation refresh previously scheduled deferred idle playback every time, which
could overwrite an arriving attack on the following frame. Initialization now
preserves established playback, deferred commands yield to intervening commands,
and locomotion preserves active CAT one-shots. Nano semantic keys are accepted by
the normalizer as well as their legacy spell names. The metadata suite now runs
the actual production resolver for melee/ranged/unarmed stances and left/right
strikes across male, female, and atrox profiles, decodes the selected clips to
verify actual bone tracks, and resolves all three nano-cast semantic keys.

## Kill lifecycle and native corpse loot

The recording contains four fatal hits, four StopFight replies, four complete
CorpseFullUpdate messages, and four source NPC despawns: two Island Reets, a Shore
Snake, and a Surf Lizard. The fatal bit in AttackInfo was being discarded, so a
target could remain locally alive when estimated HP differed from the server.
Fatal hits now force zero health. Local attack intent and progress bars stop on
death or removal, without waiting for the selected target widget to refresh.
StopFight is associated with the previous server attack target so a delayed reply
for an old target cannot cancel a new target's attack.

CorpseFullUpdate previously produced only an identity with no position or model.
It now decodes position, heading, name, stats, source identity, animation effects,
and textures. The creature MonsterData comes from the ItemAnimEffect row (30365,
30252, and 22794 in this recording). Each corpse is a separate renderable,
selectable world entity with its own 51050 identity; the source NPC's despawn does
not remove it. NPC health now drives RuntimeDynelCombatState, CAT creatures play
their death clip and hold the final pose, and corpse colliders fit the low body.
Corpses present at world entry also render through this path.

Weapon animation sets now resolve from the hand that actually struck. Equipment
state is delivered before combat events in the same update batch. Leet morphs use
their own animation set and creature attacks, rather than humanoid weapon clips.
Remote NPCs also receive native attack animations. Installed-asset checks cover
the leet and all three captured corpse species and verify humanoid attack tracks
bind to the actual player skeletons.

The existing Remains window now supports the AORebirth backend: right-clicking a
native corpse sends GenericCmd Use against its corpse identity; returned corpse
inventory populates item icons, quantities and QL; clicking an item sends
ClientMoveItemToInventory with the returned container handle and the actual loot
slot. Player inventory changes still require the server's response. Closing the
window releases the corpse, and server corpse removal closes its window. The
recording does **not** include corpse-open or loot-take clicks, so those request
layouts are checked against AORebirth's native handlers rather than claimed as
captured evidence. Loopback transport tests cover opening and taking loot, sparse
slot addressing, and keeping corpse contents separate from player inventory.

The additional `tests/Fixtures/Combat/` slice retains 35 actual attack, death,
corpse, stop and despawn packet bodies from this session. Regression replay checks
all four lifecycles, fatal flags despite deliberately mismatched HP, complete
corpse decode and truncated-packet rejection, corpse retention after NPC despawn,
and stop-target association. Visible attack/death playback and the full server
loot response still require a running-client playtest.

## Follow-up: PF 3081 terrain and Fair Trade entrance

PF 3081 is Borealis Backyard 2, an outdoor map. The renderer loaded its native
258 scenery placements, but the bundled AODB zlib reader returned the backing
buffer capacity instead of the decompressed length. Its 81-byte height blocks
became 256-byte buffers, producing 16×16 height grids instead of 9×9. Its 64-tile
texture grids also acquired padding. Seven chunks consequently spanned 420 AO
units instead of 224, misaligning terrain with the unchanged native placements.
The resource wrapper now reads the exact compressed block lengths and rebuilds
height and texture grids, preserving the existing AODB plugin for other assets.
This also restores the raw CHGA map dimensions used to select the chunk grid.

For the Borealis 800 → Fair Trade 1186 transition, the editor recorded an
entry at AO `(175.001, 5.010, 113.015)` but resolved its height to `22.155`
before native presentation completed. Interior floor support is supplied by
that asynchronous loader; its colliders were still inactive when the spawn
probe ran. Native indoor transitions now wait for presentation readiness before
applying the pending destination and restoring movement. Movement packets are
also held while playfield geometry is loading. The same waiting path retains
instance identities for native playfield copies.

Installed-data regression checks compare every height sample in PFs 3081, 800,
and 4582 with hashes independently produced by AODB 1.0.12, and verify exact tile
grid counts. The Fair Trade check runs the actual indoor helper and floor-support
builder: the entrance floor is at AO Y **5.000**. Metadata/animation checks and
the Unity project build pass. No running Unity scene was visually inspected;
re-enter PF 3081 and the Fair Trade door to confirm the visible results.

The optional installed Fair Trade check uses:

```bash
dotnet run --project tests/AO.Client.GameplayMetadata.Tests.csproj -- \
  "/home/cody/AOClientCopy/Anarchy Online" \
  "$PWD/AO.Unity/Assets/StreamingAssets/Tools/AOIndoorVisualExtractor.exe"
```

### Fair Trade movement pause follow-up

The next playtest reached PF 1186's interior but left the character running in
place above the floor. The editor shows native presentation completion, a spawn
at Y 6.250 (the floor plus the configured safety offset), and loading readiness.
The movement pause was applied to the controlled actor by the server session,
but completion resumed a separately resolved scene bridge. Completion now enables
the walker on the actual transition character, and the pending-spawn path pauses
that same walker explicitly. Teleports now use the walker's authoritative teleport
method, resetting CharacterController contact, prediction, jump, and velocity state.
The Unity build passes with zero errors; the movement result still needs a live
client check.

## Follow-up: backyard exits, world collision, and shop contents

The October 13 playtest logs contain repeated errors reading triangles from
non-readable `Statel_*` meshes. ABIFF creation discarded CPU mesh data before
playfield finalization attempted to create colliders. Static scenery now retains
readable mesh data and gets MeshColliders during instantiation, including indoor
props. Finalization counts indices through `GetIndexCount`, avoiding triangle-array
reads on non-readable meshes. Native door dynels remain non-solid walk-in objects;
AORebirth's placed-door trigger volumes handle their transitions.

Outdoor zone handoff previously set the network world's origin offset before
asynchronous terrain loading recentered the playfield root. Later movement then
used centered Unity coordinates as AO coordinates, missing native server portal
volumes. A handoff now waits for the final mapping, updates entity smoothing targets
along with the placeholder root, and holds movement packets through that update.

PF 1186's scenery `.pf` placements do not include its interactive objects. The
separate native resource 1000026 contains **73 placements**: 63 shop terminals,
seven doors, two surgery clinics, and one bank. The shared renderer now reads their
world positions and W/X/Y/Z headings, resolves each template's Mesh stat from the
installed database, and builds its original ABIFF geometry. Partial procedural
room selections do not receive this whole-playfield placement set.

The log also confirms the server sent the Bartender and the client decoded and
loaded MonsterData 26137. Late entity updates used relative positions as world
positions without the placeholder root offset, placing indoor NPCs below the
floor. World positioning now includes the offset for spawns, movement targets,
and corrections. Recreating the placeholder world resets the prior zone's offset.

Installed-data tests verify all 73 PF 1186 placements, both clinics and the bank,
the first placement's original quaternion, and every fixture's actual mesh.
Outdoor sample and existing metadata/animation regressions pass, as do AO.Assets
checks and the Unity build (zero errors). Runtime collision cooking, backyard
walk-in exits, and visible NPC placement still require a live Unity playtest.
Shop purchase UI and native door animation/state are not validated by these checks.
