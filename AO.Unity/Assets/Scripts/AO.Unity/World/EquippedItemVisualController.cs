using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using AO.Unity.Prototype;
using UnityEngine;

namespace AO.Unity.World
{
    [RequireComponent(typeof(CharacterRuntimeBridge))]
    public sealed class EquippedItemVisualController : MonoBehaviour
    {
        private sealed class EquippedVisualState
        {
            public int SlotId;
            public int AOID;
            public string ItemName;
            public int StatId;
            public int TextureId;
            public string MeshKey;
            public string AnchorKey;
            public string CatalogMeshKey;
            public string ResolutionMode;
            public string ResolutionNotes;
            public bool IsMirroredShoulder;
            public GameObject Root;
            public object RuntimeImporter;
        }

        [Serializable]
        private sealed class VisualOverrideEntry
        {
            public int AOID;
            public string MeshKey;
            public string AnchorKey;
            public int? StatId;
        }

        [Serializable]
        private sealed class VisualOverrideFile
        {
            public List<VisualOverrideEntry> Overrides = new();
        }

        [Serializable]
        private sealed class EquippedVisualDiagnosticsFile
        {
            public List<EquippedVisualDiagnosticEntry> Entries = new();
        }

        [Serializable]
        private sealed class EquippedVisualDiagnosticEntry
        {
            public int AOID;
            public string ItemName;
            public int SlotId;
            public string AnchorKey;
            public int StatId;
            public string CatalogMeshKey;
            public string ResolvedMeshKey;
            public string ResolutionMode;
            public string ResolutionNotes;
            public string VisualSystem;
            public string MeshFamily;
            public bool ResolvedMeshInResources;
            public bool ResolvedMeshInStreamingAssets;
            public string LastSeenUtc;
            public int SeenCount;
        }

        [Serializable]
        private sealed class EquippedVisualReviewFile
        {
            public string GeneratedUtc;
            public List<EquippedVisualDiagnosticEntry> Unresolved = new();
            public List<EquippedVisualDiagnosticEntry> FallbackResolved = new();
            public List<VisualOverrideEntry> SuggestedOverrides = new();
        }

        private const int StatMesh = 12;
        private const int StatBackMesh = 38;
        private const int StatShoulderMesh = 39;
        private const int StatHeadMesh = 64;
        private const int StatWeaponMesh = 209;

        [SerializeField] private CharacterRuntimeBridge bridge;
        [SerializeField] private CharacterAppearanceController appearanceController;
        [SerializeField] private bool enableEquippedItemVisuals = true;
        [SerializeField] private string itemMeshResourcesFolder = "ItemMeshes";
        [SerializeField] private bool allowItemMeshGltfFallback = true;
        [SerializeField] private bool allowItemMeshEmbeddedDataUriGltf = false;
        [SerializeField] private bool sanitizeGlbDataUris = true;
        [SerializeField] private int rightHandEquipSlotId = 6;
        [SerializeField] private int leftHandEquipSlotId = 8;
        [SerializeField] private bool invertWeaponHandVisuals = false;
        [SerializeField] private int headEquipSlotId = 2;
        [SerializeField] private int backEquipSlotId = 3;
        [SerializeField] private int feetEquipSlotId = 14;
        [SerializeField] private int chestEquipSlotId = 5;
        [SerializeField] private int rightArmEquipSlotId = 7;
        [SerializeField] private int leftArmEquipSlotId = 9;
        [SerializeField] private int legsEquipSlotId = 11;
        [SerializeField] private int rightShoulderEquipSlotId = 4;
        [SerializeField] private int leftShoulderEquipSlotId = 6;
        [SerializeField] private string forceHeadBoneName = string.Empty;
        [SerializeField] private string forceBackBoneName = string.Empty;
        [SerializeField] private string forceFeetBoneName = string.Empty;
        [SerializeField] private string forceChestBoneName = string.Empty;
        [SerializeField] private string forceRightArmBoneName = string.Empty;
        [SerializeField] private string forceLeftArmBoneName = string.Empty;
        [SerializeField] private string forceLegsBoneName = string.Empty;
        [SerializeField] private string forceRightShoulderBoneName = string.Empty;
        [SerializeField] private string forceLeftShoulderBoneName = string.Empty;
        [SerializeField] private string forceRightHandBoneName = string.Empty;
        [SerializeField] private string forceLeftHandBoneName = string.Empty;
        [SerializeField] private bool logEquippedVisualResolution = false;
        [SerializeField] private Vector3 rightHandItemLocalPosition = new Vector3(0.075f, -0.02f, 0f);
        [SerializeField] private Vector3 rightHandItemLocalEuler = new Vector3(0f, 100f, -90f);
        [SerializeField] private Vector3 rightHandItemLocalScale = new Vector3(0.8f, 0.8f, 0.8f);
        [SerializeField] private Vector3 leftHandItemLocalPosition = new Vector3(0.075f, -0.02f, 0f);
        [SerializeField] private Vector3 leftHandItemLocalEuler = new Vector3(0f, 100f, 90f);
        [SerializeField] private Vector3 leftHandItemLocalScale = new Vector3(0.8f, 0.8f, 0.8f);
        [SerializeField] private Vector3 headItemLocalPosition = new Vector3(0f, 0.02f, 0.02f);
        [SerializeField] private Vector3 headItemLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 headItemLocalScale = Vector3.one;
        [SerializeField] private bool forceHeadTransformOverride = true;
        [SerializeField] private Vector3 forcedHeadItemLocalPosition = new Vector3(0.15f, 0f, 0f);
        [SerializeField] private Vector3 forcedHeadItemLocalEuler = new Vector3(90f, 90f, 0f);
        [SerializeField] private Vector3 forcedHeadItemLocalScale = Vector3.one;
        [SerializeField] private Vector3 backItemLocalPosition = new Vector3(0f, 0.05f, -0.08f);
        [SerializeField] private Vector3 backItemLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 backItemLocalScale = Vector3.one;
        [SerializeField] private Vector3 backArmorItemLocalPosition = new Vector3(0.2f, 0.16f, 0f);
        [SerializeField] private Vector3 backArmorItemLocalEuler = new Vector3(90f, 90f, 0f);
        [SerializeField] private Vector3 backArmorItemLocalScale = Vector3.one;
        [SerializeField] private Vector3 feetItemLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 feetItemLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 feetItemLocalScale = Vector3.one;
        [SerializeField] private Vector3 chestItemLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 chestItemLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 chestItemLocalScale = Vector3.one;
        [SerializeField] private Vector3 rightArmItemLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 rightArmItemLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 rightArmItemLocalScale = Vector3.one;
        [SerializeField] private Vector3 leftArmItemLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 leftArmItemLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 leftArmItemLocalScale = Vector3.one;
        [SerializeField] private Vector3 legsItemLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 legsItemLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 legsItemLocalScale = Vector3.one;
        [SerializeField] private Vector3 rightShoulderLocalPosition = new Vector3(0.15f, 0f, 0f);
        [SerializeField] private Vector3 rightShoulderLocalEuler = new Vector3(0f, 90f, 90f);
        [SerializeField] private Vector3 rightShoulderLocalScale = Vector3.one;
        [SerializeField] private Vector3 leftShoulderLocalPosition = new Vector3(0.15f, 0f, 0f);
        [SerializeField] private Vector3 leftShoulderLocalEuler = new Vector3(0f, 90f, 270f);
        [SerializeField] private Vector3 leftShoulderLocalScale = Vector3.one;
        [SerializeField] private bool mirrorSingleShoulderToOppositeSide = true;
        [SerializeField] private bool useAthroxShoulderYawOverride = true;
        [SerializeField] private float athroxRightShoulderYaw = 45f;
        [SerializeField] private float athroxLeftShoulderYaw = 135f;
        [SerializeField] private bool autoScaleShoulderOffsetBySpan = true;
        [SerializeField] private float shoulderReferenceSpan = 0.58f;
        [SerializeField] private float shoulderSpanScaleMin = 0.75f;
        [SerializeField] private float shoulderSpanScaleMax = 1.5f;
        [SerializeField] private float athroxShoulderExtraScale = 1.3f;
        [SerializeField] private bool autoAdjustBackArmorForBodyWidth = true;
        [SerializeField] private float athroxBackArmorXScale = 1.25f;
        [SerializeField] private float athroxBackArmorYScale = 1.28f;
        [SerializeField] private string visualOverridesFileName = "equipped_item_visual_overrides.json";
        [SerializeField] private bool writeEquipVisualDiagnostics = true;
        [SerializeField] private string equipVisualDiagnosticsFileName = "equipped_item_visual_diagnostics.json";
        [SerializeField] private bool writeEquipVisualReview = true;
        [SerializeField] private string equipVisualReviewFileName = "equipped_item_visual_review.json";

        private readonly Dictionary<int, EquippedVisualState> _activeStates = new();
        private readonly Dictionary<int, int> _loadRequestIdsBySlot = new();
        private readonly Dictionary<int, VisualOverrideEntry> _visualOverridesByAoid = new();
        private readonly List<string> _availableItemMeshKeys = new();
        private readonly Dictionary<int, string> _abiffNamesById = new();
        private readonly Dictionary<int, Texture2D> _generalTextureCache = new();
        private readonly Dictionary<string, EquippedVisualDiagnosticEntry> _diagnosticsByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastDiagnosticSignatureByKey = new(StringComparer.OrdinalIgnoreCase);
        private string _activeCharacterMeshOverrideKey = string.Empty;
        private bool _itemMeshCatalogLoaded;
        private bool _isDestroying;
        private int _backVisualSwapCooldownFrames;
        private int _lastEquippedSignature;
        private float _nextEquippedFullRefreshAt;

        private void Awake()
        {
            if (bridge == null)
                bridge = GetComponent<CharacterRuntimeBridge>();
            if (appearanceController == null)
                appearanceController = GetComponent<CharacterAppearanceController>();
            // Keep natural hand mapping: right hand uses right-hand transform and vice versa.
            invertWeaponHandVisuals = false;
            // Weapon baseline orientation calibrated for current mirrored character basis.
            rightHandItemLocalPosition = new Vector3(0.075f, -0.02f, 0f);
            leftHandItemLocalPosition = new Vector3(0.075f, -0.02f, 0f);
            rightHandItemLocalEuler = new Vector3(0f, 100f, -90f);
            leftHandItemLocalEuler = new Vector3(0f, 100f, 90f);
            LoadVisualOverrides();
            LoadDiagnosticsFile();
            if (writeEquipVisualReview && writeEquipVisualDiagnostics && _diagnosticsByKey.Count > 0)
            {
                SaveReviewFile(_diagnosticsByKey.Values
                    .OrderBy(e => e.AOID)
                    .ThenBy(e => e.SlotId)
                    .ToList());
            }
        }

        private void LateUpdate()
        {
            if (!enableEquippedItemVisuals || bridge?.Character == null)
            {
                ClearAll();
                return;
            }
            int signature = ComputeEquippedSignature();
            bool fullRefreshDue = signature != _lastEquippedSignature || Time.unscaledTime >= _nextEquippedFullRefreshAt;
            if (fullRefreshDue)
            {
                _lastEquippedSignature = signature;
                _nextEquippedFullRefreshAt = Time.unscaledTime + 0.2f;
                RefreshEquippedVisuals();
            }
            else
            {
                ApplyActiveVisualTransforms();
            }
        }

        private void OnDestroy()
        {
            _isDestroying = true;
            ClearAll(skipCharacterMeshReset: true);
            foreach (var tex in _generalTextureCache.Values)
            {
                if (tex != null)
                    Destroy(tex);
            }
            _generalTextureCache.Clear();
        }

        private void RefreshEquippedVisuals()
        {
            var desired = BuildDesiredStates();
            var desiredSlots = new HashSet<int>(desired.Keys);

            foreach (int slotId in _activeStates.Keys.ToList())
            {
                if (!desiredSlots.Contains(slotId))
                    ClearSlot(slotId);
            }

            foreach (var pair in desired)
            {
                if (_activeStates.TryGetValue(pair.Key, out var existing)
                    && existing?.Root != null
                    && existing.AOID == pair.Value.AOID
                    && existing.StatId == pair.Value.StatId
                    && existing.TextureId == pair.Value.TextureId
                    && string.Equals(existing.MeshKey, pair.Value.MeshKey, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.AnchorKey, pair.Value.AnchorKey, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyLocalTransform(existing);
                    continue;
                }

                ClearSlot(pair.Key);
                CreateVisual(pair.Value);
            }
        }

        private void ApplyActiveVisualTransforms()
        {
            foreach (var state in _activeStates.Values)
            {
                if (state?.Root == null)
                    continue;
                ApplyLocalTransform(state);
            }
        }

        private int ComputeEquippedSignature()
        {
            if (bridge?.Character?.Equipment == null)
                return 0;

            unchecked
            {
                int hash = 17;
                foreach (var kv in bridge.Character.Equipment.GetAllEquipped())
                {
                    hash = (hash * 31) ^ kv.Key;
                    hash = (hash * 31) ^ kv.Value.GetHashCode();
                }

                hash = (hash * 31) ^ (enableEquippedItemVisuals ? 1 : 0);
                return hash;
            }
        }

        private Dictionary<int, EquippedVisualState> BuildDesiredStates()
        {
            var desired = new Dictionary<int, EquippedVisualState>();
            if (bridge?.Character?.Equipment == null || AO.Core.Characters.CharacterEquipment.GetItemInstance == null)
            {
                UpdateCharacterMeshOverride(string.Empty);
                return desired;
            }

            string desiredCharacterMeshOverride = string.Empty;

            foreach (var kv in bridge.Character.Equipment.GetAllEquipped())
            {
                int slotId = kv.Key;
                int canonicalSlotId = ToCanonicalEquipSlotId(slotId);
                var inst = AO.Core.Characters.CharacterEquipment.GetItemInstance(kv.Value);
                int aoid = inst?.Definition?.AOID ?? 0;
                string itemName = inst?.Definition?.Name ?? string.Empty;
                int itemClass = inst?.Definition?.DBType ?? 0;
                if (aoid <= 0)
                {
                    var dataItem = AO.Data.Unity.AODataManager.Instance != null
                        ? AO.Data.Unity.AODataManager.Instance.GetItemInstance(kv.Value)
                        : null;
                    if (dataItem?.Definition != null)
                    {
                        aoid = dataItem.DefinitionId;
                        itemName = dataItem.Definition.Name ?? itemName;
                        var raw = AO.Data.Unity.AODataManager.Instance.GetRawItemByAoid(dataItem.DefinitionId);
                        if (raw != null)
                            itemClass = raw.DBType;
                    }
                }
                if (aoid <= 0)
                    continue;

                if (!TryResolveVisualForSlot(
                        aoid,
                        itemName,
                        itemClass,
                        canonicalSlotId,
                        out var statId,
                        out var textureId,
                        out var meshKey,
                        out var anchorKey,
                        out var catalogMeshKey,
                        out var resolutionMode,
                        out var resolutionNotes))
                {
                    UpsertDiagnostic(
                        aoid,
                        itemName,
                        canonicalSlotId,
                        anchorKey,
                        statId,
                        catalogMeshKey,
                        meshKey,
                        string.IsNullOrWhiteSpace(resolutionMode) ? "Unresolved" : resolutionMode,
                        resolutionNotes);
                    continue;
                }

                if (IsCharacterMeshOverrideCandidate(canonicalSlotId, meshKey, itemName))
                {
                    if (string.IsNullOrWhiteSpace(desiredCharacterMeshOverride))
                    {
                        desiredCharacterMeshOverride = IsRobeOrCloakName(itemName)
                            ? itemName
                            : meshKey;
                    }
                    UpsertDiagnostic(
                        aoid,
                        itemName,
                        canonicalSlotId,
                        anchorKey,
                        statId,
                        catalogMeshKey,
                        meshKey,
                        "CharacterMeshOverride",
                        "Resolved as character mesh override (cloak/robe path).");
                    continue;
                }

                desired[canonicalSlotId] = new EquippedVisualState
                {
                        SlotId = canonicalSlotId,
                        AOID = aoid,
                        ItemName = itemName,
                        StatId = statId,
                        TextureId = textureId,
                        MeshKey = meshKey,
                        AnchorKey = anchorKey,
                        CatalogMeshKey = catalogMeshKey,
                        ResolutionMode = resolutionMode,
                        ResolutionNotes = resolutionNotes
                };
            }

            var uiContext = PrototypeUiContext.Active;
            var socialEquipped = uiContext?.GetSocialEquipped();
            if (socialEquipped != null && AO.Core.Characters.CharacterEquipment.GetItemInstance != null)
            {
                foreach (var kv in socialEquipped)
                {
                    int socialSlotId = kv.Key;
                    int socialCanonical = socialSlotId - 2000;
                    if (socialCanonical <= 0)
                        continue;

                    int canonicalSlot = socialCanonical == 13
                        ? 6
                        : (socialCanonical == 15 ? 8 : socialCanonical);

                    var inst = AO.Core.Characters.CharacterEquipment.GetItemInstance(kv.Value);
                    int aoid = inst?.Definition?.AOID ?? 0;
                    string itemName = inst?.Definition?.Name ?? string.Empty;
                    int itemClass = inst?.Definition?.DBType ?? 0;
                    if (aoid <= 0)
                        continue;

                    if (!TryResolveVisualForSlot(
                            aoid,
                            itemName,
                            itemClass,
                            canonicalSlot,
                            out var statId,
                            out var textureId,
                            out var meshKey,
                            out var anchorKey,
                            out var catalogMeshKey,
                            out var resolutionMode,
                            out var resolutionNotes))
                    {
                        continue;
                    }

                    desired.Remove(canonicalSlot);
                    desired[canonicalSlot] = new EquippedVisualState
                    {
                        SlotId = canonicalSlot,
                        AOID = aoid,
                        ItemName = itemName,
                        StatId = statId,
                        TextureId = textureId,
                        MeshKey = meshKey,
                        AnchorKey = anchorKey,
                        CatalogMeshKey = catalogMeshKey,
                        ResolutionMode = resolutionMode,
                        ResolutionNotes = resolutionNotes
                    };
                }
            }

            if (!desired.ContainsKey(headEquipSlotId) && bridge.DebugHeadVisualAoid > 0)
            {
                int aoid = bridge.DebugHeadVisualAoid;
                if (TryResolveVisualForSlot(
                        aoid,
                        headEquipSlotId,
                        out var statId,
                        out var textureId,
                        out var meshKey,
                        out var anchorKey,
                        out var catalogMeshKey,
                        out var resolutionMode,
                        out var resolutionNotes))
                {
                    desired[headEquipSlotId] = new EquippedVisualState
                    {
                        SlotId = headEquipSlotId,
                        AOID = aoid,
                        ItemName = "Debug Head",
                        StatId = statId,
                        TextureId = textureId,
                        MeshKey = meshKey,
                        AnchorKey = anchorKey,
                        CatalogMeshKey = catalogMeshKey,
                        ResolutionMode = resolutionMode,
                        ResolutionNotes = resolutionNotes
                    };
                }
            }

            ApplyShoulderMirroring(desired);
            bool meshOverrideChanged = UpdateCharacterMeshOverride(desiredCharacterMeshOverride);
            if (meshOverrideChanged)
                _backVisualSwapCooldownFrames = 6;

            if (_backVisualSwapCooldownFrames > 0)
            {
                desired.Remove(backEquipSlotId);
                _backVisualSwapCooldownFrames--;
            }
            return desired;
        }

        private void ApplyShoulderMirroring(Dictionary<int, EquippedVisualState> desired)
        {
            if (!mirrorSingleShoulderToOppositeSide || desired == null || desired.Count == 0)
                return;

            var right = desired.Values.FirstOrDefault(s =>
                s != null
                && s.SlotId == rightShoulderEquipSlotId
                && string.Equals(s.AnchorKey, "RightShoulder", StringComparison.OrdinalIgnoreCase));
            var left = desired.Values.FirstOrDefault(s =>
                s != null
                && s.SlotId == leftShoulderEquipSlotId
                && string.Equals(s.AnchorKey, "LeftShoulder", StringComparison.OrdinalIgnoreCase));
            bool hasRight = right != null;
            bool hasLeft = left != null;
            if (hasRight == hasLeft)
                return;

            if (hasRight && right != null)
            {
                int mirrorKey = BuildMirroredShoulderStateKey(rightShoulderEquipSlotId);
                if (!desired.ContainsKey(mirrorKey))
                {
                    desired[mirrorKey] = new EquippedVisualState
                    {
                        SlotId = mirrorKey,
                        AOID = right.AOID,
                        ItemName = right.ItemName,
                        StatId = right.StatId,
                        TextureId = right.TextureId,
                        MeshKey = right.MeshKey,
                        AnchorKey = "LeftShoulder",
                        CatalogMeshKey = right.CatalogMeshKey,
                        ResolutionMode = $"{right.ResolutionMode}+Mirror",
                        ResolutionNotes = right.ResolutionNotes,
                        IsMirroredShoulder = true
                    };
                }
            }
            else if (hasLeft && left != null)
            {
                int mirrorKey = BuildMirroredShoulderStateKey(leftShoulderEquipSlotId);
                if (!desired.ContainsKey(mirrorKey))
                {
                    desired[mirrorKey] = new EquippedVisualState
                    {
                        SlotId = mirrorKey,
                        AOID = left.AOID,
                        ItemName = left.ItemName,
                        StatId = left.StatId,
                        TextureId = left.TextureId,
                        MeshKey = left.MeshKey,
                        AnchorKey = "RightShoulder",
                        CatalogMeshKey = left.CatalogMeshKey,
                        ResolutionMode = $"{left.ResolutionMode}+Mirror",
                        ResolutionNotes = left.ResolutionNotes,
                        IsMirroredShoulder = true
                    };
                }
            }
        }

        private static int BuildMirroredShoulderStateKey(int sourceShoulderSlotId)
        {
            return 100000 + sourceShoulderSlotId;
        }

        private bool IsCharacterMeshOverrideCandidate(int slotId, string meshKey, string itemName)
        {
            bool isBackOrChestSlot = slotId == backEquipSlotId || slotId == chestEquipSlotId;
            if (!isBackOrChestSlot)
                return false;

            string item = itemName ?? string.Empty;
            bool robeOrCloakItem = item.IndexOf("robe", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("cloak", StringComparison.OrdinalIgnoreCase) >= 0;
            bool robeOrCloakMesh = !string.IsNullOrWhiteSpace(meshKey) && (
                meshKey.IndexOf("robe", StringComparison.OrdinalIgnoreCase) >= 0
                || meshKey.IndexOf("cloak", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!robeOrCloakItem && !robeOrCloakMesh)
                return false;

            // Some cloaks/robes resolve to generic backitem meshes for certain breeds.
            // If the item identity is robe/cloak, prefer character mesh override path.
            return true;
        }

        private bool UpdateCharacterMeshOverride(string meshKey)
        {
            string normalized = ResolveCharacterMeshOverrideKey(meshKey);
            if (string.Equals(_activeCharacterMeshOverrideKey, normalized, StringComparison.OrdinalIgnoreCase))
                return false;

            _activeCharacterMeshOverrideKey = normalized;
            if (appearanceController == null)
                return true;

            if (string.IsNullOrWhiteSpace(normalized))
                appearanceController.ClearTemporaryMeshResourceOverride();
            else
                appearanceController.SetTemporaryMeshResourceOverride(normalized);
            return true;
        }

        private string ResolveCharacterMeshOverrideKey(string sourceMeshKey)
        {
            string key = sourceMeshKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            bool wantsRobe = key.IndexOf("cloak", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("robe", StringComparison.OrdinalIgnoreCase) >= 0
                || IsRobeOrCloakName(key);
            if (!wantsRobe)
                return key;

            string sexToken = ResolveBodySexToken();
            string currentMesh = appearanceController != null ? appearanceController.CurrentMeshResourceName : string.Empty;
            bool isThin = currentMesh.IndexOf("_thin", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isFat = currentMesh.IndexOf("_fat", StringComparison.OrdinalIgnoreCase) >= 0;
            string sizeToken = isThin ? "_thin" : (isFat ? "_fat" : string.Empty);

            foreach (var raceToken in ResolveBodyRaceTokenCandidates())
            {
                if (!string.IsNullOrWhiteSpace(sizeToken))
                {
                    string sizedRobeKey = $"{raceToken}_{sexToken}{sizeToken}_robe";
                    if (CharacterMeshKeyExists(sizedRobeKey))
                        return sizedRobeKey;
                }

                string robeKey = $"{raceToken}_{sexToken}_robe";
                if (CharacterMeshKeyExists(robeKey))
                    return robeKey;
            }

            return key;
        }

        private bool CharacterMeshKeyExists(string meshKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
                return false;

            try
            {
                if (Resources.Load<GameObject>($"CharacterMeshes/{meshKey}") != null)
                    return true;
            }
            catch
            {
            }

            string[] candidates =
            {
                Path.Combine(Application.streamingAssetsPath, "AOData", "CharacterMeshes", $"{meshKey}.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", "CharacterMeshes", $"{meshKey}.gltf"),
                Path.Combine(Application.dataPath, "Resources", "CharacterMeshes", $"{meshKey}.glb"),
                Path.Combine(Application.dataPath, "Resources", "CharacterMeshes", $"{meshKey}.gltf")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                    return true;
            }

            return false;
        }

        private bool TryResolveVisualForSlot(
            int aoid,
            int slotId,
            out int statId,
            out int textureId,
            out string meshKey,
            out string anchorKey,
            out string catalogMeshKey,
            out string resolutionMode,
            out string resolutionNotes)
        {
            return TryResolveVisualForSlot(
                aoid,
                string.Empty,
                0,
                slotId,
                out statId,
                out textureId,
                out meshKey,
                out anchorKey,
                out catalogMeshKey,
                out resolutionMode,
                out resolutionNotes);
        }

        private bool TryResolveVisualForSlot(
            int aoid,
            string itemName,
            int itemClass,
            int slotId,
            out int statId,
            out int textureId,
            out string meshKey,
            out string anchorKey,
            out string catalogMeshKey,
            out string resolutionMode,
            out string resolutionNotes)
        {
            statId = 0;
            textureId = 0;
            meshKey = string.Empty;
            anchorKey = string.Empty;
            catalogMeshKey = string.Empty;
            resolutionMode = string.Empty;
            resolutionNotes = string.Empty;

            bool slotIsAmbiguousLeftShoulderOrRightHand = slotId == rightHandEquipSlotId && slotId == leftShoulderEquipSlotId;
            bool preferShoulderForThisItem = slotIsAmbiguousLeftShoulderOrRightHand && itemClass == 2; // Armor
            bool allowShoulderResolution = !slotIsAmbiguousLeftShoulderOrRightHand || preferShoulderForThisItem;

            if (_visualOverridesByAoid.TryGetValue(aoid, out var ov) && !string.IsNullOrWhiteSpace(ov.MeshKey))
            {
                statId = ov.StatId ?? 0;
                meshKey = ov.MeshKey;
                anchorKey = string.IsNullOrWhiteSpace(ov.AnchorKey) ? ResolveDefaultAnchorKey(slotId) : ov.AnchorKey;
                catalogMeshKey = ov.MeshKey;
                bool ok = TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
                if (ok)
                    resolutionMode = "Override";
                return ok;
            }

            if ((slotId == headEquipSlotId
                    || (allowShoulderResolution && (slotId == rightShoulderEquipSlotId || slotId == leftShoulderEquipSlotId)))
                && TryResolveVisualFromSpellDataAB(
                    aoid,
                    itemName,
                    itemClass,
                    slotId,
                    out statId,
                    out textureId,
                    out meshKey,
                    out anchorKey,
                    out catalogMeshKey,
                    out resolutionMode,
                    out resolutionNotes))
            {
                return true;
            }

            var catalog = ItemVisualCatalog.Instance;
            if (catalog == null)
                return false;

            if (slotId == headEquipSlotId && catalog.TryGetVisual(aoid, StatHeadMesh, out var headSpecific))
            {
                statId = headSpecific.StatId;
                meshKey = headSpecific.MeshKey;
                anchorKey = "Head";
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (slotId == backEquipSlotId && catalog.TryGetVisual(aoid, StatBackMesh, out var backSpecific))
            {
                statId = backSpecific.StatId;
                meshKey = backSpecific.MeshKey;
                anchorKey = "Back";
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (slotId == feetEquipSlotId && catalog.TryGetVisual(aoid, StatMesh, out var feetSpecific))
            {
                statId = feetSpecific.StatId;
                meshKey = feetSpecific.MeshKey;
                anchorKey = "Feet";
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (!preferShoulderForThisItem
                && (slotId == rightHandEquipSlotId || slotId == leftHandEquipSlotId)
                && catalog.TryGetVisual(aoid, StatWeaponMesh, out var weaponSpecific))
            {
                statId = weaponSpecific.StatId;
                meshKey = weaponSpecific.MeshKey;
                anchorKey = ResolveWeaponHandAnchor(slotId);
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (allowShoulderResolution
                && (slotId == rightShoulderEquipSlotId || slotId == leftShoulderEquipSlotId)
                && catalog.TryGetVisual(aoid, StatShoulderMesh, out var shoulderSpecific))
            {
                statId = shoulderSpecific.StatId;
                meshKey = shoulderSpecific.MeshKey;
                anchorKey = preferShoulderForThisItem ? "LeftShoulder" : (slotId == leftShoulderEquipSlotId ? "LeftShoulder" : "RightShoulder");
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (slotId == headEquipSlotId && catalog.TryGetVisual(aoid, StatMesh, out var headFallback))
            {
                statId = headFallback.StatId;
                meshKey = headFallback.MeshKey;
                anchorKey = "Head";
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (slotId == backEquipSlotId && catalog.TryGetVisual(aoid, StatMesh, out var backFallback))
            {
                statId = backFallback.StatId;
                meshKey = backFallback.MeshKey;
                anchorKey = "Back";
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (slotId == feetEquipSlotId && catalog.TryGetVisual(aoid, StatMesh, out var feetFallback))
            {
                statId = feetFallback.StatId;
                meshKey = feetFallback.MeshKey;
                anchorKey = "Feet";
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (!preferShoulderForThisItem
                && (slotId == rightHandEquipSlotId || slotId == leftHandEquipSlotId)
                && catalog.TryGetVisual(aoid, StatMesh, out var weaponFallback))
            {
                statId = weaponFallback.StatId;
                meshKey = weaponFallback.MeshKey;
                anchorKey = ResolveWeaponHandAnchor(slotId);
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            if (allowShoulderResolution
                && (slotId == rightShoulderEquipSlotId || slotId == leftShoulderEquipSlotId)
                && catalog.TryGetVisual(aoid, StatMesh, out var shoulderFallback))
            {
                statId = shoulderFallback.StatId;
                meshKey = shoulderFallback.MeshKey;
                anchorKey = preferShoulderForThisItem ? "LeftShoulder" : (slotId == leftShoulderEquipSlotId ? "LeftShoulder" : "RightShoulder");
                catalogMeshKey = meshKey;
                return TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            }

            anchorKey = slotIsAmbiguousLeftShoulderOrRightHand
                ? (preferShoulderForThisItem ? "LeftShoulder" : "RightHand")
                : ResolveDefaultAnchorKey(slotId);
            catalogMeshKey = meshKey;
            bool resolved = TryResolveWearableFallbackMesh(itemName, slotId, ref meshKey);
            if (resolved && logEquippedVisualResolution)
            {
                Debug.Log($"Equip visual fallback AOID={aoid} slot={slotId} item='{itemName}' anchor={anchorKey} mesh='{meshKey}'.");
            }
            else if (!resolved && logEquippedVisualResolution)
            {
                Debug.LogWarning($"Equip visual unresolved AOID={aoid} slot={slotId} item='{itemName}'. No usable mesh key found.");
            }

            resolutionMode = resolved ? "FallbackWearable" : "Unresolved";
            resolutionNotes = resolved
                ? "Resolved via wearable mesh fallback rules."
                : "No usable catalog mesh and no wearable fallback match.";
            return resolved;
        }

        private bool TryResolveVisualFromSpellDataAB(
            int aoid,
            string itemName,
            int itemClass,
            int slotId,
            out int statId,
            out int textureId,
            out string meshKey,
            out string anchorKey,
            out string catalogMeshKey,
            out string resolutionMode,
            out string resolutionNotes)
        {
            statId = 0;
            textureId = 0;
            meshKey = string.Empty;
            anchorKey = ResolveDefaultAnchorKey(slotId);
            catalogMeshKey = string.Empty;
            resolutionMode = string.Empty;
            resolutionNotes = string.Empty;

            var mgr = AO.Data.Unity.AODataManager.Instance;
            var raw = mgr != null ? mgr.GetRawItemByAoid(aoid) : null;
            if (raw?.SpellData == null)
                return false;

            EnsureAbiffNamesLoaded();
            int breedValue = bridge?.Character?.BreedId ?? 1;
            int genderValue = ResolveCriteriaGenderValue();
            bool ambiguousShoulderHandSlot = slotId == rightHandEquipSlotId && slotId == leftShoulderEquipSlotId;
            bool preferLeftShoulder = ambiguousShoulderHandSlot && itemClass == 2;

            AO.Data.Core.ItemSpellData bestSpell = null;
            int bestScore = int.MinValue;
            string bestMesh = string.Empty;
            foreach (var group in raw.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (spell == null || spell.Target != 2)
                        continue;
                    if (!TryParseFlexibleInt(spell.B, out int meshB) || meshB <= 0)
                        continue;
                    if (!_abiffNamesById.TryGetValue(meshB, out var spellMesh) || string.IsNullOrWhiteSpace(spellMesh))
                        continue;

                    if (!EvaluateSpellCriteria(spell.Criteria, breedValue, genderValue, out int score))
                        continue;

                    if (bestSpell == null || score > bestScore)
                    {
                        bestSpell = spell;
                        bestScore = score;
                        bestMesh = spellMesh;
                    }
                }
            }

            if (bestSpell == null || string.IsNullOrWhiteSpace(bestMesh))
                return false;
            int bestTextureA = 0;
            if (TryParseFlexibleInt(bestSpell.A, out int parsedTextureA) && parsedTextureA > 0)
                bestTextureA = parsedTextureA;
            if (!TryParseFlexibleInt(bestSpell.B, out int bestMeshB) || bestMeshB <= 0)
                return false;

            statId = slotId switch
            {
                _ when slotId == headEquipSlotId => StatHeadMesh,
                _ when slotId == rightShoulderEquipSlotId || slotId == leftShoulderEquipSlotId => StatShoulderMesh,
                _ when slotId == backEquipSlotId => StatBackMesh,
                _ => StatMesh
            };
            textureId = bestTextureA;
            meshKey = bestMesh;
            catalogMeshKey = bestMesh;
            if (slotId == rightShoulderEquipSlotId || slotId == leftShoulderEquipSlotId)
                anchorKey = preferLeftShoulder ? "LeftShoulder" : (slotId == leftShoulderEquipSlotId ? "LeftShoulder" : "RightShoulder");

            bool ok = TryResolveFinalMeshKey(itemName, slotId, ref meshKey, aoid, statId, anchorKey, out resolutionMode, out resolutionNotes);
            if (ok)
            {
                resolutionMode = "SpellAB";
                resolutionNotes = $"Resolved from SpellData A/B (A={bestTextureA}, B={bestMeshB}).";
            }

            return ok;
        }

        private static bool TryParseFlexibleInt(object value, out int result)
        {
            result = 0;
            if (value == null)
                return false;

            if (value is int i)
            {
                result = i;
                return true;
            }
            if (value is long l && l >= int.MinValue && l <= int.MaxValue)
            {
                result = (int)l;
                return true;
            }
            if (value is float f)
            {
                result = Mathf.RoundToInt(f);
                return true;
            }
            if (value is double d)
            {
                result = (int)Math.Round(d);
                return true;
            }

            string text = value.ToString()?.Trim() ?? string.Empty;
            return int.TryParse(text, out result);
        }

        private static bool EvaluateSpellCriteria(IReadOnlyList<AO.Data.Core.ItemActionCriterion> criteria, int breedValue, int genderValue, out int score)
        {
            score = 0;
            if (criteria == null || criteria.Count == 0)
                return true;

            for (int i = 0; i < criteria.Count; i++)
            {
                var c = criteria[i];
                if (c == null || c.Operator == 4 || c.Value1 <= 0)
                    continue;

                if (c.Value1 == 4) // Breed
                {
                    if (!EvaluateCriterion(c.Operator, breedValue, c.Value2))
                        return false;
                    score += 2;
                    continue;
                }

                if (c.Value1 == 59) // Gender
                {
                    if (!EvaluateCriterion(c.Operator, genderValue, c.Value2))
                        return false;
                    score += 1;
                    continue;
                }
            }

            return true;
        }

        private static bool EvaluateCriterion(int op, int current, int target)
        {
            return op switch
            {
                0 => current == target,
                1 => current != target,
                2 => current > target,
                3 => current < target,
                5 => current >= target,
                6 => current <= target,
                _ => true
            };
        }

        private int ResolveCriteriaGenderValue()
        {
            var sex = bridge != null ? bridge.Sex : CharacterRuntimeBridge.CharacterSex.Male;
            return sex switch
            {
                CharacterRuntimeBridge.CharacterSex.Uni => 1,
                CharacterRuntimeBridge.CharacterSex.Male => 2,
                CharacterRuntimeBridge.CharacterSex.Female => 3,
                _ => 2
            };
        }

        private void EnsureAbiffNamesLoaded()
        {
            if (_abiffNamesById.Count > 0)
                return;

            string path = Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder, "AbiffNames.json");
            if (!File.Exists(path))
                return;

            try
            {
                var parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (parsed == null)
                    return;

                foreach (var pair in parsed)
                {
                    if (!int.TryParse(pair.Key, out int id) || string.IsNullOrWhiteSpace(pair.Value))
                        continue;
                    _abiffNamesById[id] = pair.Value.Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading AbiffNames.json: {ex.Message}");
            }
        }

        private static bool IsResolvedMeshKeyUsable(string meshKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
                return false;

            string key = meshKey.Trim();
            if (key.StartsWith("pickupbox_", StringComparison.OrdinalIgnoreCase))
                return false;
            if (key.Equals("pickupbox_misc", StringComparison.OrdinalIgnoreCase))
                return false;
            if (key.Equals("pickupbox_weapon", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private string ResolveDefaultAnchorKey(int slotId)
        {
            if (slotId == headEquipSlotId)
                return "Head";
            if (slotId == backEquipSlotId)
                return "Back";
            if (slotId == feetEquipSlotId)
                return "Feet";
            if (slotId == chestEquipSlotId)
                return "Chest";
            if (slotId == rightArmEquipSlotId)
                return "RightArm";
            if (slotId == leftArmEquipSlotId)
                return "LeftArm";
            if (slotId == legsEquipSlotId)
                return "Legs";
            if (slotId == rightShoulderEquipSlotId)
                return "RightShoulder";
            if (slotId == leftShoulderEquipSlotId)
                return "LeftShoulder";
            if (slotId == leftHandEquipSlotId)
                return "LeftHand";
            return "RightHand";
        }

        private string ResolveWeaponHandAnchor(int slotId)
        {
            if (!invertWeaponHandVisuals)
                return slotId == leftHandEquipSlotId ? "LeftHand" : "RightHand";

            return slotId == leftHandEquipSlotId ? "RightHand" : "LeftHand";
        }

        private bool ShouldUseRightHandTransformForWeapon(EquippedVisualState state)
        {
            return invertWeaponHandVisuals
                && state != null
                && state.StatId == StatWeaponMesh
                && state.SlotId == rightHandEquipSlotId;
        }

        private bool ShouldUseLeftHandTransformForWeapon(EquippedVisualState state)
        {
            return invertWeaponHandVisuals
                && state != null
                && state.StatId == StatWeaponMesh
                && state.SlotId == leftHandEquipSlotId;
        }

        private bool TryResolveWearableFallbackMesh(string itemName, int slotId, ref string meshKey)
        {
            EnsureItemMeshCatalogLoaded();
            if (_availableItemMeshKeys.Count == 0)
                return false;

            string item = itemName ?? string.Empty;
            string sexToken = ResolveBodySexToken();
            var raceTokens = ResolveBodyRaceTokenCandidates();
            var prefixes = new List<string>();
            for (int i = 0; i < raceTokens.Count; i++)
            {
                string raceToken = raceTokens[i];
                if (!string.IsNullOrWhiteSpace(raceToken) && !string.IsNullOrWhiteSpace(sexToken))
                    prefixes.Add($"{raceToken}{sexToken}");
                if (!string.IsNullOrWhiteSpace(raceToken))
                    prefixes.Add(raceToken);
                if (!string.IsNullOrWhiteSpace(raceToken) && !string.IsNullOrWhiteSpace(sexToken))
                    prefixes.Add($"{raceToken}_{sexToken}");
            }

            bool wantsBackLike = slotId == backEquipSlotId
                || item.IndexOf("cloak", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("backpack", StringComparison.OrdinalIgnoreCase) >= 0;
            bool wantsHeadLike = slotId == headEquipSlotId
                || item.IndexOf("hood", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("helmet", StringComparison.OrdinalIgnoreCase) >= 0;
            bool wantsCloakHeadLike = wantsHeadLike && (
                item.IndexOf("hood", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("cloak", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("robe", StringComparison.OrdinalIgnoreCase) >= 0);
            bool wantsFeetLike = slotId == feetEquipSlotId
                || item.IndexOf("boot", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("shoe", StringComparison.OrdinalIgnoreCase) >= 0;
            bool wantsBackArmorLike = wantsBackLike && (
                item.IndexOf("tank armor", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("tank armour", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("diving suit", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("divingsuit", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("battle suit", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("battlesuit", StringComparison.OrdinalIgnoreCase) >= 0
                || item.IndexOf("biohazard", StringComparison.OrdinalIgnoreCase) >= 0);

            if (wantsBackLike)
            {
                if (wantsBackArmorLike)
                {
                    foreach (string p in prefixes)
                    {
                        if (TryPickArmorBackMeshByType($"{p}_armour_", item, out meshKey))
                            return true;
                        if (TryPickArmorBackMeshByType($"{p}_armor_", item, out meshKey))
                            return true;
                    }
                }

                foreach (string p in prefixes)
                {
                    if (TryPickMeshByStartsWith($"{p}_cloak_", out meshKey))
                        return true;
                    if (TryPickMeshByStartsWith($"{p}_backpack_", out meshKey))
                        return true;
                    if (TryPickMeshByStartsWith($"{p}_backitem_", out meshKey))
                        return true;
                }

                if (TryPickMeshByStartsWith("backitem_", out meshKey)
                    || TryPickMeshByStartsWith("backitem2_", out meshKey))
                    return true;
            }

            if (wantsHeadLike)
            {
                foreach (string p in prefixes)
                {
                    if (wantsCloakHeadLike)
                    {
                        if (TryPickMeshByStartsWith($"{p}_helmet_cloak", out meshKey))
                            return true;
                        if (TryPickMeshByStartsWith($"{p}_helmet_robe", out meshKey))
                            return true;
                    }
                    if (TryPickMeshByStartsWith($"{p}_hood", out meshKey))
                        return true;
                    if (TryPickMeshByStartsWith($"{p}_helmet_", out meshKey))
                        return true;
                }
            }

            if (wantsFeetLike)
            {
                foreach (string p in prefixes)
                {
                    if (TryPickMeshByStartsWith($"{p}_boots", out meshKey))
                        return true;
                    if (TryPickMeshByStartsWith($"{p}_feet", out meshKey))
                        return true;
                }
            }

            return false;
        }

        private bool TryPickArmorBackMeshByType(string prefix, string itemName, out string meshKey)
        {
            meshKey = string.Empty;
            if (string.IsNullOrWhiteSpace(prefix))
                return false;

            string item = itemName ?? string.Empty;
            string[] typeTokens = { "heavy", "medium", "light", "verylight", "pandemonium", "divingsuit", "biohazardsuit" };

            if (item.IndexOf("tank", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string foundTank = _availableItemMeshKeys
                    .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && k.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(k => k.Length)
                    .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(foundTank))
                {
                    meshKey = foundTank;
                    return true;
                }
            }

            foreach (string token in typeTokens)
            {
                if (item.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string foundTyped = _availableItemMeshKeys
                    .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && k.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(k => k.Length)
                    .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(foundTyped))
                {
                    meshKey = foundTyped;
                    return true;
                }
            }

            string foundGeneric = _availableItemMeshKeys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.Length)
                .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(foundGeneric))
            {
                meshKey = foundGeneric;
                return true;
            }

            return false;
        }

        private bool TryResolveFinalMeshKey(
            string itemName,
            int slotId,
            ref string meshKey,
            int aoid,
            int statId,
            string anchorKey,
            out string resolutionMode,
            out string resolutionNotes)
        {
            resolutionMode = string.Empty;
            resolutionNotes = string.Empty;
            string candidate = meshKey;
            bool directUsable = IsResolvedMeshKeyUsable(candidate) && MeshKeyExists(candidate);
            if (directUsable)
            {
                resolutionMode = "Catalog";
                resolutionNotes = "Using catalog mesh key.";
                return true;
            }

            if (TryResolveWearableFallbackMesh(itemName, slotId, ref meshKey))
            {
                resolutionMode = "FallbackWearable";
                resolutionNotes = $"Catalog mesh '{candidate}' was unusable.";
                if (logEquippedVisualResolution)
                {
                    Debug.Log(
                        $"Equip visual fallback AOID={aoid} slot={slotId} item='{itemName}' anchor={anchorKey} stat={statId} " +
                        $"catalogMesh='{candidate}' -> resolvedMesh='{meshKey}'.");
                }
                return true;
            }

            resolutionMode = "Unresolved";
            resolutionNotes = $"Catalog mesh '{candidate}' was unusable and no fallback match was found.";
            if (logEquippedVisualResolution)
            {
                Debug.LogWarning(
                    $"Equip visual unresolved AOID={aoid} slot={slotId} item='{itemName}' anchor={anchorKey} stat={statId} " +
                    $"catalogMesh='{candidate}'.");
            }

            return false;
        }

        private bool MeshKeyExists(string meshKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
                return false;

            var prefab = Resources.Load<GameObject>($"{itemMeshResourcesFolder}/{meshKey}");
            if (prefab != null)
                return true;

            return !string.IsNullOrWhiteSpace(ResolveItemMeshPath(meshKey));
        }

        private bool TryPickMeshByStartsWith(string prefix, out string meshKey)
        {
            meshKey = string.Empty;
            if (string.IsNullOrWhiteSpace(prefix))
                return false;

            string found = _availableItemMeshKeys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.Length)
                .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(found))
                return false;

            meshKey = found;
            return true;
        }

        private void EnsureItemMeshCatalogLoaded()
        {
            if (_itemMeshCatalogLoaded)
                return;

            _itemMeshCatalogLoaded = true;
            _availableItemMeshKeys.Clear();

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            TryCollectMeshKeysFromFolder(Path.Combine(Application.streamingAssetsPath, "AOData", itemMeshResourcesFolder), keys);
            TryCollectMeshKeysFromFolder(Path.Combine(Application.streamingAssetsPath, itemMeshResourcesFolder), keys);
            TryCollectMeshKeysFromFolder(Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder), keys);

            _availableItemMeshKeys.AddRange(keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        }

        private static void TryCollectMeshKeysFromFolder(string folder, HashSet<string> keys)
        {
            if (keys == null || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.glb", SearchOption.TopDirectoryOnly))
                    keys.Add(Path.GetFileNameWithoutExtension(file));
                foreach (var file in Directory.EnumerateFiles(folder, "*.gltf", SearchOption.TopDirectoryOnly))
                    keys.Add(Path.GetFileNameWithoutExtension(file));
            }
            catch
            {
            }
        }

        private string ResolveBodyRaceToken()
        {
            int breed = bridge?.Character?.BreedId ?? 1;
            return breed switch
            {
                2 => "opifex",
                3 => "nanomage",
                4 => "athrox",
                _ => "solitus"
            };
        }

        private List<string> ResolveBodyRaceTokenCandidates()
        {
            string primary = ResolveBodyRaceToken();
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(primary))
                list.Add(primary);

            if (string.Equals(primary, "athrox", StringComparison.OrdinalIgnoreCase))
                list.Add("atrox");
            else if (string.Equals(primary, "atrox", StringComparison.OrdinalIgnoreCase))
                list.Add("athrox");

            return list;
        }

        private string ResolveBodySexToken()
        {
            var sex = bridge != null ? bridge.Sex : CharacterRuntimeBridge.CharacterSex.Male;
            return sex == CharacterRuntimeBridge.CharacterSex.Female ? "female" : "male";
        }

        private void LoadVisualOverrides()
        {
            _visualOverridesByAoid.Clear();
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "AOData", visualOverridesFileName);
                if (!File.Exists(path))
                    return;

                var parsed = JsonConvert.DeserializeObject<VisualOverrideFile>(File.ReadAllText(path));
                if (parsed?.Overrides == null)
                    return;

                foreach (var ov in parsed.Overrides)
                {
                    if (ov == null || ov.AOID <= 0 || string.IsNullOrWhiteSpace(ov.MeshKey))
                        continue;
                    _visualOverridesByAoid[ov.AOID] = ov;
                }

                Debug.Log($"Loaded equipped item visual overrides: {_visualOverridesByAoid.Count} entries.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load equipped item visual overrides: {ex.Message}");
            }
        }

        private void CreateVisual(EquippedVisualState state)
        {
            Transform anchor = ResolveAnchor(state.AnchorKey);
            if (anchor == null)
                return;

            var root = new GameObject($"Equipped_{state.SlotId}_{state.MeshKey}");
            root.transform.SetParent(anchor, false);

            var prefab = Resources.Load<GameObject>($"{itemMeshResourcesFolder}/{state.MeshKey}");
            if (prefab != null)
            {
                var child = Instantiate(prefab, root.transform, false);
                child.name = state.MeshKey;
                ApplyStateTextureToRoot(state);
            }
            else
            {
                int requestId = 1;
                if (_loadRequestIdsBySlot.TryGetValue(state.SlotId, out var existingRequestId))
                    requestId = existingRequestId + 1;
                _loadRequestIdsBySlot[state.SlotId] = requestId;
                _ = LoadItemMeshFromGlbAsync(state, root.transform, requestId);
            }

            state.Root = root;
            _activeStates[state.SlotId] = state;
            ApplyLocalTransform(state);
            if (!state.IsMirroredShoulder)
            {
                UpsertDiagnostic(
                    state.AOID,
                    state.ItemName,
                    state.SlotId,
                    state.AnchorKey,
                    state.StatId,
                    state.CatalogMeshKey,
                    state.MeshKey,
                    state.ResolutionMode,
                    state.ResolutionNotes);
            }
        }

        private void ApplyLocalTransform(EquippedVisualState state)
        {
            if (state?.Root == null)
                return;

            switch (state.AnchorKey)
            {
                case "LeftHand":
                    if (ShouldUseRightHandTransformForWeapon(state))
                    {
                        state.Root.transform.localPosition = rightHandItemLocalPosition;
                        state.Root.transform.localRotation = Quaternion.Euler(rightHandItemLocalEuler);
                        state.Root.transform.localScale = rightHandItemLocalScale;
                    }
                    else
                    {
                        state.Root.transform.localPosition = leftHandItemLocalPosition;
                        state.Root.transform.localRotation = Quaternion.Euler(leftHandItemLocalEuler);
                        state.Root.transform.localScale = leftHandItemLocalScale;
                    }
                    break;
                case "RightHand":
                    if (ShouldUseLeftHandTransformForWeapon(state))
                    {
                        state.Root.transform.localPosition = leftHandItemLocalPosition;
                        state.Root.transform.localRotation = Quaternion.Euler(leftHandItemLocalEuler);
                        state.Root.transform.localScale = leftHandItemLocalScale;
                    }
                    else
                    {
                        state.Root.transform.localPosition = rightHandItemLocalPosition;
                        state.Root.transform.localRotation = Quaternion.Euler(rightHandItemLocalEuler);
                        state.Root.transform.localScale = rightHandItemLocalScale;
                    }
                    break;
                case "Head":
                    if (forceHeadTransformOverride)
                    {
                        state.Root.transform.localPosition = forcedHeadItemLocalPosition;
                        state.Root.transform.localRotation = Quaternion.Euler(forcedHeadItemLocalEuler);
                        state.Root.transform.localScale = forcedHeadItemLocalScale;
                    }
                    else
                    {
                        state.Root.transform.localPosition = headItemLocalPosition;
                        state.Root.transform.localRotation = Quaternion.Euler(headItemLocalEuler);
                        state.Root.transform.localScale = headItemLocalScale;
                    }
                    break;
                case "Back":
                    if (IsBackArmorItem(state?.ItemName))
                    {
                        state.Root.transform.localPosition = ResolveAdaptiveBackArmorLocalPosition(backArmorItemLocalPosition);
                        state.Root.transform.localRotation = Quaternion.Euler(backArmorItemLocalEuler);
                        state.Root.transform.localScale = backArmorItemLocalScale;
                    }
                    else
                    {
                        state.Root.transform.localPosition = backItemLocalPosition;
                        state.Root.transform.localRotation = Quaternion.Euler(backItemLocalEuler);
                        state.Root.transform.localScale = backItemLocalScale;
                    }
                    break;
                case "Feet":
                    state.Root.transform.localPosition = feetItemLocalPosition;
                    state.Root.transform.localRotation = Quaternion.Euler(feetItemLocalEuler);
                    state.Root.transform.localScale = feetItemLocalScale;
                    break;
                case "Chest":
                    state.Root.transform.localPosition = chestItemLocalPosition;
                    state.Root.transform.localRotation = Quaternion.Euler(chestItemLocalEuler);
                    state.Root.transform.localScale = chestItemLocalScale;
                    break;
                case "RightArm":
                    state.Root.transform.localPosition = rightArmItemLocalPosition;
                    state.Root.transform.localRotation = Quaternion.Euler(rightArmItemLocalEuler);
                    state.Root.transform.localScale = rightArmItemLocalScale;
                    break;
                case "LeftArm":
                    state.Root.transform.localPosition = leftArmItemLocalPosition;
                    state.Root.transform.localRotation = Quaternion.Euler(leftArmItemLocalEuler);
                    state.Root.transform.localScale = leftArmItemLocalScale;
                    break;
                case "Legs":
                    state.Root.transform.localPosition = legsItemLocalPosition;
                    state.Root.transform.localRotation = Quaternion.Euler(legsItemLocalEuler);
                    state.Root.transform.localScale = legsItemLocalScale;
                    break;
                case "RightShoulder":
                    var rightEuler = rightShoulderLocalEuler;
                    if (useAthroxShoulderYawOverride && (bridge?.Character?.BreedId ?? 1) == 4)
                        rightEuler.y = athroxRightShoulderYaw;
                    state.Root.transform.localPosition = ResolveAdaptiveShoulderLocalPosition(rightShoulderLocalPosition);
                    state.Root.transform.localRotation = Quaternion.Euler(rightEuler);
                    state.Root.transform.localScale = rightShoulderLocalScale;
                    break;
                case "LeftShoulder":
                    var leftEuler = leftShoulderLocalEuler;
                    if (useAthroxShoulderYawOverride && (bridge?.Character?.BreedId ?? 1) == 4)
                        leftEuler.y = athroxLeftShoulderYaw;
                    state.Root.transform.localPosition = ResolveAdaptiveShoulderLocalPosition(leftShoulderLocalPosition);
                    state.Root.transform.localRotation = Quaternion.Euler(leftEuler);
                    state.Root.transform.localScale = leftShoulderLocalScale;
                    break;
            }
        }

        private Vector3 ResolveAdaptiveShoulderLocalPosition(Vector3 baseLocalPosition)
        {
            if (!autoScaleShoulderOffsetBySpan)
                return baseLocalPosition;

            float scale = 1f;
            var right = ResolveShoulderAnchor(
                forceRightShoulderBoneName,
                HumanBodyBones.RightShoulder,
                HumanBodyBones.RightUpperArm,
                "r clavicle", "right clavicle", "rclavicle", "rightshoulder", "right shoulder", "shoulder.r", "clavicle_r", "upperarm_r", "r upperarm");
            var left = ResolveShoulderAnchor(
                forceLeftShoulderBoneName,
                HumanBodyBones.LeftShoulder,
                HumanBodyBones.LeftUpperArm,
                "l clavicle", "left clavicle", "lclavicle", "leftshoulder", "left shoulder", "shoulder.l", "clavicle_l", "upperarm_l", "l upperarm");
            if (right != null && left != null && shoulderReferenceSpan > 0.0001f)
            {
                float span = Vector3.Distance(right.position, left.position);
                if (span > 0.0001f)
                    scale *= Mathf.Clamp(span / shoulderReferenceSpan, shoulderSpanScaleMin, shoulderSpanScaleMax);
            }

            if ((bridge?.Character?.BreedId ?? 1) == 4)
                scale *= athroxShoulderExtraScale;
            return new Vector3(baseLocalPosition.x * scale, baseLocalPosition.y, baseLocalPosition.z);
        }

        private Vector3 ResolveAdaptiveBackArmorLocalPosition(Vector3 baseLocalPosition)
        {
            if (!autoAdjustBackArmorForBodyWidth)
                return baseLocalPosition;

            float xScale = 1f;
            float yScale = 1f;
            if ((bridge?.Character?.BreedId ?? 1) == 4)
            {
                xScale *= athroxBackArmorXScale;
                yScale *= athroxBackArmorYScale;
            }

            string currentMesh = appearanceController != null ? appearanceController.CurrentMeshResourceName : string.Empty;
            if (currentMesh.IndexOf("_thin", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                xScale *= 0.93f;
                yScale *= 0.97f;
            }
            else if (currentMesh.IndexOf("_fat", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                xScale *= 1.08f;
                yScale *= 1.03f;
            }

            return new Vector3(baseLocalPosition.x * xScale, baseLocalPosition.y * yScale, baseLocalPosition.z);
        }

        private static bool IsRobeOrCloakName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.IndexOf("robe", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("cloak", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Transform ResolveShoulderAnchor(
            string forcedToken,
            HumanBodyBones primaryBone,
            HumanBodyBones fallbackBone,
            params string[] tokens)
        {
            if (!string.IsNullOrWhiteSpace(forcedToken))
            {
                var forced = FindTransformByNameContains(forcedToken);
                if (forced != null)
                    return forced;
            }

            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman)
            {
                var fromHumanoid = animator.GetBoneTransform(primaryBone) ?? animator.GetBoneTransform(fallbackBone);
                if (fromHumanoid != null)
                    return fromHumanoid;
            }

            return FindAnchor(string.Empty, tokens);
        }

        private static bool IsBackArmorItem(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return false;
            return itemName.IndexOf("tank armor", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("tank armour", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("diving suit", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("divingsuit", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("battle suit", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("battlesuit", StringComparison.OrdinalIgnoreCase) >= 0
                || itemName.IndexOf("biohazard", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Transform ResolveAnchor(string anchorKey)
        {
            return anchorKey switch
            {
                "LeftHand" => FindAnchor(forceLeftHandBoneName, "l_hand", "left hand", "lefthand", "hand.l", "bip01 l hand", "left_hand", "mixamorig:lefthand", "weapon_l"),
                "RightHand" => FindAnchor(forceRightHandBoneName, "r_hand", "right hand", "rhand", "hand.r", "bip01 r hand", "right_hand", "mixamorig:righthand", "weapon_r"),
                "Head" => FindAnchor(forceHeadBoneName, "head", "bip01 head", "neck"),
                "Back" => FindAnchor(forceBackBoneName, "spine2", "spine 2", "spine", "back", "chest"),
                "Feet" => FindAnchor(forceFeetBoneName, "pelvis", "hips", "bip01 pelvis", "spine"),
                "Chest" => FindAnchor(forceChestBoneName, "spine2", "spine 2", "chest", "spine", "pelvis"),
                "RightArm" => FindAnchor(forceRightArmBoneName, "r upperarm", "rightarm", "bip01 r upperarm", "r arm"),
                "LeftArm" => FindAnchor(forceLeftArmBoneName, "l upperarm", "leftarm", "bip01 l upperarm", "l arm"),
                "Legs" => FindAnchor(forceLegsBoneName, "pelvis", "hips", "bip01 pelvis", "spine"),
                "RightShoulder" => FindAnchor(forceRightShoulderBoneName, "r clavicle", "rightshoulder", "right shoulder", "shoulder.r", "clavicle_r"),
                "LeftShoulder" => FindAnchor(forceLeftShoulderBoneName, "l clavicle", "leftshoulder", "left shoulder", "shoulder.l", "clavicle_l"),
                _ => transform
            };
        }

        private Transform FindAnchor(string forcedToken, params string[] tokens)
        {
            if (!string.IsNullOrWhiteSpace(forcedToken))
            {
                var forced = FindTransformByNameContains(forcedToken);
                if (forced != null)
                    return forced;
            }

            foreach (var token in tokens)
            {
                var found = FindTransformByNameContains(token);
                if (found != null)
                    return found;
            }

            return transform;
        }

        private Transform FindTransformByNameContains(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return all[i];
            }

            return null;
        }

        private static int ToCanonicalEquipSlotId(int slotId)
        {
            if (slotId > 1000 && slotId < 1100)
                return slotId - 1000;
            if (slotId == 3006 || slotId == 3008)
                return slotId - 3000;
            return slotId;
        }

        private string ResolveItemMeshPath(string meshResourceName)
        {
            if (string.IsNullOrWhiteSpace(meshResourceName))
                return string.Empty;

            var keyCandidates = new List<string>();
            string trimmed = meshResourceName.Trim();
            keyCandidates.Add(trimmed);

            string trimmedUnderscore = trimmed.TrimEnd('_');
            if (!string.Equals(trimmedUnderscore, trimmed, StringComparison.Ordinal))
                keyCandidates.Add(trimmedUnderscore);

            string collapsed = trimmed.Replace("__", "_");
            if (!string.Equals(collapsed, trimmed, StringComparison.Ordinal))
                keyCandidates.Add(collapsed);

            foreach (var key in keyCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var candidates = new[]
                {
                    Path.Combine(Application.streamingAssetsPath, "AOData", itemMeshResourcesFolder, $"{key}.glb"),
                    Path.Combine(Application.streamingAssetsPath, itemMeshResourcesFolder, $"{key}.glb"),
                    Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder, $"{key}.glb"),
                    Path.Combine(Application.streamingAssetsPath, "AOData", itemMeshResourcesFolder, $"{key}.gltf"),
                    Path.Combine(Application.streamingAssetsPath, itemMeshResourcesFolder, $"{key}.gltf"),
                    Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder, $"{key}.gltf")
                };

                foreach (var path in candidates)
                {
                    if (!File.Exists(path))
                        continue;

                    if (path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!allowItemMeshGltfFallback)
                            continue;
                        if (ContainsEmbeddedDataUri(path))
                            continue;
                    }

                    if (File.Exists(path))
                        return path;
                }
            }

            var originalCandidates = new[]
            {
                Path.Combine(Application.streamingAssetsPath, "AOData", itemMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.streamingAssetsPath, itemMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", itemMeshResourcesFolder, $"{meshResourceName}.gltf"),
                Path.Combine(Application.streamingAssetsPath, itemMeshResourcesFolder, $"{meshResourceName}.gltf"),
                Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder, $"{meshResourceName}.gltf")
            };

            foreach (var path in originalCandidates)
            {
                if (!File.Exists(path))
                    continue;

                if (path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allowItemMeshGltfFallback)
                        continue;
                    if (ContainsEmbeddedDataUri(path))
                        continue;
                }

                if (File.Exists(path))
                    return path;
            }

            return string.Empty;
        }

        private static bool ContainsEmbeddedDataUri(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;

                var info = new FileInfo(path);
                if (info.Length > 8 * 1024 * 1024)
                    return false;

                string text = File.ReadAllText(path);
                return text.IndexOf("data:", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task LoadItemMeshFromGlbAsync(EquippedVisualState state, Transform parent, int requestId)
        {
            object importer = null;
            string meshResourceName = state?.MeshKey ?? string.Empty;
            try
            {
                if (state == null)
                    return;

                meshResourceName = state.MeshKey;
                int slotId = state.SlotId;
                string meshPath = ResolveItemMeshPath(meshResourceName);
                meshPath = GlbDataUriLoadPathResolver.Resolve(meshPath, true);
                if (string.IsNullOrWhiteSpace(meshPath))
                    return;
                if (!_loadRequestIdsBySlot.TryGetValue(slotId, out var activeRequestId) || activeRequestId != requestId)
                    return;
                if (parent == null)
                    return;

                bool instantiated = await TryInstantiateGlbWithReflection(meshPath, parent, loadedImporter => importer = loadedImporter);
                if (parent == null)
                    return;

                if (!instantiated && parent != null)
                    Debug.LogWarning($"Failed to instantiate equipped item mesh '{meshResourceName}' from '{meshPath}'.");
                else if (instantiated)
                {
                    state.RuntimeImporter = importer;
                    importer = null;
                    ApplyStateTextureToRoot(state);
                }
            }
            catch (Exception ex)
            {
                if (IsDestroyedObjectException(ex))
                    return;

                string detail = ex.InnerException != null ? $"{ex.Message} | Inner: {ex.InnerException.Message}" : ex.Message;
                Debug.LogWarning($"Equipped item mesh load failed for '{meshResourceName}': {detail}");
            }
            finally
            {
                DisposeImporter(importer);
            }
        }

        private static async Task<bool> TryInstantiateGlbWithReflection(string fullPath, Transform parent, Action<object> onImporterLoaded = null)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || parent == null)
                return false;

            fullPath = GlbDataUriLoadPathResolver.Resolve(fullPath, true);

            var gltfType = Type.GetType("GLTFast.GltfImport, glTFast");
            if (gltfType == null)
                return false;

            Type importSettingsType = Type.GetType("GLTFast.ImportSettings, glTFast");
            object importSettings = CreateImportSettings(importSettingsType, disableAnimations: true);
            object importer = CreateGltfImporterInstance(gltfType);
            if (importer == null)
                return false;
            bool handedOff = false;

            string uriPath = fullPath.Replace("\\", "/");
            if (!uriPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                uriPath = $"file:///{uriPath}";

            try
            {
                var loadFileMethod = gltfType.GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "LoadFile"
                        && m.GetParameters().Length >= 1
                        && m.GetParameters()[0].ParameterType == typeof(string));
                if (loadFileMethod != null)
                {
                    try
                    {
                        var args = BuildLoadArgs(loadFileMethod.GetParameters(), fullPath, importSettingsType, importSettings);
                        var taskObj = loadFileMethod.Invoke(importer, args);
                        if (!await AwaitBoolTask(taskObj))
                            return false;
                        if (parent == null)
                            return false;
                        bool instantiated = await InstantiateMainScene(importer, parent);
                        if (!instantiated)
                            return false;
                        onImporterLoaded?.Invoke(importer);
                        handedOff = true;
                        return true;
                    }
                    catch (Exception ex) when (IsDestroyedObjectException(ex))
                    {
                        return false;
                    }
                }

                var loadUriMethod = gltfType.GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "Load"
                        && m.GetParameters().Length >= 1
                        && m.GetParameters()[0].ParameterType == typeof(Uri));
                if (loadUriMethod != null)
                {
                    try
                    {
                        var uri = new Uri(uriPath);
                        var args = BuildLoadArgs(loadUriMethod.GetParameters(), uri, importSettingsType, importSettings);
                        var taskObj = loadUriMethod.Invoke(importer, args);
                        if (!await AwaitBoolTask(taskObj))
                            return false;
                        if (parent == null)
                            return false;
                        bool instantiated = await InstantiateMainScene(importer, parent);
                        if (!instantiated)
                            return false;
                        onImporterLoaded?.Invoke(importer);
                        handedOff = true;
                        return true;
                    }
                    catch (Exception ex) when (IsDestroyedObjectException(ex))
                    {
                        return false;
                    }
                }

                var loadStringMethod = gltfType.GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "Load"
                        && m.GetParameters().Length >= 1
                        && m.GetParameters()[0].ParameterType == typeof(string));
                if (loadStringMethod != null)
                {
                    try
                    {
                        var stringArgs = BuildLoadArgs(loadStringMethod.GetParameters(), uriPath, importSettingsType, importSettings);
                        var taskObj = loadStringMethod.Invoke(importer, stringArgs);
                        if (!await AwaitBoolTask(taskObj))
                        {
                            stringArgs = BuildLoadArgs(loadStringMethod.GetParameters(), fullPath, importSettingsType, importSettings);
                            taskObj = loadStringMethod.Invoke(importer, stringArgs);
                            if (!await AwaitBoolTask(taskObj))
                                return false;
                        }

                        if (parent == null)
                            return false;
                        bool instantiated = await InstantiateMainScene(importer, parent);
                        if (!instantiated)
                            return false;
                        onImporterLoaded?.Invoke(importer);
                        handedOff = true;
                        return true;
                    }
                    catch (Exception ex) when (IsDestroyedObjectException(ex))
                    {
                        return false;
                    }
                }

                return false;
            }
            finally
            {
                if (!handedOff)
                    DisposeImporter(importer);
            }
        }

        private void ApplyStateTextureToRoot(EquippedVisualState state)
        {
            if (!Application.isPlaying || state?.Root == null || state.TextureId <= 0)
                return;
            if (!TryGetGeneralTexture(state.TextureId, out var tex))
                return;

            var renderers = state.Root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;

                var mats = r.materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null)
                        continue;

                    if (mat.HasProperty("_BaseMap"))
                        mat.SetTexture("_BaseMap", tex);
                    if (mat.HasProperty("_MainTex"))
                        mat.SetTexture("_MainTex", tex);
                    mat.mainTexture = tex;
                }
            }
        }

        private bool TryGetGeneralTexture(int textureId, out Texture2D texture)
        {
            texture = null;
            if (textureId <= 0)
                return false;

            if (_generalTextureCache.TryGetValue(textureId, out var cached) && cached != null)
            {
                texture = cached;
                return true;
            }

            string basePath = Path.Combine(Application.streamingAssetsPath, "AOData", "GeneralTextures");
            string jpgPath = Path.Combine(basePath, $"{textureId}.jpg");
            string pngPath = Path.Combine(basePath, $"{textureId}.png");
            string path = File.Exists(jpgPath) ? jpgPath : (File.Exists(pngPath) ? pngPath : string.Empty);
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var bytes = File.ReadAllBytes(path);
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!loaded.LoadImage(bytes))
            {
                Destroy(loaded);
                return false;
            }

            loaded.wrapMode = TextureWrapMode.Repeat;
            loaded.filterMode = FilterMode.Point;
            _generalTextureCache[textureId] = loaded;
            texture = loaded;
            return true;
        }

        private static async Task<bool> InstantiateMainScene(object importer, Transform parent)
        {
            if (parent == null)
                return false;

            var importerType = importer.GetType();

            var instantiateMethod = importerType.GetMethods()
                .FirstOrDefault(m =>
                    (m.Name == "InstantiateMainScene" || m.Name == "InstantiateScene")
                    && m.GetParameters().Length >= 1
                    && typeof(Transform).IsAssignableFrom(m.GetParameters()[0].ParameterType));
            if (instantiateMethod == null)
                return false;

            var args = BuildInstantiateArgs(instantiateMethod.GetParameters(), parent);
            object result;
            try
            {
                result = instantiateMethod.Invoke(importer, args);
            }
            catch (Exception ex) when (IsDestroyedObjectException(ex))
            {
                return false;
            }
            if (result is bool boolResult)
                return boolResult;
            if (result is Task<bool> boolTask)
                return await boolTask;
            if (result is Task task)
            {
                await task;
                return true;
            }

            return true;
        }

        private static bool IsDestroyedObjectException(Exception ex)
        {
            if (ex == null)
                return false;

            if (ex is AggregateException ae)
            {
                foreach (var inner in ae.Flatten().InnerExceptions)
                {
                    if (IsDestroyedObjectException(inner))
                        return true;
                }
            }

            string message = ex.Message ?? string.Empty;
            if (message.IndexOf("has been destroyed", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return ex.InnerException != null && IsDestroyedObjectException(ex.InnerException);
        }

        private static object[] BuildInstantiateArgs(ParameterInfo[] parameters, Transform parent)
        {
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i == 0)
                {
                    args[i] = parent;
                    continue;
                }

                var p = parameters[i];
                if (p.HasDefaultValue)
                {
                    args[i] = p.DefaultValue;
                }
                else if (p.ParameterType.IsValueType)
                {
                    args[i] = Activator.CreateInstance(p.ParameterType);
                }
                else
                {
                    args[i] = null;
                }
            }

            return args;
        }

        private static object[] BuildLoadArgs(ParameterInfo[] parameters, object firstArg, Type importSettingsType = null, object importSettings = null)
        {
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (i == 0)
                {
                    args[i] = firstArg;
                    continue;
                }

                var p = parameters[i];
                if (importSettings != null && importSettingsType != null && p.ParameterType == importSettingsType)
                {
                    args[i] = importSettings;
                }
                else if (p.HasDefaultValue)
                {
                    args[i] = p.DefaultValue;
                }
                else if (p.ParameterType.IsValueType)
                {
                    args[i] = Activator.CreateInstance(p.ParameterType);
                }
                else
                {
                    args[i] = null;
                }
            }

            return args;
        }

        private static object CreateGltfImporterInstance(Type gltfType)
        {
            var constructors = gltfType
                .GetConstructors()
                .OrderBy(c => c.GetParameters().Length)
                .ToArray();

            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                var args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var pType = parameters[i].ParameterType;
                    args[i] = pType.IsValueType ? Activator.CreateInstance(pType) : null;
                }

                try
                {
                    var instance = ctor.Invoke(args);
                    if (instance != null)
                        return instance;
                }
                catch
                {
                }
            }

            return null;
        }

        private static object CreateImportSettings(Type importSettingsType, bool disableAnimations)
        {
            if (importSettingsType == null)
                return null;

            object settings = Activator.CreateInstance(importSettingsType);
            if (settings == null)
                return null;

            var animationMethodProperty = importSettingsType.GetProperty("AnimationMethod");
            if (disableAnimations && animationMethodProperty != null && animationMethodProperty.CanWrite)
            {
                Type animationMethodType = animationMethodProperty.PropertyType;
                object noneValue = Enum.ToObject(animationMethodType, 0);
                animationMethodProperty.SetValue(settings, noneValue);
            }

            return settings;
        }

        private static void DisposeImporter(object importer)
        {
            if (importer == null)
                return;

            if (importer is IDisposable disposable)
            {
                disposable.Dispose();
                return;
            }

            var disposeMethod = importer.GetType().GetMethod("Dispose", Type.EmptyTypes);
            disposeMethod?.Invoke(importer, null);
        }

        private static async Task<bool> AwaitBoolTask(object taskObject)
        {
            if (taskObject is Task<bool> boolTask)
                return await boolTask;

            if (taskObject is Task task)
            {
                await task;
                var resultProperty = task.GetType().GetProperty("Result");
                if (resultProperty != null && resultProperty.PropertyType == typeof(bool))
                    return (bool)resultProperty.GetValue(task);
                return true;
            }

            if (taskObject is bool boolResult)
                return boolResult;

            return false;
        }

        private void ClearSlot(int slotId)
        {
            if (_activeStates.TryGetValue(slotId, out var existing))
            {
                DisposeImporter(existing.RuntimeImporter);
                existing.RuntimeImporter = null;
                if (existing.Root != null)
                    Destroy(existing.Root);
                _activeStates.Remove(slotId);
            }

            _loadRequestIdsBySlot.Remove(slotId);
        }

        private void ClearAll(bool skipCharacterMeshReset = false)
        {
            foreach (var state in _activeStates.Values)
            {
                DisposeImporter(state?.RuntimeImporter);
                if (state?.Root != null)
                    Destroy(state.Root);
            }

            _activeStates.Clear();
            _loadRequestIdsBySlot.Clear();
            if (!skipCharacterMeshReset && !_isDestroying)
                UpdateCharacterMeshOverride(string.Empty);
        }

        private void LoadDiagnosticsFile()
        {
            _diagnosticsByKey.Clear();
            _lastDiagnosticSignatureByKey.Clear();
            if (!writeEquipVisualDiagnostics)
                return;

            try
            {
                string path = ResolveDiagnosticsPath();
                if (!File.Exists(path))
                    return;

                var parsed = JsonConvert.DeserializeObject<EquippedVisualDiagnosticsFile>(File.ReadAllText(path));
                if (parsed?.Entries == null)
                    return;

                foreach (var entry in parsed.Entries)
                {
                    if (entry == null || entry.AOID <= 0 || entry.SlotId <= 0)
                        continue;

                    string key = BuildDiagnosticKey(entry.AOID, entry.SlotId);
                    _diagnosticsByKey[key] = entry;
                    _lastDiagnosticSignatureByKey[key] = BuildDiagnosticSignature(
                        entry.AOID,
                        entry.ItemName,
                        entry.SlotId,
                        entry.AnchorKey,
                        entry.StatId,
                        entry.CatalogMeshKey,
                        entry.ResolvedMeshKey,
                        entry.ResolutionMode,
                        entry.ResolutionNotes);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load equipped visual diagnostics file: {ex.Message}");
            }
        }

        private void UpsertDiagnostic(
            int aoid,
            string itemName,
            int slotId,
            string anchorKey,
            int statId,
            string catalogMeshKey,
            string resolvedMeshKey,
            string resolutionMode,
            string resolutionNotes)
        {
            if (!writeEquipVisualDiagnostics || aoid <= 0 || slotId <= 0)
                return;

            string key = BuildDiagnosticKey(aoid, slotId);
            string signature = BuildDiagnosticSignature(
                aoid,
                itemName,
                slotId,
                anchorKey,
                statId,
                catalogMeshKey,
                resolvedMeshKey,
                resolutionMode,
                resolutionNotes);

            if (_lastDiagnosticSignatureByKey.TryGetValue(key, out var prev) && string.Equals(prev, signature, StringComparison.Ordinal))
                return;

            ComputeMeshAvailability(resolvedMeshKey, out bool inResources, out bool inStreaming);

            if (!_diagnosticsByKey.TryGetValue(key, out var entry) || entry == null)
            {
                entry = new EquippedVisualDiagnosticEntry
                {
                    AOID = aoid,
                    SlotId = slotId,
                    SeenCount = 0
                };
                _diagnosticsByKey[key] = entry;
            }

            entry.AOID = aoid;
            entry.ItemName = itemName ?? string.Empty;
            entry.SlotId = slotId;
            entry.AnchorKey = anchorKey ?? string.Empty;
            entry.StatId = statId;
            entry.CatalogMeshKey = catalogMeshKey ?? string.Empty;
            entry.ResolvedMeshKey = resolvedMeshKey ?? string.Empty;
            entry.ResolutionMode = resolutionMode ?? string.Empty;
            entry.ResolutionNotes = resolutionNotes ?? string.Empty;
            entry.VisualSystem = ResolveVisualSystem(entry.ResolutionMode, entry.ResolvedMeshKey);
            entry.MeshFamily = ResolveMeshFamily(entry.ResolvedMeshKey);
            entry.ResolvedMeshInResources = inResources;
            entry.ResolvedMeshInStreamingAssets = inStreaming;
            entry.LastSeenUtc = DateTime.UtcNow.ToString("o");
            entry.SeenCount = Mathf.Max(0, entry.SeenCount) + 1;

            _lastDiagnosticSignatureByKey[key] = signature;
            SaveDiagnosticsFile();
        }

        private void SaveDiagnosticsFile()
        {
            if (!writeEquipVisualDiagnostics)
                return;

            try
            {
                var file = new EquippedVisualDiagnosticsFile
                {
                    Entries = _diagnosticsByKey.Values
                        .Select(NormalizeDiagnosticDerivedFields)
                        .OrderBy(e => e.AOID)
                        .ThenBy(e => e.SlotId)
                        .ToList()
                };

                string path = ResolveDiagnosticsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
                SaveReviewFile(file.Entries);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save equipped visual diagnostics file: {ex.Message}");
            }
        }

        private void SaveReviewFile(List<EquippedVisualDiagnosticEntry> entries)
        {
            if (!writeEquipVisualReview)
                return;

            try
            {
                entries ??= new List<EquippedVisualDiagnosticEntry>();
                var unresolved = entries
                    .Where(e => e != null && string.Equals(e.ResolutionMode, "Unresolved", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.ItemName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.SlotId)
                    .ToList();

                var fallback = entries
                    .Where(e => e != null
                        && string.Equals(e.ResolutionMode, "FallbackWearable", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(e.ResolvedMeshKey))
                    .OrderBy(e => e.ItemName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.SlotId)
                    .ToList();

                var suggestions = fallback
                    .GroupBy(e => e.AOID)
                    .Select(g => g.First())
                    .Select(e => new VisualOverrideEntry
                    {
                        AOID = e.AOID,
                        MeshKey = e.ResolvedMeshKey,
                        AnchorKey = e.AnchorKey,
                        StatId = e.StatId > 0 ? e.StatId : null
                    })
                    .OrderBy(o => o.AOID)
                    .ToList();

                var review = new EquippedVisualReviewFile
                {
                    GeneratedUtc = DateTime.UtcNow.ToString("o"),
                    Unresolved = unresolved,
                    FallbackResolved = fallback,
                    SuggestedOverrides = suggestions
                };

                string path = Path.Combine(Application.streamingAssetsPath, "AOData", equipVisualReviewFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                File.WriteAllText(path, JsonConvert.SerializeObject(review, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to save equipped visual review file: {ex.Message}");
            }
        }

        private string ResolveDiagnosticsPath()
        {
            return Path.Combine(Application.streamingAssetsPath, "AOData", equipVisualDiagnosticsFileName);
        }

        private static string BuildDiagnosticKey(int aoid, int slotId)
        {
            return $"{aoid}:{slotId}";
        }

        private static string BuildDiagnosticSignature(
            int aoid,
            string itemName,
            int slotId,
            string anchorKey,
            int statId,
            string catalogMeshKey,
            string resolvedMeshKey,
            string resolutionMode,
            string resolutionNotes)
        {
            return string.Join("|",
                aoid.ToString(),
                (itemName ?? string.Empty).Trim(),
                slotId.ToString(),
                (anchorKey ?? string.Empty).Trim(),
                statId.ToString(),
                (catalogMeshKey ?? string.Empty).Trim(),
                (resolvedMeshKey ?? string.Empty).Trim(),
                (resolutionMode ?? string.Empty).Trim(),
                (resolutionNotes ?? string.Empty).Trim());
        }

        private static EquippedVisualDiagnosticEntry NormalizeDiagnosticDerivedFields(EquippedVisualDiagnosticEntry entry)
        {
            if (entry == null)
                return null;

            entry.VisualSystem = ResolveVisualSystem(entry.ResolutionMode, entry.ResolvedMeshKey);
            entry.MeshFamily = ResolveMeshFamily(entry.ResolvedMeshKey);
            return entry;
        }

        private static string ResolveVisualSystem(string resolutionMode, string resolvedMeshKey)
        {
            if (string.Equals(resolutionMode, "CharacterMeshOverride", StringComparison.OrdinalIgnoreCase))
                return "CharacterMeshOverride";

            if (string.Equals(resolutionMode, "Unresolved", StringComparison.OrdinalIgnoreCase))
                return "UnresolvedUnknown";

            if (string.IsNullOrWhiteSpace(resolvedMeshKey))
                return "UnresolvedUnknown";

            return "ItemMeshAttachment";
        }

        private static string ResolveMeshFamily(string resolvedMeshKey)
        {
            if (string.IsNullOrWhiteSpace(resolvedMeshKey))
                return "None";

            string key = resolvedMeshKey.Trim();
            if (key.IndexOf("cloak", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Cloak";
            if (key.IndexOf("backitem", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("backpack", StringComparison.OrdinalIgnoreCase) >= 0)
                return "BackItem";
            if (key.IndexOf("_armour_", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("_armor_", StringComparison.OrdinalIgnoreCase) >= 0)
                return "ArmourBody";
            if (key.IndexOf("helmet", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("hood", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Headwear";
            if (key.IndexOf("boot", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("feet", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Footwear";

            return "Other";
        }

        private void ComputeMeshAvailability(string meshKey, out bool inResources, out bool inStreamingAssets)
        {
            inResources = false;
            inStreamingAssets = false;
            if (string.IsNullOrWhiteSpace(meshKey))
                return;

            try
            {
                inResources = Resources.Load<GameObject>($"{itemMeshResourcesFolder}/{meshKey}") != null;
            }
            catch
            {
                inResources = false;
            }

            inStreamingAssets = !string.IsNullOrWhiteSpace(ResolveItemMeshPath(meshKey));
        }
    }
}
