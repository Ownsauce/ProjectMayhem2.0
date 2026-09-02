# AO Install Asset Resolution

## Purpose

Project Mayhem should use a hybrid, on-demand asset pipeline. Instead of
redistributing a complete export of Anarchy Online assets, each user supplies
their own legitimate AO installation. Project Mayhem resolves requested assets
from that installation, converts them locally, and stores the result in a
user-local cache.

This approach keeps AO-owned presentation assets out of the repository and
release packages while preserving predictable runtime loading performance after
an asset has been converted once.

## Asset Lookup Flow

```text
Project Mayhem requests an asset
        |
        v
Check the user-local converted cache
        |
        +-- Found ------> Load the converted asset
        |
        +-- Missing ----> Read it from the configured AO installation
                                |
                                v
                         Convert and cache it
                                |
                                +-- Success --> Load the converted asset
                                |
                                +-- Failure --> Use a Project Mayhem placeholder
```

The current `StreamingAssets/AOData` files can remain available during the
migration as an optional development or compatibility fallback. They should not
be the long-term source of redistributed AO-owned visual assets.

## Ownership Boundaries

### Locally resolved from the AO installation

- Character, creature, item, and world meshes
- Textures and icons
- Animations
- Sounds and music, when supported
- Static playfield resources
- Other presentation data stored in AO resource databases

### Maintained by Project Mayhem

- Mob placement and respawn rules
- NPC behavior and AI
- Combat and gameplay rules
- Quest reconstruction and scripting
- Server state and persistence
- Corrections, overrides, and asset-ID mappings created for Project Mayhem
- Original placeholder assets

An installed AO client does not necessarily contain the authoritative server
rules needed to reconstruct spawns, AI, quests, or live world state. Those
systems must remain Project Mayhem or server responsibilities even when their
visual assets are resolved from AO locally.

## Proposed Runtime Interface

Unity systems should request assets through a resolver rather than constructing
paths directly into `StreamingAssets`.

```csharp
public interface IAOAssetResolver
{
    Task<string> ResolveMeshAsync(int resourceId);
    Task<string> ResolveTextureAsync(int resourceId);
    Task<string> ResolveAnimationAsync(int resourceId);
}
```

The initial implementation may return converted file paths so the existing GLB
and texture-loading code can remain mostly unchanged. The interface can later
return richer result objects containing asset type, provenance, diagnostics,
and fallback information.

The resolver should be responsible for:

1. Checking the converted cache.
2. Locating and validating the configured AO installation.
3. Reading the requested AO resource through the parser/extraction layer.
4. Converting the resource into a Unity-compatible format.
5. Writing it atomically into the cache.
6. Returning a placeholder or a clear error when resolution fails.

## Cache Design

Generated AO content should live outside the repository and application install
folder. Unity's `Application.persistentDataPath` is an appropriate base:

```text
Application.persistentDataPath/
└── AOAssetCache/
    ├── meshes/
    ├── textures/
    ├── animations/
    ├── audio/
    └── manifests/
```

Cache identities should include enough information to invalidate stale output:

- AO resource type and resource ID
- Converter/parser version
- Output format version
- Source AO database fingerprint or version
- Relevant conversion settings

Conversion should write to a temporary file and rename it only after successful
completion so interrupted conversions cannot leave apparently valid cache
entries. Concurrent requests for the same resource should share one conversion
operation.

## Configuration

Project Mayhem should expose an AO installation-path setting with:

- Automatic discovery of common install locations
- Manual folder selection
- Validation that required AO resource databases exist
- A visible validation result and detected client version
- An option to clear or rebuild the generated cache

The path must not be hard-coded or committed to source control. A build should
remain operable with placeholders when AO is unavailable, unless a particular
release deliberately requires an AO installation.

## Migration Strategy

1. Introduce `IAOAssetResolver` without changing current visual behavior.
2. Implement a `StreamingAssets` resolver around the existing exported files.
3. Add an AO-install resolver backed by the existing extraction/parser tooling.
4. Add the persistent converted-cache layer in front of both sources.
5. Migrate character and item meshes first because their current GLB loading
   paths already provide a useful integration boundary.
6. Migrate textures, animations, static world objects, and playfield assets in
   small, independently testable stages.
7. Replace committed AO-derived assets with original placeholders after direct
   resolution has adequate coverage.
8. Add packaging checks that reject extracted or cached AO assets from releases.

During migration, the recommended priority order is:

```text
User-local converted cache
    -> configured AO installation
    -> optional development StreamingAssets export
    -> Project Mayhem placeholder
```

For production packages intended not to redistribute extracted AO assets, the
development `StreamingAssets` fallback should be disabled or verified to contain
only Project Mayhem-owned data.

## Performance and Reliability

On-demand conversion must not block Unity's main thread. Loading screens may
await required world assets, while optional or distant assets can stream in
incrementally. Common assets can be prewarmed after startup or character
selection.

Useful diagnostics include:

- Cache hit and miss counts
- Extraction and conversion duration
- Failed resource IDs and parser errors
- Placeholder usage
- Cache size and converter version
- AO installation validation state

The runtime should tolerate individual corrupt or unsupported resources without
failing an entire playfield load.

## Distribution and Legal Considerations

This architecture is intended to reduce redistribution risk by requiring users
to obtain AO content from their own installation and generating converted files
locally. Project Mayhem releases should not include extracted AO meshes,
textures, audio, animations, or other proprietary content.

Repository and build safeguards should include:

- Ignore rules for generated caches and extraction output
- Release validation that scans packages for prohibited generated content
- Clear documentation requiring a legitimately obtained AO installation
- Separation between Project Mayhem-authored data and AO-derived data
- No automatic upload or sharing of locally converted cache contents

This design is a technical risk-reduction measure and not legal advice. Project
maintainers should still review applicable licenses, terms, and interoperability
requirements before distribution.

## Recommended Direction

Adopt the hybrid resolver and local cache as the long-term asset architecture.
Keep gameplay authority with the connected live or private server, resolve
AO-owned presentation resources from the user's installation, and retain
original placeholders for missing or unsupported assets.
