# AO indoor surface extractor

AO indoor `SurfaceResource` records are materialized by the 32-bit client
`DatabaseController.dll`; their on-disk payload is not the in-memory mesh
layout consumed by AOSharp. This helper runs out of process, loads the DLLs
from a user-selected AO installation, and writes a compact `AOIS` triangle
stream containing both room `SurfaceResource` meshes and the dungeon
tilemap's height/collision arrays. This matches the two geometry sources used
by SharpNav's `TerrainData.GetTriGeometry`. It never reads an
AOSharp/AOModelViewer cache.

Build on Linux for Windows/Wine:

```bash
i686-w64-mingw32-g++ -std=c++17 -O2 -static -static-libgcc -static-libstdc++ \
  AOIndoorExtractor.cpp -o AOIndoorExtractor.exe
```

The managed AO.Assets layer validates and consumes the resulting stream. Its
triangles are the source geometry for the SharpNav dungeon bake and for Unity
render meshes.
