# AO.Assets

`AO.Assets` is the engine-neutral local-resource layer. It discovers and
validates an AO installation, indexes its resource database, decodes records,
and manages persistent generated caches.

## Package boundaries

- `ResourceDatabase/` — installation discovery, validation, indexed raw access,
  and resource-name lookup
- `Decoders/` — conversion from database records or streams into neutral models
- `Conversion/` — focused conversion helpers, currently including indoor room
  surface extraction
- `Cache/` — versioned paths and atomic publication of generated results
- `Resolution/` — high-level resolution and ordered fallback sources
- `Navigation/` — indoor terrain and navigation generation
- `SharpNav/` — the navigation implementation used by `Navigation/`

## Current implementation

- `AOInstallLocator` checks the AO executable, `version.id`, and the paired
  `ResourceDatabase.dat`/`.idx` files. It probes common Windows and Wine paths.
- `AOResourceDatabase` indexes `ResourceDatabase.idx`, lazily opens database
  segments, and returns raw records.
- `AOResourceCatalog` reads resource-name metadata and supports typed ID/name
  lookup and search.
- `AOInstallAssetResolver` deduplicates concurrent conversion requests and
  publishes cache files atomically.
- `AOAssetCache` namespaces output by database fingerprint and converter
  version.
- `AOCompositeAssetResolver` supports ordered override, install, cache, and
  fallback sources.
- Unity persists the selected path through `AOInstallConfiguration`; the
  `PROJECTMAYHEM_AO_INSTALL` environment variable can seed it.

The Unity runtime also includes direct typed readers backed by the bundled
`AODB.dll` and `AODB.Common.dll`. Unity-facing code should snapshot AODB records
into Project Mayhem models before background processing or presentation.

Generated files belong under `Application.persistentDataPath/AOAssetCache`, not
inside this package.

## Direction

Direct, targeted database reads are the primary runtime path. Decode only the
resource required for the current playfield, character, item, animation, or
texture, then cache derived results when that materially improves load time.

AOGLTF and other exporters remain useful diagnostic references for comparing
geometry, transforms, UVs, and materials. They are not required runtime
dependencies and are not part of first-run setup.
