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
