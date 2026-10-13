# Tests

Prefer small deterministic fixtures that exercise one behavior at a time.
Initial coverage should target cache keys and invalidation, coordinate
conversion, packet framing, decoder error handling, and resolver fallback
behavior.

`AO.Client.DimensionDiscovery.Tests.cs` is a dependency-free executable test
for `DimensionServer.url` and `dimensions_v3.txt` parsing. It deliberately uses
synthetic fixture text rather than contacting or embedding data from a server.

`AO.Client.CharacterManagement.Tests.cs` runs against a loopback mock login
server. It checks creation packet fields, server-assigned IDs, name rejection,
account roster membership, delayed create/delete acknowledgements, mismatched
deletion IDs, and cancellation recovery. It never contacts a real game server
or changes real characters:

```sh
dotnet run --project tests/AO.Client.CharacterManagement.Tests.csproj
```

For Unity acceptance, check Enter from both credential fields (including empty
and rejected credentials), a steady loading logo with a moving indicator,
profession/zone details, arrow-key and button carousel navigation, and restoring
the selected preview after canceling creation. With a disposable test character,
verify creation and deletion survive a fresh sign-in and that a duplicate name
does not add a local entry. Check an existing character's head/body preview after
entering the world once and signing in again.

`AO.Client.AORebirth.LoginProbe.cs` is a manual local integration probe. It
accepts an ignored two-line credentials file and optional character ID,
authenticates against AORebirth Local, prints only returned character metadata,
and verifies selection through zone compression negotiation. It never prints
credentials. It then decompresses the initial zone stream and verifies the
authoritative playfield and position bootstrap.

`AO.Client.WorldDelta.Tests.cs` also checks native item-use wire layout, unsigned
packed backpack handles, container hydration/reopening, upload acknowledgements,
truncated/foreign replies, and rejection of empty slots before sending a request.
The item-use transport test uses only a local loopback socket.

The world delta suite checks native attack/stop layouts, hit/miss decoding,
truncation handling, health updates from AttackInfo without a stat packet, and
immediate isolated-packet delivery with cancellation preserving queued replies.

The world delta suite also checks native special requests, special-hit hand/skill
fields, SpecialUsed/Available cooldowns, truncated and foreign cooldown replies,
and delivery of cooldowns once.

`AO.Client.GameplayMetadata.Tests.csproj` is an installed-data integration check:

```sh
dotnet run --project tests/AO.Client.GameplayMetadata.Tests.csproj -- '/path/to/Anarchy Online'
```

It uses the production metadata/icon readers to verify nano identity, PNG icon
payloads, NCU cost, tooltip effects, equipped capacity bonuses and weapon attack
skills. It also checks the combat animation tokens against the installed catalog.
It does not connect to a server or require Unity. `PROJECTMAYHEM_AO_INSTALL` can
supply the installation path instead of the argument.

The installed-data suite also checks creature/weapon CAT animation selection,
every terrain height sample for PFs 3081, 800, and 4582, and all 73 native Fair
Trade placements and their mesh resources. The terrain hashes refer to the
validated local database version; changed game data may require revalidation.
An optional second argument runs the indoor helper to check PF 1186's entrance
floor using the production floor-support builder:

```sh
dotnet run --project tests/AO.Client.GameplayMetadata.Tests.csproj -- \
  '/path/to/Anarchy Online' \
  "$PWD/AO.Unity/Assets/StreamingAssets/Tools/AOIndoorVisualExtractor.exe"
```

The world-delta suite replays the selected N3 gameplay bodies in
`Fixtures/Captures/` and the combat/corpse lifecycle in `Fixtures/Combat/`.
It checks running-nano restoration, implicit/explicit self casts, morph updates,
fatal-hit attack termination, corpse persistence, and loopback corpse open/take
requests. Login restoration and loot transport cases include synthetic packets;
the raw captures contain no login exchange or corpse-loot clicks. See the
[capture review](../docs/review/captured-gameplay-20261012.md) for provenance.

World delta regression cases cover unequip/equip acknowledgements followed by
stats in the same batch, preserved equipment revisions, personal buffs with an
NPC selected, friendly player targets, hostile nano targeting, and native NCU
cancellation wire layout/transport. Installed-data tests verify Composite Utility
Expertise's four-NCU cost and targeting flags.
