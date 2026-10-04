# Lost Eden equipment visuals review

Reviewed 2026-09-09 against Lost Eden commit `4865d41`. This is a source review; it does not establish that every item or live equipment transition works in Lost Eden.

## Recommendation

Adopt Lost Eden's server appearance snapshots, skin/armor compositing, and model-authored attachment sockets in our existing runtime. Keep Core equipment/stat validation separate from rendering. Our direct CAT/ABIFF asset support already supplies much of the foundation, so replacing the whole renderer is unnecessary.

| Concern | Lost Eden | Project Mayhem | Recommended change |
| --- | --- | --- | --- |
| Appearance source | Full character updates and appearance updates supply texture and mesh arrays. | AORebirth's full-update parser returns after visible title; equipment visuals resolve local equipped item definitions, stats, catalogs, and fallbacks. | Decode appearance arrays and update messages into backend-neutral appearance data for local and remote characters. |
| Body armor | Composites armor over breed/sex/race skin with AO green-key transparency. | CharacterAppearanceController applies the equipment texture directly to body materials. A TextureCompositor exists but has no callers in Assets. | Connect compositing to body appearance; cache results by skin and armor identity. |
| Weapons and attachments | Loads ABIFF meshes by server ID, uses texture overrides, and parents to CAT attractors. | Already loads direct RDB ABIFF meshes, but retains many manual offsets and name/catalog fallbacks. CAT runtime already creates attractor transforms. | Use authored socket transforms where available; retain offsets only for assets lacking sockets. |
| Visibility and layering | Uses visual flags for helmet and shoulders; chooses the highest head mesh layer. | Equipment-slot resolution is the primary attachment path. | Preserve server position, layer, override texture, and visibility flags through rendering. |
| Build cost | Shares CAT prototypes, deduplicates in-flight builds, and caches baked body textures. | Existing equipment signature avoids rebuilding unchanged equipment. | Retain incremental updates; introduce bounded cache ownership instead of blindly copying the static bake cache. |

## Source map

- `LostEden/Lost-Eden/Assets/Scripts/Dynel/Character.cs`: `Apply(SimpleCharFullUpdateMessage)` and `Apply(AppearanceUpdateMessage)` store appearance arrays and mark the visual stale.
- `LostEden/Lost-Eden/Assets/Scripts/Dynel/VisualDynel.cs`: `StoreTextures`, `StoreMeshes`, `CollectBodyTextureJobs`, `ApplyBodySlotTexturesAsync`, `ApplyAttachedMeshes`, `ShouldShowMesh`, and `AttachMesh` implement composition and attachments.
- `LostEden/Lost-Eden/Assets/Scripts/Rendering/CatMesh/CatMeshLoader.cs`: prototype caching and in-flight build coordination.
- `AO.Client/Backends/AORebirth/AORebirthProtocol.cs`: `TryReadSimpleCharFullUpdate` currently does not retain texture/mesh arrays.
- `AO.Unity/Assets/Scripts/AO.Unity/World/CharacterAppearanceController.cs`: body material equipment texture application around lines 1884–1912.
- `AO.Unity/Assets/Scripts/AO.Unity/World/EquippedItemVisualController.cs`: `BuildDesiredStates`, `TryCreateDirectRdbVisual`, and `ApplyLocalTransform`.
- `AO.Unity/Assets/Scripts/AO.Unity/World/DirectCatMeshRuntime.cs`: creates `AOAttractor_` transforms from CAT model data.
- `AO.Unity/Assets/Scripts/AO.Unity/World/DirectOutdoor/TextureCompositor.cs`: existing green-key compositor ready for integration.

## Integration order and validation

1. Extend AORebirth appearance decoding and backend-neutral snapshots. Test variable-length records, truncated packets, texture overrides, and appearance updates for existing entities.
2. Make authoritative appearance snapshots drive local and remote visual rendering. Retain item-definition resolution for offline previews or missing server appearance data.
3. Integrate armor/skin compositing and socket-based attachments. Verify equip/unequip, dual wield, helmets, both shoulders, back items, breed/sex variants, zoning, and remote appearance changes in Unity.
4. Validate server-driven social appearance; head-layer selection alone does not prove complete social-tab handling.

The rendering approach does not depend on upgrading Unity. Lost Eden uses HDRP while this project uses URP, so shader/material configuration must remain appropriate to our pipeline.

## Unity version review

The project starts on `6000.3.8f1`; Lost Eden specifies `6000.6.0f1` (`f7f8ed4d1e24`). Unity's official release page dates 6000.6.0f1 to August 31, 2026. It is the newest production release verified in this review; newer preview versions are a separate channel.

- [Unity 6000.6.0f1 release notes](https://unity.com/releases/editor/whats-new/6000.6.0f1)
- [Unity 6.6 upgrade guide](https://docs.unity3d.com/6000.6/Documentation/Manual/UpgradeGuideUnity66.html)

Upgrade validation is pending editor installation and a batch import/compile. Updating ProjectVersion.txt alone would not establish compatibility.
