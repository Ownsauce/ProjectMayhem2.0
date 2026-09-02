# Tests

Prioritize fixtures that are synthetic or otherwise redistributable. Do not
commit extracted AO data. Initial coverage should target cache keys and
invalidation, coordinate conversion, packet framing, decoder error handling,
and resolver fallback behavior.

`AO.Client.DimensionDiscovery.Tests.cs` is a dependency-free executable test
for `DimensionServer.url` and `dimensions_v3.txt` parsing. It deliberately uses
synthetic fixture text rather than contacting or embedding data from a server.

`AO.Client.AORebirth.LoginProbe.cs` is a manual local integration probe. It
accepts an ignored two-line credentials file and optional character ID,
authenticates against AORebirth Local, prints only returned character metadata,
and verifies selection through zone compression negotiation. It never prints
credentials. It then decompresses the initial zone stream and verifies the
authoritative playfield and position bootstrap.
