# AO.Assets

This package reads configuration for a user's local AO installation and owns
the contracts for decoding, conversion, and persistent versioned caching.
Never place extracted AO content in this directory.

Expected boundaries:

- `ResourceDatabase/` — installation discovery, validation, and indexed access
- `Decoders/` — proprietary resource decoding into neutral in-memory models
- `Conversion/` — conversion into Unity-loadable or cache formats
- `Cache/` — keys, manifests, invalidation, atomic writes, and deduplication
- `Resolution/` — high-level mesh, texture, animation, and playfield resolver

## Implemented foundation

- `AOInstallLocator` validates the executable, `version.id`, and the actual
  `cd_image/data/db/ResourceDatabase.dat`, and probes common Windows/Wine locations.
- `IAOAssetResolver` is the runtime boundary used by presentation code.
- `IAOAssetConverter` makes conversion backends replaceable. An AOGLTF process
  adapter can be used first without making it the permanent architecture.
- `AOInstallAssetResolver` deduplicates concurrent requests, converts cache
  misses away from the caller, and publishes completed files atomically.
- `AOAssetCache` namespaces generated files by AO version and converter version.
- `AOResourceDatabase` independently reads `ResourceDatabase.idx`, indexes
  resource identities, opens split `.dat` segments lazily, and retrieves raw
  records without AO's Windows database DLL or an external exporter.
- `AOResourceCatalog` decodes the database's resource-name metadata in process,
  supports typed ID/name lookup and search, and defines the known resource types
  used by the first mesh, texture, animation, and playfield decoders.
- `AOCompositeAssetResolver` allows ordered sources. Put an
  `AODirectoryAssetResolver` first for user-created replacement assets, followed
  by the AO-install resolver and its persistent converted cache.
- Unity stores the chosen path locally through `AOInstallConfiguration`; it may
  also be supplied initially with the `PROJECTMAYHEM_AO_INSTALL` environment
  variable.

Generated content belongs under Unity's `Application.persistentDataPath`, not
in this package, `StreamingAssets`, or a release build.

## Converter direction

AOGLTF is a useful first adapter and validation oracle. It may have startup and
whole-export costs, so calls should be asynchronous, deduplicated, and cached.
The preferred long-term backend is indexed access to `rdb.db` with targeted
decoders for only the requested resource. Both backends implement the same
`IAOAssetConverter` contract.

The direct reader has been exercised against client version `18.8.62_EP1`: it
indexed 460,193 records and retrieved the known resource-name metadata record.
Format-specific mesh, texture, animation, and playfield decoders remain layered
above this raw-record boundary.

The initial override directory convention is `<root>/<kind>/<resourceId>.<ext>`,
for example `mesh/1234.glb` or `texture/4567.png`. A versioned manifest can be
added later without changing the resolver contract.
