using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine;
using UnityEngine.UI;
using AO.Unity;
using AO.Unity.Assets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.World
{
    public sealed partial class CharacterAppearanceController : MonoBehaviour
    {
        [Serializable]
        private sealed class AppearanceMapFile
        {
            public Dictionary<string, int> breed_texture_ids = new();
            public Dictionary<string, float> breed_scale_multipliers = new();
            public Dictionary<string, string> breed_mesh_resources = new();
        }

        [Serializable]
        private sealed class AnimationMapFile
        {
            public Dictionary<string, string> swaps = new();
            public Dictionary<string, string> animations = new();
        }

        [Serializable]
        private sealed class AttackAnimationRulesFile
        {
            public bool enabled = true;
            public AttackAnimationRule unarmed = new();
            public List<AttackAnimationRule> rules = new();
        }

        [Serializable]
        private sealed class HeadPreviewOffsetsFile
        {
            public List<HeadPreviewOffsetEntry> entries = new();
        }

        [Serializable]
        private sealed class HeadPreviewOffsetEntry
        {
            public string meshKey = string.Empty;
            public float x;
            public float y;
            public float z;
            public float rx;
            public float ry;
            public float rz;
            public float anchorNormalizedY = -1f;
        }

        [Serializable]
        private sealed class AttackAnimationRule
        {
            public string id = string.Empty;
            public bool enabled = true;
            public string handMode = "any"; // any | single_only | either_or_both
            public List<int> attackSkillIds = new();
            public string actionKey = "Idle MA";
            public string startActionKey = string.Empty;
            public string idleActionKey = string.Empty;
            public string stopActionKey = string.Empty;
            public List<string> hitActionKeys = new();
            public List<string> specialActionKeys = new();
            public int specialEveryHits = 0;
            public float hitIntervalSeconds = 1.05f;
            public List<string> clipCandidates = new();
            public Dictionary<string, List<string>> clipCandidatesBySex = new();
        }

        private enum WeaponHandMode
        {
            Any = 0,
            SingleOnly = 1,
            EitherOrBoth = 2
        }

        [Serializable]
        private sealed class ItemMeshManifestEntry
        {
            public int StatelId;
            public string MeshName;
        }

        private sealed class TimedVfxInstance
        {
            public GameObject Root;
            public float ExpireAt;
        }

        private sealed class ActiveProjectile
        {
            public GameObject Root;
            public Vector3 Start;
            public Vector3 End;
            public float StartAt;
            public float Duration;
            public float ArcHeight;
            public bool ExplodeOnImpact;
        }

        [Serializable]
        private sealed class WeaponVfxMapFile
        {
            public List<WeaponVfxMapEntry> Entries = new();
        }

        [Serializable]
        private sealed class WeaponVfxMapEntry
        {
            public int AOID;
            public int AttackSkillId;
            public string VfxKey;
        }

        [SerializeField] private CharacterRuntimeBridge bridge;
        [SerializeField] private CharacterAnimationController animationController;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private bool createVisualIfMissing = true;
        [SerializeField] private string generatedVisualName = "PlayerVisual";
        [SerializeField] private Vector3 generatedVisualLocalPosition = new Vector3(0f, 0.9f, 0f);
        [SerializeField] private string bodyTextureSizeFolder = "Small";
        [SerializeField] private string characterMeshResourcesFolder = "CharacterMeshes";
        [SerializeField] private bool allowCharacterMeshGltfFallback = true;
        [SerializeField] private bool allowCharacterMeshEmbeddedDataUriGltf = false;
        [SerializeField] private bool sanitizeCharacterMeshGlbDataUris = true;
        [SerializeField] private bool disableCharacterMeshAnimations = false;
        [SerializeField] private string appearanceMapFileName = "character_appearance_map.json";
        [SerializeField] private string animationsMapFileName = "animations.json";
        [SerializeField] private string attackAnimationRulesFileName = "attack_animation_rules.json";
        [SerializeField] private string headPreviewOffsetsFileName = "head_preview_offsets.json";
        [SerializeField] private int fallbackTextureId = 8760;
        [SerializeField] private bool overrideImportedMeshTextures = false;
        [SerializeField] private bool flipCharacterVisualX = true;
        [SerializeField] private float animationBlendSpeed = 5f;
        [SerializeField] private bool playMoveStartClip = false;
        [SerializeField] private bool enableUnarmedAttackToggle = true;
        [SerializeField] private bool attackToggleActive;
        public bool IsAttackToggleActive => attackToggleActive;
        [SerializeField] private string idleClipOverride = string.Empty;
        [SerializeField] private string moveStartClipOverride = string.Empty;
        [SerializeField] private string moveClipOverride = string.Empty;
        [SerializeField] private string attackClipOverride = string.Empty;
        private string _temporaryIdleClipOverride = string.Empty;
        private string _temporaryMoveClipOverride = string.Empty;
        [SerializeField] private float moveClipLoopStartSeconds = 0.95f;
        [SerializeField] private float moveClipLoopEndSeconds = 2.18f;
        [SerializeField] private List<string> availableAnimationClipNames = new();
        [Header("Temp Item Visuals")]
        [SerializeField] private bool enableTemporaryItemVisuals = true;
        [SerializeField] private string tempWeaponTestItemName = "Omni Med Scalpel";
        [SerializeField] private string[] tempWeaponMeshCandidates = { "weapon_smallknife01", "weapon_knife" };
        [SerializeField] private string itemMeshResourcesFolder = "ItemMeshes";
        [SerializeField] private bool allowItemMeshGltfFallback = true;
        [SerializeField] private bool allowItemMeshEmbeddedDataUriGltf = false;
        [SerializeField] private bool sanitizeItemMeshGlbDataUris = true;
        [SerializeField] private int rightHandEquipSlotId = 6;
        [SerializeField] private int leftHandEquipSlotId = 8;
        [SerializeField] private string forceRightHandBoneName = string.Empty;
        [SerializeField] private string forceLeftHandBoneName = string.Empty;
        [SerializeField] private bool logTemporaryItemAttachment = false;
        [SerializeField] private bool logAttackTimingResolution = false;
        [SerializeField] private Vector3 rightHandItemLocalPosition = new Vector3(0.02f, 0.02f, 0.02f);
        [SerializeField] private Vector3 rightHandItemLocalEuler = new Vector3(0f, 90f, 0f);
        [SerializeField] private Vector3 rightHandItemLocalScale = new Vector3(0.8f, 0.8f, 0.8f);
        [SerializeField] private Vector3 leftHandItemLocalPosition = new Vector3(-0.02f, 0.02f, 0.02f);
        [SerializeField] private Vector3 leftHandItemLocalEuler = new Vector3(0f, -90f, 0f);
        [SerializeField] private Vector3 leftHandItemLocalScale = new Vector3(0.8f, 0.8f, 0.8f);
        [Header("Weapon VFX")]
        [SerializeField] private bool enableWeaponShotVfx = true;
        [SerializeField] private bool useWeaponVfxMapOnly = true;
        [SerializeField] private bool allowMeshBasedWeaponShotVfx = false;
        [SerializeField] private string weaponVfxMapFileName = "weapon_vfx_map.json";
        [SerializeField] private bool logWeaponShotVfx = false;
        [SerializeField] private float weaponShotVfxLifetimeSeconds = 0.18f;
        [SerializeField] private float weaponShotVfxMinSpawnIntervalSeconds = 0.03f;
        [SerializeField] private Vector3 rightHandShotVfxLocalPosition = new Vector3(0.14f, 0f, 0f);
        [SerializeField] private Vector3 rightHandShotVfxLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 rightHandShotVfxLocalScale = Vector3.one;
        [SerializeField] private Vector3 leftHandShotVfxLocalPosition = new Vector3(0.14f, 0f, 0f);
        [SerializeField] private Vector3 leftHandShotVfxLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 leftHandShotVfxLocalScale = Vector3.one;
        [SerializeField] private float proceduralTracerDistance = 30f;
        [SerializeField] private float proceduralTracerLifetime = 0.12f;
        [SerializeField] private float proceduralTracerWidth = 0.03f;
        [SerializeField] private float proceduralSmokeLifetime = 0.35f;
        [SerializeField] private int proceduralSmokeBurstCount = 8;
        [SerializeField] private bool enableMeleeHitVfx = true;
        [SerializeField] private float meleeHitVfxMinSpawnIntervalSeconds = 0.04f;
        [SerializeField] private float meleeHitVfxLifetimeSeconds = 0.35f;
        [SerializeField] private int meleeHitSparkCount = 3;
        [SerializeField] private float meleeHitFlashSize = 0.18f;
        [Header("Debug Head Preview")]
        [SerializeField] private Vector3 debugHeadLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 debugHeadLocalEuler = new Vector3(90f, 90f, 0f);
        [SerializeField] private Vector3 debugHeadLocalScale = Vector3.one;
        [SerializeField, Range(0f, 1f)] private float debugHeadAnchorNormalizedY = 0.18f;
        [SerializeField, Range(0f, 1f)] private float opifexFemaleHeadAnchorNormalizedY = 0.15f;

        private readonly Dictionary<int, Texture2D> _textureCache = new();
        private readonly Dictionary<int, Texture2D> _generalTextureCache = new();
        private readonly Dictionary<Material, (Texture baseMap, Texture mainTex)> _bodyMaterialTextureSnapshot = new();
        private readonly Dictionary<int, int> _breedTextureIds = new()
        {
            { 1, 8760 }, // Solitus
            { 2, 8854 }, // Opifex
            { 3, 8967 }, // Nanomage
            { 4, 9217 }, // Atrox
            { 7, 9255 }  // HumanMonster
        };
        private readonly Dictionary<int, float> _breedScaleMultipliers = new()
        {
            { 1, 1.00f },
            { 2, 0.96f },
            { 3, 0.94f },
            { 4, 1.10f },
            { 7, 1.00f }
        };
        private readonly Dictionary<int, string> _breedMeshResourceNames = new()
        {
            { 1, "solitus_male" },
            { 2, "opifex_male" },
            { 3, "nanomage_male" },
            { 4, "athrox_male" }
        };
        private readonly Dictionary<string, string> _animationSwaps = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _animationTemplates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> _headPreviewOffsetByMeshKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Vector3> _headPreviewEulerByMeshKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _headPreviewAnchorYByMeshKey = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Vector3 DefaultDebugHeadLocalEuler = new Vector3(90f, 90f, 0f);

        private const string ActionRun = "Run";
        private const string ActionWalk = "Walk";
        private const string ActionRunBackwards = "Run Backwards";
        private const string ActionStrafeRight = "Strafe right";
        private const string ActionStrafeLeft = "Strafe Left";
        private const string ActionSitDown = "Sit Down";
        private const string ActionIdleSit = "Idle Sit";
        private const string ActionStandUp = "Stand Up";
        private const string ActionStartAttackMa = "Start Attack MA";
        private const string ActionIdleMa = "Idle MA";
        private const string ActionJumpForward = "Jump Forward";
        private const string ActionJumpFromIdle = "Jumping from idle stance";
        private const string ActionLandAfterJump = "Land after jump";
        private const string ActionLandAfterJumpRun = "Land after jump and keep running";
        private const string PreferredAttackIdleClip = "_idle-unarmed_01_01(Clone)";

        private GameObject _generatedVisual;
        private GameObject _spawnedPrefabVisual;
        private object _runtimeVisualImporter;
        private GameObject _debugHeadVisual;
        private object _debugHeadVisualImporter;
        private string _debugHeadMeshKey = string.Empty;
        private int _debugHeadLoadRequestId;
        private string _debugHeadLoadInProgressKey = string.Empty;
        private Material _runtimeMaterial;
        private Vector3 _baseVisualScale = Vector3.one;
        private int _lastBreedId = int.MinValue;
        private bool _initializedScale;
        private string _currentMeshResourceName = string.Empty;
        private int _meshLoadRequestId;
        private string _meshLoadInProgressName = string.Empty;
        private bool _characterVisualsVisible = true;
        private Animator _spawnedAnimator;
        private PlayableGraph _animationGraph;
        private AnimationMixerPlayable _animationMixer;
        private AnimationClipPlayable _idleClipPlayable;
        private AnimationClipPlayable _moveClipPlayable;
        private AnimationClipPlayable _attackClipPlayable;
        private bool _animationGraphReady;
        private Animation _legacyAnimation;
        private bool _legacyAnimationReady;
        private string _idleLegacyClipName = string.Empty;
        private string _moveStartLegacyClipName = string.Empty;
        private string _moveLegacyClipName = string.Empty;
        private string _attackLegacyClipName = string.Empty;
        private bool _legacyMovePlaying;
        private bool _legacyStartPlaying;
        private bool _legacyAttackPlaying;
        private float _legacyStartEndTime;
        private float _moveBlendWeight;
        private float _attackBlendWeight;
        private float _moveLoopStartResolved;
        private float _moveLoopEndResolved;
        private float _movePlayableLoopTime;
        private bool _walkModeEnabled;
        private float _externalMoveUntil = -1f;
        private string _externalMoveAction = ActionRun;
        private bool _sitToggled;
        private bool _sitTransitionPlaying;
        private float _sitTransitionEndTime;
        private bool _standingUpTransition;
        private string _lastResolvedSexToken = string.Empty;
        private CharacterRuntimeBridge.CharacterSex _lastSex = CharacterRuntimeBridge.CharacterSex.Male;
        private string _activeMoveAction = ActionRun;
        private string _runtimeAttackClipOverride = string.Empty;
        private string _temporaryMeshResourceOverride = string.Empty;
        private string _lastTemporaryMeshResourceOverride = string.Empty;
        private string _runtimeAttackActionKey = ActionIdleMa;
        private bool _runtimeAttackLoopEnabled = true;
        private float _runtimeAttackStartTime;
        private float _attackLoopStartResolved;
        private float _attackLoopEndResolved;
        private float _attackPlayableLoopTime;
        private float _attackOneShotStartWallTime;
        private float _attackOneShotDuration;
        private CharacterController _characterController;
        private bool _wasGroundedLastFrame = true;
        private bool _jumpInAir;
        private bool _jumpLandingTransitionPlaying;
        private float _jumpTransitionEndTime;
        private bool _jumpForwardIntentLatched;
        private bool _jumpStartedWithForwardIntent;
        private bool _jumpPlayedForwardAction;
        private string _pendingJumpActionKey = string.Empty;
        private float _lastJumpKeyPressTime = -10f;
        private bool _restoreAttackPoseAfterJump;
        private float _localLocomotionForward;
        private float _localLocomotionStrafe;
        private int _localLocomotionIntentFrame = -1;
        private GameObject _equippedItemVisual;
        private string _equippedItemVisualKey = string.Empty;
        private int _equippedItemVisualSlotId = -1;
        private int _itemMeshLoadRequestId;
        private string _itemMeshLoadInProgressKey = string.Empty;
        private bool _equippedItemOnLeftHand;
        private Vector3 _lastRootPosition;
        private bool _hasLastRootPosition;
        private bool _isDestroying;
        private string _lastEquippedTextureSignature = string.Empty;
        private int _lastEquippedTextureVisualInstanceId;
        private readonly Dictionary<int, int> _lastAppliedEquippedTextureByLocation = new();
        private readonly Dictionary<int, Material[]> _rendererMaterialCache = new();
        private AttackAnimationRulesFile _attackAnimationRules = new();
        private string _lastAttackAnimationSignature = string.Empty;
        private AttackAnimationRule _activeAttackRule;
        private string _activeAttackIdleActionKey = ActionIdleMa;
        private float _attackRuleNextHitTime;
        private float _attackRuleOneShotEndTime;
        private int _attackRuleHitCounter;
        private int _attackRuleHitCursor;
        private int _attackRuleSpecialCursor;
        private bool _attackPreferLeftNext = true;
        private bool _attackRightWeaponEquipped;
        private bool _attackLeftWeaponEquipped;
        private string _attackLastActionKey = string.Empty;
        private bool _attackCycleRunning;
        private bool _attackCyclePaused;
        private float _attackCyclePauseStartedAt;
        private bool _attackPhaseIsRecharge;
        private float _attackPhaseStartTime;
        private float _attackPhaseDuration = 1f;
        private float _attackPhaseAttackSeconds = 1f;
        private float _attackPhaseRechargeSeconds = 1f;
        private int _attackPhaseSide;
        private bool _freezeAttackLoopPose;
        private Canvas _attackCycleCanvas;
        private RectTransform _attackCycleRoot;
        private Image _attackCycleBackgroundImage;
        private Image _attackCycleFillImage;
        private Texture2D _attackCycleUiTexture;
        private Sprite _attackCycleUiSprite;
        private int _activeAttackSkillId;
        private bool _activeAttackIsRanged;
        private bool _externalAttackCyclePaused;
        private bool _externalDeathPlaybackLocked;
        private float _externalDeathPlaybackLockUntil;
        private float _lastWeaponShotVfxSpawnTime = -999f;
        private bool _itemMeshManifestLoaded;
        private bool _spellFormatsLoaded;
        private bool _abiffNamesLoaded;
        private bool _weaponVfxMapLoaded;
        private readonly Dictionary<int, string> _itemMeshKeyByStatelId = new();
        private readonly Dictionary<int, string> _spellFormatById = new();
        private readonly Dictionary<int, string> _abiffNamesById = new();
        private readonly Dictionary<int, string> _weaponVfxKeyByAoid = new();
        private readonly Dictionary<int, string> _weaponVfxKeyByAttackSkill = new();
        private readonly Dictionary<string, GameObject> _weaponShotVfxTemplateByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, object> _weaponShotVfxImporterByKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _weaponShotVfxLoadInProgress = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _weaponShotVfxFailedKeys = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TimedVfxInstance> _activeWeaponShotVfx = new();
        private readonly List<ActiveProjectile> _activeProjectiles = new();
        private Material _proceduralSmokeMaterial;
        private Material _proceduralTracerMaterial;
        private Material _proceduralBulletMaterial;
        private Material _proceduralGrenadeMaterial;
        private Material _proceduralHitMaterial;
        private float _lastMeleeHitVfxSpawnTime;
        private static readonly Dictionary<int, string> KnownAttractorFxByCode = new()
        {
            { 2000, "fx_muzzleflash" },
            { 2101, "fx_muzzleflash" },
            { 2601, "fx_muzzleflash" },
            { 2660, "fx_muzzleflash" },
            { 2710, "fx_muzzleflash" },
            { 2771, "fx_muzzleflash" }
        };

        public void ConfigureCreateVisualIfMissing(bool value)
        {
            createVisualIfMissing = value;
        }

        public void ConfigureDisableCharacterMeshAnimations(bool value)
        {
            disableCharacterMeshAnimations = value;
        }

        public void SetTemporaryMeshResourceOverride(string meshResourceName)
        {
            if (_isDestroying || !Application.isPlaying)
                return;

            string normalized = meshResourceName?.Trim() ?? string.Empty;
            if (string.Equals(_temporaryMeshResourceOverride, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            _temporaryMeshResourceOverride = normalized;
            ApplyIfChanged(force: true);
        }

        public void ClearTemporaryMeshResourceOverride()
        {
            if (_isDestroying || !Application.isPlaying)
                return;

            if (string.IsNullOrWhiteSpace(_temporaryMeshResourceOverride))
                return;

            _temporaryMeshResourceOverride = string.Empty;
            ApplyIfChanged(force: true);
        }

        public void PrewarmCurrentVisual()
        {
            int breedId = bridge?.Character?.BreedId ?? 1;
            EnsureBreedVisualPrefab(breedId <= 0 ? 1 : breedId);
        }

        public bool IsCurrentBodyVisualReady
        {
            get
            {
                if (bridge?.Character == null || _spawnedPrefabVisual == null || targetRenderer == null)
                    return false;

                int breedId = bridge.Character.BreedId <= 0 ? 1 : bridge.Character.BreedId;
                if (!_breedMeshResourceNames.TryGetValue(breedId, out string baseName))
                    return false;
                string expected = ResolveMeshResourceForSex(baseName, breedId);
                if (!string.IsNullOrWhiteSpace(_temporaryMeshResourceOverride))
                    expected = _temporaryMeshResourceOverride;
                return string.Equals(_currentMeshResourceName, expected,
                    StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(_meshLoadInProgressName);
            }
        }

        public bool IsCurrentHeadVisualReady
        {
            get
            {
                string expected = bridge != null ? bridge.DebugHeadMeshKey ?? string.Empty : string.Empty;
                return string.IsNullOrWhiteSpace(expected)
                    ? _debugHeadVisual == null && string.IsNullOrWhiteSpace(_debugHeadLoadInProgressKey)
                    : _debugHeadVisual != null
                        && string.Equals(_debugHeadMeshKey, expected, StringComparison.OrdinalIgnoreCase)
                        && string.IsNullOrWhiteSpace(_debugHeadLoadInProgressKey);
            }
        }

        public void SetBodyVisualVisible(bool visible)
        {
            _characterVisualsVisible = visible;
            if (_spawnedPrefabVisual != null)
                _spawnedPrefabVisual.SetActive(visible);
            if (_generatedVisual != null)
                _generatedVisual.SetActive(visible);
            if (_debugHeadVisual != null)
                _debugHeadVisual.SetActive(visible);
        }

        public string CurrentMeshResourceName => _currentMeshResourceName ?? string.Empty;

        public Vector3 GetDebugHeadLocalEuler() => debugHeadLocalEuler;

        public void AdjustDebugHeadLocalEuler(Vector3 delta)
        {
            debugHeadLocalEuler = NormalizeEuler(debugHeadLocalEuler + delta);
            ApplyDebugHeadVisualTransform();
        }

        public Vector3 GetDebugHeadLocalPosition() => debugHeadLocalPosition;

        public void AdjustDebugHeadLocalPosition(Vector3 delta)
        {
            debugHeadLocalPosition += delta;
            ApplyDebugHeadVisualTransform();
        }

        public string GetCurrentDebugHeadMeshKey()
        {
            return _debugHeadMeshKey ?? string.Empty;
        }

        public Vector3 GetCurrentDebugHeadMeshOffset()
        {
            if (string.IsNullOrWhiteSpace(_debugHeadMeshKey))
                return Vector3.zero;
            return _headPreviewOffsetByMeshKey.TryGetValue(_debugHeadMeshKey, out var value)
                ? value
                : Vector3.zero;
        }

        public Vector3 GetCurrentDebugHeadMeshEulerOffset()
        {
            if (string.IsNullOrWhiteSpace(_debugHeadMeshKey))
                return Vector3.zero;
            return _headPreviewEulerByMeshKey.TryGetValue(_debugHeadMeshKey, out var value)
                ? value
                : Vector3.zero;
        }

        public void AdjustCurrentDebugHeadMeshOffset(Vector3 delta)
        {
            if (string.IsNullOrWhiteSpace(_debugHeadMeshKey))
                return;

            Vector3 value = GetCurrentDebugHeadMeshOffset() + delta;
            _headPreviewOffsetByMeshKey[_debugHeadMeshKey] = value;
            ApplyDebugHeadVisualTransform();
        }

        public void AdjustCurrentDebugHeadMeshEulerOffset(Vector3 delta)
        {
            if (string.IsNullOrWhiteSpace(_debugHeadMeshKey))
                return;

            Vector3 value = NormalizeEuler(GetCurrentDebugHeadMeshEulerOffset() + delta);
            _headPreviewEulerByMeshKey[_debugHeadMeshKey] = value;
            ApplyDebugHeadVisualTransform();
        }

        public bool SaveHeadPreviewOffsets()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(headPreviewOffsetsFileName))
                    return false;

                string file = Path.Combine(
                    Application.streamingAssetsPath,
                    "AOData",
                    headPreviewOffsetsFileName);

                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in _headPreviewOffsetByMeshKey.Keys)
                    keys.Add(key);
                foreach (var key in _headPreviewEulerByMeshKey.Keys)
                    keys.Add(key);
                foreach (var key in _headPreviewAnchorYByMeshKey.Keys)
                    keys.Add(key);

                var entries = keys
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .Select(key =>
                    {
                        Vector3 pos = _headPreviewOffsetByMeshKey.TryGetValue(key, out var p) ? p : Vector3.zero;
                        Vector3 euler = _headPreviewEulerByMeshKey.TryGetValue(key, out var r) ? r : Vector3.zero;
                        float anchorY = _headPreviewAnchorYByMeshKey.TryGetValue(key, out var a) ? a : -1f;
                        return new HeadPreviewOffsetEntry
                        {
                            meshKey = key,
                            x = pos.x,
                            y = pos.y,
                            z = pos.z,
                            rx = euler.x,
                            ry = euler.y,
                            rz = euler.z,
                            anchorNormalizedY = anchorY
                        };
                    })
                    .ToList();

                var root = new HeadPreviewOffsetsFile { entries = entries };
                string json = JsonConvert.SerializeObject(root, Formatting.Indented);
                File.WriteAllText(file, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed saving {headPreviewOffsetsFileName}: {ex.Message}");
                return false;
            }
        }

        public void ReloadHeadPreviewOffsets()
        {
            LoadHeadPreviewOffsets();
            ApplyDebugHeadVisualTransform();
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(
                NormalizeAngle(euler.x),
                NormalizeAngle(euler.y),
                NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f)
                angle -= 360f;
            if (angle <= -180f)
                angle += 360f;
            return angle;
        }

        private void Awake()
        {
            if (bridge == null)
                bridge = GetComponent<CharacterRuntimeBridge>();
            if (animationController == null)
                animationController = GetComponent<CharacterAnimationController>();
            if (animationController == null)
                animationController = gameObject.AddComponent<CharacterAnimationController>();
            if (_characterController == null)
                _characterController = GetComponent<CharacterController>();

            if (debugHeadLocalEuler == Vector3.zero)
                debugHeadLocalEuler = DefaultDebugHeadLocalEuler;
            else
                debugHeadLocalEuler = DefaultDebugHeadLocalEuler;

            // Ensure mirrored exports are corrected by default during runtime.
            flipCharacterVisualX = true;
            // Legacy temporary weapon visual path can duplicate GLB loads and cause stalls.
            // EquippedItemVisualController is the authoritative equip visual path now.
            enableTemporaryItemVisuals = false;

            LoadAppearanceMapOverrides();
            LoadAnimationsMap();
            LoadAttackAnimationRules();
            LoadHeadPreviewOffsets();
        }

        private void Start()
        {
            ApplyIfChanged(force: true);
        }

        private void OnValidate()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
                return;

            ApplyAnimationOverridesNow();
            ApplyTemporaryItemVisualTransform();
        }

        private void Update()
        {
            UpdateWeaponShotVfxLifetime();
            HandleWalkToggleInput();
            HandleSitToggleInput();
            HandleAttackToggleInput();
            RefreshAttackAnimationRuleIfNeeded();
            ApplyIfChanged(force: false);
            RefreshEquippedBodyTextures();
            RefreshDebugHeadVisual();
            RefreshAnimationMappingsIfNeeded();
        }

        private void OnDestroy()
        {
            _isDestroying = true;
            _meshLoadRequestId++;
            _debugHeadLoadRequestId++;
            _itemMeshLoadRequestId++;
            _meshLoadInProgressName = string.Empty;
            _debugHeadLoadInProgressKey = string.Empty;
            _itemMeshLoadInProgressKey = string.Empty;

            DisposeAnimationGraph();

            if (_runtimeMaterial != null)
                Destroy(_runtimeMaterial);

            DisposeImporter(_runtimeVisualImporter);
            _runtimeVisualImporter = null;
            DisposeImporter(_debugHeadVisualImporter);
            _debugHeadVisualImporter = null;

            if (_spawnedPrefabVisual != null)
                Destroy(_spawnedPrefabVisual);
            if (_debugHeadVisual != null)
                Destroy(_debugHeadVisual);

            ClearTemporaryItemVisual();
            ClearWeaponShotVfxState();

            foreach (var texture in _textureCache.Values)
            {
                if (texture != null)
                    Destroy(texture);
            }

            _textureCache.Clear();

            foreach (var texture in _generalTextureCache.Values)
            {
                if (texture != null)
                    Destroy(texture);
            }

            _generalTextureCache.Clear();
            _bodyMaterialTextureSnapshot.Clear();
            _rendererMaterialCache.Clear();

            if (_attackCycleCanvas != null)
                Destroy(_attackCycleCanvas.gameObject);
            if (_attackCycleUiSprite != null)
                Destroy(_attackCycleUiSprite);
            if (_attackCycleUiTexture != null)
                Destroy(_attackCycleUiTexture);
        }

        private void ApplyIfChanged(bool force)
        {
            if (_isDestroying || !Application.isPlaying)
                return;

            if (bridge == null)
                bridge = GetComponent<CharacterRuntimeBridge>();

            var character = bridge?.Character;
            if (character == null)
                return;

            int breedId = character.BreedId <= 0 ? 1 : character.BreedId;
            var sex = bridge != null ? bridge.Sex : CharacterRuntimeBridge.CharacterSex.Male;
            if (!force
                && breedId == _lastBreedId
                && sex == _lastSex
                && string.Equals(_temporaryMeshResourceOverride, _lastTemporaryMeshResourceOverride, StringComparison.OrdinalIgnoreCase))
                return;

            EnsureBreedVisualPrefab(breedId);
            EnsureRenderer();
            if (targetRenderer == null)
                return;

            _lastBreedId = breedId;
            _lastSex = sex;
            _lastTemporaryMeshResourceOverride = _temporaryMeshResourceOverride;
            ApplyBreedScale(breedId);
            ApplyBreedTexture(breedId);
        }

        private void EnsureRenderer()
        {
            if (targetRenderer != null)
                return;

            if (ShouldWaitForBreedVisual())
                return;

            targetRenderer = GetComponentInChildren<Renderer>(true);
            if (targetRenderer != null)
            {
                CaptureBaseScale();
                return;
            }

            if (!createVisualIfMissing)
                return;

            _generatedVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _generatedVisual.name = generatedVisualName;
            _generatedVisual.transform.SetParent(transform, false);
            _generatedVisual.transform.localPosition = generatedVisualLocalPosition;
            _generatedVisual.SetActive(_characterVisualsVisible);
            _currentMeshResourceName = string.Empty;

            var generatedCollider = _generatedVisual.GetComponent<Collider>();
            if (generatedCollider != null)
                Destroy(generatedCollider);

            targetRenderer = _generatedVisual.GetComponent<Renderer>();
            CaptureBaseScale();
        }

        private bool ShouldWaitForBreedVisual()
        {
            if (_spawnedPrefabVisual != null)
                return false;

            if (bridge?.Character == null)
                return false;

            int breedId = bridge.Character.BreedId <= 0 ? 1 : bridge.Character.BreedId;
            if (!_breedMeshResourceNames.TryGetValue(breedId, out var baseMeshResourceName)
                || string.IsNullOrWhiteSpace(baseMeshResourceName))
            {
                return false;
            }

            string meshResourceName = ResolveMeshResourceForSex(baseMeshResourceName, breedId);
            if (!string.IsNullOrWhiteSpace(_temporaryMeshResourceOverride))
                meshResourceName = _temporaryMeshResourceOverride;
            if (string.IsNullOrWhiteSpace(meshResourceName))
                return false;

            if (string.Equals(_meshLoadInProgressName, meshResourceName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (Resources.Load<GameObject>($"{characterMeshResourcesFolder}/{meshResourceName}") != null)
                return true;

            string meshPath = ResolveMeshPath(meshResourceName);
            return !string.IsNullOrWhiteSpace(meshPath);
        }

        private void EnsureBreedVisualPrefab(int breedId)
        {
            if (!_breedMeshResourceNames.TryGetValue(breedId, out var baseMeshResourceName)
                || string.IsNullOrWhiteSpace(baseMeshResourceName))
            {
                return;
            }

            string meshResourceName = ResolveMeshResourceForSex(baseMeshResourceName, breedId);
            if (!string.IsNullOrWhiteSpace(_temporaryMeshResourceOverride))
                meshResourceName = _temporaryMeshResourceOverride;

            if (string.Equals(_currentMeshResourceName, meshResourceName, StringComparison.OrdinalIgnoreCase)
                && _spawnedPrefabVisual != null
                && targetRenderer != null)
            {
                return;
            }

            if (TryLoadBreedVisualFromResources(meshResourceName, breedId))
                return;

            // Runtime load fallback for external mesh files.
            TryStartLoadFromGlb(meshResourceName, breedId);
        }

        private string ResolveMeshResourceForSex(string baseMeshResourceName, int breedId)
        {
            if (string.IsNullOrWhiteSpace(baseMeshResourceName))
                return baseMeshResourceName;

            if (breedId == 4)
                return baseMeshResourceName;

            var sex = bridge != null ? bridge.Sex : CharacterRuntimeBridge.CharacterSex.Male;
            if (sex == CharacterRuntimeBridge.CharacterSex.Female)
            {
                int idx = baseMeshResourceName.IndexOf("_male", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    return baseMeshResourceName.Substring(0, idx) + "_female" + baseMeshResourceName.Substring(idx + "_male".Length);
            }

            return baseMeshResourceName;
        }

        private bool TryLoadBreedVisualFromResources(string meshResourceName, int breedId)
        {
            var prefab = Resources.Load<GameObject>($"{characterMeshResourcesFolder}/{meshResourceName}");
            if (prefab == null)
                return false;

            // Cancel any previous async mesh load request.
            _meshLoadRequestId++;
            _meshLoadInProgressName = string.Empty;

            if (_spawnedPrefabVisual != null)
                Destroy(_spawnedPrefabVisual);

            DisposeImporter(_runtimeVisualImporter);
            _runtimeVisualImporter = null;

            _spawnedPrefabVisual = Instantiate(prefab, transform, false);
            _spawnedPrefabVisual.name = meshResourceName;
            _spawnedPrefabVisual.SetActive(_characterVisualsVisible);
            ApplyVisualRootMirror();
            InvalidateEquippedTextureState();
            RemoveDefaultFallbackVisual();
            if (_generatedVisual != null)
            {
                Destroy(_generatedVisual);
                _generatedVisual = null;
            }

            _currentMeshResourceName = meshResourceName;
            targetRenderer = _spawnedPrefabVisual.GetComponentInChildren<Renderer>(true);
            _initializedScale = false;
            CaptureBaseScale();
            SyncHandsTintToArms();
            InitializeAnimationPlayback();
            ApplyBreedScale(breedId);
            ApplyBreedTexture(breedId);

            return true;
        }

        private void RemoveDefaultFallbackVisual()
        {
            if (targetRenderer == null)
                return;

            var candidate = targetRenderer.transform;
            if (candidate == null || candidate.parent != transform)
                return;

            if (!string.Equals(candidate.name, generatedVisualName, StringComparison.Ordinal))
                return;

            Destroy(candidate.gameObject);
            targetRenderer = null;
            _initializedScale = false;
        }
        private void TryStartLoadFromGlb(string meshResourceName, int breedId)
        {
            if (!Application.isPlaying || _isDestroying || !isActiveAndEnabled)
                return;

            if (string.Equals(_meshLoadInProgressName, meshResourceName, StringComparison.OrdinalIgnoreCase))
                return;

            _meshLoadInProgressName = meshResourceName;
            int requestId = ++_meshLoadRequestId;
            _ = LoadBreedVisualFromGlbAsync(meshResourceName, breedId, requestId);
        }

        private async Task LoadBreedVisualFromGlbAsync(string meshResourceName, int breedId, int requestId)
        {
            object importer = null;
            try
            {
                GameObject directVisual = await DirectCatMeshRuntime.InstantiateAsync(meshResourceName, transform);
                if (directVisual != null)
                {
                    if (!CanAttachRuntimeVisual(requestId))
                    {
                        Destroy(directVisual);
                        return;
                    }

                    var directRenderer = directVisual.GetComponentInChildren<Renderer>(true);
                    if (directRenderer != null)
                    {
                        if (_spawnedPrefabVisual != null)
                            Destroy(_spawnedPrefabVisual);
                        DisposeImporter(_runtimeVisualImporter);
                        _runtimeVisualImporter = null;
                        var directPreviousRenderer = targetRenderer;
                        _spawnedPrefabVisual = directVisual;
                        _spawnedPrefabVisual.SetActive(_characterVisualsVisible);
                        ApplyVisualRootMirror();
                        InvalidateEquippedTextureState();
                        targetRenderer = directPreviousRenderer;
                        RemoveDefaultFallbackVisual();
                        if (_generatedVisual != null)
                        {
                            Destroy(_generatedVisual);
                            _generatedVisual = null;
                        }
                        targetRenderer = directRenderer;
                        _currentMeshResourceName = meshResourceName;
                        _initializedScale = false;
                        CaptureBaseScale();
                        SyncHandsTintToArms();
                        InitializeAnimationPlayback();
                        ApplyBreedScale(breedId);
                        ApplyBreedTexture(breedId);
                        Debug.Log($"Loaded '{meshResourceName}' directly from AO CatMesh data.");
                        return;
                    }
                    Destroy(directVisual);
                }

                string meshPath = ResolveMeshPath(meshResourceName);
                if (string.IsNullOrWhiteSpace(meshPath) || !File.Exists(meshPath))
                    meshPath = await AOCharacterMeshResolver.ResolveAsync(meshResourceName);
                meshPath = GlbDataUriLoadPathResolver.Resolve(meshPath, true);
                if (string.IsNullOrWhiteSpace(meshPath) || !File.Exists(meshPath))
                {
                    Debug.LogWarning($"Character mesh not found for '{meshResourceName}'.");
                    return;
                }

                if (!CanAttachRuntimeVisual(requestId))
                    return;
                GameObject nextVisual = new GameObject(meshResourceName);
                nextVisual.transform.SetParent(transform, false);

                bool instantiated = await TryInstantiateGlbWithReflection(
                    meshPath,
                    nextVisual.transform,
                    loadedImporter => importer = loadedImporter,
                    disableAnimations: disableCharacterMeshAnimations);
                if (!instantiated)
                {
                    Destroy(nextVisual);
                    Debug.LogWarning($"Failed to instantiate character scene: {meshPath}");
                    return;
                }

                if (!CanAttachRuntimeVisual(requestId))
                {
                    Destroy(nextVisual);
                    return;
                }

                var nextRenderer = nextVisual.GetComponentInChildren<Renderer>(true);
                if (nextRenderer == null)
                {
                    Destroy(nextVisual);
                    Debug.LogWarning($"Character mesh loaded but no renderer was found for '{meshResourceName}'. Keeping existing visual.");
                    return;
                }

                if (_spawnedPrefabVisual != null)
                    Destroy(_spawnedPrefabVisual);

                DisposeImporter(_runtimeVisualImporter);
                _runtimeVisualImporter = null;

                var previousRenderer = targetRenderer;
                _spawnedPrefabVisual = nextVisual;
                _spawnedPrefabVisual.SetActive(_characterVisualsVisible);
                ApplyVisualRootMirror();
                InvalidateEquippedTextureState();
                targetRenderer = previousRenderer;
                RemoveDefaultFallbackVisual();
                if (_generatedVisual != null)
                {
                    Destroy(_generatedVisual);
                    _generatedVisual = null;
                }

                targetRenderer = nextRenderer;
                _currentMeshResourceName = meshResourceName;
                _initializedScale = false;
                CaptureBaseScale();
                SyncHandsTintToArms();
                BindLegacyAnimationFromImporter(importer);
                InitializeAnimationPlayback();
                ApplyBreedScale(breedId);
                ApplyBreedTexture(breedId);
                _runtimeVisualImporter = importer;
                importer = null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Character mesh load failed for '{meshResourceName}': {ex.Message}");
            }
            finally
            {
                DisposeImporter(importer);
                if (requestId == _meshLoadRequestId)
                    _meshLoadInProgressName = string.Empty;
            }
        }

        private static async Task<bool> TryInstantiateGlbWithReflection(
            string glbPath,
            Transform parent,
            Action<object> onImporterLoaded = null,
            bool disableAnimations = false)
        {
            glbPath = GlbDataUriLoadPathResolver.Resolve(glbPath, true);

            var gltfType = Type.GetType("GLTFast.GltfImport, glTFast");
            if (gltfType == null)
                return false;

            Type importSettingsType = Type.GetType("GLTFast.ImportSettings, glTFast");
            object importSettings = CreateImportSettings(importSettingsType, disableAnimations: disableAnimations);
            object importer = CreateGltfImporterInstance(gltfType);
            if (importer == null)
                return false;
            bool handedOff = false;
            try
            {
                if (!await TryLoadScene(importer, gltfType, glbPath, importSettingsType, importSettings))
                    return false;

                bool instantiated = await InstantiateLoadedScene(importer, parent);
                if (!instantiated)
                    return false;
                onImporterLoaded?.Invoke(importer);
                handedOff = true;
                return true;
            }
            finally
            {
                if (!handedOff)
                    DisposeImporter(importer);
            }
        }

        private static async Task<bool> TryLoadScene(object importer, Type gltfType, string path, Type importSettingsType, object importSettings)
        {
            string fullPath = Path.GetFullPath(path);
            string uriPath = new Uri(fullPath).AbsoluteUri;

            var loadFileMethod = gltfType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "LoadFile"
                    && m.GetParameters().Length >= 1
                    && m.GetParameters()[0].ParameterType == typeof(string));
            if (loadFileMethod != null)
            {
                var args = BuildLoadArgs(loadFileMethod.GetParameters(), fullPath, importSettingsType, importSettings);
                var taskObj = loadFileMethod.Invoke(importer, args);
                if (await AwaitBoolTask(taskObj))
                    return true;
            }

            var loadUriMethod = gltfType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "Load"
                    && m.GetParameters().Length >= 1
                    && m.GetParameters()[0].ParameterType == typeof(Uri));
            if (loadUriMethod != null)
            {
                var uri = new Uri(fullPath);
                var args = BuildLoadArgs(loadUriMethod.GetParameters(), uri, importSettingsType, importSettings);
                var taskObj = loadUriMethod.Invoke(importer, args);
                if (await AwaitBoolTask(taskObj))
                    return true;
            }

            var loadStringMethod = gltfType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "Load"
                    && m.GetParameters().Length >= 1
                    && m.GetParameters()[0].ParameterType == typeof(string));
            if (loadStringMethod != null)
            {
                var stringArgs = BuildLoadArgs(loadStringMethod.GetParameters(), uriPath, importSettingsType, importSettings);
                var taskObj = loadStringMethod.Invoke(importer, stringArgs);
                if (await AwaitBoolTask(taskObj))
                    return true;

                // Final fallback: raw path string for older signatures.
                stringArgs = BuildLoadArgs(loadStringMethod.GetParameters(), fullPath, importSettingsType, importSettings);
                taskObj = loadStringMethod.Invoke(importer, stringArgs);
                if (await AwaitBoolTask(taskObj))
                    return true;
            }

            return false;
        }

        private static object[] BuildLoadArgs(System.Reflection.ParameterInfo[] parameters, object firstArg, Type importSettingsType = null, object importSettings = null)
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
                else
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

        private static async Task<bool> InstantiateLoadedScene(object importer, Transform parent)
        {
            var importerType = importer.GetType();

            var asyncMainSceneMethod = importerType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "InstantiateMainSceneAsync"
                    && m.GetParameters().Length >= 1
                    && typeof(Transform).IsAssignableFrom(m.GetParameters()[0].ParameterType));
            if (asyncMainSceneMethod != null)
            {
                var args = BuildInstantiateArgs(asyncMainSceneMethod.GetParameters(), parent);
                return await AwaitBoolTask(asyncMainSceneMethod.Invoke(importer, args));
            }

            var asyncSceneMethod = importerType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "InstantiateSceneAsync"
                    && m.GetParameters().Length >= 1);
            if (asyncSceneMethod != null)
            {
                var args = BuildSceneInstantiateArgs(asyncSceneMethod.GetParameters(), parent);
                return await AwaitBoolTask(asyncSceneMethod.Invoke(importer, args));
            }

            var syncMainSceneMethod = importerType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "InstantiateMainScene"
                    && m.GetParameters().Length >= 1
                    && typeof(Transform).IsAssignableFrom(m.GetParameters()[0].ParameterType));
            if (syncMainSceneMethod != null)
            {
                var args = BuildInstantiateArgs(syncMainSceneMethod.GetParameters(), parent);
                return await AwaitBoolTask(syncMainSceneMethod.Invoke(importer, args));
            }

            var syncSceneMethod = importerType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "InstantiateScene"
                    && m.GetParameters().Length >= 1);
            if (syncSceneMethod != null)
            {
                var args = BuildSceneInstantiateArgs(syncSceneMethod.GetParameters(), parent);
                return await AwaitBoolTask(syncSceneMethod.Invoke(importer, args));
            }

            return false;
        }

        private static object[] BuildInstantiateArgs(System.Reflection.ParameterInfo[] parameters, Transform parent)
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

        private static object[] BuildSceneInstantiateArgs(System.Reflection.ParameterInfo[] parameters, Transform parent)
        {
            var args = new object[parameters.Length];
            bool transformAssigned = false;
            bool intAssigned = false;

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (!transformAssigned && typeof(Transform).IsAssignableFrom(p.ParameterType))
                {
                    args[i] = parent;
                    transformAssigned = true;
                    continue;
                }

                if (!intAssigned && (p.ParameterType == typeof(int) || p.ParameterType == typeof(uint)))
                {
                    args[i] = 0;
                    intAssigned = true;
                    continue;
                }

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
                    // Try next available constructor shape.
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

            return false;
        }
        private string ResolveMeshPath(string meshResourceName)
        {
            var candidates = new[]
            {
                Path.Combine(Application.dataPath, "Resources", characterMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", characterMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.streamingAssetsPath, characterMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.dataPath, "Resources", characterMeshResourcesFolder, $"{meshResourceName}.gltf"),
                Path.Combine(Application.streamingAssetsPath, "AOData", characterMeshResourcesFolder, $"{meshResourceName}.gltf"),
                Path.Combine(Application.streamingAssetsPath, characterMeshResourcesFolder, $"{meshResourceName}.gltf")
            };

            foreach (var path in candidates)
            {
                if (!File.Exists(path))
                    continue;

                if (path.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
                {
                    if (!allowCharacterMeshGltfFallback)
                        continue;
                    if (ContainsEmbeddedDataUri(path))
                        continue;
                }

                if (File.Exists(path))
                    return path;
            }

            return string.Empty;
        }

        private void CaptureBaseScale()
        {
            if (_initializedScale || targetRenderer == null)
                return;

            _baseVisualScale = targetRenderer.transform.localScale;
            _initializedScale = true;
        }

        private void ApplyBreedScale(int breedId)
        {
            if (targetRenderer == null)
                return;

            if (!_initializedScale)
                CaptureBaseScale();

            float scale = _breedScaleMultipliers.TryGetValue(breedId, out var mapped) ? mapped : 1f;
            Vector3 nextScale = _baseVisualScale * Mathf.Max(0.1f, scale);
            nextScale.x = Mathf.Abs(nextScale.x);
            targetRenderer.transform.localScale = nextScale;
        }

        private void ApplyVisualRootMirror()
        {
            if (_spawnedPrefabVisual == null)
                return;

            var rootScale = _spawnedPrefabVisual.transform.localScale;
            float xAbs = Mathf.Abs(rootScale.x);
            rootScale.x = flipCharacterVisualX ? -xAbs : xAbs;
            _spawnedPrefabVisual.transform.localScale = rootScale;
        }

        private void LateUpdate()
        {
            UpdateMovementAnimation();
            ApplyDebugHeadVisualTransform();
            UpdateTemporaryEquippedItemVisual();
            ApplyTemporaryItemVisualTransform();
        }

        private void UpdateMovementAnimation()
        {
            CatAnimPlayer directAnim = _spawnedPrefabVisual != null
                ? _spawnedPrefabVisual.GetComponentInChildren<CatAnimPlayer>(true)
                : null;
            if (directAnim == null && !_animationGraphReady && !_legacyAnimationReady)
                return;

            // Once death playback starts, suppress regular idle/move/attack blending so
            // it cannot override the one-shot death clip.
            if (_externalDeathPlaybackLocked)
            {
                if (_externalDeathPlaybackLockUntil <= 0f || Time.time < _externalDeathPlaybackLockUntil)
                    return;
                _externalDeathPlaybackLocked = false;
            }

            bool forwardPressed = IsForwardPressed();
            bool backwardPressed = IsBackwardPressed();
            bool strafeLeftPressed = IsStrafeLeftPressed();
            bool strafeRightPressed = IsStrafeRightPressed();
            // The local motor owns gameplay input. Reuse its exact sample instead of
            // polling again here: UI focus checks can otherwise make LateUpdate see
            // no keys and overwrite the motor-selected locomotion clip with idle.
            if (_localLocomotionIntentFrame >= Time.frameCount - 1)
            {
                forwardPressed = _localLocomotionForward > 0.001f;
                backwardPressed = _localLocomotionForward < -0.001f;
                strafeLeftPressed = _localLocomotionStrafe < -0.001f;
                strafeRightPressed = _localLocomotionStrafe > 0.001f;
            }
            bool externalMove = Time.time < _externalMoveUntil;
            if (externalMove)
                forwardPressed = true;
            bool jumpPressedThisFrame = WasSpacePressedThisFrame();
            if (jumpPressedThisFrame)
                _lastJumpKeyPressTime = Time.time;
            CalculateCurrentSpeed();
            bool hasMotionInput = externalMove || forwardPressed || backwardPressed || strafeLeftPressed || strafeRightPressed;
            // Local locomotion follows held movement flags. The reference motor calls
            // Halt when the last translation flag clears, so key-up must select idle
            // immediately instead of preserving a stale direction from root velocity.
            bool shouldMove = hasMotionInput && !_sitToggled && !_sitTransitionPlaying;
            bool shouldAttackIdle = !shouldMove && attackToggleActive && !_sitToggled && !_sitTransitionPlaying;
            bool shouldSitIdle = _sitToggled && !_sitTransitionPlaying;
            string desiredMoveAction = externalMove
                ? _externalMoveAction
                : hasMotionInput
                    ? ResolveDesiredMoveAction(forwardPressed, backwardPressed, strafeLeftPressed, strafeRightPressed)
                    : _activeMoveAction;
            bool jumpBusy = UpdateJumpTransitions(forwardPressed, jumpPressedThisFrame);
            if (jumpBusy)
            {
                shouldMove = false;
                shouldAttackIdle = false;
            }
            bool attackOneShotPlaying = attackToggleActive && Time.time < _attackRuleOneShotEndTime;
            float targetMoveWeight = shouldMove ? 1f : 0f;
            float targetAttackWeight = (shouldAttackIdle || attackOneShotPlaying || shouldSitIdle || _sitTransitionPlaying) ? 1f : 0f;
            if (jumpBusy)
            {
                // Jump one-shots are routed through the action/attack layer in playable mode.
                // Keep that layer visible while jump is active so the clip is not weight-zeroed.
                targetMoveWeight = 0f;
                targetAttackWeight = 1f;
            }
            bool attackCycleAllowed = attackToggleActive && !_sitToggled && !_sitTransitionPlaying && !jumpBusy;
            UpdateAttackCombatSequence(attackCycleAllowed, shouldMove);

            if (directAnim != null)
            {
                // CAT sit transitions are one-shots. Do not let the locomotion
                // update replace sit-start/sit-stop before their callback runs.
                if (_sitTransitionPlaying || _jumpInAir)
                    return;

                // Keep current locomotion beneath the landing overlay.
                // This lets idle->run and run->idle changes take effect immediately
                // instead of leaving the character gliding in a landing/run pose.
                if (_jumpLandingTransitionPlaying)
                {
                    directAnim.Play(ResolveDirectLocomotionLogicalName(
                        forwardPressed, backwardPressed,
                        strafeLeftPressed, strafeRightPressed));
                    return;
                }

                string logical = shouldSitIdle
                    ? "idle-sit"
                    : ResolveDirectLocomotionLogicalName(
                        forwardPressed, backwardPressed,
                        strafeLeftPressed, strafeRightPressed);
                directAnim.Play(logical);
                return;
            }

            if (shouldMove && !string.Equals(desiredMoveAction, _activeMoveAction, StringComparison.OrdinalIgnoreCase))
            {
                _activeMoveAction = desiredMoveAction;
                ApplyAnimationOverridesNow();
                return;
            }

            if (_sitTransitionPlaying && Time.time >= _sitTransitionEndTime)
            {
                _sitTransitionPlaying = false;
                if (_standingUpTransition)
                {
                    _standingUpTransition = false;
                    _runtimeAttackClipOverride = string.Empty;
                    attackToggleActive = false;
                    ApplyAnimationOverridesNow();
                }
                else
                {
                    SetRuntimeAttackClipToAction(ActionIdleSit);
                    attackToggleActive = true;
                }
            }

            if (_legacyAnimationReady)
            {
                if (_sitTransitionPlaying)
                {
                    return;
                }

                // Jump one-shots are played explicitly in UpdateJumpTransitions.
                // Do not let the generic idle/move logic override them mid-air/landing.
                if (jumpBusy)
                    return;

                if (shouldSitIdle)
                {
                    string sitIdleClip = ResolveActionClipNameFromMap(availableAnimationClipNames, ActionIdleSit, _idleLegacyClipName);
                    if (!_legacyAnimation.IsPlaying(sitIdleClip))
                        _legacyAnimation.CrossFade(sitIdleClip, 0.10f);
                    return;
                }

                if (shouldMove)
                {
                    string desiredMoveClip = _moveLegacyClipName;
                    if (backwardPressed)
                        desiredMoveClip = ResolveActionClipNameFromMap(availableAnimationClipNames, ActionRunBackwards, _moveLegacyClipName);
                    else if (strafeLeftPressed)
                        desiredMoveClip = ResolveActionClipNameFromMap(availableAnimationClipNames, ActionStrafeRight, _moveLegacyClipName);
                    else if (strafeRightPressed)
                        desiredMoveClip = ResolveActionClipNameFromMap(availableAnimationClipNames, ActionStrafeLeft, _moveLegacyClipName);
                    else if (_walkModeEnabled)
                        desiredMoveClip = ResolveActionClipNameFromMap(availableAnimationClipNames, ActionWalk, _moveLegacyClipName);
                    else
                        desiredMoveClip = ResolveActionClipNameFromMap(availableAnimationClipNames, ActionRun, _moveLegacyClipName);

                    if (!string.Equals(_moveLegacyClipName, desiredMoveClip, StringComparison.OrdinalIgnoreCase))
                    {
                        _moveLegacyClipName = desiredMoveClip;
                        if (_legacyAnimation.GetClip(_moveLegacyClipName) != null)
                        {
                            _legacyAnimation.CrossFade(_moveLegacyClipName, 0.20f);
                            SeekLegacyMoveLoopStart();
                            _legacyMovePlaying = true;
                            _legacyStartPlaying = false;
                        }
                    }

                    if (!_legacyMovePlaying && !_legacyStartPlaying)
                    {
                        if (playMoveStartClip
                            && !string.IsNullOrWhiteSpace(_moveStartLegacyClipName)
                            && _legacyAnimation.GetClip(_moveStartLegacyClipName) != null)
                        {
                            _legacyAnimation.CrossFade(_moveStartLegacyClipName, 0.20f);
                            _legacyStartPlaying = true;
                            var startClip = _legacyAnimation.GetClip(_moveStartLegacyClipName);
                            _legacyStartEndTime = Time.time + Mathf.Max(0.05f, startClip.length * 0.95f);
                        }
                        else if (!string.IsNullOrWhiteSpace(_moveLegacyClipName))
                        {
                            _legacyAnimation.CrossFade(_moveLegacyClipName, 0.20f);
                            SeekLegacyMoveLoopStart();
                            _legacyMovePlaying = true;
                        }
                        _legacyAttackPlaying = false;
                    }

                    if (_legacyStartPlaying
                        && (!string.IsNullOrWhiteSpace(_moveLegacyClipName))
                        && (Time.time >= _legacyStartEndTime || !_legacyAnimation.IsPlaying(_moveStartLegacyClipName)))
                    {
                        _legacyAnimation.CrossFade(_moveLegacyClipName, 0.20f);
                        SeekLegacyMoveLoopStart();
                        _legacyStartPlaying = false;
                        _legacyMovePlaying = true;
                    }

                    MaintainLegacyMoveLoopWindow();
                }
                else
                {
                    if (shouldAttackIdle
                        && !string.IsNullOrWhiteSpace(_attackLegacyClipName)
                        && _legacyAnimation.GetClip(_attackLegacyClipName) != null)
                    {
                        if (!_legacyAttackPlaying && Time.time >= _attackRuleOneShotEndTime)
                            _legacyAnimation.CrossFade(_attackLegacyClipName, 0.10f);
                        if (_attackCycleRunning)
                        {
                            var st = _legacyAnimation[_attackLegacyClipName];
                            if (st != null)
                            {
                                ResolveActionLoopWindow(_activeAttackIdleActionKey, st.length, out var segStart, out _);
                                st.speed = 0f;
                                st.time = segStart;
                            }
                        }
                        _legacyAttackPlaying = true;
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(_attackLegacyClipName) && _legacyAnimation.GetClip(_attackLegacyClipName) != null)
                        {
                            var st = _legacyAnimation[_attackLegacyClipName];
                            if (st != null)
                                st.speed = 1f;
                        }
                        if (_legacyMovePlaying || _legacyStartPlaying || _legacyAttackPlaying)
                            _legacyAnimation.CrossFade(_idleLegacyClipName, 0.20f);
                        _legacyAttackPlaying = false;
                    }
                    _legacyStartPlaying = false;
                    _legacyMovePlaying = false;
                }
                return;
            }

            _moveBlendWeight = Mathf.MoveTowards(_moveBlendWeight, targetMoveWeight, animationBlendSpeed * Time.deltaTime);
            _attackBlendWeight = Mathf.MoveTowards(_attackBlendWeight, targetAttackWeight, animationBlendSpeed * Time.deltaTime);
            if (jumpBusy)
            {
                // In playable mode, keep jump one-shot fully visible and prevent
                // per-frame loop maintenance from swapping back to idle/attack clips.
                _moveBlendWeight = 0f;
                _attackBlendWeight = 1f;
                _animationMixer.SetInputWeight(0, 0f);
                _animationMixer.SetInputWeight(1, 0f);
                _animationMixer.SetInputWeight(2, 1f);
                return;
            }
            if (attackToggleActive && !shouldSitIdle && !_sitTransitionPlaying && !jumpBusy)
            {
                _moveBlendWeight = 0f;
                _attackBlendWeight = 1f;
            }
            MaintainPlayableMoveLoopWindow(shouldMove);
            MaintainPlayableAttackActionLoop(targetAttackWeight > 0.01f);

            float idleWeight = Mathf.Clamp01(1f - _moveBlendWeight - _attackBlendWeight);
            _animationMixer.SetInputWeight(0, idleWeight);
            _animationMixer.SetInputWeight(1, _moveBlendWeight);
            _animationMixer.SetInputWeight(2, _attackBlendWeight);
        }

        private void ApplyBreedTexture(int breedId)
        {
            if (targetRenderer == null)
                return;

            if (_spawnedPrefabVisual != null && !overrideImportedMeshTextures)
                return;

            int textureId = _breedTextureIds.TryGetValue(breedId, out var mapped) ? mapped : fallbackTextureId;
            if (!TryGetTexture(textureId, out var texture))
                return;

            EnsureRuntimeMaterial();
            if (_runtimeMaterial == null)
                return;

            if (_runtimeMaterial.HasProperty("_BaseMap"))
                _runtimeMaterial.SetTexture("_BaseMap", texture);
            if (_runtimeMaterial.HasProperty("_MainTex"))
                _runtimeMaterial.SetTexture("_MainTex", texture);
            _runtimeMaterial.mainTexture = texture;
            targetRenderer.sharedMaterial = _runtimeMaterial;
        }

        private void SyncHandsTintToArms()
        {
            if (!Application.isPlaying)
                return;

            if (_spawnedPrefabVisual == null)
                return;

            var renderers = _spawnedPrefabVisual.GetComponentsInChildren<Renderer>(true);
            var allMaterials = new List<Material>();
            var handCandidates = new List<Material>();
            Material armReference = null;

            foreach (var r in renderers)
            {
                if (r == null)
                    continue;

                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null)
                        continue;

                    allMaterials.Add(mat);
                    string n = mat.name.ToLowerInvariant();
                    if (armReference == null && (n.Contains("arms") || n.Contains("arm")))
                        armReference = mat;
                    if (n.Contains("hands") || n.Contains("hand"))
                        handCandidates.Add(mat);
                }

                // Common AO export order fallback: legs, feet, arms, hands, body
                if (mats.Length >= 4)
                {
                    armReference ??= mats[2];
                    handCandidates.Add(mats[3]);
                }
            }

            if (allMaterials.Count == 0)
                return;

            armReference ??= allMaterials.FirstOrDefault(m => m != null);
            if (armReference == null)
                return;

            foreach (var hand in handCandidates.Where(m => m != null).Distinct())
                CopySurfaceColorAndMaps(armReference, hand);

            // Fallback for exports where hand slots are not named: replace obvious green tint outliers.
            foreach (var mat in allMaterials.Where(m => m != null && m != armReference))
            {
                if (IsStrongGreenOutlier(mat))
                    CopySurfaceColorAndMaps(armReference, mat);
            }
        }

        private void RefreshEquippedBodyTextures()
        {
            if (!Application.isPlaying || _isDestroying)
                return;
            if (_spawnedPrefabVisual == null || bridge?.Character?.Equipment == null || AO.Data.Unity.AODataManager.Instance == null)
                return;
            if (AO.Core.Characters.CharacterEquipment.GetItemInstance == null)
                return;

            int visualId = _spawnedPrefabVisual.GetInstanceID();
            if (_lastEquippedTextureVisualInstanceId != visualId)
            {
                _lastEquippedTextureVisualInstanceId = visualId;
                _lastEquippedTextureSignature = string.Empty;
                _bodyMaterialTextureSnapshot.Clear();
                _lastAppliedEquippedTextureByLocation.Clear();
                _rendererMaterialCache.Clear();
            }

            var equipped = bridge.Character.Equipment.GetAllEquipped();
            if (equipped == null)
                return;

            var resolvedByLocation = new Dictionary<int, int>();
            var signature = new StringBuilder(128);
            signature.Append("v:").Append(visualId).Append('|');
            foreach (var kv in equipped.OrderBy(k => k.Key))
            {
                var instance = AO.Core.Characters.CharacterEquipment.GetItemInstance(kv.Value);
                int aoid = instance?.Definition?.AOID ?? 0;
                if (aoid <= 0)
                    continue;

                signature.Append(kv.Key).Append(':').Append(aoid).Append(';');
                var applications = AO.Data.Unity.AODataManager.Instance.GetItemTextureApplications(aoid);
                if (applications == null || applications.Count == 0)
                    continue;

                foreach (var app in applications)
                {
                    if (app == null || app.TextureId <= 0 || app.LocationId < 0 || app.LocationId > 4)
                        continue;
                    // Deterministic precedence: later processed entries overwrite earlier ones.
                    resolvedByLocation[app.LocationId] = app.TextureId;
                }
            }

            string nextSignature = signature.ToString();
            if (string.Equals(_lastEquippedTextureSignature, nextSignature, StringComparison.Ordinal))
                return;
            _lastEquippedTextureSignature = nextSignature;

            var materialsByLocation = CollectBodyPartMaterials();
            if (materialsByLocation.Count == 0)
                return;

            // Reset body-part materials to their captured base first, then apply
            // current equipment textures. This guarantees clean unequip behavior.
            foreach (var mat in materialsByLocation.Values
                         .Where(list => list != null)
                         .SelectMany(list => list)
                         .Where(m => m != null)
                         .Distinct())
            {
                RestoreSnapshotTexture(mat);
            }
            _lastAppliedEquippedTextureByLocation.Clear();

            for (int locationId = 0; locationId <= 4; locationId++)
            {
                if (!materialsByLocation.TryGetValue(locationId, out var mats) || mats.Count == 0)
                    continue;

                if (resolvedByLocation.TryGetValue(locationId, out int textureId)
                    && TryGetGeneralTexture(textureId, out var texture))
                {
                    foreach (var mat in mats)
                        ApplyTextureToMaterial(mat, texture);
                    _lastAppliedEquippedTextureByLocation[locationId] = textureId;
                }
            }
        }

        private Dictionary<int, List<Material>> CollectBodyPartMaterials()
        {
            var result = new Dictionary<int, List<Material>>
            {
                { 0, new List<Material>() }, // Hands
                { 1, new List<Material>() }, // Body
                { 2, new List<Material>() }, // Feet
                { 3, new List<Material>() }, // Arms
                { 4, new List<Material>() }  // Legs
            };

            var seen = new Dictionary<int, HashSet<Material>>
            {
                { 0, new HashSet<Material>() },
                { 1, new HashSet<Material>() },
                { 2, new HashSet<Material>() },
                { 3, new HashSet<Material>() },
                { 4, new HashSet<Material>() }
            };
            var globallyAssigned = new HashSet<Material>();

            var renderers = _spawnedPrefabVisual.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;
                if (IsAttachedEquippedItemRenderer(renderer))
                    continue;

                var mats = GetRendererMaterials(renderer);
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null)
                        continue;

                    Material sharedMat = (shared != null && i < shared.Length) ? shared[i] : null;
                    CacheSnapshotTexture(mat, sharedMat);
                    string n = mat.name.ToLowerInvariant();
                    int? location = ResolveLocationFromMaterialName(n);
                    if (location.HasValue && globallyAssigned.Add(mat) && seen[location.Value].Add(mat))
                        result[location.Value].Add(mat);
                }

                // Common AO export fallback: legs, feet, arms, hands, body
                if (mats.Length >= 5)
                {
                    AddFallback(4, mats[0]);
                    AddFallback(2, mats[1]);
                    AddFallback(3, mats[2]);
                    AddFallback(0, mats[3]);
                    AddFallback(1, mats[4]);
                }
            }

            return result;

            void AddFallback(int locationId, Material mat)
            {
                if (mat == null)
                    return;
                CacheSnapshotTexture(mat);
                if (globallyAssigned.Add(mat) && seen[locationId].Add(mat))
                    result[locationId].Add(mat);
            }
        }

        private Material[] GetRendererMaterials(Renderer renderer)
        {
            if (renderer == null)
                return Array.Empty<Material>();

            int id = renderer.GetInstanceID();
            if (_rendererMaterialCache.TryGetValue(id, out var cached) && cached != null && cached.Length > 0)
                return cached;

            // Accessing renderer.materials instantiates per-renderer runtime materials.
            // Cache once to avoid repeated allocations/material leaks.
            var mats = renderer.materials;
            _rendererMaterialCache[id] = mats;
            return mats ?? Array.Empty<Material>();
        }

        private static bool IsAttachedEquippedItemRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            var t = renderer.transform;
            while (t != null)
            {
                if (t.name.StartsWith("Equipped_", StringComparison.Ordinal))
                    return true;
                t = t.parent;
            }

            return false;
        }

        private static int? ResolveLocationFromMaterialName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            if (name.Contains("hands") || name.Contains("hand"))
                return 0;
            if (name.Contains("body") || name.Contains("torso") || name.Contains("chest"))
                return 1;
            if (name.Contains("feet") || name.Contains("foot") || name.Contains("boots") || name.Contains("shoe"))
                return 2;
            if (name.Contains("arms") || name.Contains("arm"))
                return 3;
            if (name.Contains("legs") || name.Contains("leg") || name.Contains("pants"))
                return 4;

            return null;
        }

        private void CacheSnapshotTexture(Material mat, Material sharedReference = null)
        {
            if (mat == null)
                return;

            bool hasExisting = _bodyMaterialTextureSnapshot.ContainsKey(mat);
            if (hasExisting && sharedReference == null)
                return;

            Material source = sharedReference != null ? sharedReference : mat;
            Texture baseMap = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            Texture mainTex = source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
            _bodyMaterialTextureSnapshot[mat] = (baseMap, mainTex);
        }

        private static void ApplyTextureToMaterial(Material mat, Texture texture)
        {
            if (mat == null || texture == null)
                return;

            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", texture);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", texture);
            mat.mainTexture = texture;
        }

        private void RestoreSnapshotTexture(Material mat)
        {
            if (mat == null)
                return;
            if (!_bodyMaterialTextureSnapshot.TryGetValue(mat, out var snapshot))
                return;

            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", snapshot.baseMap);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", snapshot.mainTex);
            if (snapshot.mainTex != null)
                mat.mainTexture = snapshot.mainTex;
            else if (snapshot.baseMap != null)
                mat.mainTexture = snapshot.baseMap;
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

        private void InvalidateEquippedTextureState()
        {
            _lastEquippedTextureSignature = string.Empty;
            _lastEquippedTextureVisualInstanceId = 0;
            _bodyMaterialTextureSnapshot.Clear();
            _lastAppliedEquippedTextureByLocation.Clear();
            _rendererMaterialCache.Clear();
        }

        private bool CanAttachRuntimeVisual(int requestId)
        {
            if (!Application.isPlaying || _isDestroying || requestId != _meshLoadRequestId)
                return false;
            if (this == null || gameObject == null || transform == null)
                return false;

            return true;
        }

        private static bool IsStrongGreenOutlier(Material material)
        {
            if (material == null)
                return false;

            Color color;
            if (material.HasProperty("_BaseColor"))
                color = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color"))
                color = material.GetColor("_Color");
            else
                return false;

            return color.g > (color.r * 1.25f) && color.g > (color.b * 1.25f) && color.g > 0.25f;
        }

        private static void CopySurfaceColorAndMaps(Material source, Material target)
        {
            if (source == null || target == null)
                return;

            if (source.HasProperty("_BaseColor") && target.HasProperty("_BaseColor"))
            {
                var src = source.GetColor("_BaseColor");
                var dst = target.GetColor("_BaseColor");
                target.SetColor("_BaseColor", new Color(src.r, src.g, src.b, dst.a));
            }
            if (source.HasProperty("_Color") && target.HasProperty("_Color"))
            {
                var src = source.GetColor("_Color");
                var dst = target.GetColor("_Color");
                target.SetColor("_Color", new Color(src.r, src.g, src.b, dst.a));
            }

            if (source.HasProperty("_BaseMap") && target.HasProperty("_BaseMap"))
            {
                var tex = source.GetTexture("_BaseMap");
                if (tex != null)
                    target.SetTexture("_BaseMap", tex);
            }
            if (source.HasProperty("_MainTex") && target.HasProperty("_MainTex"))
            {
                var tex = source.GetTexture("_MainTex");
                if (tex != null)
                    target.SetTexture("_MainTex", tex);
            }

            if (source.HasProperty("_EmissionColor") && target.HasProperty("_EmissionColor"))
                target.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
            if (source.HasProperty("_EmissionMap") && target.HasProperty("_EmissionMap"))
            {
                var emissionTex = source.GetTexture("_EmissionMap");
                if (emissionTex != null)
                    target.SetTexture("_EmissionMap", emissionTex);
            }
        }

        private void BindLegacyAnimationFromImporter(object importer)
        {
            if (_spawnedPrefabVisual == null || importer == null)
                return;

            var importerType = importer.GetType();
            var clipsMethod = importerType.GetMethod("GetAnimationClips", Type.EmptyTypes);
            if (clipsMethod == null)
                return;

            var clipsObj = clipsMethod.Invoke(importer, null);
            if (clipsObj is not IEnumerable<AnimationClip> clipsEnum)
                return;

            var clips = clipsEnum.Where(c => c != null).ToList();
            if (clips.Count == 0)
                return;

            _legacyAnimation = _spawnedPrefabVisual.GetComponentInChildren<Animation>(true);
            if (_legacyAnimation == null)
                _legacyAnimation = _spawnedPrefabVisual.AddComponent<Animation>();

            foreach (var clip in clips)
            {
                clip.legacy = true;
                _legacyAnimation.AddClip(clip, clip.name);
            }
        }

        private void InitializeAnimationPlayback()
        {
            DisposeAnimationGraph();

            if (_spawnedPrefabVisual == null)
                return;

            _legacyAnimation = _spawnedPrefabVisual.GetComponentInChildren<Animation>(true);
            if (TryInitializeLegacyAnimation())
                return;

            if (TryBindLegacyAnimationFromResources(_currentMeshResourceName) && TryInitializeLegacyAnimation())
                return;

            // Direct CAT meshes animate from AO's CATAnim records and intentionally
            // have no Unity AnimatorController or legacy Animation component.
            CatAnimPlayer directAnim = _spawnedPrefabVisual
                .GetComponentInChildren<CatAnimPlayer>(true);
            if (directAnim != null)
            {
                directAnim.PlayDeferred("idle");
                return;
            }

            _spawnedAnimator = _spawnedPrefabVisual.GetComponentInChildren<Animator>(true);
            if (_spawnedAnimator == null || _spawnedAnimator.runtimeAnimatorController == null)
            {
                Debug.LogWarning($"No Animator/Animation clips found for {_currentMeshResourceName}.");
                return;
            }

            var clips = _spawnedAnimator.runtimeAnimatorController.animationClips
                .Where(c => c != null)
                .Distinct()
                .ToList();
            if (clips.Count == 0)
                return;

            RefreshAvailableClipNames(clips.Select(c => c.name));
            _lastAttackAnimationSignature = string.Empty;

            string effectiveIdleClipOverride = string.IsNullOrWhiteSpace(_temporaryIdleClipOverride) ? idleClipOverride : _temporaryIdleClipOverride;
            string effectiveMoveClipOverride = string.IsNullOrWhiteSpace(_temporaryMoveClipOverride) ? moveClipOverride : _temporaryMoveClipOverride;
            var idleClip = ResolveClipOverride(effectiveIdleClipOverride, clips, ResolveActionClipFromMap(clips, ActionIdleMa, FindBestIdleClip(clips)));
            var moveClip = ResolveClipOverride(effectiveMoveClipOverride, clips, ResolveActionClipFromMap(clips, _activeMoveAction, FindBestMoveClip(clips)));
            string effectiveAttackOverride = string.IsNullOrWhiteSpace(_runtimeAttackClipOverride) ? attackClipOverride : _runtimeAttackClipOverride;
            var attackClip = ResolveClipOverride(
                effectiveAttackOverride,
                clips,
                ResolveActionClipFromMap(clips, _runtimeAttackActionKey, FindBestAttackClip(clips)));
            if (idleClip == null || moveClip == null)
            {
                Debug.LogWarning($"Missing idle/move clips for {_currentMeshResourceName}. idle={idleClip != null} move={moveClip != null}");
                return;
            }

            _animationGraph = PlayableGraph.Create("CharacterAppearanceAnimationGraph");
            _animationGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            _animationMixer = AnimationMixerPlayable.Create(_animationGraph, 3);
            _idleClipPlayable = AnimationClipPlayable.Create(_animationGraph, idleClip);
            _moveClipPlayable = AnimationClipPlayable.Create(_animationGraph, moveClip);
            _attackClipPlayable = AnimationClipPlayable.Create(_animationGraph, attackClip != null ? attackClip : idleClip);

            _idleClipPlayable.SetApplyFootIK(false);
            _moveClipPlayable.SetApplyFootIK(false);
            _attackClipPlayable.SetApplyFootIK(false);
            _moveClipPlayable.SetSpeed(0d);
            // Always drive attack playable time manually to avoid implicit clip looping.
            _attackClipPlayable.SetSpeed(0d);

            ResolveMoveLoopWindow(_activeMoveAction, moveClip != null ? moveClip.length : 0f, out _moveLoopStartResolved, out _moveLoopEndResolved);
            _movePlayableLoopTime = _moveLoopStartResolved;
            _moveClipPlayable.SetTime(_movePlayableLoopTime);
            ResolveActionLoopWindow(_runtimeAttackActionKey, attackClip != null ? attackClip.length : 0f, out _attackLoopStartResolved, out _attackLoopEndResolved);
            _attackPlayableLoopTime = _runtimeAttackLoopEnabled ? _attackLoopStartResolved : _runtimeAttackStartTime;
            _attackClipPlayable.SetTime(_attackPlayableLoopTime);

            _animationGraph.Connect(_idleClipPlayable, 0, _animationMixer, 0);
            _animationGraph.Connect(_moveClipPlayable, 0, _animationMixer, 1);
            _animationGraph.Connect(_attackClipPlayable, 0, _animationMixer, 2);
            _animationMixer.SetInputWeight(0, 1f);
            _animationMixer.SetInputWeight(1, 0f);
            _animationMixer.SetInputWeight(2, 0f);

            var output = AnimationPlayableOutput.Create(_animationGraph, "CharacterAppearanceAnimation", _spawnedAnimator);
            output.SetSourcePlayable(_animationMixer);
            _animationGraph.Play();

            _moveBlendWeight = 0f;
            _attackBlendWeight = 0f;
            _lastRootPosition = transform.position;
            _hasLastRootPosition = true;
            _animationGraphReady = true;
        }

        private bool TryBindLegacyAnimationFromResources(string meshResourceName)
        {
            if (_spawnedPrefabVisual == null || string.IsNullOrWhiteSpace(meshResourceName))
                return false;

            var assets = Resources.LoadAll($"{characterMeshResourcesFolder}/{meshResourceName}");
            if (assets == null || assets.Length == 0)
                return false;

            var clips = assets
                .OfType<AnimationClip>()
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.name))
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                .GroupBy(c => c.name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (clips.Count == 0)
                return false;

            _legacyAnimation = _spawnedPrefabVisual.GetComponentInChildren<Animation>(true);
            if (_legacyAnimation == null)
                _legacyAnimation = _spawnedPrefabVisual.AddComponent<Animation>();

            int added = 0;
            foreach (var clip in clips)
            {
                if (_legacyAnimation.GetClip(clip.name) != null)
                    continue;

                var runtimeClip = Instantiate(clip);
                runtimeClip.legacy = true;
                _legacyAnimation.AddClip(runtimeClip, runtimeClip.name);
                added++;
            }

            if (added > 0)
                Debug.Log($"Bound {added} animation clips from Resources for '{meshResourceName}'.");

            return added > 0;
        }

        private bool HasAnimationPlaybackAvailable()
        {
            return _animationGraphReady || _legacyAnimationReady;
        }

        private bool TryInitializeLegacyAnimation()
        {
            _legacyAnimationReady = false;
            _legacyMovePlaying = false;
            _legacyStartPlaying = false;
            _legacyAttackPlaying = false;
            _idleLegacyClipName = string.Empty;
            _moveStartLegacyClipName = string.Empty;
            _moveLegacyClipName = string.Empty;
            _attackLegacyClipName = string.Empty;
            _legacyStartEndTime = 0f;

            if (_legacyAnimation == null)
                return false;

            var clipNames = new List<string>();
            foreach (AnimationState state in _legacyAnimation)
            {
                if (state?.clip == null || string.IsNullOrWhiteSpace(state.name))
                    continue;
                clipNames.Add(state.name);
            }

            if (clipNames.Count == 0)
                return false;

            RefreshAvailableClipNames(clipNames);
            _lastAttackAnimationSignature = string.Empty;

            string effectiveIdleClipOverride = string.IsNullOrWhiteSpace(_temporaryIdleClipOverride) ? idleClipOverride : _temporaryIdleClipOverride;
            string effectiveMoveClipOverride = string.IsNullOrWhiteSpace(_temporaryMoveClipOverride) ? moveClipOverride : _temporaryMoveClipOverride;
            _idleLegacyClipName = ResolveClipNameOverride(effectiveIdleClipOverride, clipNames, ResolveActionClipNameFromMap(clipNames, ActionIdleMa, FindBestIdleClipName(clipNames)));
            _moveStartLegacyClipName = ResolveClipNameOverride(moveStartClipOverride, clipNames, FindBestMoveStartClipName(clipNames));
            _moveLegacyClipName = ResolveClipNameOverride(effectiveMoveClipOverride, clipNames, ResolveActionClipNameFromMap(clipNames, _activeMoveAction, FindBestMoveClipName(clipNames)));
            string effectiveAttackNameOverride = string.IsNullOrWhiteSpace(_runtimeAttackClipOverride) ? attackClipOverride : _runtimeAttackClipOverride;
            _attackLegacyClipName = ResolveClipNameOverride(
                effectiveAttackNameOverride,
                clipNames,
                ResolveActionClipNameFromMap(clipNames, _runtimeAttackActionKey, FindBestAttackClipName(clipNames)));
            if (string.IsNullOrWhiteSpace(_idleLegacyClipName) || string.IsNullOrWhiteSpace(_moveLegacyClipName))
                return false;

            _legacyAnimation.wrapMode = WrapMode.Loop;
            if (!string.IsNullOrWhiteSpace(_idleLegacyClipName))
                _legacyAnimation[_idleLegacyClipName].wrapMode = WrapMode.Loop;
            if (!string.IsNullOrWhiteSpace(_moveLegacyClipName))
                _legacyAnimation[_moveLegacyClipName].wrapMode = WrapMode.Loop;
            if (!string.IsNullOrWhiteSpace(_attackLegacyClipName) && _legacyAnimation.GetClip(_attackLegacyClipName) != null)
                _legacyAnimation[_attackLegacyClipName].wrapMode = WrapMode.Loop;
            if (!string.IsNullOrWhiteSpace(_moveStartLegacyClipName) && _legacyAnimation.GetClip(_moveStartLegacyClipName) != null)
                _legacyAnimation[_moveStartLegacyClipName].wrapMode = WrapMode.Once;
            _legacyAnimation.Play(_idleLegacyClipName);
            _legacyAnimationReady = true;
            return true;
        }

        private static string FindBestIdleClipName(List<string> names)
        {
            return names
                .OrderByDescending(ScoreIdleName)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static string FindBestMoveClipName(List<string> names)
        {
            return names
                .OrderByDescending(ScoreMoveName)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static string FindBestMoveStartClipName(List<string> names)
        {
            return names
                .OrderByDescending(ScoreMoveStartName)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static string FindBestAttackClipName(List<string> names)
        {
            return names
                .OrderByDescending(ScoreAttackName)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static AnimationClip FindBestIdleClip(List<AnimationClip> clips)
        {
            return clips
                .OrderByDescending(c => ScoreIdleName(c.name))
                .ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static AnimationClip FindBestMoveClip(List<AnimationClip> clips)
        {
            return clips
                .OrderByDescending(c => ScoreMoveName(c.name))
                .ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static AnimationClip FindBestAttackClip(List<AnimationClip> clips)
        {
            return clips
                .OrderByDescending(c => ScoreAttackName(c.name))
                .ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static int ScoreIdleName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return int.MinValue;

            int score = 0;
            if (ContainsToken(name, "idle")) score += 300;
            if (ContainsToken(name, "stand")) score += 200;
            if (ContainsToken(name, "run") || ContainsToken(name, "walk")) score -= 200;
            if (ContainsAny(name, "start", "stop", "jump", "land", "back", "left", "right")) score -= 400;
            if (ContainsAny(name, "2h", "blade", "rifle", "smallarms", "unarmed")) score -= 80;
            return score;
        }

        private static int ScoreMoveName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return int.MinValue;

            int score = 0;
            if (ContainsToken(name, "run")) score += 400;
            if (ContainsToken(name, "walk")) score += 300;

            // Prioritize clean locomotion loops over transition/landing clips.
            if (ContainsAny(name, "start", "stop", "jump", "land")) score -= 600;
            if (ContainsAny(name, "back", "left", "right")) score -= 250;
            if (ContainsAny(name, "crawl", "swim", "chair", "fallback", "ground")) score -= 350;
            if (ContainsAny(name, "2h", "blade", "rifle", "smallarms", "unarmed")) score -= 90;
            if (ContainsToken(name, "idle")) score -= 300;
            return score;
        }

        private static int ScoreMoveStartName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return int.MinValue;

            int score = 0;
            if (ContainsToken(name, "unarmed-start")) score += 900;
            if (ContainsToken(name, "start")) score += 700;
            if (ContainsToken(name, "jump-land-run")) score += 550;
            if (ContainsAny(name, "run", "walk")) score += 120;

            if (ContainsToken(name, "stop")) score -= 800;
            if (ContainsToken(name, "idle")) score -= 300;
            if (ContainsAny(name, "back", "left", "right", "crawl", "swim")) score -= 250;
            return score;
        }

        private static int ScoreAttackName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return int.MinValue;

            int score = 0;
            if (ContainsAny(name, "attack", "punch", "kick", "combat")) score += 800;
            if (ContainsAny(name, "unarmed", "fist", "ma", "martial")) score += 450;
            if (ContainsToken(name, "start")) score += 120;
            if (ContainsAny(name, "run", "walk", "jump", "land", "sit", "swim")) score -= 650;
            if (ContainsAny(name, "2h", "rifle", "blade", "smallarms")) score -= 500;
            if (ContainsToken(name, "idle")) score -= 400;
            return score;
        }

        private static bool ContainsToken(string name, string token)
        {
            return name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsAny(string name, params string[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (ContainsToken(name, tokens[i]))
                    return true;
            }

            return false;
        }

        private void DisposeAnimationGraph()
        {
            _animationGraphReady = false;
            _legacyAnimationReady = false;
            _legacyMovePlaying = false;
            _legacyStartPlaying = false;
            _legacyAttackPlaying = false;
            _idleLegacyClipName = string.Empty;
            _moveStartLegacyClipName = string.Empty;
            _moveLegacyClipName = string.Empty;
            _attackLegacyClipName = string.Empty;
            _legacyStartEndTime = 0f;
            _hasLastRootPosition = false;
            _moveBlendWeight = 0f;
            _attackBlendWeight = 0f;

            if (_animationGraph.IsValid())
                _animationGraph.Destroy();
        }

        public IReadOnlyList<string> GetAvailableAnimationClipNames() => availableAnimationClipNames;

        public void SetExternalLocomotionState(bool moving, bool walk)
        {
            if (!moving)
            {
                _externalMoveUntil = -1f;
                return;
            }

            _externalMoveAction = walk ? ActionWalk : ActionRun;
            _externalMoveUntil = Time.time + 0.35f;
        }

        /// <summary>
        /// Drives the direct AO CAT visual from the same input sample used by the
        /// character motor. This keeps presentation deterministic even when Unity's
        /// component/LateUpdate ordering differs between the preview and world player.
        /// </summary>
        public void SetLocalLocomotionIntent(float forward, float strafe)
        {
            _localLocomotionForward = forward;
            _localLocomotionStrafe = strafe;
            _localLocomotionIntentFrame = Time.frameCount;

            if (_spawnedPrefabVisual == null || _sitToggled || _sitTransitionPlaying
                || _jumpInAir || _jumpLandingTransitionPlaying)
                return;

            CatAnimPlayer directAnim = _spawnedPrefabVisual
                .GetComponentInChildren<CatAnimPlayer>(true);
            if (directAnim == null)
                return;

            directAnim.Play(ResolveDirectLocomotionLogicalName(
                forward > 0.001f,
                forward < -0.001f,
                strafe < -0.001f,
                strafe > 0.001f), CatAnimPlayer.DefaultBlendSeconds);
        }

        public void SetTemporaryLocomotionClipOverride(string preferredClipName)
        {
            if (string.IsNullOrWhiteSpace(preferredClipName))
            {
                ClearTemporaryLocomotionClipOverride();
                return;
            }

            RefreshAnimationClipCatalogForInspector();
            string resolved = FindBestNameMatch(availableAnimationClipNames, preferredClipName);
            if (string.IsNullOrWhiteSpace(resolved))
                resolved = preferredClipName;

            _temporaryIdleClipOverride = resolved;
            _temporaryMoveClipOverride = resolved;
            ApplyAnimationOverridesNow();
        }

        public void ClearTemporaryLocomotionClipOverride()
        {
            if (string.IsNullOrWhiteSpace(_temporaryIdleClipOverride) && string.IsNullOrWhiteSpace(_temporaryMoveClipOverride))
                return;

            _temporaryIdleClipOverride = string.Empty;
            _temporaryMoveClipOverride = string.Empty;
            ApplyAnimationOverridesNow();
        }

        public void RefreshAnimationClipCatalogForInspector()
        {
            var names = new List<string>();

            if (_legacyAnimation != null)
            {
                foreach (AnimationState state in _legacyAnimation)
                {
                    if (state?.clip == null || string.IsNullOrWhiteSpace(state.name))
                        continue;
                    names.Add(state.name);
                }
            }

            if (_spawnedAnimator != null && _spawnedAnimator.runtimeAnimatorController != null)
            {
                names.AddRange(_spawnedAnimator.runtimeAnimatorController.animationClips
                    .Where(c => c != null && !string.IsNullOrWhiteSpace(c.name))
                    .Select(c => c.name));
            }

            RefreshAvailableClipNames(names);
        }

        public void ApplyAnimationOverridesNow()
        {
            if (_spawnedPrefabVisual == null)
                return;

            InitializeAnimationPlayback();
            UpdateMovementAnimation();
        }

        private void RefreshAvailableClipNames(IEnumerable<string> rawNames)
        {
            availableAnimationClipNames = rawNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ResolveClipNameOverride(string configuredName, List<string> availableNames, string fallback)
        {
            if (string.IsNullOrWhiteSpace(configuredName))
                return fallback;

            return availableNames.FirstOrDefault(n => string.Equals(n, configuredName, StringComparison.OrdinalIgnoreCase))
                ?? fallback;
        }

        private static AnimationClip ResolveClipOverride(string configuredName, List<AnimationClip> availableClips, AnimationClip fallback)
        {
            if (string.IsNullOrWhiteSpace(configuredName))
                return fallback;

            return availableClips.FirstOrDefault(c => c != null && string.Equals(c.name, configuredName, StringComparison.OrdinalIgnoreCase))
                ?? fallback;
        }

        private string ResolveSexSwapToken()
        {
            var sex = bridge != null ? bridge.Sex : CharacterRuntimeBridge.CharacterSex.Male;
            string sexKey = sex switch
            {
                CharacterRuntimeBridge.CharacterSex.Female => "female",
                CharacterRuntimeBridge.CharacterSex.Uni => "uni",
                _ => "male"
            };

            if (_animationSwaps.TryGetValue(sexKey, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
                return mapped;

            return sexKey;
        }

        private void RefreshAnimationMappingsIfNeeded()
        {
            string token = ResolveSexSwapToken();
            if (string.Equals(token, _lastResolvedSexToken, StringComparison.OrdinalIgnoreCase))
                return;

            _lastResolvedSexToken = token;
            ApplyAnimationOverridesNow();
        }

        private string ResolveActionTemplateToClipName(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey))
                return string.Empty;

            if (!_animationTemplates.TryGetValue(actionKey, out var template) || string.IsNullOrWhiteSpace(template))
                return string.Empty;

            string token = ResolveSexSwapToken();
            return template
                .Replace("{swaps}", token)
                .Replace("{SWAPS}", token)
                .Replace("{Swaps}", token);
        }

        private string ResolveActionClipNameFromMap(IEnumerable<string> availableNames, string actionKey, string fallback)
        {
            if (animationController != null)
            {
                string explicitOverride = animationController.GetClipOverride(actionKey);
                if (!string.IsNullOrWhiteSpace(explicitOverride))
                {
                    var explicitMatch = FindBestNameMatch(availableNames, explicitOverride);
                    if (!string.IsNullOrWhiteSpace(explicitMatch))
                        return explicitMatch;
                }
            }

            string resolved = ResolveActionTemplateToClipName(actionKey);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                var matched = FindBestNameMatch(availableNames, resolved);
                if (!string.IsNullOrWhiteSpace(matched))
                    return matched;
            }

            var semantic = FindSemanticLocomotionNameMatch(availableNames, actionKey);
            if (!string.IsNullOrWhiteSpace(semantic))
                return semantic;

            var direct = FindBestNameMatch(availableNames, actionKey);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            return fallback;
        }

        private AnimationClip ResolveActionClipFromMap(List<AnimationClip> availableClips, string actionKey, AnimationClip fallback)
        {
            if (animationController != null)
            {
                string explicitOverride = animationController.GetClipOverride(actionKey);
                if (!string.IsNullOrWhiteSpace(explicitOverride))
                {
                    var explicitMatch = FindBestClipMatch(availableClips, explicitOverride);
                    if (explicitMatch != null)
                        return explicitMatch;
                }
            }

            string resolved = ResolveActionTemplateToClipName(actionKey);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                var matched = FindBestClipMatch(availableClips, resolved);
                if (matched != null)
                    return matched;
            }

            string semanticName = FindSemanticLocomotionNameMatch(
                availableClips?.Where(clip => clip != null).Select(clip => clip.name),
                actionKey);
            if (!string.IsNullOrWhiteSpace(semanticName))
            {
                AnimationClip semanticClip = availableClips.FirstOrDefault(clip =>
                    clip != null && string.Equals(
                        clip.name, semanticName, StringComparison.OrdinalIgnoreCase));
                if (semanticClip != null)
                    return semanticClip;
            }

            return fallback;
        }

        private static string FindSemanticLocomotionNameMatch(
            IEnumerable<string> availableNames, string actionKey)
        {
            if (availableNames == null || string.IsNullOrWhiteSpace(actionKey))
                return null;

            string[] aliases;
            if (string.Equals(actionKey, ActionRunBackwards, StringComparison.OrdinalIgnoreCase))
                aliases = new[] { "run-back", "run_back", "run-backwards", "run_backwards" };
            else if (string.Equals(actionKey, ActionStrafeLeft, StringComparison.OrdinalIgnoreCase))
                aliases = new[] { "walk-left", "walk_left", "strafe-left", "strafe_left" };
            else if (string.Equals(actionKey, ActionStrafeRight, StringComparison.OrdinalIgnoreCase))
                aliases = new[] { "walk-right", "walk_right", "strafe-right", "strafe_right" };
            else if (string.Equals(actionKey, ActionWalk, StringComparison.OrdinalIgnoreCase))
                aliases = new[] { "_walk_", "-walk-", "walk-stand", "walk_stand" };
            else if (string.Equals(actionKey, ActionRun, StringComparison.OrdinalIgnoreCase))
                aliases = new[] { "_run_", "-run-", "run-stand", "run_stand" };
            else
                return null;

            var names = availableNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            bool forwardLocomotion =
                string.Equals(actionKey, ActionRun, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionKey, ActionWalk, StringComparison.OrdinalIgnoreCase);
            foreach (string alias in aliases)
            {
                string match = names.FirstOrDefault(name =>
                    name.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0
                    && (!forwardLocomotion
                        || (name.IndexOf("back", StringComparison.OrdinalIgnoreCase) < 0
                            && name.IndexOf("left", StringComparison.OrdinalIgnoreCase) < 0
                            && name.IndexOf("right", StringComparison.OrdinalIgnoreCase) < 0
                            && name.IndexOf("jump", StringComparison.OrdinalIgnoreCase) < 0
                            && name.IndexOf("land", StringComparison.OrdinalIgnoreCase) < 0)));
                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }

            return null;
        }

        private static string FindBestNameMatch(IEnumerable<string> availableNames, string desired)
        {
            if (availableNames == null || string.IsNullOrWhiteSpace(desired))
                return null;

            var names = availableNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            var exact = names.FirstOrDefault(n => string.Equals(n, desired, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact))
                return exact;

            var endsWith = names.FirstOrDefault(n => n.EndsWith(desired, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(endsWith))
                return endsWith;

            return names.FirstOrDefault(n => n.IndexOf(desired, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static AnimationClip FindBestClipMatch(IEnumerable<AnimationClip> availableClips, string desired)
        {
            if (availableClips == null || string.IsNullOrWhiteSpace(desired))
                return null;

            var clips = availableClips.Where(c => c != null && !string.IsNullOrWhiteSpace(c.name)).ToList();
            var exact = clips.FirstOrDefault(c => string.Equals(c.name, desired, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var endsWith = clips.FirstOrDefault(c => c.name.EndsWith(desired, StringComparison.OrdinalIgnoreCase));
            if (endsWith != null)
                return endsWith;

            return clips.FirstOrDefault(c => c.name.IndexOf(desired, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void HandleAttackToggleInput()
        {
            if (!enableUnarmedAttackToggle)
                return;

            if (WasQPressedThisFrame())
            {
                attackToggleActive = !attackToggleActive;
                if (attackToggleActive)
                {
                    ApplyAttackAnimationRule(force: true);
                    BeginAttackCombatSequence();
                }
                else if (!_sitToggled && !_sitTransitionPlaying)
                {
                    EndAttackCombatSequence();
                    _runtimeAttackClipOverride = string.Empty;
                    _freezeAttackLoopPose = false;
                }
                ApplyAnimationOverridesNow();
            }
        }

        private void RefreshAttackAnimationRuleIfNeeded()
        {
            if (!attackToggleActive || _sitToggled || _sitTransitionPlaying)
                return;
            if (_isDestroying || !Application.isPlaying)
                return;

            string signature = BuildAttackAnimationSignature();
            if (string.Equals(signature, _lastAttackAnimationSignature, StringComparison.Ordinal))
                return;

            ApplyAttackAnimationRule(force: true);
            _lastAttackAnimationSignature = signature;
        }

        private void ApplyAttackAnimationRule(bool force)
        {
            var resolved = ResolveAttackAnimationSelection();
            if (!resolved.HasValue)
            {
                if (force)
                    SetRuntimeAttackClipToAction(ActionIdleMa, PreferredAttackIdleClip);
                _activeAttackRule = null;
                _activeAttackIdleActionKey = ActionIdleMa;
                _activeAttackSkillId = 0;
                _activeAttackIsRanged = false;
                return;
            }

            var pick = resolved.Value;
            _activeAttackRule = pick.rule;
            _activeAttackIdleActionKey = ResolvePreferredIdleActionKey(pick.rule, pick.actionKey);
            _activeAttackSkillId = pick.attackSkillId;
            _activeAttackIsRanged = IsRangedAttackSkill(_activeAttackSkillId);
            SetRuntimeAttackClipToAction(
                _activeAttackIdleActionKey,
                null);
        }

        private (string actionKey, string clipName, AttackAnimationRule rule, int attackSkillId)? ResolveAttackAnimationSelection()
        {
            if (_attackAnimationRules != null && !_attackAnimationRules.enabled)
                return (ActionIdleMa, PreferredAttackIdleClip, null, 0);

            string sexToken = ResolveAttackSexToken();
            if (!TryBuildAttackWeaponProfile(out int attackSkillId, out WeaponHandMode handMode))
            {
                var unarmed = _attackAnimationRules?.unarmed;
                string unarmedClip = ResolveRuleClipCandidate(unarmed, sexToken);
                if (string.IsNullOrWhiteSpace(unarmedClip))
                    unarmedClip = PreferredAttackIdleClip;
                return (string.IsNullOrWhiteSpace(unarmed?.actionKey) ? ActionIdleMa : unarmed.actionKey, unarmedClip, unarmed, 0);
            }

            var rules = _attackAnimationRules?.rules;
            if (rules == null || rules.Count == 0)
                return (ActionIdleMa, PreferredAttackIdleClip, null, attackSkillId);

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null || !rule.enabled)
                    continue;

                if (!RuleMatchesHandMode(rule.handMode, handMode))
                    continue;
                if (rule.attackSkillIds != null && rule.attackSkillIds.Count > 0 && !rule.attackSkillIds.Contains(attackSkillId))
                    continue;

                string clip = ResolveRuleClipCandidate(rule, sexToken);
                return (string.IsNullOrWhiteSpace(rule.actionKey) ? ActionIdleMa : rule.actionKey, clip, rule, attackSkillId);
            }

            // Fallback for single-wield one-handed weapons:
            // If no strict handMode match was found, allow "either_or_both" rules when
            // the attack skill matches. This prevents pistols/1he from dropping to unarmed.
            if (handMode == WeaponHandMode.SingleOnly)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    if (rule == null || !rule.enabled)
                        continue;
                    if (!string.Equals((rule.handMode ?? string.Empty).Trim(), "either_or_both", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (rule.attackSkillIds != null && rule.attackSkillIds.Count > 0 && !rule.attackSkillIds.Contains(attackSkillId))
                        continue;

                    string clip = ResolveRuleClipCandidate(rule, sexToken);
                    return (string.IsNullOrWhiteSpace(rule.actionKey) ? ActionIdleMa : rule.actionKey, clip, rule, attackSkillId);
                }
            }

            int fallbackSkillId = attackSkillId;
            if (fallbackSkillId <= 0 || (!IsRangedAttackSkill(fallbackSkillId) && IsLikelyRangedWeaponEquipped()))
                fallbackSkillId = handMode == WeaponHandMode.EitherOrBoth ? 112 : 116;

            if (TryBuildRangedFallbackAttackRule(fallbackSkillId, handMode, out var fallbackRule))
            {
                string clip = ResolveRuleClipCandidate(fallbackRule, sexToken);
                return (fallbackRule.actionKey, clip, fallbackRule, fallbackSkillId);
            }

            return (ActionIdleMa, PreferredAttackIdleClip, null, attackSkillId);
        }

        private string ResolvePreferredIdleActionKey(AttackAnimationRule rule, string fallbackActionKey)
        {
            string configured = rule?.idleActionKey;
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.Trim();
            if (!string.IsNullOrWhiteSpace(fallbackActionKey))
                return fallbackActionKey.Trim();
            return ActionIdleMa;
        }

        private void BeginAttackCombatSequence()
        {
            _attackRuleHitCounter = 0;
            _attackRuleHitCursor = 0;
            _attackRuleSpecialCursor = 0;
            _attackRuleOneShotEndTime = 0f;
            _attackPreferLeftNext = true;
            _attackLastActionKey = string.Empty;
            RefreshAttackHandContext();
            StartAttackWindupPhase();

            string startAction = ResolveStartActionForCurrentHands(_activeAttackRule);
            if (!string.IsNullOrWhiteSpace(startAction))
            {
                PlayActionAsOneShot(startAction);
            }
        }

        private string ResolveStartActionForCurrentHands(AttackAnimationRule rule)
        {
            string configuredStart = rule?.startActionKey?.Trim();
            if (string.IsNullOrWhiteSpace(configuredStart))
                return string.Empty;

            int configuredSide = ResolveActionSide(configuredStart);
            if (configuredSide != 0)
                return configuredStart;

            bool singleLeft = _attackLeftWeaponEquipped && !_attackRightWeaponEquipped;
            bool singleRight = _attackRightWeaponEquipped && !_attackLeftWeaponEquipped;
            if (!singleLeft && !singleRight)
                return configuredStart;

            int desiredSide = singleLeft ? -1 : 1;
            var sidedHits = rule?.hitActionKeys;
            if (sidedHits == null || sidedHits.Count == 0)
                return configuredStart;

            for (int i = 0; i < sidedHits.Count; i++)
            {
                string key = sidedHits[i]?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (ResolveActionSide(key) != desiredSide)
                    continue;
                if (!IsValidCombatActionCandidate(key))
                    continue;
                return key;
            }

            return configuredStart;
        }

        private void EndAttackCombatSequence()
        {
            _attackRuleOneShotEndTime = 0f;
            _attackRuleNextHitTime = 0f;
            _attackRuleHitCounter = 0;
            _attackRuleHitCursor = 0;
            _attackRuleSpecialCursor = 0;
            _attackLastActionKey = string.Empty;
            _attackCycleRunning = false;
            _attackCyclePaused = false;
            _freezeAttackLoopPose = false;
            SetAttackCycleUiVisible(false);
        }

        private void UpdateAttackCombatSequence(bool canRunCombatSequence, bool isMoving)
        {
            if (!attackToggleActive || !canRunCombatSequence)
            {
                _attackCycleRunning = false;
                _attackCyclePaused = false;
                SetAttackCycleUiVisible(false);
                return;
            }
            if (_sitToggled || _sitTransitionPlaying)
            {
                SetAttackCycleUiVisible(false);
                return;
            }
            if (_isDestroying || !Application.isPlaying)
                return;

            EnsureAttackCycleUi();
            SetAttackCycleUiVisible(true);
            if (!_attackCycleRunning)
                StartAttackWindupPhase();

            bool notGrounded = _characterController != null && !_characterController.isGrounded;
            bool rangedMustStandStill = _activeAttackIsRanged && (isMoving || _jumpInAir || _jumpLandingTransitionPlaying || notGrounded);
            bool shouldPauseForMovement = rangedMustStandStill || _externalAttackCyclePaused;
            if (shouldPauseForMovement)
            {
                if (!_attackCyclePaused)
                {
                    _attackCyclePaused = true;
                    _attackCyclePauseStartedAt = Time.time;
                }
                return;
            }
            if (_attackCyclePaused)
            {
                float pausedDuration = Mathf.Max(0f, Time.time - _attackCyclePauseStartedAt);
                _attackPhaseStartTime += pausedDuration;
                _attackCyclePaused = false;
            }

            if (!_runtimeAttackLoopEnabled)
            {
                if (!_activeAttackIsRanged && isMoving)
                {
                    // Melee should return to movement animation between strikes while moving.
                    _runtimeAttackClipOverride = string.Empty;
                    _runtimeAttackLoopEnabled = true;
                    _freezeAttackLoopPose = false;
                }
                else
                {
                    SetRuntimeAttackClipToAction(_activeAttackIdleActionKey);
                }
            }

            UpdateAttackCycleUi();

            if (Time.time < (_attackPhaseStartTime + _attackPhaseDuration))
                return;

            RefreshAttackHandContext();
            if (!_attackPhaseIsRecharge)
            {
                string actionKey = ResolveNextCombatActionKey(_activeAttackRule);
                if (!string.IsNullOrWhiteSpace(actionKey))
                {
                    float duration = PlayActionAsOneShot(actionKey);
                    duration = AdjustCombatActionDuration(actionKey, duration);
                    _attackRuleOneShotEndTime = Time.time + Mathf.Max(0.05f, duration);
                    if (_activeAttackIsRanged)
                        TriggerWeaponShotVfx(_attackPhaseSide, actionKey);
                    else
                        TriggerMeleeHitVfx();
                    _attackRuleHitCounter++;
                    _attackLastActionKey = actionKey;
                }

                StartAttackRechargePhase();
            }
            else
            {
                StartAttackWindupPhase();
            }
        }

        private float ResolveAttackHitInterval(AttackAnimationRule rule)
        {
            float configured = rule != null ? rule.hitIntervalSeconds : 1.05f;
            return Mathf.Max(0.15f, configured);
        }

        private static float AdjustCombatActionDuration(string actionKey, float duration)
        {
            if (string.IsNullOrWhiteSpace(actionKey))
                return duration;

            string key = actionKey.Trim();
            // 1h ranged shot/burst/fullauto clips often contain multiple recoil beats.
            // Clamp one-shot hold time so side alternation can advance per shot.
            if (key.StartsWith("1h ranged shot", StringComparison.OrdinalIgnoreCase))
                return Mathf.Min(duration, 0.22f);
            if (key.StartsWith("1h ranged Burst", StringComparison.OrdinalIgnoreCase))
                return Mathf.Min(duration, 0.28f);
            if (key.StartsWith("1h ranged Full Auto", StringComparison.OrdinalIgnoreCase))
                return Mathf.Min(duration, 0.32f);

            return duration;
        }

        private string ResolveNextCombatActionKey(AttackAnimationRule rule)
        {
            if (rule == null)
                return string.Empty;

            return ResolveActionFromList(rule.hitActionKeys, ref _attackRuleHitCursor);
        }

        private string ResolveActionFromList(List<string> actionKeys, ref int cursor)
        {
            if (actionKeys == null || actionKeys.Count == 0)
                return string.Empty;

            int count = actionKeys.Count;
            int preferredSide = ResolvePreferredAttackSide();
            var candidates = new List<(string actionKey, int side, int index)>(count);

            for (int i = 0; i < count; i++)
            {
                int idx = (cursor + i) % count;
                string raw = actionKeys[idx];
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                string candidate = raw.Trim();
                if (!IsValidCombatActionCandidate(candidate))
                    continue;
                int side = ResolveActionSide(candidate);
                if (side != 0 && !IsActionSideAllowedByEquippedHands(side))
                    continue;
                candidates.Add((candidate, side, idx));
            }

            if (candidates.Count == 0)
                return string.Empty;

            bool dualAlternatingPossible =
                _attackLeftWeaponEquipped
                && _attackRightWeaponEquipped
                && candidates.Any(c => c.side < 0)
                && candidates.Any(c => c.side > 0);

            if (dualAlternatingPossible)
            {
                int desiredSide = preferredSide != 0 ? preferredSide : (_attackPreferLeftNext ? -1 : 1);
                var desired = candidates.Where(c => c.side == desiredSide).ToList();
                var desiredNoRepeat = desired
                    .Where(c => !string.Equals(c.actionKey, _attackLastActionKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var picked = desiredNoRepeat.Count > 0 ? desiredNoRepeat[0] : desired[0];
                cursor = (picked.index + 1) % count;
                OnCombatActionPicked(picked.side);
                return picked.actionKey;
            }

            // Pass 1: prefer desired side cadence (left/right) when possible.
            for (int i = 0; i < count; i++)
            {
                int idx = (cursor + i) % count;
                string action = actionKeys[idx];
                if (string.IsNullOrWhiteSpace(action))
                    continue;
                string candidate = action.Trim();
                if (!IsValidCombatActionCandidate(candidate))
                    continue;
                int side = ResolveActionSide(candidate);
                if (preferredSide != 0 && side != 0 && side != preferredSide)
                    continue;
                if (side != 0 && !IsActionSideAllowedByEquippedHands(side))
                    continue;
                if (count > 1 && string.Equals(candidate, _attackLastActionKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                cursor = (idx + 1) % count;
                OnCombatActionPicked(side);
                return candidate;
            }

            // Pass 2: any allowed side that resolves and isn't an immediate duplicate.
            for (int i = 0; i < count; i++)
            {
                int idx = (cursor + i) % count;
                string action = actionKeys[idx];
                if (string.IsNullOrWhiteSpace(action))
                    continue;
                string candidate = action.Trim();
                if (!IsValidCombatActionCandidate(candidate))
                    continue;
                int side = ResolveActionSide(candidate);
                if (side != 0 && !IsActionSideAllowedByEquippedHands(side))
                    continue;
                if (count > 1 && string.Equals(candidate, _attackLastActionKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                cursor = (idx + 1) % count;
                OnCombatActionPicked(side);
                return candidate;
            }

            // Pass 3: last-resort allowed action (avoids deadlock when only one valid key exists).
            for (int i = 0; i < count; i++)
            {
                int idx = (cursor + i) % count;
                string action = actionKeys[idx];
                if (string.IsNullOrWhiteSpace(action))
                    continue;
                string candidate = action.Trim();
                if (!IsValidCombatActionCandidate(candidate))
                    continue;
                int side = ResolveActionSide(candidate);
                if (side != 0 && !IsActionSideAllowedByEquippedHands(side))
                    continue;

                cursor = (idx + 1) % count;
                OnCombatActionPicked(side);
                return candidate;
            }

            return string.Empty;
        }

        private void RefreshAttackHandContext()
        {
            _attackRightWeaponEquipped = IsWeaponEquippedInAttackSlot(rightHandEquipSlotId);
            _attackLeftWeaponEquipped = IsWeaponEquippedInAttackSlot(leftHandEquipSlotId);

            // Unarmed profile should alternate both sides naturally.
            if (!_attackRightWeaponEquipped && !_attackLeftWeaponEquipped)
            {
                _attackRightWeaponEquipped = true;
                _attackLeftWeaponEquipped = true;
            }
        }

        private bool IsWeaponEquippedInAttackSlot(int slotId)
        {
            var equipped = bridge?.Character?.Equipment?.GetAllEquipped();
            if (equipped == null)
                return false;
            if (!equipped.TryGetValue(slotId, out var instanceId) || instanceId == 0)
                return false;
            return true;
        }

        private void StartAttackWindupPhase()
        {
            _attackCycleRunning = true;
            _attackCyclePaused = false;
            _attackPhaseIsRecharge = false;

            int side = ResolvePreferredAttackSide();
            if (side == 0)
                side = _attackRightWeaponEquipped ? 1 : (_attackLeftWeaponEquipped ? -1 : 1);
            _attackPhaseSide = side;

            ResolveAttackTimingsForSide(side, out _attackPhaseAttackSeconds, out _attackPhaseRechargeSeconds);
            _attackPhaseDuration = Mathf.Max(0.05f, _attackPhaseAttackSeconds);
            _attackPhaseStartTime = Time.time;
            UpdateAttackCycleUi();
        }

        private void StartAttackRechargePhase()
        {
            _attackPhaseIsRecharge = true;
            _attackPhaseDuration = Mathf.Max(0.05f, _attackPhaseRechargeSeconds);
            _attackPhaseStartTime = Time.time;
            UpdateAttackCycleUi();
        }

        private void ResolveAttackTimingsForSide(int side, out float attackSeconds, out float rechargeSeconds)
        {
            attackSeconds = 1f;
            rechargeSeconds = 1f;

            long instanceId = GetEquippedInstanceIdForAttackSide(side);
            if (instanceId == 0)
                return;

            if (AO.Core.Characters.CharacterEquipment.GetItemInstance == null)
                return;

            var coreInst = AO.Core.Characters.CharacterEquipment.GetItemInstance(instanceId);
            int aoid = coreInst?.Definition?.AOID ?? 0;
            if (aoid <= 0)
                return;

            AO.Data.Unity.AODataManager.EnsureInstance();
            var dataManager = AO.Data.Unity.AODataManager.Instance;
            var raw = dataManager?.GetRawItemByAoid(aoid);
            int attackRaw = 0;
            int rechargeRaw = 0;
            int attackSpeedRaw = 0;
            if (raw?.StatValues != null)
            {
                attackRaw = raw.StatValues.FirstOrDefault(v => v != null && v.Stat == 294)?.RawValue ?? 0;   // AttackDelay
                rechargeRaw = raw.StatValues.FirstOrDefault(v => v != null && v.Stat == 210)?.RawValue ?? 0; // RechargeDelay
                attackSpeedRaw = raw.StatValues.FirstOrDefault(v => v != null && v.Stat == 3)?.RawValue ?? 0; // AttackSpeed
            }

            // Fallback to core metadata if raw lookup is unavailable.
            if ((attackRaw <= 0 || rechargeRaw <= 0 || attackSpeedRaw <= 0) && coreInst?.Definition?.Modifiers != null)
            {
                var coreMods = coreInst.Definition.Modifiers;
                if (attackRaw <= 0)
                    attackRaw = coreMods.Where(m => m.StatId == 294).Select(m => m.Value).FirstOrDefault();
                if (rechargeRaw <= 0)
                    rechargeRaw = coreMods.Where(m => m.StatId == 210).Select(m => m.Value).FirstOrDefault();
                if (attackSpeedRaw <= 0)
                    attackSpeedRaw = coreMods.Where(m => m.StatId == 3).Select(m => m.Value).FirstOrDefault();
            }

            float resolvedAttack = ConvertAttackDelayToSeconds(attackRaw, attackSpeedRaw);
            float resolvedRecharge = ConvertRechargeDelayToSeconds(rechargeRaw);

            attackSeconds = Mathf.Clamp(resolvedAttack, 0.05f, 10f);
            rechargeSeconds = Mathf.Clamp(resolvedRecharge, 0.05f, 10f);

            if (logAttackTimingResolution)
            {
                Debug.Log(
                    $"[AttackTiming] side={(side < 0 ? "L" : "R")} aoid={aoid} " +
                    $"attackRaw={attackRaw} rechargeRaw={rechargeRaw} attackSpeedRaw={attackSpeedRaw} " +
                    $"resolvedAttack={attackSeconds:0.00}s resolvedRecharge={rechargeSeconds:0.00}s");
            }
        }

        private static float ConvertAttackDelayToSeconds(int attackDelayRaw, int attackSpeedRaw)
        {
            if (attackDelayRaw > 0)
            {
                // Some DB exports store delay directly in seconds (e.g. 3),
                // others in centiseconds (e.g. 129 => 1.29s).
                return attackDelayRaw <= 50
                    ? attackDelayRaw
                    : (attackDelayRaw / 100f);
            }

            if (attackSpeedRaw > 0)
            {
                // DB exports may store AttackSpeed either as direct seconds (e.g. 3)
                // or centiseconds (e.g. 300). Handle both.
                return attackSpeedRaw > 50
                    ? (attackSpeedRaw / 100f)
                    : attackSpeedRaw;
            }

            return 1f;
        }

        private static float ConvertRechargeDelayToSeconds(int rechargeDelayRaw)
        {
            if (rechargeDelayRaw > 0)
            {
                return rechargeDelayRaw <= 50
                    ? rechargeDelayRaw
                    : (rechargeDelayRaw / 100f);
            }
            return 1f;
        }


        private void EnsureAttackCycleUi()
        {
            if (_attackCycleCanvas != null)
                return;

            var canvasGo = new GameObject("AttackCycleUi", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _attackCycleCanvas = canvasGo.GetComponent<Canvas>();
            _attackCycleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _attackCycleCanvas.sortingOrder = 2000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var rootGo = new GameObject("BarRoot", typeof(RectTransform), typeof(Image));
            rootGo.transform.SetParent(canvasGo.transform, false);
            _attackCycleRoot = rootGo.GetComponent<RectTransform>();
            _attackCycleRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _attackCycleRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _attackCycleRoot.pivot = new Vector2(0.5f, 0.5f);
            _attackCycleRoot.anchoredPosition = new Vector2(0f, -120f);
            _attackCycleRoot.sizeDelta = new Vector2(180f, 12f);
            _attackCycleBackgroundImage = rootGo.GetComponent<Image>();
            EnsureAttackCycleUiSprite();
            _attackCycleBackgroundImage.sprite = _attackCycleUiSprite;
            _attackCycleBackgroundImage.type = Image.Type.Sliced;
            _attackCycleBackgroundImage.color = new Color(0f, 0f, 0f, 0.55f);
            _attackCycleBackgroundImage.raycastTarget = false;

            var fillGo = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(rootGo.transform, false);
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = new Vector2(1f, 1f);
            fillRect.offsetMax = new Vector2(-1f, -1f);
            _attackCycleFillImage = fillGo.GetComponent<Image>();
            _attackCycleFillImage.sprite = _attackCycleUiSprite;
            _attackCycleFillImage.type = Image.Type.Filled;
            _attackCycleFillImage.fillMethod = Image.FillMethod.Horizontal;
            _attackCycleFillImage.fillOrigin = 0;
            _attackCycleFillImage.fillAmount = 0f;
            _attackCycleFillImage.color = new Color(0.93f, 0.58f, 0.12f, 0.95f);
            _attackCycleFillImage.raycastTarget = false;

            canvasGo.SetActive(false);
        }

        private void EnsureAttackCycleUiSprite()
        {
            if (_attackCycleUiSprite != null)
                return;

            _attackCycleUiTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _attackCycleUiTexture.SetPixels(new[]
            {
                Color.white, Color.white,
                Color.white, Color.white
            });
            _attackCycleUiTexture.Apply(false, true);
            _attackCycleUiSprite = Sprite.Create(
                _attackCycleUiTexture,
                new Rect(0f, 0f, _attackCycleUiTexture.width, _attackCycleUiTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
        }

        private void SetAttackCycleUiVisible(bool visible)
        {
            if (_attackCycleCanvas == null)
                return;
            if (_attackCycleCanvas.gameObject.activeSelf != visible)
                _attackCycleCanvas.gameObject.SetActive(visible);
        }

        private void UpdateAttackCycleUi()
        {
            if (_attackCycleFillImage == null)
                return;

            float t = _attackPhaseDuration > 0.0001f
                ? Mathf.Clamp01((Time.time - _attackPhaseStartTime) / _attackPhaseDuration)
                : 1f;

            if (_attackPhaseIsRecharge)
            {
                _attackCycleFillImage.fillAmount = 1f - t;
                _attackCycleFillImage.color = new Color(0.25f, 0.71f, 0.95f, 0.95f);
            }
            else
            {
                _attackCycleFillImage.fillAmount = t;
                _attackCycleFillImage.color = new Color(0.93f, 0.58f, 0.12f, 0.95f);
            }
        }

        private int ResolvePreferredAttackSide()
        {
            if (_attackLeftWeaponEquipped && _attackRightWeaponEquipped)
                return _attackPreferLeftNext ? -1 : 1;
            if (_attackLeftWeaponEquipped)
                return -1;
            if (_attackRightWeaponEquipped)
                return 1;
            return 0;
        }

        private bool IsActionSideAllowedByEquippedHands(int side)
        {
            if (side == 0)
                return true;
            if (side < 0)
                return _attackLeftWeaponEquipped;
            return _attackRightWeaponEquipped;
        }

        private void OnCombatActionPicked(int side)
        {
            if (side < 0)
                _attackPreferLeftNext = false;
            else if (side > 0)
                _attackPreferLeftNext = true;
        }

        private bool IsValidCombatActionCandidate(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey))
                return false;

            string clipName = ResolveActionClipNameFromMap(availableAnimationClipNames, actionKey, string.Empty);
            return !string.IsNullOrWhiteSpace(clipName);
        }

        private static int ResolveActionSide(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey))
                return 0;

            string s = actionKey.ToLowerInvariant();
            bool left = s.Contains("lhand")
                        || s.Contains("(larm)")
                        || s.Contains(" left")
                        || s.StartsWith("left ");
            bool right = s.Contains("rhand")
                         || s.Contains("(rarm)")
                         || s.Contains(" right")
                         || s.StartsWith("right ");

            if (left && right)
                return 0;
            if (left)
                return -1;
            if (right)
                return 1;
            return 0;
        }

        private static bool RuleMatchesHandMode(string ruleHandMode, WeaponHandMode handMode)
        {
            string mode = (ruleHandMode ?? "any").Trim().ToLowerInvariant();
            return mode switch
            {
                "single_only" => handMode == WeaponHandMode.SingleOnly,
                "either_or_both" => handMode == WeaponHandMode.EitherOrBoth,
                _ => true
            };
        }

        private static bool IsRangedAttackSkill(int statId)
        {
            return statId is 109 or 110 or 111 or 112 or 114 or 115 or 116 or 121 or 133;
        }

        private string ResolveRuleClipCandidate(AttackAnimationRule rule, string sexToken)
        {
            if (rule == null)
                return string.Empty;

            if (rule.clipCandidatesBySex != null && rule.clipCandidatesBySex.Count > 0)
            {
                if (rule.clipCandidatesBySex.TryGetValue(sexToken, out var sexCandidates))
                {
                    string match = FindFirstAvailableClipCandidate(sexCandidates);
                    if (!string.IsNullOrWhiteSpace(match))
                        return match;
                }

                if (rule.clipCandidatesBySex.TryGetValue("default", out var defaults))
                {
                    string match = FindFirstAvailableClipCandidate(defaults);
                    if (!string.IsNullOrWhiteSpace(match))
                        return match;
                }
            }

            return FindFirstAvailableClipCandidate(rule.clipCandidates);
        }

        private string FindFirstAvailableClipCandidate(IEnumerable<string> candidates)
        {
            if (candidates == null)
                return string.Empty;

            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;
                string normalized = candidate.Trim();
                string match = FindBestNameMatch(availableAnimationClipNames, normalized);
                if (!string.IsNullOrWhiteSpace(match))
                    return match;

                string[] prefixes = { "male_", "female_", "uni_" };
                for (int i = 0; i < prefixes.Length; i++)
                {
                    if (!normalized.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                        continue;
                    string unprefixed = normalized.Substring(prefixes[i].Length);
                    match = FindBestNameMatch(availableAnimationClipNames, unprefixed);
                    if (!string.IsNullOrWhiteSpace(match))
                        return match;
                }
            }

            return string.Empty;
        }

        private string ResolveAttackSexToken()
        {
            var sex = bridge != null ? bridge.Sex : CharacterRuntimeBridge.CharacterSex.Male;
            return sex switch
            {
                CharacterRuntimeBridge.CharacterSex.Female => "female",
                CharacterRuntimeBridge.CharacterSex.Uni => "uni",
                _ => "male"
            };
        }

        private bool TryBuildAttackWeaponProfile(out int attackSkillId, out WeaponHandMode handMode)
        {
            attackSkillId = 0;
            handMode = WeaponHandMode.Any;

            var equipped = bridge?.Character?.Equipment?.GetAllEquipped();
            if (equipped == null || equipped.Count == 0 || AO.Core.Characters.CharacterEquipment.GetItemInstance == null)
                return false;

            long weaponInstanceId = 0;
            long fallbackInstanceId = 0;
            bool hasRight = equipped.TryGetValue(rightHandEquipSlotId, out var rightInst);
            bool hasLeft = equipped.TryGetValue(leftHandEquipSlotId, out var leftInst);
            bool rightOccupied = hasRight && rightInst != 0;
            bool leftOccupied = hasLeft && leftInst != 0;
            if (hasRight && rightInst != 0)
            {
                var rightCore = AO.Core.Characters.CharacterEquipment.GetItemInstance(rightInst);
                if (IsWeaponDefinition(rightCore?.Definition))
                    weaponInstanceId = rightInst;
                else if (rightInst != 0 && fallbackInstanceId == 0)
                    fallbackInstanceId = rightInst;
            }
            if (weaponInstanceId == 0 && hasLeft && leftInst != 0)
            {
                var leftCore = AO.Core.Characters.CharacterEquipment.GetItemInstance(leftInst);
                if (IsWeaponDefinition(leftCore?.Definition))
                    weaponInstanceId = leftInst;
                else if (leftInst != 0 && fallbackInstanceId == 0)
                    fallbackInstanceId = leftInst;
            }
            if (weaponInstanceId == 0)
                weaponInstanceId = fallbackInstanceId;
            if (weaponInstanceId == 0)
                return false;

            var coreInst = AO.Core.Characters.CharacterEquipment.GetItemInstance(weaponInstanceId);
            var coreDef = coreInst?.Definition;
            int aoid = coreDef?.AOID ?? 0;

            // Always try deriving from core-definition metadata first; this works even if
            // raw AO data lookup is temporarily unavailable.
            const int equipSlotBitmapStatId = 298;
            const int rightHandBit = 64;
            const int leftHandBit = 256;
            int coreSlotBitmap = coreDef?.Modifiers
                .Where(m => m.StatId == equipSlotBitmapStatId)
                .Select(m => m.Value)
                .FirstOrDefault() ?? 0;
            bool coreCanRight = (coreSlotBitmap & rightHandBit) != 0;
            bool coreCanLeft = (coreSlotBitmap & leftHandBit) != 0;
            if (coreCanRight || coreCanLeft)
            {
                handMode = (coreCanRight && coreCanLeft)
                    ? WeaponHandMode.EitherOrBoth
                    : WeaponHandMode.SingleOnly;
            }
            else
            {
                handMode = (rightOccupied && leftOccupied) ? WeaponHandMode.EitherOrBoth : WeaponHandMode.SingleOnly;
            }
            attackSkillId = ResolvePrimaryAttackSkillIdFromCoreDefinition(coreDef);

            AO.Data.Unity.AODataManager.EnsureInstance();
            var dataManager = AO.Data.Unity.AODataManager.Instance;
            if (aoid <= 0 || dataManager == null)
                return true;

            var raw = dataManager.GetRawItemByAoid(aoid);
            if (raw == null)
                return true;

            int slotBitmap = raw.StatModifiers?.FirstOrDefault(m => m != null && m.StatId == equipSlotBitmapStatId)?.Value ?? 0;
            bool canRight = (slotBitmap & rightHandBit) != 0;
            bool canLeft = (slotBitmap & leftHandBit) != 0;
            if (canRight || canLeft)
            {
                handMode = (canRight && canLeft)
                    ? WeaponHandMode.EitherOrBoth
                    : WeaponHandMode.SingleOnly;
            }
            else
            {
                handMode = (rightOccupied && leftOccupied)
                    ? WeaponHandMode.EitherOrBoth
                    : WeaponHandMode.SingleOnly;
            }

            int rawSkill = ResolvePrimaryAttackSkillId(raw);
            if (rawSkill > 0)
                attackSkillId = rawSkill;
            return true;
        }

        private static int ResolvePrimaryAttackSkillIdFromCoreDefinition(AO.Core.Items.ItemDefinition def)
        {
            if (def?.Modifiers == null || def.Modifiers.Count == 0)
                return 0;

            int[] weaponSkillStatIds =
            {
                102, 103, 104, 105, 106, 107,
                109, 110, 111, 112, 114, 115, 116, 121, 133
            };

            var top = def.Modifiers
                .Where(m => m.Value > 0 && weaponSkillStatIds.Contains(m.StatId))
                .OrderByDescending(m => m.Value)
                .ThenBy(m => m.StatId)
                .FirstOrDefault();
            return top.StatId;
        }

        private static int ResolvePrimaryAttackSkillId(AO.Data.Core.Item raw)
        {
            if (raw?.AttackDefenseData?.Attack != null && raw.AttackDefenseData.Attack.Count > 0)
            {
                // Most weapon records expose attack skill percentages here; pick the strongest contribution.
                var top = raw.AttackDefenseData.Attack
                    .Where(a => a != null && a.Stat > 0)
                    .OrderByDescending(a => a.RawValue)
                    .ThenBy(a => a.Stat)
                    .FirstOrDefault();
                if ((top?.Stat ?? 0) > 0)
                    return top.Stat;
            }

            // Fallback: infer primary weapon skill from positive weapon-skill modifiers.
            if (raw?.StatModifiers != null && raw.StatModifiers.Count > 0)
            {
                int[] weaponSkillStatIds =
                {
                    102, 103, 104, 105, 106, 107, // 1h/2h melee + piercing + melee energy
                    109, 110, 111, 112, 114, 115, 116, 121, 133 // bow/heavy/AR/pistol/smg/shotgun/rifle/grenade/RE
                };

                var topMod = raw.StatModifiers
                    .Where(m => m != null && m.Value > 0 && weaponSkillStatIds.Contains(m.StatId))
                    .OrderByDescending(m => m.Value)
                    .ThenBy(m => m.StatId)
                    .FirstOrDefault();
                if ((topMod?.StatId ?? 0) > 0)
                    return topMod.StatId;
            }

            return 0;
        }

        private static bool IsWeaponDefinition(AO.Core.Items.ItemDefinition def)
        {
            if (def == null)
                return false;
            if (def.DBType == 1)
                return true;

            const int itemClassStatId = 76;
            int classFromMod = def.Modifiers
                .Where(m => m.StatId == itemClassStatId)
                .Select(m => m.Value)
                .FirstOrDefault();
            return classFromMod == 1;
        }

        private bool TryBuildRangedFallbackAttackRule(int attackSkillId, WeaponHandMode handMode, out AttackAnimationRule fallbackRule)
        {
            fallbackRule = null;
            if (!IsRangedAttackSkill(attackSkillId))
                return false;

            bool useSmallArms = ShouldUseSmallArmsFallback(attackSkillId, handMode);
            if (useSmallArms)
            {
                fallbackRule = new AttackAnimationRule
                {
                    id = "fallback_ranged_smallarms",
                    enabled = true,
                    handMode = "either_or_both",
                    actionKey = "1h ranged Start Attack",
                    startActionKey = "1h ranged Start Attack",
                    idleActionKey = "1h ranged idle",
                    stopActionKey = "1h ranged Stop Attack",
                    hitActionKeys = new List<string> { "1h ranged shot Rhand", "1h ranged shot Lhand" },
                    specialActionKeys = new List<string> { "1h ranged Burst Rhand", "1h ranged Burst Lhand", "1h ranged Full Auto Special Rhand", "1h ranged Full Auto Special Lhand" },
                    specialEveryHits = 0,
                    hitIntervalSeconds = 0.75f,
                    clipCandidates = new List<string> { "male_idle-smallarms_01_01", "idle-smallarms_01_01", "_idle-smallarms_01_01", "male_smallarms-start_01_01", "smallarms-start_01_01", "_smallarms-start_01_01" }
                };
                fallbackRule.clipCandidatesBySex = new Dictionary<string, List<string>>
                {
                    ["default"] = new List<string>(fallbackRule.clipCandidates)
                };
                return true;
            }

            fallbackRule = new AttackAnimationRule
            {
                id = "fallback_ranged_twohand",
                enabled = true,
                handMode = "single_only",
                actionKey = "idle 2h Ranged",
                startActionKey = "Rifle Start",
                idleActionKey = "idle 2h Ranged",
                stopActionKey = "RIfle Stop",
                hitActionKeys = new List<string> { "Rifle Shoot" },
                specialActionKeys = new List<string> { "2h ranged Burst Special", "2h ranged Full Auto Special" },
                specialEveryHits = 4,
                hitIntervalSeconds = 0.85f,
                clipCandidates = new List<string> { "male_idle-2h_01_01", "idle-2h_01_01", "_idle-2h_01_01" }
            };
            fallbackRule.clipCandidatesBySex = new Dictionary<string, List<string>>
            {
                ["default"] = new List<string>(fallbackRule.clipCandidates)
            };
            return true;
        }

        private static bool ShouldUseSmallArmsFallback(int attackSkillId, WeaponHandMode handMode)
        {
            // Dual-capable shotgun/smg/re are treated like pistol/smallarms only when dual-wield capable.
            if (attackSkillId is 112)
                return true;
            if (handMode == WeaponHandMode.EitherOrBoth && (attackSkillId is 114 or 115 or 133))
                return true;
            return false;
        }

        private bool IsLikelyRangedWeaponEquipped()
        {
            var equipped = bridge?.Character?.Equipment?.GetAllEquipped();
            if (equipped == null || AO.Core.Characters.CharacterEquipment.GetItemInstance == null)
                return false;

            bool IsLikelyRangedName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return false;
                string s = name.ToLowerInvariant();
                return s.Contains("rifle")
                    || s.Contains("sniper")
                    || s.Contains("grenade")
                    || s.Contains("shotgun")
                    || s.Contains("pistol")
                    || s.Contains("smg")
                    || s.Contains("bow");
            }

            if (equipped.TryGetValue(rightHandEquipSlotId, out var rightInst) && rightInst != 0)
            {
                var right = AO.Core.Characters.CharacterEquipment.GetItemInstance(rightInst)?.Definition;
                if (IsLikelyRangedName(right?.Name))
                    return true;
            }
            if (equipped.TryGetValue(leftHandEquipSlotId, out var leftInst) && leftInst != 0)
            {
                var left = AO.Core.Characters.CharacterEquipment.GetItemInstance(leftInst)?.Definition;
                if (IsLikelyRangedName(left?.Name))
                    return true;
            }

            return false;
        }

        private string BuildAttackAnimationSignature()
        {
            var sb = new StringBuilder(64);
            sb.Append(ResolveAttackSexToken()).Append('|');
            var equipped = bridge?.Character?.Equipment?.GetAllEquipped();
            if (equipped != null)
            {
                if (equipped.TryGetValue(rightHandEquipSlotId, out var rightInst))
                    sb.Append("R=").Append(rightInst);
                sb.Append(';');
                if (equipped.TryGetValue(leftHandEquipSlotId, out var leftInst))
                    sb.Append("L=").Append(leftInst);
            }
            return sb.ToString();
        }

        private string ResolveDesiredMoveAction(bool forwardPressed, bool backwardPressed, bool strafeLeftPressed, bool strafeRightPressed)
        {
            if (backwardPressed)
                return ActionRunBackwards;
            if (strafeLeftPressed)
                return ActionStrafeRight;
            if (strafeRightPressed)
                return ActionStrafeLeft;
            if (_walkModeEnabled && forwardPressed)
                return ActionWalk;
            return ActionRun;
        }

        private string ResolveDirectLocomotionLogicalName(
            bool forwardPressed,
            bool backwardPressed,
            bool strafeLeftPressed,
            bool strafeRightPressed)
        {
            // Match AO locomotion priority. Strafe changes the movement
            // vector, but forward/backward owns the animation when axes are combined.
            if (forwardPressed)
                return _walkModeEnabled ? "walk" : "run";
            if (backwardPressed)
                return _walkModeEnabled ? "walk-back" : "run-back";
            // The rendered CAT root is mirrored on X, so swap visual strafe poses.
            if (strafeLeftPressed)
                return "walk-right";
            if (strafeRightPressed)
                return "walk-left";
            return "idle";
        }

        private void HandleWalkToggleInput()
        {
            if (WasBackspacePressedThisFrame())
            {
                _walkModeEnabled = !_walkModeEnabled;
                _activeMoveAction = _walkModeEnabled ? ActionWalk : ActionRun;
                ApplyAnimationOverridesNow();
            }
        }

        private void StartSitTransition(string actionKey, bool standingUp)
        {
            CatAnimPlayer directAnim = _spawnedPrefabVisual != null
                ? _spawnedPrefabVisual.GetComponentInChildren<CatAnimPlayer>(true)
                : null;
            if (directAnim != null)
            {
                _sitTransitionPlaying = true;
                _standingUpTransition = standingUp;
                string logical = standingUp ? "sit-stop" : "sit-start";
                bool started = directAnim.PlayOnce(logical, CatAnimPlayer.DefaultBlendSeconds, () =>
                {
                    _sitTransitionPlaying = false;
                    _standingUpTransition = false;
                    directAnim.Play(standingUp ? "idle" : "idle-sit",
                        CatAnimPlayer.DefaultBlendSeconds);
                });
                if (!started)
                {
                    _sitTransitionPlaying = false;
                    _standingUpTransition = false;
                    directAnim.Play(standingUp ? "idle" : "idle-sit",
                        CatAnimPlayer.DefaultBlendSeconds);
                }
                return;
            }

            string clipName = ResolveActionClipNameFromMap(availableAnimationClipNames, actionKey, string.Empty);
            if (string.IsNullOrWhiteSpace(clipName))
            {
                _sitTransitionPlaying = false;
                _standingUpTransition = standingUp;
                if (standingUp)
                {
                    _runtimeAttackClipOverride = string.Empty;
                    attackToggleActive = false;
                    ApplyAnimationOverridesNow();
                }
                else
                {
                    SetRuntimeAttackClipToAction(ActionIdleSit);
                    attackToggleActive = true;
                }
                return;
            }

            _sitTransitionPlaying = true;
            _standingUpTransition = standingUp;
            _sitTransitionEndTime = Time.time + 0.6f;

            if (_legacyAnimationReady && _legacyAnimation != null && _legacyAnimation.GetClip(clipName) != null)
            {
                _legacyAnimation.CrossFade(clipName, 0.08f);
                var clip = _legacyAnimation.GetClip(clipName);
                ResolveActionLoopWindow(actionKey, clip.length, out var segStart, out var segEnd);
                var state = _legacyAnimation[clipName];
                if (state != null)
                    state.time = segStart;
                _sitTransitionEndTime = Time.time + Mathf.Max(0.08f, segEnd - segStart);
                return;
            }

            if (_animationGraphReady)
            {
                ResolveActionLoopWindow(actionKey, GetClipLengthByName(clipName), out var runtimeStart, out _);
                _runtimeAttackClipOverride = clipName;
                _runtimeAttackActionKey = actionKey;
                _runtimeAttackLoopEnabled = false;
                attackToggleActive = true;
                _runtimeAttackStartTime = runtimeStart;
                _sitTransitionEndTime = Time.time + Mathf.Max(0.08f, ResolveActionSegmentDuration(actionKey, GetClipLengthByName(clipName)));
                ApplyAnimationOverridesNow();
            }
        }

        private void SetRuntimeAttackClipToAction(string actionKey, string preferredClipName = null)
        {
            string clipName = string.Empty;
            if (!string.IsNullOrWhiteSpace(preferredClipName))
                clipName = FindBestNameMatch(availableAnimationClipNames, preferredClipName);

            if (string.IsNullOrWhiteSpace(clipName))
                clipName = ResolveActionClipNameFromMap(availableAnimationClipNames, actionKey, string.Empty);
            if (string.IsNullOrWhiteSpace(clipName))
                return;
            _runtimeAttackClipOverride = clipName;
            _runtimeAttackActionKey = actionKey;
            _runtimeAttackLoopEnabled = true;
            _freezeAttackLoopPose =
                _attackCycleRunning
                && !string.IsNullOrWhiteSpace(_activeAttackIdleActionKey)
                && string.Equals(actionKey, _activeAttackIdleActionKey, StringComparison.OrdinalIgnoreCase);
            ResolveActionLoopWindow(actionKey, GetClipLengthByName(clipName), out _runtimeAttackStartTime, out _);
            ApplyAnimationOverridesNow();
        }

        private void HandleSitToggleInput()
        {
            if (!WasXPressedThisFrame())
                return;

            if (_sitTransitionPlaying)
            {
                _sitTransitionPlaying = false;
                _standingUpTransition = false;
            }

            if (!_sitToggled)
            {
                _sitToggled = true;
                StartSitTransition(ActionSitDown, standingUp: false);
            }
            else
            {
                StartSitTransition(ActionStandUp, standingUp: true);
                _sitToggled = false;
            }
        }

        public bool IsSitting => _sitToggled || _sitTransitionPlaying;
        public bool IsWalkModeEnabled => _walkModeEnabled;

        private static bool IsUiTextInputBlockingGameplay()
        {
            return UiInputUtility.IsTextInputFocused();
        }

        private bool WasQPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                return !IsUiTextInputBlockingGameplay() && !shift && kb.qKey.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            return !IsUiTextInputBlockingGameplay() && !shiftHeld && Input.GetKeyDown(KeyCode.Q);
#else
            return false;
#endif
        }

        private bool IsForwardPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
                return !IsUiTextInputBlockingGameplay() && kb.wKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return !IsUiTextInputBlockingGameplay() && Input.GetKey(KeyCode.W);
#else
            return false;
#endif
        }

        private bool IsBackwardPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
                return !IsUiTextInputBlockingGameplay() && kb.sKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return !IsUiTextInputBlockingGameplay() && Input.GetKey(KeyCode.S);
#else
            return false;
#endif
        }

        private bool IsStrafeLeftPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
                return !IsUiTextInputBlockingGameplay() && kb.zKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return !IsUiTextInputBlockingGameplay() && Input.GetKey(KeyCode.Z);
#else
            return false;
#endif
        }

        private bool IsStrafeRightPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
                return !IsUiTextInputBlockingGameplay() && kb.cKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return !IsUiTextInputBlockingGameplay() && Input.GetKey(KeyCode.C);
#else
            return false;
#endif
        }

        private bool WasBackspacePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
                return !IsUiTextInputBlockingGameplay() && kb.backspaceKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return !IsUiTextInputBlockingGameplay() && Input.GetKeyDown(KeyCode.Backspace);
#else
            return false;
#endif
        }

        private bool WasXPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
                return !IsUiTextInputBlockingGameplay() && kb.xKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return !IsUiTextInputBlockingGameplay() && Input.GetKeyDown(KeyCode.X);
#else
            return false;
#endif
        }

        private bool WasSpacePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
                return !IsUiTextInputBlockingGameplay() && kb.spaceKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return !IsUiTextInputBlockingGameplay() && Input.GetKeyDown(KeyCode.Space);
#else
            return false;
#endif
        }

        private float CalculateCurrentSpeed()
        {
            float speed = 0f;
            Vector3 current = transform.position;
            if (_hasLastRootPosition && Time.deltaTime > 0.0001f)
            {
                Vector3 delta = current - _lastRootPosition;
                delta.y = 0f;
                speed = delta.magnitude / Time.deltaTime;
            }

            _lastRootPosition = current;
            _hasLastRootPosition = true;
            return speed;
        }

        private bool UpdateJumpTransitions(bool forwardPressed, bool jumpPressedThisFrame)
        {
            if (_characterController == null)
                _characterController = GetComponent<CharacterController>();
            if (_characterController == null)
            {
                // Fallback path for scenes where CharacterController is absent or not ready.
                if (jumpPressedThisFrame && !_sitToggled && !_sitTransitionPlaying)
                {
                    bool forwardIntent = forwardPressed;
                    string jumpAction = forwardIntent ? ActionJumpForward : ActionJumpFromIdle;
                    float duration = PlayActionAsOneShot(jumpAction);
                    _jumpInAir = true;
                    _jumpLandingTransitionPlaying = true;
                    _jumpTransitionEndTime = Time.time + Mathf.Max(0.08f, duration);
                }

                if (_jumpLandingTransitionPlaying && Time.time >= _jumpTransitionEndTime)
                {
                    _jumpInAir = false;
                    _jumpLandingTransitionPlaying = false;
                }

                return _jumpInAir || _jumpLandingTransitionPlaying;
            }

            bool grounded = _characterController.isGrounded;
            bool justTookOff = _wasGroundedLastFrame && !grounded;
            bool justLanded = !_wasGroundedLastFrame && grounded;

            // Fire jump one-shot immediately on jump input while grounded.
            // This avoids missed visuals when CharacterController airborne edge
            // transitions are delayed or skipped for a frame.
            if (grounded && !_jumpInAir && !_jumpLandingTransitionPlaying && jumpPressedThisFrame)
            {
                bool forwardIntent = forwardPressed;
                _pendingJumpActionKey = forwardIntent ? ActionJumpForward : ActionJumpFromIdle;
                _jumpStartedWithForwardIntent = forwardIntent;
                _jumpInAir = false;
                _jumpLandingTransitionPlaying = false;
                // Preserve the direction present at the jump key edge. The timestamp
                // below distinguishes a real jump from slope micro-airtime; this flag
                // only chooses jump-forward versus jump-stand.
                _jumpForwardIntentLatched = forwardIntent;
                _restoreAttackPoseAfterJump = attackToggleActive && !_sitToggled && !_sitTransitionPlaying;
            }

            if (justTookOff)
            {
                bool inputDrivenTakeoff = _jumpForwardIntentLatched || ((Time.time - _lastJumpKeyPressTime) <= 0.25f);
                // Slope/hill micro takeoffs should not trigger jump/land animation flow.
                if (!inputDrivenTakeoff)
                {
                    _jumpInAir = false;
                    _jumpLandingTransitionPlaying = false;
                    _restoreAttackPoseAfterJump = false;
                    _jumpPlayedForwardAction = false;
                    _pendingJumpActionKey = string.Empty;
                }
                else
                {
                    _jumpInAir = true;
                    _jumpLandingTransitionPlaying = false;
                    _restoreAttackPoseAfterJump = attackToggleActive && !_sitToggled && !_sitTransitionPlaying;
                    bool movingAtTakeoff = _jumpForwardIntentLatched || forwardPressed;
                    string jumpAction = string.IsNullOrWhiteSpace(_pendingJumpActionKey)
                        ? (movingAtTakeoff ? ActionJumpForward : ActionJumpFromIdle)
                        : _pendingJumpActionKey;
                    if (movingAtTakeoff && string.Equals(jumpAction, ActionJumpFromIdle, StringComparison.OrdinalIgnoreCase))
                        jumpAction = ActionJumpForward;
                    PlayActionAsOneShot(jumpAction);
                    _jumpPlayedForwardAction = string.Equals(jumpAction, ActionJumpForward, StringComparison.OrdinalIgnoreCase);
                }
                _jumpForwardIntentLatched = false;
                _pendingJumpActionKey = string.Empty;
            }
            else if (justLanded && _jumpInAir)
            {
                _jumpInAir = false;
                _jumpLandingTransitionPlaying = true;
                // Landing is selected from current touchdown intent,
                // not the direction that was held at takeoff.
                bool forwardAtLanding = _localLocomotionForward > 0.001f;
                string landAction = forwardAtLanding
                    ? ActionLandAfterJumpRun
                    : ActionLandAfterJump;
                float duration = PlayActionAsOneShot(landAction);
                _jumpTransitionEndTime = Time.time + Mathf.Max(0.08f, duration);
                _jumpStartedWithForwardIntent = false;
                _jumpPlayedForwardAction = false;
            }

            if (_jumpLandingTransitionPlaying && Time.time >= _jumpTransitionEndTime)
            {
                _jumpLandingTransitionPlaying = false;
                if (_restoreAttackPoseAfterJump && !_sitToggled && !_sitTransitionPlaying)
                {
                    attackToggleActive = true;
                    ApplyAttackAnimationRule(force: true);
                }
                else if (!_sitToggled && !_sitTransitionPlaying && !attackToggleActive)
                {
                    _runtimeAttackClipOverride = string.Empty;
                    _runtimeAttackActionKey = ActionIdleMa;
                    _runtimeAttackLoopEnabled = true;
                    ApplyAnimationOverridesNow();
                }
                _restoreAttackPoseAfterJump = false;
            }
            if (grounded && !_jumpInAir && !_jumpLandingTransitionPlaying && !WasSpacePressedThisFrame())
                _jumpForwardIntentLatched = false;
            if (grounded && !_jumpInAir && !_jumpLandingTransitionPlaying)
            {
                _jumpStartedWithForwardIntent = false;
                _jumpPlayedForwardAction = false;
            }

            _wasGroundedLastFrame = grounded;
            return _jumpInAir || _jumpLandingTransitionPlaying;
        }

        private float PlayActionAsOneShot(string actionKey)
        {
            CatAnimPlayer directAnim = _spawnedPrefabVisual != null
                ? _spawnedPrefabVisual.GetComponentInChildren<CatAnimPlayer>(true)
                : null;
            if (directAnim != null)
            {
                string logical = actionKey switch
                {
                    ActionJumpForward => "jump-forward",
                    ActionJumpFromIdle => "jump-stand",
                    ActionLandAfterJumpRun => "jump-land-run",
                    ActionLandAfterJump => "jump-land-idle",
                    _ => actionKey
                };
                bool isLanding = string.Equals(actionKey, ActionLandAfterJump,
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(actionKey, ActionLandAfterJumpRun,
                        StringComparison.OrdinalIgnoreCase);
                if (isLanding)
                {
                    directAnim.Play(ResolveDirectLocomotionLogicalName(
                        _localLocomotionForward > 0.001f,
                        _localLocomotionForward < -0.001f,
                        _localLocomotionStrafe < -0.001f,
                        _localLocomotionStrafe > 0.001f));
                    if (directAnim.PlayOverlayOnce(
                            logical, CatAnimPlayer.DefaultBlendSeconds, null))
                        return Mathf.Max(0.08f, directAnim.OverlayDuration);
                }
                else if (directAnim.PlayOnce(logical, CatAnimPlayer.DefaultBlendSeconds, null))
                {
                    return Mathf.Max(0.08f, directAnim.Duration);
                }
            }

            string clipName = ResolveActionClipNameFromMap(availableAnimationClipNames, actionKey, string.Empty);
            bool isJumpAction = string.Equals(actionKey, ActionJumpForward, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionKey, ActionJumpFromIdle, StringComparison.OrdinalIgnoreCase);
            bool isLandAction = string.Equals(actionKey, ActionLandAfterJump, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionKey, ActionLandAfterJumpRun, StringComparison.OrdinalIgnoreCase);
            if (isJumpAction)
            {
                // Jump is sensitive to weak action-map matches; prefer explicit known-good names.
                string forcedJump = ResolvePreferredJumpClipName(actionKey);
                if (!string.IsNullOrWhiteSpace(forcedJump))
                    clipName = forcedJump;
            }
            else if (isLandAction)
            {
                string forcedLand = ResolvePreferredLandClipName(actionKey);
                if (!string.IsNullOrWhiteSpace(forcedLand))
                    clipName = forcedLand;
            }
            if (string.IsNullOrWhiteSpace(clipName))
                clipName = ResolveJumpLandFallbackClipName(actionKey);
            if (string.IsNullOrWhiteSpace(clipName))
                return 0.2f;

            float clipLength = GetClipLengthByName(clipName);
            ResolveActionLoopWindow(actionKey, clipLength, out var segStart, out var segEnd);
            float duration = Mathf.Max(0.08f, segEnd - segStart);

            if (_legacyAnimationReady && _legacyAnimation != null && _legacyAnimation.GetClip(clipName) != null)
            {
                _legacyAnimation.CrossFade(clipName, 0.08f);
                var state = _legacyAnimation[clipName];
                if (state != null)
                {
                    // One-shot actions must never loop while waiting for the next cycle.
                    state.wrapMode = WrapMode.Once;
                    state.speed = 1f;
                    state.time = segStart;
                }
                return duration;
            }

            if (_animationGraphReady)
            {
                _runtimeAttackClipOverride = clipName;
                _runtimeAttackActionKey = actionKey;
                _runtimeAttackLoopEnabled = false;
                _freezeAttackLoopPose = false;
                _runtimeAttackStartTime = segStart;
                _attackOneShotStartWallTime = Time.time;
                _attackOneShotDuration = duration;
                ApplyAnimationOverridesNow();
            }

            return duration;
        }

        private string ResolvePreferredJumpClipName(string actionKey)
        {
            var names = CollectAvailableClipNames();
            if (names.Count == 0)
                return string.Empty;

            static bool Has(string value, string token) =>
                value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

            // Explicit known-good mappings from AO naming.
            string[] preferredExact = string.Equals(actionKey, ActionJumpForward, StringComparison.OrdinalIgnoreCase)
                ? new[] { "male_jump-forward_01_01", "female_jump-forward_01_01", "athrox_jump-forward_01_01" }
                : new[] { "male_jump-stand_01_01", "female_jump-stand_01_01", "athrox_jump-stand_01_01" };
            for (int i = 0; i < preferredExact.Length; i++)
            {
                string direct = FindBestNameMatch(names, preferredExact[i]);
                if (!string.IsNullOrWhiteSpace(direct))
                    return direct;
            }

            string best = names.FirstOrDefault(n =>
                Has(n, "jump")
                && !Has(n, "land")
                && !Has(n, "idle")
                && !Has(n, "noanim")
                && Has(n, "forward"));
            if (!string.IsNullOrWhiteSpace(best))
                return best;

            bool isForwardAction = string.Equals(actionKey, ActionJumpForward, StringComparison.OrdinalIgnoreCase);
            if (!isForwardAction)
            {
                best = names.FirstOrDefault(n =>
                    Has(n, "jump")
                    && !Has(n, "land")
                    && !Has(n, "idle")
                    && !Has(n, "noanim"));
                if (!string.IsNullOrWhiteSpace(best))
                    return best;
            }

            return string.Empty;
        }

        private string ResolvePreferredLandClipName(string actionKey)
        {
            var names = CollectAvailableClipNames();
            if (names.Count == 0)
                return string.Empty;

            bool runLand = string.Equals(actionKey, ActionLandAfterJumpRun, StringComparison.OrdinalIgnoreCase);
            string[] preferredExact = runLand
                ? new[] { "male_jump-land-run_01_01", "female_jump-land-run_01_01", "athrox_jump-land-run_01_01" }
                : new[] { "male_jump-land-idle_01_01", "female_jump-land-idle_01_01", "athrox_jump-land-idle_01_01" };
            for (int i = 0; i < preferredExact.Length; i++)
            {
                string direct = FindBestNameMatch(names, preferredExact[i]);
                if (!string.IsNullOrWhiteSpace(direct))
                    return direct;
            }

            static bool Has(string value, string token) =>
                value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

            if (runLand)
            {
                string landRun = names.FirstOrDefault(n => Has(n, "jump-land-run"));
                if (!string.IsNullOrWhiteSpace(landRun))
                    return landRun;
            }
            else
            {
                string landIdle = names.FirstOrDefault(n => Has(n, "jump-land-idle"));
                if (!string.IsNullOrWhiteSpace(landIdle))
                    return landIdle;
            }

            string anyLand = names.FirstOrDefault(n => Has(n, "land"));
            return anyLand ?? string.Empty;
        }

        private List<string> CollectAvailableClipNames()
        {
            var names = new List<string>();
            if (availableAnimationClipNames != null)
                names.AddRange(availableAnimationClipNames.Where(n => !string.IsNullOrWhiteSpace(n)));
            if (_legacyAnimation != null)
            {
                foreach (AnimationState state in _legacyAnimation)
                {
                    if (state?.clip != null && !string.IsNullOrWhiteSpace(state.clip.name))
                        names.Add(state.clip.name);
                }
            }
            if (_spawnedAnimator?.runtimeAnimatorController != null)
            {
                var clips = _spawnedAnimator.runtimeAnimatorController.animationClips;
                for (int i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    if (clip != null && !string.IsNullOrWhiteSpace(clip.name))
                        names.Add(clip.name);
                }
            }

            return names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string ResolveJumpLandFallbackClipName(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey))
                return string.Empty;

            bool isJumpAction = string.Equals(actionKey, ActionJumpForward, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionKey, ActionJumpFromIdle, StringComparison.OrdinalIgnoreCase);
            bool isLandAction = string.Equals(actionKey, ActionLandAfterJump, StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionKey, ActionLandAfterJumpRun, StringComparison.OrdinalIgnoreCase);
            if (!isJumpAction && !isLandAction)
                return string.Empty;

            var names = new List<string>();
            if (availableAnimationClipNames != null)
                names.AddRange(availableAnimationClipNames.Where(n => !string.IsNullOrWhiteSpace(n)));
            if (_legacyAnimation != null)
            {
                foreach (AnimationState state in _legacyAnimation)
                {
                    if (state?.clip != null && !string.IsNullOrWhiteSpace(state.clip.name))
                        names.Add(state.clip.name);
                }
            }
            if (_spawnedAnimator?.runtimeAnimatorController != null)
            {
                var clips = _spawnedAnimator.runtimeAnimatorController.animationClips;
                for (int i = 0; i < clips.Length; i++)
                {
                    var clip = clips[i];
                    if (clip != null && !string.IsNullOrWhiteSpace(clip.name))
                        names.Add(clip.name);
                }
            }
            names = names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count == 0)
                return string.Empty;

            if (isJumpAction)
            {
                string forwardJump = names.FirstOrDefault(n =>
                    n.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0
                    && n.IndexOf("land", StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("forward", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrWhiteSpace(forwardJump))
                    return forwardJump;

                string anyJump = names.FirstOrDefault(n =>
                    n.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0
                    && n.IndexOf("land", StringComparison.OrdinalIgnoreCase) < 0);
                if (!string.IsNullOrWhiteSpace(anyJump))
                    return anyJump;
            }

            if (isLandAction)
            {
                string runLand = names.FirstOrDefault(n =>
                    n.IndexOf("jump", StringComparison.OrdinalIgnoreCase) >= 0
                    && n.IndexOf("land", StringComparison.OrdinalIgnoreCase) >= 0
                    && n.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrWhiteSpace(runLand))
                    return runLand;

                string anyLand = names.FirstOrDefault(n =>
                    n.IndexOf("land", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrWhiteSpace(anyLand))
                    return anyLand;
            }

            return string.Empty;
        }

        public bool TryPlayOneShotAction(string actionKey, out float duration)
        {
            duration = 0f;
            if (string.IsNullOrWhiteSpace(actionKey))
                return false;

            string clipName = ResolveActionClipNameFromMap(availableAnimationClipNames, actionKey, string.Empty);
            if (string.IsNullOrWhiteSpace(clipName))
                return false;

            duration = PlayActionAsOneShot(actionKey);
            return duration > 0f;
        }

        public bool TryPlayRecentAttackHitAction(out float duration)
        {
            duration = 0f;

            // Prefer replaying the exact last hit action currently used by the attack cycle.
            if (!string.IsNullOrWhiteSpace(_attackLastActionKey) && TryPlayOneShotAction(_attackLastActionKey, out duration))
                return true;

            // Fallback: resolve from active attack rule without mutating cycle cursors.
            if (_activeAttackRule?.hitActionKeys != null && _activeAttackRule.hitActionKeys.Count > 0)
            {
                int cursor = _attackRuleHitCursor;
                string actionKey = ResolveActionFromList(_activeAttackRule.hitActionKeys, ref cursor);
                if (!string.IsNullOrWhiteSpace(actionKey) && TryPlayOneShotAction(actionKey, out duration))
                    return true;
            }

            return false;
        }

        public int AttackImpactCounter => _attackRuleHitCounter;

        public bool TryPlayDeathOneShot(IReadOnlyList<string> actionKeys, out float duration)
        {
            duration = 0f;
            if (actionKeys == null || actionKeys.Count == 0)
                return false;

            // Death should not be overridden by attack cycle updates.
            _externalAttackCyclePaused = true;
            attackToggleActive = false;
            _runtimeAttackLoopEnabled = false;
            _freezeAttackLoopPose = false;
            EndAttackCombatSequence();
            _runtimeAttackClipOverride = string.Empty;
            _runtimeAttackActionKey = string.Empty;
            // Engage a short pre-lock before attempting playback so same-frame movement/idle
            // logic cannot clobber the death one-shot as it starts.
            _externalDeathPlaybackLocked = true;
            _externalDeathPlaybackLockUntil = Time.time + 4f;
            ForceInterruptCurrentAnimationPlayback();

            for (int i = 0; i < actionKeys.Count; i++)
            {
                string candidate = actionKeys[i];
                if (TryPlayOneShotClipByName(candidate, out duration) && duration > 0f)
                {
                    LockDeathPlayback(duration);
                    return true;
                }
            }

            // Final fallback: choose any death-like clip name available on this model.
            string fallbackDeathClip = FindAnyDeathLikeClipName();
            if (!string.IsNullOrWhiteSpace(fallbackDeathClip)
                && TryPlayOneShotClipByName(fallbackDeathClip, out duration)
                && duration > 0f)
            {
                LockDeathPlayback(duration);
                return true;
            }

            // Last-resort fallback: some rigs resolve death better via action key mapping.
            string[] fallbackDeathActions =
            {
                "DieDefault",
                "die",
                "death",
                "die-pain",
                "default_monster_die-pain_01_01"
            };
            for (int i = 0; i < fallbackDeathActions.Length; i++)
            {
                if (TryPlayOneShotAction(fallbackDeathActions[i], out duration) && duration > 0f)
                {
                    LockDeathPlayback(duration);
                    return true;
                }
            }

            // No death clip actually started; release lock so normal animation can continue.
            var available = CollectAvailableClipNames();
            string availableJoined = available != null && available.Count > 0
                ? string.Join(", ", available)
                : "<none>";
            Debug.LogWarning($"[DeathAnim] No death clip started on '{name}'. candidates={actionKeys.Count}. available=[{availableJoined}]");
            _externalDeathPlaybackLocked = false;
            _externalDeathPlaybackLockUntil = 0f;
            return false;
        }

        private void ForceInterruptCurrentAnimationPlayback()
        {
            if (_legacyAnimation != null)
                _legacyAnimation.Stop();
            if (_spawnedAnimator != null)
                _spawnedAnimator.speed = 1f;

            if (_animationGraphReady)
            {
                _moveBlendWeight = 0f;
                _attackBlendWeight = 1f;
                if (_animationMixer.IsValid())
                {
                    _animationMixer.SetInputWeight(0, 0f);
                    _animationMixer.SetInputWeight(1, 0f);
                    _animationMixer.SetInputWeight(2, 1f);
                }
            }
        }

        private void LockDeathPlayback(float clipDuration)
        {
            _externalDeathPlaybackLocked = true;
            _externalDeathPlaybackLockUntil = Time.time + Mathf.Max(0.5f, clipDuration);
        }

        private string FindAnyDeathLikeClipName()
        {
            var clipNames = CollectAvailableClipNames();
            if (clipNames == null || clipNames.Count == 0)
                return string.Empty;

            static bool HasToken(string value, string token) =>
                !string.IsNullOrWhiteSpace(value) && value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

            for (int i = 0; i < clipNames.Count; i++)
            {
                string n = clipNames[i];
                if (string.IsNullOrWhiteSpace(n))
                    continue;

                if (HasToken(n, "die")
                    || HasToken(n, "death")
                    || HasToken(n, "dead")
                    || HasToken(n, "collapse")
                    || HasToken(n, "fall"))
                {
                    return n;
                }
            }

            return string.Empty;
        }

        public bool TryPlayOneShotClipByName(string requestedClipName, out float duration)
        {
            duration = 0f;
            if (string.IsNullOrWhiteSpace(requestedClipName))
                return false;

            if (_spawnedPrefabVisual != null)
            {
                CatAnimPlayer directAnim = _spawnedPrefabVisual
                    .GetComponentInChildren<CatAnimPlayer>(true);
                if (directAnim != null
                    && directAnim.PlayOnce(
                        requestedClipName, CatAnimPlayer.DefaultBlendSeconds, null))
                {
                    duration = Mathf.Max(0.08f, directAnim.Duration);
                    return true;
                }
            }

            // Resolve against a freshly-collected clip list to avoid stale mapping races.
            var currentClipNames = CollectAvailableClipNames();
            string clipName = FindBestNameMatch(currentClipNames, requestedClipName);
            if (string.IsNullOrWhiteSpace(clipName))
                return false;

            float clipLength = Mathf.Max(0.08f, GetClipLengthByName(clipName));

            // Prefer the active playable graph path first; this is the renderer-driving path for
            // runtime GLB characters and avoids driving non-visible legacy branches.
            if (_animationGraphReady)
            {
                _runtimeAttackClipOverride = clipName;
                _runtimeAttackActionKey = string.Empty;
                _runtimeAttackLoopEnabled = false;
                _freezeAttackLoopPose = false;
                _runtimeAttackStartTime = 0f;
                _attackOneShotStartWallTime = Time.time;
                _attackOneShotDuration = clipLength;
                ApplyAnimationOverridesNow();

                duration = clipLength;
                return true;
            }

            if (_legacyAnimationReady && _legacyAnimation != null && _legacyAnimation.GetClip(clipName) != null)
            {
                _legacyAnimation.Stop();
                _legacyAnimation.Play(clipName);
                var state = _legacyAnimation[clipName];
                if (state != null)
                {
                    state.wrapMode = WrapMode.Once;
                    state.speed = 1f;
                    state.time = 0f;
                    state.enabled = true;
                    state.weight = 1f;
                }

                duration = clipLength;
                return true;
            }

            // Death fallback for animator-driven rigs: force legacy one-shot playback of the
            // resolved clip on the animator host so it is not subject to graph state blending.
            if (_spawnedAnimator != null)
            {
                var legacyHost = _spawnedAnimator.gameObject != null ? _spawnedAnimator.gameObject : gameObject;
                if (legacyHost != null)
                {
                    var legacy = legacyHost.GetComponent<Animation>();
                    if (legacy == null)
                        legacy = legacyHost.AddComponent<Animation>();

                    AnimationClip resolvedClip = null;
                    if (_spawnedAnimator.runtimeAnimatorController != null)
                    {
                        var clips = _spawnedAnimator.runtimeAnimatorController.animationClips;
                        if (clips != null)
                        {
                            for (int i = 0; i < clips.Length; i++)
                            {
                                var c = clips[i];
                                if (c == null || string.IsNullOrWhiteSpace(c.name))
                                    continue;
                                if (string.Equals(c.name, clipName, StringComparison.OrdinalIgnoreCase))
                                {
                                    resolvedClip = c;
                                    break;
                                }
                            }
                        }
                    }

                    if (resolvedClip != null)
                    {
                        resolvedClip.legacy = true;
                        if (legacy.GetClip(clipName) == null)
                            legacy.AddClip(resolvedClip, clipName);
                        legacy.wrapMode = WrapMode.Once;
                        var st = legacy[clipName];
                        if (st != null)
                        {
                            st.wrapMode = WrapMode.Once;
                            st.speed = 1f;
                            st.time = 0f;
                            st.enabled = true;
                            st.weight = 1f;
                        }

                        _spawnedAnimator.enabled = false;
                        legacy.Stop();
                        legacy.Play(clipName);
                        duration = clipLength;
                        return true;
                    }
                }
            }

            return false;
        }

        public bool TryPlayOneShotOverlayByName(string requestedClipName, out float duration)
        {
            duration = 0f;
            if (string.IsNullOrWhiteSpace(requestedClipName) || _spawnedPrefabVisual == null)
                return false;

            CatAnimPlayer directAnim = _spawnedPrefabVisual
                .GetComponentInChildren<CatAnimPlayer>(true);
            if (directAnim == null
                || !directAnim.PlayOverlayOnce(
                    requestedClipName, CatAnimPlayer.DefaultBlendSeconds, null))
                return false;

            duration = Mathf.Max(0.08f, directAnim.OverlayDuration);
            return true;
        }

        public void SetExternalAttackCyclePaused(bool paused)
        {
            _externalAttackCyclePaused = paused;
        }

        private float GetClipLengthByName(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
                return 0f;

            if (_legacyAnimation != null && _legacyAnimation.GetClip(clipName) != null)
                return _legacyAnimation.GetClip(clipName).length;

            if (_spawnedAnimator != null && _spawnedAnimator.runtimeAnimatorController != null)
            {
                var clip = _spawnedAnimator.runtimeAnimatorController.animationClips
                    .FirstOrDefault(c => c != null && string.Equals(c.name, clipName, StringComparison.OrdinalIgnoreCase));
                if (clip != null)
                    return clip.length;
            }

            return 0f;
        }

        private float ResolveActionSegmentDuration(string actionKey, float clipLength)
        {
            ResolveActionLoopWindow(actionKey, clipLength, out var segStart, out var segEnd);
            return Mathf.Max(0.08f, segEnd - segStart);
        }

        private void ResolveActionLoopWindow(string actionKey, float clipLength, out float start, out float end)
        {
            float safeLength = Mathf.Max(0f, clipLength);
            if (safeLength <= 0.01f)
            {
                start = 0f;
                end = 0.01f;
                return;
            }

            float configuredStart = 0f;
            float configuredEnd = 0f;
            if (animationController != null && animationController.TryGetLoopWindow(actionKey, out var cfgStart, out var cfgEnd))
            {
                configuredStart = cfgStart;
                configuredEnd = cfgEnd;
            }

            start = Mathf.Clamp(configuredStart, 0f, Mathf.Max(0f, safeLength - 0.01f));
            if (configuredEnd > 0f)
                end = Mathf.Clamp(configuredEnd, start + 0.01f, safeLength);
            else
                end = safeLength;
        }

        private void MaintainPlayableAttackActionLoop(bool attackChannelActive)
        {
            if (!_animationGraphReady)
                return;

            if (_runtimeAttackLoopEnabled)
            {
                if (attackChannelActive)
                {
                    if (_freezeAttackLoopPose)
                    {
                        _attackPlayableLoopTime = _attackLoopStartResolved;
                        _attackClipPlayable.SetTime(_attackPlayableLoopTime);
                        return;
                    }

                    float span = Mathf.Max(0.01f, _attackLoopEndResolved - _attackLoopStartResolved);
                    _attackPlayableLoopTime += Time.deltaTime;
                    if (_attackPlayableLoopTime >= _attackLoopEndResolved)
                        _attackPlayableLoopTime = _attackLoopStartResolved + Mathf.Repeat(_attackPlayableLoopTime - _attackLoopStartResolved, span);

                    _attackClipPlayable.SetTime(_attackPlayableLoopTime);
                }
                else
                {
                    _attackPlayableLoopTime = _attackLoopStartResolved;
                    _attackClipPlayable.SetTime(_attackPlayableLoopTime);
                }
            }
            else
            {
                // One-shot mode: advance once and clamp to the end of the segment (no looping).
                float elapsed = Mathf.Max(0f, Time.time - _attackOneShotStartWallTime);
                float oneShotSpan = Mathf.Max(0.01f, _attackOneShotDuration);
                float clamped = Mathf.Min(elapsed, Mathf.Max(0f, oneShotSpan - 0.01f));
                float maxPlayable = Mathf.Max(_runtimeAttackStartTime, _attackLoopEndResolved - 0.01f);
                float t = Mathf.Min(_runtimeAttackStartTime + clamped, maxPlayable);
                _attackClipPlayable.SetTime(t);
            }
        }

        private void UpdateTemporaryEquippedItemVisual()
        {
            if (!enableTemporaryItemVisuals || bridge?.Character == null)
            {
                ClearTemporaryItemVisual();
                return;
            }

            if (!TryFindEquippedItemSlotByName(bridge.Character, tempWeaponTestItemName, out var equippedSlotId))
            {
                ClearTemporaryItemVisual();
                return;
            }

            string desiredKey = ResolveTempWeaponMeshKey();
            if (string.IsNullOrWhiteSpace(desiredKey))
            {
                ClearTemporaryItemVisual();
                return;
            }

            if (_equippedItemVisual != null
                && string.Equals(_equippedItemVisualKey, desiredKey, StringComparison.OrdinalIgnoreCase)
                && _equippedItemVisualSlotId == equippedSlotId)
                return;

            ClearTemporaryItemVisual();

            bool equipOnLeft = equippedSlotId == leftHandEquipSlotId;
            var parent = FindEquipHandAnchor(equippedSlotId) ?? transform;
            _equippedItemVisual = new GameObject($"TempEquipped_{desiredKey}");
            _equippedItemVisual.transform.SetParent(parent, false);
            _equippedItemVisual.name = $"TempEquipped_{desiredKey}";
            _equippedItemVisualKey = desiredKey;
            _equippedItemVisualSlotId = equippedSlotId;
            _equippedItemOnLeftHand = equipOnLeft;
            ApplyTemporaryItemVisualTransform();
            if (logTemporaryItemAttachment)
                Debug.Log($"Temp item attach '{desiredKey}' slot={equippedSlotId} hand={(equipOnLeft ? "Left" : "Right")} anchor='{parent.name}'");

            var prefab = Resources.Load<GameObject>($"{itemMeshResourcesFolder}/{desiredKey}");
            if (prefab != null)
            {
                var child = Instantiate(prefab, _equippedItemVisual.transform, false);
                child.name = desiredKey;
                return;
            }

            TryStartLoadItemMeshFromGlb(desiredKey, _equippedItemVisual.transform);
        }

        private void ClearTemporaryItemVisual()
        {
            _itemMeshLoadRequestId++;
            _itemMeshLoadInProgressKey = string.Empty;
            if (_equippedItemVisual != null)
                Destroy(_equippedItemVisual);
            _equippedItemVisual = null;
            _equippedItemVisualKey = string.Empty;
            _equippedItemVisualSlotId = -1;
            _equippedItemOnLeftHand = false;
        }

        private void ApplyTemporaryItemVisualTransform()
        {
            if (_equippedItemVisual == null)
                return;

            _equippedItemVisual.transform.localPosition = _equippedItemOnLeftHand ? leftHandItemLocalPosition : rightHandItemLocalPosition;
            _equippedItemVisual.transform.localRotation = Quaternion.Euler(_equippedItemOnLeftHand ? leftHandItemLocalEuler : rightHandItemLocalEuler);
            _equippedItemVisual.transform.localScale = _equippedItemOnLeftHand ? leftHandItemLocalScale : rightHandItemLocalScale;
        }

        private static bool TryFindEquippedItemSlotByName(AO.Core.Characters.Character character, string wantedName, out int slotId)
        {
            slotId = -1;
            if (character?.Equipment == null || AO.Core.Characters.CharacterEquipment.GetItemInstance == null)
                return false;
            if (string.IsNullOrWhiteSpace(wantedName))
                return false;

            string wanted = NormalizeNameToken(wantedName);
            bool wantsScalpel = wanted.Contains("scalpel", StringComparison.OrdinalIgnoreCase);

            var equipped = character.Equipment.GetAllEquipped();
            foreach (var kv in equipped)
            {
                var inst = AO.Core.Characters.CharacterEquipment.GetItemInstance(kv.Value);
                var name = inst?.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string equippedName = NormalizeNameToken(name);
                if (equippedName.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                    || (wantsScalpel && equippedName.Contains("scalpel", StringComparison.OrdinalIgnoreCase)))
                {
                    slotId = kv.Key;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeNameToken(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var chars = input.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars).ToLowerInvariant();
        }

        private string ResolveTempWeaponMeshKey()
        {
            if (tempWeaponMeshCandidates == null || tempWeaponMeshCandidates.Length == 0)
                return string.Empty;

            for (int i = 0; i < tempWeaponMeshCandidates.Length; i++)
            {
                var key = tempWeaponMeshCandidates[i];
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                var prefab = Resources.Load<GameObject>($"{itemMeshResourcesFolder}/{key}");
                if (prefab != null)
                    return key;
                if (!string.IsNullOrWhiteSpace(ResolveItemMeshPath(key)))
                    return key;
            }

            return string.Empty;
        }

        private void RefreshDebugHeadVisual()
        {
            string desiredKey = bridge != null ? bridge.DebugHeadMeshKey : string.Empty;
            if (string.Equals(_debugHeadMeshKey, desiredKey, StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(_debugHeadLoadInProgressKey, desiredKey, StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrWhiteSpace(desiredKey))
            {
                ClearDebugHeadVisual();
                return;
            }

            if (_spawnedPrefabVisual == null)
                return;

            _debugHeadLoadInProgressKey = desiredKey;
            int requestId = ++_debugHeadLoadRequestId;
            _ = LoadDebugHeadVisualAsync(desiredKey, requestId);
        }

        private async Task LoadDebugHeadVisualAsync(string meshKey, int requestId)
        {
            object importer = null;
            GameObject nextVisual = null;
            try
            {
                if (FindHeadAnchor() == null)
                    return;

                nextVisual = new GameObject($"DebugHead_{meshKey}");
                nextVisual.transform.SetParent(transform, false);

                var prefab = Resources.Load<GameObject>($"{itemMeshResourcesFolder}/{meshKey}");
                if (prefab != null)
                {
                    var instance = Instantiate(prefab, nextVisual.transform, false);
                    instance.name = meshKey;
                }
                else
                {
                    string meshPath = ResolveItemMeshPath(meshKey);
                    if (string.IsNullOrWhiteSpace(meshPath) || !File.Exists(meshPath))
                        meshPath = await AOCharacterMeshResolver.ResolveAsync(meshKey);
                    meshPath = GlbDataUriLoadPathResolver.Resolve(meshPath, true);
                    if (string.IsNullOrWhiteSpace(meshPath))
                    {
                        Destroy(nextVisual);
                        return;
                    }

                    bool instantiated = await TryInstantiateGlbWithReflection(
                        meshPath,
                        nextVisual.transform,
                        loadedImporter => importer = loadedImporter,
                        disableAnimations: true);
                    if (!instantiated)
                    {
                        Destroy(nextVisual);
                        return;
                    }
                }

                if (requestId != _debugHeadLoadRequestId)
                {
                    Destroy(nextVisual);
                    return;
                }

                ClearDebugHeadVisual();
                _debugHeadVisual = nextVisual;
                _debugHeadVisual.SetActive(_characterVisualsVisible);
                _debugHeadVisualImporter = importer;
                _debugHeadMeshKey = meshKey;
                importer = null;
                ApplyDebugHeadVisualTransform();
            }
            catch (Exception ex)
            {
                if (nextVisual != null)
                    Destroy(nextVisual);
                Debug.LogWarning($"Head visual load failed for '{meshKey}': {ex.Message}");
            }
            finally
            {
                DisposeImporter(importer);
                if (requestId == _debugHeadLoadRequestId)
                    _debugHeadLoadInProgressKey = string.Empty;
            }
        }

        private void ClearDebugHeadVisual()
        {
            DisposeImporter(_debugHeadVisualImporter);
            _debugHeadVisualImporter = null;
            if (_debugHeadVisual != null)
                Destroy(_debugHeadVisual);
            _debugHeadVisual = null;
            _debugHeadMeshKey = string.Empty;
            _debugHeadLoadInProgressKey = string.Empty;
        }

        private void ApplyDebugHeadVisualTransform()
        {
            if (_debugHeadVisual == null)
                return;

            Transform authoredAttractor = FindAuthoredHeadAttractor();
            if (authoredAttractor != null)
            {
                if (_debugHeadVisual.transform.parent != authoredAttractor)
                    _debugHeadVisual.transform.SetParent(authoredAttractor, false);

                // AO authored this attachment completely. Legacy preview offsets and
                // bounds corrections were compensating for the missing attractor.
                _debugHeadVisual.transform.localPosition = Vector3.zero;
                _debugHeadVisual.transform.localRotation = Quaternion.identity;
                _debugHeadVisual.transform.localScale = Vector3.one;
                return;
            }

            Transform anchor = FindHeadAnchor();
            if (anchor == null)
                return;

            _debugHeadVisual.transform.rotation = anchor.rotation * Quaternion.Euler(debugHeadLocalEuler);
            _debugHeadVisual.transform.localScale = debugHeadLocalScale;
            if (!string.IsNullOrWhiteSpace(_debugHeadMeshKey)
                && _headPreviewEulerByMeshKey.TryGetValue(_debugHeadMeshKey, out var extraEuler))
            {
                _debugHeadVisual.transform.rotation *= Quaternion.Euler(extraEuler);
            }

            Vector3 basePosition = anchor.position + anchor.TransformVector(debugHeadLocalPosition);
            _debugHeadVisual.transform.position = basePosition;

            // ABIFF head origins are not authored at the neck seam. Align the bottom of
            // the face renderer (not hair, which may extend downward) to the skeleton
            // head anchor. A percentage of total height buried some face variants.
            var renderers = _debugHeadVisual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Renderer faceRenderer = renderers.FirstOrDefault(renderer =>
                    renderer != null
                    && renderer.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? renderers[0];
                Bounds combined = faceRenderer.bounds;
                _debugHeadVisual.transform.position += Vector3.up
                    * (basePosition.y - combined.min.y);

                combined = faceRenderer.bounds;
                Vector3 centerOffset = combined.center - _debugHeadVisual.transform.position;
                _debugHeadVisual.transform.position -= new Vector3(centerOffset.x, 0f, centerOffset.z);
            }

            if (!string.IsNullOrWhiteSpace(_debugHeadMeshKey)
                && _headPreviewOffsetByMeshKey.TryGetValue(_debugHeadMeshKey, out var extraOffset))
            {
                _debugHeadVisual.transform.position += anchor.TransformVector(extraOffset);
            }
        }

        private Transform FindAuthoredHeadAttractor()
        {
            if (_spawnedPrefabVisual == null)
                return null;

            foreach (Transform candidate in _spawnedPrefabVisual.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null
                    && candidate.name.StartsWith(
                        "AOAttractor_Attractor01_head", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        private void LoadHeadPreviewOffsets()
        {
            _headPreviewOffsetByMeshKey.Clear();
            _headPreviewEulerByMeshKey.Clear();
            _headPreviewAnchorYByMeshKey.Clear();

            if (string.IsNullOrWhiteSpace(headPreviewOffsetsFileName))
                return;

            string file = Path.Combine(
                Application.streamingAssetsPath,
                "AOData",
                headPreviewOffsetsFileName);

            if (!File.Exists(file))
                return;

            try
            {
                var parsed = JsonConvert.DeserializeObject<HeadPreviewOffsetsFile>(File.ReadAllText(file));
                if (parsed?.entries == null)
                    return;

                foreach (var entry in parsed.entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.meshKey))
                        continue;

                    string key = entry.meshKey.Trim();
                    _headPreviewOffsetByMeshKey[key] = new Vector3(entry.x, entry.y, entry.z);
                    _headPreviewEulerByMeshKey[key] = new Vector3(entry.rx, entry.ry, entry.rz);
                    if (entry.anchorNormalizedY >= 0f && entry.anchorNormalizedY <= 1f)
                        _headPreviewAnchorYByMeshKey[key] = entry.anchorNormalizedY;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading {headPreviewOffsetsFileName}: {ex.Message}");
            }
        }

        private Transform FindHeadAnchor()
        {
            if (_spawnedPrefabVisual == null)
                return null;

            var all = _spawnedPrefabVisual.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (string.IsNullOrWhiteSpace(n))
                    continue;
                if (n.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("bip01 head", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("neck", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i];
                }
            }

            return _spawnedPrefabVisual.transform;
        }

        private string ResolveItemMeshPath(string meshResourceName)
        {
            var candidates = new[]
            {
                Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", itemMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.streamingAssetsPath, itemMeshResourcesFolder, $"{meshResourceName}.glb"),
                Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder, $"{meshResourceName}.gltf"),
                Path.Combine(Application.streamingAssetsPath, "AOData", itemMeshResourcesFolder, $"{meshResourceName}.gltf"),
                Path.Combine(Application.streamingAssetsPath, itemMeshResourcesFolder, $"{meshResourceName}.gltf")
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

        private void TryStartLoadItemMeshFromGlb(string meshResourceName, Transform parent)
        {
            if (string.IsNullOrWhiteSpace(meshResourceName) || parent == null)
                return;
            if (string.Equals(_itemMeshLoadInProgressKey, meshResourceName, StringComparison.OrdinalIgnoreCase))
                return;

            _itemMeshLoadInProgressKey = meshResourceName;
            int requestId = ++_itemMeshLoadRequestId;
            _ = LoadItemMeshFromGlbAsync(meshResourceName, parent, requestId);
        }

        private async Task LoadItemMeshFromGlbAsync(string meshResourceName, Transform parent, int requestId)
        {
            try
            {
                string meshPath = ResolveItemMeshPath(meshResourceName);
                meshPath = GlbDataUriLoadPathResolver.Resolve(meshPath, true);
                if (string.IsNullOrWhiteSpace(meshPath) || !File.Exists(meshPath))
                    return;
                if (requestId != _itemMeshLoadRequestId || parent == null)
                    return;

                bool instantiated = await TryInstantiateGlbWithReflection(meshPath, parent);
                if (!instantiated)
                    return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Item mesh load failed for '{meshResourceName}': {ex.Message}");
            }
            finally
            {
                if (requestId == _itemMeshLoadRequestId)
                    _itemMeshLoadInProgressKey = string.Empty;
            }
        }

        private Transform FindEquipHandAnchor(int equipSlotId)
        {
            bool left = equipSlotId == leftHandEquipSlotId;
            bool right = equipSlotId == rightHandEquipSlotId || !left;
            return left ? FindLeftHandAnchor() : (right ? FindRightHandAnchor() : FindRightHandAnchor());
        }

        private Transform FindRightHandAnchor()
        {
            if (_spawnedPrefabVisual == null)
                return null;

            if (!string.IsNullOrWhiteSpace(forceRightHandBoneName))
            {
                var forced = FindTransformByNameContains(forceRightHandBoneName);
                if (forced != null)
                    return forced;
            }

            var all = _spawnedPrefabVisual.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (string.IsNullOrWhiteSpace(n))
                    continue;
                if (n.IndexOf("r_hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("right hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("rhand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("hand.r", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i];
                }
            }

            // Common exporter naming
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (string.IsNullOrWhiteSpace(n))
                    continue;
                if (n.IndexOf("bip01 r hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("right_hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("mixamorig:righthand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("weapon_r", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i];
                }
            }

            return _spawnedPrefabVisual.transform;
        }

        private Transform FindLeftHandAnchor()
        {
            if (_spawnedPrefabVisual == null)
                return null;

            if (!string.IsNullOrWhiteSpace(forceLeftHandBoneName))
            {
                var forced = FindTransformByNameContains(forceLeftHandBoneName);
                if (forced != null)
                    return forced;
            }

            var all = _spawnedPrefabVisual.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string n = all[i].name;
                if (string.IsNullOrWhiteSpace(n))
                    continue;
                if (n.IndexOf("l_hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("left hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("lhand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("hand.l", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("bip01 l hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("left_hand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("mixamorig:lefthand", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("weapon_l", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i];
                }
            }

            return _spawnedPrefabVisual.transform;
        }

        private Transform FindTransformByNameContains(string token)
        {
            if (_spawnedPrefabVisual == null || string.IsNullOrWhiteSpace(token))
                return null;

            var all = _spawnedPrefabVisual.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return all[i];
            }

            return null;
        }

        private void ResolveMoveLoopWindow(string actionKey, float clipLength, out float start, out float end)
        {
            float safeLength = Mathf.Max(0f, clipLength);
            if (safeLength <= 0.01f)
            {
                start = 0f;
                end = 0.01f;
                return;
            }

            float configuredStart = moveClipLoopStartSeconds;
            float configuredEnd = moveClipLoopEndSeconds;
            if (animationController != null && animationController.TryGetLoopWindow(actionKey, out var cfgStart, out var cfgEnd))
            {
                configuredStart = cfgStart;
                configuredEnd = cfgEnd;
            }

            start = Mathf.Clamp(configuredStart, 0f, Mathf.Max(0f, safeLength - 0.01f));
            if (configuredEnd > 0f)
                end = Mathf.Clamp(configuredEnd, start + 0.01f, safeLength);
            else
                end = safeLength;
        }

        private void SeekLegacyMoveLoopStart()
        {
            if (_legacyAnimation == null || string.IsNullOrWhiteSpace(_moveLegacyClipName))
                return;

            var clip = _legacyAnimation.GetClip(_moveLegacyClipName);
            if (clip == null)
                return;

            ResolveMoveLoopWindow(_activeMoveAction, clip.length, out _moveLoopStartResolved, out _moveLoopEndResolved);
            var state = _legacyAnimation[_moveLegacyClipName];
            if (state != null)
                state.time = _moveLoopStartResolved;
        }

        private void MaintainLegacyMoveLoopWindow()
        {
            if (_legacyAnimation == null || string.IsNullOrWhiteSpace(_moveLegacyClipName))
                return;

            var clip = _legacyAnimation.GetClip(_moveLegacyClipName);
            if (clip == null || !_legacyAnimation.IsPlaying(_moveLegacyClipName))
                return;

            ResolveMoveLoopWindow(_activeMoveAction, clip.length, out _moveLoopStartResolved, out _moveLoopEndResolved);
            var state = _legacyAnimation[_moveLegacyClipName];
            if (state == null)
                return;

            float span = Mathf.Max(0.01f, _moveLoopEndResolved - _moveLoopStartResolved);
            if (state.time >= _moveLoopEndResolved)
                state.time = _moveLoopStartResolved + Mathf.Repeat(state.time - _moveLoopStartResolved, span);
        }

        private void MaintainPlayableMoveLoopWindow(bool shouldMove)
        {
            if (!_animationGraphReady)
                return;

            if (shouldMove)
            {
                float span = Mathf.Max(0.01f, _moveLoopEndResolved - _moveLoopStartResolved);
                _movePlayableLoopTime += Time.deltaTime;
                if (_movePlayableLoopTime >= _moveLoopEndResolved)
                    _movePlayableLoopTime = _moveLoopStartResolved + Mathf.Repeat(_movePlayableLoopTime - _moveLoopStartResolved, span);

                _moveClipPlayable.SetTime(_movePlayableLoopTime);
            }
            else
            {
                _movePlayableLoopTime = _moveLoopStartResolved;
                _moveClipPlayable.SetTime(_movePlayableLoopTime);
            }
        }

        private void EnsureRuntimeMaterial()
        {
            if (_runtimeMaterial != null)
                return;

            var source = targetRenderer.sharedMaterial;
            var shader = source != null && source.shader != null && source.shader.isSupported
                ? source.shader
                : Shader.Find("Universal Render Pipeline/Lit")
                  ?? Shader.Find("Standard")
                  ?? Shader.Find("Unlit/Texture");

            if (shader == null)
                return;

            _runtimeMaterial = source != null ? new Material(source) : new Material(shader);
            _runtimeMaterial.name = "PlayerAppearanceMat";
        }

        private bool TryGetTexture(int textureId, out Texture2D texture)
        {
            texture = null;
            if (textureId <= 0)
                return false;

            if (_textureCache.TryGetValue(textureId, out var cached) && cached != null)
            {
                texture = cached;
                return true;
            }

            string path = Path.Combine(
                Application.streamingAssetsPath,
                "AOData",
                "BodyTextures",
                bodyTextureSizeFolder,
                $"{textureId}.jpg");

            if (!File.Exists(path))
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
            _textureCache[textureId] = loaded;
            texture = loaded;
            return true;
        }

        private void LoadAppearanceMapOverrides()
        {
            string path = Path.Combine(
                Application.streamingAssetsPath,
                "AOData",
                appearanceMapFileName);

            if (!File.Exists(path))
                return;

            try
            {
                var parsed = JsonConvert.DeserializeObject<AppearanceMapFile>(File.ReadAllText(path));
                if (parsed == null)
                    return;

                if (parsed.breed_texture_ids != null)
                {
                    foreach (var pair in parsed.breed_texture_ids)
                    {
                        if (!int.TryParse(pair.Key, out int breedId))
                            continue;
                        _breedTextureIds[breedId] = pair.Value;
                    }
                }

                if (parsed.breed_scale_multipliers != null)
                {
                    foreach (var pair in parsed.breed_scale_multipliers)
                    {
                        if (!int.TryParse(pair.Key, out int breedId))
                            continue;
                        _breedScaleMultipliers[breedId] = pair.Value;
                    }
                }

                if (parsed.breed_mesh_resources != null)
                {
                    foreach (var pair in parsed.breed_mesh_resources)
                    {
                        if (!int.TryParse(pair.Key, out int breedId))
                            continue;
                        _breedMeshResourceNames[breedId] = pair.Value ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading {appearanceMapFileName}: {ex.Message}");
            }
        }

        private void LoadAnimationsMap()
        {
            _animationSwaps.Clear();
            _animationTemplates.Clear();

            string path = Path.Combine(
                Application.streamingAssetsPath,
                "AOData",
                animationsMapFileName);

            if (!File.Exists(path))
                return;

            try
            {
                var parsed = JsonConvert.DeserializeObject<AnimationMapFile>(File.ReadAllText(path));
                if (parsed == null)
                    return;

                if (parsed.swaps != null)
                {
                    foreach (var pair in parsed.swaps)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                            continue;
                        _animationSwaps[pair.Key.Trim()] = pair.Value.Trim();
                    }
                }

                if (parsed.animations != null)
                {
                    foreach (var pair in parsed.animations)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                            continue;
                        _animationTemplates[pair.Key.Trim()] = pair.Value.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading {animationsMapFileName}: {ex.Message}");
            }
        }

        private void LoadAttackAnimationRules()
        {
            _attackAnimationRules = new AttackAnimationRulesFile();
            string path = Path.Combine(
                Application.streamingAssetsPath,
                "AOData",
                attackAnimationRulesFileName);

            if (!File.Exists(path))
                return;

            try
            {
                var parsed = JsonConvert.DeserializeObject<AttackAnimationRulesFile>(File.ReadAllText(path));
                if (parsed != null)
                    _attackAnimationRules = parsed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading {attackAnimationRulesFileName}: {ex.Message}");
            }
        }
    }
}



