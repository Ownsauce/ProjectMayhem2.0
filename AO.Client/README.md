# AO.Client

`AO.Client` is the backend-neutral boundary between Unity presentation and an
AO server. `IGameServerBackend` defines the first vertical slice: connect,
authenticate, list characters, select a character, and enter the world.

Character creation and deletion use `CharacterManagement` and the shared
`CharacterCreationRequest`. The AORebirth adapter sends native login-server
requests and updates its roster only after a valid server acknowledgement.
Creation retains the returned server ID; deletion requires an ID from the
authenticated account and a matching deletion acknowledgement. Unavailable
names leave the session usable. Interrupted or malformed replies require a
fresh sign-in to reconcile the roster rather than allowing a late reply to
complete another operation.

The login character list includes profession, breed, gender, and playfield,
but does not supply a full appearance snapshot. Unity resolves zone names from
the installation when the server supplies no useful area name. Head/body
previews use appearance cached after world entry, matched by server, account,
and character ID. Characters without that cache use a default breed/gender
preview; current equipped appearance cannot be guaranteed from the login list.

Backend wire types belong under `Backends/<backend-name>` and must be translated
to the shared models before reaching Unity. Planned adapters include the live
service, Ithaca, AORebirth, and the existing Project Mayhem TCP/JSON test server.

`Discovery/DimensionServerUrl` reads the launcher's configured dimension-list
location. `Discovery/DimensionListParser` parses its `STARTINFO` records into
shared dimension definitions and TCP server endpoints. Fetching the dimension
list is transport work and remains separate from parsing so it can be tested
without network access.

When given an AO installation, `DimensionServerLocator` prefers
`cd_image/data/launcher/DimensionServer.url`. This is the user-managed combined
catalog (currently `192.168.8.116:8085/dimensions_v3.txt`) and can advertise
AORebirth, Ithaca, and additional private servers. The root
`DimensionServer.url` is only a fallback because server-specific patchers may
replace it with their own catalog.

Authentication secrets must remain in memory only. Adapters must not log them,
serialize them into settings, or include them in diagnostic payloads.

`Backends/AORebirth/AORebirthBackend` implements connection, AO login-key
authentication, character-list decoding, character selection, login-to-zone
redirection, zone login, and initial compression-negotiation detection. World
bootstrap handling then opens the zlib stream and translates the initial
`PlayfieldAnarchyF` packet into backend-neutral character position and
playfield state. It also decodes `SimpleCharFullUpdate` zone packets into
backend-neutral nearby player/NPC records containing identity, name, level,
health, playfield, and position. AO's `IsPet` bit is not used as authoritative
pet ownership because captured server packets also set it on ordinary entities.
The adapter maintains an identity-keyed snapshot and emits bounded delta batches
for full-update upserts, `CharDCMove`/`DropDynel` position changes, `Stat`
life/health changes, and `ToClientQuit` visibility removals.
Static world state currently includes vending machines with names, playfields,
direct transforms, or transforms resolved through their linked NPC dynels.
Door and corpse full updates are retained safely by identity while their richer
capture-dependent layouts remain backend-specific work.

The backend exposes `CharacterLimit` from the login roster response. It is unknown
before authentication and cleared on disconnect. Character creation is blocked
locally once the acknowledged roster reaches that account/server limit.

Connection diagnostics expose the active login/zone endpoint, successfully sent
and received packet counts, and the UTC time of the last complete received packet.
Counts reset for a new connection and include authentication and zone traffic.
The AORebirth adapter currently does not expose a server tick; UI diagnostics show
that value as unavailable rather than reuse the legacy JSON client tick.

Native item use sends GenericCmd Use for carried/worn slots or a packed backpack
handle/slot address. Rechargers, stims, nano crystals, and backpack opening use
this path. Effects and consumption remain server-owned. UploadNano replies update
the acknowledged program list, and container inventory updates are delivered
separately from the main inventory. Known backpack open replies republish cached
contents. Backpack handles are cleared when the world/session changes. Corpse
transfer acknowledgements apply confirmed packed handle/slot moves to main
inventory and publish both changed snapshots immediately. General backpack
item moves still require a separate transfer implementation.

Vendor stock and acknowledged selections are exposed through `ShopSnapshot`.
Native shop intents select stock and confirm or decline the purchase; accepted
`AddTemplate` grants fill the first free inventory slot, with credit changes
supplied by authoritative stats. Unity matches placed terminal visuals to the
server-advertised runtime machine and opens NPC-owned shops through the NPC.
See [protocol details](../docs/AORebirth.Protocol.md) and [tests](../tests/README.md).

Item use now correlates GenericCmd acknowledgements and reports rejections/timeouts.
Nano casting sends CharacterAction CastNano; SetNanoDuration and Buff removal
messages drive active NCU effects. Unity counts their NCU costs (excluding hostile
and no-NCU effects) and floors capacity at eight. Server MaxNCU is treated as a
total; interpolated OnWear belt/memory bonuses provide a fallback without being
added to that total again.
Unity synchronizes sit/stand movement states before using recharge items.

Native Q combat sends Attack/StopFight. Attack and StopFight replies reconcile
animation state; AttackInfo/MissedAttackInfo provide damage and miss events.
Weapon hits update nearby and controlled health immediately, without waiting
for a separate stat packet. The client does not calculate native weapon damage.
Explicit zero current HP remains a death even when maximum HP was omitted;
movement and appearance updates preserve that state for immediate combat shutdown.

World updates wait for the first packet, then drain only packets already queued
within a four-millisecond processing budget. There is no trailing aggregation
wait, and login/zone sockets use TCP_NODELAY. Server item EquipDelay remains in
effect (the installed starter pistol is 100 centiseconds, Blackmane's belt 200).
The reference copy's Connection.dll exposes Connection_t::Send,
TcpHandler_t::Send_i/FlushWaitBuffer and ACE receive/send; N3.dll exposes native
network-message dispatch. Packet fields were checked against the AORebirth
message contracts. Binary exports alone do not establish original-client latency.

Uploaded nano metadata comes from resource type 1040005, separately from carried
item records (1000020). NCU usage uses authoritative CurrentNCU and confirmed
running effects; the template's stat 54 supplies the native NCU cost. Nano OnUse
functions are decoded for tooltips, without applying their modifiers locally.
The same icon payload reader handles uploaded and running program icons.

Native specials use CharSecSpecAttack (target and skill ID). SpecialAttackInfo
and misses retain the special and hand slot; SpecialUsed (0xAA) / SpecialAvailable
(0xA4) drive cooldowns. Unity exposes equipped specials as clickable buttons with
countdowns, and routes the ranged/melee special hotkeys through this protocol.
Direct CAT models select weapon stances and play installed attack/special clips
from server hits. Per-frame locomotion does not cancel these combat one-shots.

Equipment snapshots carry an equipment revision through subsequent stat/upload/
combat snapshots. Unity consumes that revision even when the last packet in a
batch is stats-only, so acknowledged unequips and replacements clear worn icons.

Unflagged personal buffs default to the caster with an NPC selected; selected
players and explicitly supported friendly NPC/pet targets remain valid recipients. Self-only and hostile nanos retain their respective
rules. NCU right-click sends RemoveFriendlyNano (0x41) and waits for the server's
Buff removal before removing the running entry. It no longer clears NCU locally
while leaving the buff active on the server.
