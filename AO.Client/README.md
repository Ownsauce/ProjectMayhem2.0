# AO.Client

`AO.Client` is the backend-neutral boundary between Unity presentation and an
AO server. `IGameServerBackend` defines the first vertical slice: connect,
authenticate, list characters, select a character, and enter the world.

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
