using AO.Core.Characters;
using AO.Core.Stats;
using AO.Data.Unity;
using AO.Assets.Decoders;
using AO.Assets.Conversion;
using AO.Assets.Navigation;
using AO.Assets.ResourceDatabase;
using AO.Unity.Assets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.World
{
    public partial class PrototypeWorldBootstrap : MonoBehaviour
    {
        public enum ClientLoadPhase
        {
            Boot = 0,
            CharacterFlowReady = 1,
            EnterWorldLoading = 2,
            InWorld = 3
        }

        private sealed class RuntimeDynelTargetMetadata : MonoBehaviour
        {
            public bool UseNonBlockingColliders;
            public string ObjectType;
        }

        private sealed class RuntimePlaceholderVisualMarker : MonoBehaviour
        {
        }

        private sealed class RuntimeDynelGlbFallbackState : MonoBehaviour
        {
            public List<string> CandidatePaths = new();
            public float NextRetryAt;
            public int RetryCount;
        }

        private sealed class PendingRuntimeSpawnEntry
        {
            public RuntimeWorldObjectData Obj;
            public Vector3 AoPosition;
            public string MeshName;
            public string MeshKey;
            public Vector3 PerMeshRotationEuler;
            public bool PerMeshRotationIsAbsolute;
            public Vector3 PerMeshPositionOffset;
        }

        private sealed class PendingStaticStatelSpawnEntry
        {
            public StatelData Statel;
            public string MeshName;
            public string MeshKey;
            public Vector3 PerMeshRotationEuler;
            public bool PerMeshRotationIsAbsolute;
            public Vector3 PerMeshPositionOffset;
        }

        [Header("Character")]
        [SerializeField] private string characterName = "PrototypeCharacter";
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 1f, 0f);
        [SerializeField] private bool createVisualIfMissing = true;
        [SerializeField] private bool enableAuthoritativeNetworking = false;
        [SerializeField] private bool strictAuthoritativeServerRequired = false;
        [SerializeField] private string authoritativeServerHost = "127.0.0.1";
        [SerializeField] private int authoritativeServerPort = 4000;
        [SerializeField] private bool authoritativeMovement = true;

        [Header("World")]
        [SerializeField] private bool createGroundPlane = true;
        [SerializeField] private bool createPlayfieldFallbackGround = true;
        [SerializeField] private float playfieldFallbackGroundHeight = 0.5f;
        [SerializeField] private float playfieldFallbackGroundPadding = 8f;
        [SerializeField] private float playfieldFallbackGroundDefaultSize = 512f;
        [SerializeField] private bool loadPlayfieldFromJson = false;
        [SerializeField] private bool deferInitialWorldLoadUntilCharacterSelection = true;
        [SerializeField] private bool requireNearbyRuntimeContentReadyBeforeEnterWorld = true;
        [SerializeField] private float enterWorldNearbyContentRadius = 70f;
        [SerializeField] private int enterWorldNearbyContentMinReadyDynels = 2;
        [SerializeField] private float enterWorldNearbyContentTimeoutSeconds = 4f;
        [SerializeField] private int playfieldId = 100;
        [SerializeField] private int maxStatelsToSpawn = 2000;
        [SerializeField] private bool deferStaticStatelSpawn = true;
        [SerializeField] private int staticStatelImmediateSpawnSeedCount = 128;
        [SerializeField] private int staticStatelBackgroundSpawnBatch = 24;
        [SerializeField] private float staticStatelSpawnFrameBudgetMs = 2.0f;
        [SerializeField] private float placeholderScaleMultiplier = 2f;
        [SerializeField] private string playfieldsSubfolder = "AOData/Playfields";
        [SerializeField] private bool loadStatelMeshMap = true;
        [SerializeField] private bool enablePlayfieldPackageOverride = false;
        [SerializeField] private bool preferPlayfieldPackageFolder = true;
        [SerializeField] private int playfieldPackageOverridePlayfieldId = 800;
        [SerializeField] private bool playfieldPackageOverrideStrict = true;
        [SerializeField] private bool autoSelectPlayfieldOverrideByFolder = true;
        [SerializeField] private bool enablePackageMeshPartTransforms = false;
        [SerializeField] private bool packageMeshPartApplyPosition = true;
        [SerializeField] private bool packageMeshPartApplyScale = true;
        [SerializeField] private bool packageMeshPartApplyRotation = false;
        [SerializeField] private bool packageMeshPartConvertAoQuaternionToUnity = true;
        [Header("Playfield GLB Override")]
        [SerializeField] private bool enablePlayfieldGlbOverride = false;
        [SerializeField] private int playfieldGlbOverridePlayfieldId = 800;
        [SerializeField] private bool playfieldGlbOverrideStrict = false;
        [SerializeField] private string playfieldGlbOverrideFolderNameFormat = "Playfield_{0}_test";
        [SerializeField] private bool loadPlayfieldGlbStatels = true;
        [SerializeField] private bool loadPlayfieldGlbTerrain = true;
        [SerializeField] private bool loadPlayfieldGlbWater = true;
        [SerializeField] private bool playfieldGlbUsesAoUnits = true;
        [SerializeField] private bool playfieldGlbTerrainFlipUvV = true;
        [SerializeField] private bool playfieldGlbTerrainFlipUvU = false;
        [SerializeField] private bool playfieldGlbTerrainSwapUvAxes = false;
        [SerializeField] private bool playfieldGlbOverrideLoadTerrainFromJson = false;
        [SerializeField] private bool playfieldGlbOverrideLoadWaterFromJson = false;
        [SerializeField] private bool addCollidersForPlayfieldGlbOverride = true;
        [SerializeField] private int maxPlayfieldGlbColliderTriangles = 2097152;
        [SerializeField] private bool playfieldGlbAutoCenterHorizontally = true;
        [SerializeField] private bool playfieldGlbCreateSafetyGround = true;
        [SerializeField] private float playfieldGlbSafetyGroundPadding = 40f;
        [SerializeField] private float playfieldGlbSafetyGroundThickness = 2f;
        [SerializeField] private bool loadMeshPrefabsFromResources = true;
        [SerializeField] private string meshPrefabResourcesFolder = "WorldMeshes";
        [SerializeField] private Vector3 meshPrefabRotationOffsetEuler = Vector3.zero;
        [SerializeField] private bool applyGlobalMeshBasisCorrection = false;
        [SerializeField] private Vector3 globalMeshBasisCorrectionEuler = new Vector3(180f, 0f, 0f);
        [SerializeField] private bool enableBuiltInMeshPlacementCorrection = true;
        [SerializeField] private bool loadMeshRotationOverrides = true;
        [SerializeField] private bool logMeshRotationOverrideApplications = false;
        [SerializeField] private string meshRotationOverrideDebugFilter = "jobe_building_bridge_120m";
        [SerializeField] private bool enableBuiltInMeshRotationOverrides = false;
        [SerializeField] private string meshRotationOverridesSuffix = "_mesh_rotation_overrides.json";
        [SerializeField] private string globalMeshRotationOverridesFile = "mesh_rotation_overrides_global.json";
        [SerializeField] private bool loadGlobalCollisionOverrides = true;
        [SerializeField] private string globalCollisionOverridesFile = "collision_overrides_global.json";
        [SerializeField] private bool loadTerrainFromJson = true;
        [SerializeField] private bool flipPackageTerrainV = false;
        [SerializeField] private bool forceFlipVForAogltfManifestTerrain = false;
        [SerializeField] private bool swapPackageTerrainUvAxes = false;
        [SerializeField] private bool flipPackageTerrainU = false;
        [SerializeField] private string terrainJsonSuffix = "_terrain.json";
        [SerializeField] private string terrainAtlasSuffix = "_terrain_atlas.png";
        [SerializeField] private int maxTerrainJsonMegabytes = 256;
        [SerializeField] private bool loadWaterFromJson = true;
        [SerializeField] private string waterJsonSuffix = "_water.json";
        [SerializeField] private bool loadRuntimeWorldObjectsFromJson = true;
        [SerializeField] private bool allowRuntimeWorldObjectsJsonWhenAuthoritativeNetworking = true;
        [SerializeField] private bool enableRuntimeObjectChunkRings = true;
        [SerializeField] private float runtimeObjectRing0Radius = 85f;
        [SerializeField] private int runtimeObjectBackgroundSpawnBatch = 32;
        [SerializeField] private float runtimeObjectStreamingRadius = 130f;
        [SerializeField] private float runtimeObjectStreamingMaxDeferSeconds = 20f;
        [SerializeField] private int runtimeObjectImmediateSpawnSeedCount = 4;
        [SerializeField] private float runtimeObjectSpawnFrameBudgetMs = 2.0f;
        [SerializeField] private bool loadRuntimeObjectVisualsFromGlbFallback = true;
        [SerializeField] private bool persistRuntimeGlbTemplateCacheAcrossZones = true;
        [SerializeField] private int maxConcurrentRuntimeGlbLoads = 1;
        [SerializeField] private int maxRuntimeGlbStartsPerFrame = 1;
        [SerializeField] private int maxRuntimeGlbAttachesPerFrame = 1;
        [SerializeField] private float runtimeGlbAttachFrameBudgetMs = 1.25f;
        [SerializeField] private bool prewarmRuntimeGlbTemplates = false;
        [SerializeField] private int maxRuntimeGlbTemplatePrewarmPerPlayfield = 8;
        [SerializeField] private bool normalizeRuntimeGlbDataUriMimeTypes = true;
        [SerializeField] private bool sanitizeRuntimeGlbDataUris = true;
        [SerializeField] private bool allowRuntimeObjectGltfFallback = false;
        [SerializeField] private bool runtimeMobDynelsUseNonBlockingSelectionColliders = true;
        [SerializeField] private float runtimeMobDynelSelectionColliderMinRadius = 0.45f;
        [SerializeField] private bool logRuntimeObjectGlbFallback = false;
        [SerializeField] private float runtimeDynelGlbRetryIntervalSeconds = 1.5f;
        [SerializeField] private int runtimeDynelGlbRetryBudgetPerSweep = 8;
        [SerializeField] private int runtimeDynelGlbMaxRetriesPerHost = 3;
        [SerializeField] private bool hideRuntimePlaceholdersWhenMeshCandidateExists = true;
        [SerializeField] private float runtimeGlbFailureCooldownSeconds = 12f;
        [SerializeField] private bool runtimeGlbBlockFailedPathForSession = false;
        [SerializeField] private string runtimeWorldObjectsJsonSuffix = "_runtime_world_objects.json";
        [SerializeField] private bool testUseAoSharpRuntimeObjectsOnly = false;
        [SerializeField] private bool loadIndoorRoomFallbackFromJson = true;
        [SerializeField] private bool loadIndoorRoomSurfacesFromJson = true;
        [SerializeField] private string roomJsonSuffix = "_rooms.json";
        [SerializeField] private string roomSurfacesJsonSuffix = "_room_surfaces.json";
        [SerializeField] private float indoorStatelCullRadiusAo = 450f;
        [SerializeField] private float indoorRoomFloorThickness = 0.5f;
        [SerializeField] private Material indoorRoomFloorMaterialTemplate;
        [SerializeField] private Color indoorRoomFloorColor = new Color(0.32f, 0.30f, 0.28f, 1f);
        [SerializeField] private Color indoorRoomWallColor = new Color(0.86f, 0.84f, 0.80f, 1f);
        [SerializeField] private Color indoorRoomCeilingColor = new Color(0.56f, 0.58f, 0.60f, 1f);
        [SerializeField] private bool useDistinctIndoorSurfaceColors = true;
        [SerializeField] private bool addIndoorRoomSurfaceColliders = true;
        [SerializeField] private bool addTerrainColliders = true;
        [SerializeField] private bool addStatelColliders = true;
        [SerializeField] private bool statelCollidersConvex = false;
        [SerializeField] private bool statelCollidersIsTrigger = false;
        [SerializeField] private int maxMeshCollidersPerStatel = 24;
        [SerializeField] private bool enableZoneTransitions = true;
        [SerializeField] private bool enablePrototypeMovement = true;
        [SerializeField] private bool logMissingMeshPrefabs = true;
        [SerializeField] private int maxMissingMeshPrefabLogs = 20;
        [SerializeField] private bool placeWorldInAoCoordinates = false;
        [SerializeField] private bool centerPlayfieldAroundOrigin = true;
        [SerializeField] private float coordinateScale = 0.05f;
        [SerializeField] private float maxStatelAbsoluteCoordinate = 100000f;
        [SerializeField] private bool spawnCharacterAtPlayfieldCenter = true;
        [SerializeField] private bool alignMainCameraToSpawn = true;
        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 2f, -6f);
        [SerializeField] private float cameraLookAtHeight = 1.2f;
        [SerializeField] private bool spawnRotationDebugMeshAtOrigin = true;
        [SerializeField] private string rotationDebugMeshName = "tree_jungle_medium.abiff";
        [SerializeField] private Vector3 rotationDebugSpawnPosition = new Vector3(37f, 24f, -17f);
        [SerializeField] private bool debugMeshApplyBuiltInPlacementCorrection = false;
        [SerializeField] private Vector3 debugMeshExtraEuler = new Vector3(-90f, 0f, 0f);
        [SerializeField] private float debugMeshExtraYOffset = 58f;
        [SerializeField] private float safeSpawnHeightOffset = 1.25f;
        [SerializeField] private float safeSpawnProbeHeight = 2000f;
        [SerializeField] private float safeSpawnSearchStep = 12f;
        [SerializeField] private int safeSpawnSearchRings = 6;
        [Header("Launch Logo Overlay")]
        [SerializeField] private bool showLaunchLogoOnStartup = true;
        [SerializeField] private string playfieldLoadingLogoResourcePath = "Logo/project_mayhem_logo.svg";
        [SerializeField] private Color playfieldLoadingOverlayColor = new Color(0f, 0f, 0f, 0.82f);
        [SerializeField] private Color playfieldLoadingLogoTint = Color.white;
        [SerializeField] private Vector2 playfieldLoadingLogoSize = new Vector2(420f, 420f);
        [SerializeField] private bool playfieldLoadingLogoFillScreen = true;
        [SerializeField, Range(0.05f, 0.95f)] private float playfieldLoadingLogoLetterHighlightWidthNormalized = 0.28f;
        [SerializeField, Range(0.05f, 0.95f)] private float playfieldLoadingLogoLetterHighlightHeightNormalized = 0.34f;
        [SerializeField, Range(0.05f, 0.95f)] private float playfieldLoadingLogoPPositionNormalized = 0.40f;
        [SerializeField, Range(0.05f, 0.95f)] private float playfieldLoadingLogoMPositionNormalized = 0.57f;
        [SerializeField, Range(0.05f, 0.95f)] private float playfieldLoadingLogoLetterYPositionNormalized = 0.56f;
        [SerializeField] private float playfieldLoadingLogoLetterHighlightDurationSeconds = 0.55f;
        [SerializeField] private float playfieldLoadingLogoSweepPauseSeconds = 0.10f;
        [SerializeField] private Color playfieldLoadingLogoPSweepTint = new Color(0.22f, 0.92f, 1f, 0.88f);
        [SerializeField] private Color playfieldLoadingLogoMSweepTint = new Color(0.93f, 0.35f, 1f, 0.88f);
        [SerializeField, Range(0f, 0.35f)] private float playfieldLoadingLogoPulseAmount = 0.12f;
        [SerializeField] private float playfieldLoadingLogoPulseDurationSeconds = 1.1f;
        [SerializeField] private float launchLogoDurationSeconds = 6f;
        [SerializeField] private bool pauseGameplayDuringLaunchLogo = true;

        [Header("Scene References")]
        [SerializeField] private CharacterRuntimeBridge existingBridge;
        [SerializeField] private Material terrainMaterialTemplate;
        [SerializeField] private Material waterMaterialTemplate;
        [SerializeField] private Color defaultWaterColor = new Color(0.18f, 0.62f, 0.88f, 0.38f);

        private Transform _worldRoot;
        private Transform _activePlayfieldRoot;
        private int _activePlayfieldId = -1;
        private Vector3 _activeHorizontalCenter = Vector3.zero;
        private bool _activePlayfieldUsesIndoorRoomSurfaces;
        private Dictionary<int, string> _modelNameByInstanceId;
        private readonly Dictionary<string, GameObject> _runtimeGlbVisualTemplateByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _runtimeGlbPathBlockedUntil = new(StringComparer.OrdinalIgnoreCase);
        private Transform _runtimeGlbVisualTemplateRoot;
        private readonly Queue<RuntimeGlbLoadRequest> _runtimeGlbLoadQueue = new();
        private readonly Queue<RuntimeGlbAttachRequest> _runtimeGlbAttachQueue = new();
        private readonly HashSet<string> _runtimeGlbPathsInFlight = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<GameObject>> _runtimeGlbWaitersByPath = new(StringComparer.OrdinalIgnoreCase);
        private Coroutine _runtimeGlbLoadPumpCoroutine;
        private int _runtimeGlbLoadsInFlight;
        private float _nextRuntimeDynelGlbRetrySweepAt;
        private readonly Dictionary<int, Dictionary<uint, string>> _packageStatelMeshMapByPlayfield = new();
        private Dictionary<string, GameObject> _activeRuntimePrefabCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _runtimeGlbLoadPathCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _runtimeGlbDisableAnimationsByPath = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _runtimeGlbSanitizedPathByOriginal = new(StringComparer.OrdinalIgnoreCase);
        private bool _runtimeGlbAnimationSafetyCacheLoaded;
        private readonly Dictionary<int, Texture2D> _runtimeGeneralTextureCache = new();
        private Coroutine _runtimeObjectBackgroundSpawnCoroutine;
        private Coroutine _staticStatelBackgroundSpawnCoroutine;
        private MeshRotationOverrideSet _activeMeshRotationOverrides = new();
        private readonly List<object> _activePlayfieldGlbImporters = new();
        private bool _activePlayfieldGlbLoadInProgress;
        private int _activePlayfieldGlbLoadTicket;
        private Transform _pendingTransitionCharacter;
        private Vector3? _pendingTransitionAoPosition;
        private float? _pendingTransitionYaw;
        private int _pendingTransitionPlayfieldId = -1;
        private int _pendingTransitionLoadTicket = -1;
        private Vector3 _pendingTransitionFallbackWorldPosition = Vector3.zero;
        private Rigidbody _pendingTransitionRigidbody;
        private bool _pendingTransitionRbUseGravity;
        private bool _pendingTransitionRbIsKinematic;
        private bool _activePlayfieldLoadedFromPackage;
        private bool _activeUseCenteredCoordinates;
        private float _activeCoordinateScale = 1f;
        private GameObject _playfieldLoadingOverlayRoot;
        private Image _playfieldLoadingOverlayLogo;
        private RectTransform _playfieldLoadingOverlayLogoRect;
        private RectTransform _playfieldLoadingOverlayLogoPHighlightMaskRect;
        private RectTransform _playfieldLoadingOverlayLogoMHighlightMaskRect;
        private RectTransform _playfieldLoadingOverlayLogoPHighlightRect;
        private RectTransform _playfieldLoadingOverlayLogoMHighlightRect;
        private Image _playfieldLoadingOverlayLogoPHighlightImage;
        private Image _playfieldLoadingOverlayLogoMHighlightImage;
        private Coroutine _playfieldLoadingOverlayLogoSweepCoroutine;
        private Coroutine _playfieldLoadingOverlayHideCoroutine;
        private float _playfieldLoadingOverlayShownAt = -1f;
        private bool _externalWorldEntryLoadingHold;
        private Coroutine _launchLogoRoutine;
        private float _timeScaleBeforeLaunchLogo = 1f;
        private CollisionOverrideSet _collisionOverrides = CreateDefaultCollisionOverrideSet();
        private static CollisionOverrideSet _activeCollisionOverrides = CreateDefaultCollisionOverrideSet();
        private static readonly bool ApplyPickPatchesToIndoorRooms = true;
        private static readonly string IndoorRoomPickJsonlSuffix = "_picks.jsonl";
        private static readonly float IndoorRoomPickPatchSizeAo = 2f;
        private static readonly float IndoorRoomPickPatchHeightOffset = 0.02f;
        private ClientLoadPhase _clientLoadPhase = ClientLoadPhase.Boot;
        private float _enterWorldLoadingStartedAt = -1f;
        private float _bootStartedAt = -1f;
        private float _characterFlowReadyAt = -1f;
        private float _spawnReadyAt = -1f;
        private bool _spawnReadyLogged;
        private float _deferredRuntimeSpawnStartedAt = -1f;

        [Header("Performance Budgets")]
        [SerializeField] private float budgetBootToCharacterFlowReadyMsWarn = 12000f;
        [SerializeField] private float budgetBootToCharacterFlowReadyMsError = 20000f;
        [SerializeField] private float budgetEnterWorldToSpawnReadyMsWarn = 4000f;
        [SerializeField] private float budgetEnterWorldToSpawnReadyMsError = 8000f;
        [SerializeField] private float budgetSpawnReadyToNearbyContentReadyMsWarn = 3000f;
        [SerializeField] private float budgetSpawnReadyToNearbyContentReadyMsError = 7000f;
        [SerializeField] private float frameSpikeWarnMs = 50f;
        [SerializeField] private float frameSpikeErrorMs = 120f;
        [SerializeField] private float frameSpikeLogCooldownSeconds = 1.5f;
        private float _nextFrameSpikeLogAt;
        private int _loadBudgetWarnCount;
        private int _loadBudgetErrorCount;
        private int _frameBudgetWarnCount;
        private int _frameBudgetErrorCount;
        private int _runtimeDeferredQueueCount;
        private int _staticDeferredQueueCount;

        public int ActivePlayfieldId => _activePlayfieldId;
        public Transform ActivePlayfieldRoot => _activePlayfieldRoot;
        public bool ActivePlayfieldGlbLoadInProgress => _activePlayfieldGlbLoadInProgress;
        public ClientLoadPhase CurrentClientLoadPhase => _clientLoadPhase;
        public bool IsStrictAuthoritativeServerRequired => enableAuthoritativeNetworking && strictAuthoritativeServerRequired;
        private bool UseCenteredPlayfieldCoordinates => !placeWorldInAoCoordinates && centerPlayfieldAroundOrigin;
        private float EffectivePlayfieldCoordinateScale => placeWorldInAoCoordinates ? 1f : Mathf.Max(0.0001f, coordinateScale);

        private sealed class PlayfieldData
        {
            public int PlayfieldId { get; set; }
            public string Name { get; set; }
            public List<StatelData> Statels { get; set; } = new();
        }

        private sealed class PlayfieldPackageObjectsFile
        {
            public int PlayfieldId { get; set; }
            public List<PlayfieldPackageObjectData> Objects { get; set; } = new();
        }

        private sealed class PlayfieldPackageObjectData
        {
            public string Source { get; set; }
            public int ObjectId { get; set; }
            public int TemplateId { get; set; }
            public int? MeshId { get; set; }
            public Vector3Data Position { get; set; }
            public QuaternionData RotationQuaternion { get; set; }
            public Vector3Data Scale { get; set; }
            public string Name { get; set; }
            public List<PlayfieldPackageMeshPartData> MeshParts { get; set; } = new();
        }

        private sealed class PlayfieldPackageMeshPartData
        {
            public int SubMeshIndex { get; set; }
            public Vector3Data Position { get; set; }
            public QuaternionData RotationQuaternion { get; set; }
            public Vector3Data Scale { get; set; }
        }

        private sealed class PlayfieldPackageAssetMapFile
        {
            public int PlayfieldId { get; set; }
            public List<PlayfieldPackageAssetMapEntry> Meshes { get; set; } = new();
        }

        private sealed class PlayfieldPackageAssetMapEntry
        {
            public int MeshId { get; set; }
            public string Name { get; set; }
            public string FileName { get; set; }
            public string RelativePath { get; set; }
        }

        private sealed class StatelData
        {
            public int StatelId { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 Rotation { get; set; }
            public float Scale { get; set; } = 1f;
            public Vector3 ScaleVector { get; set; } = Vector3.one;
            public List<StatelMeshPartData> MeshParts { get; set; } = new();
        }

        private sealed class StatelMeshPartData
        {
            public int SubMeshIndex { get; set; }
            public Vector3 Position { get; set; } = Vector3.zero;
            public Quaternion Rotation { get; set; } = Quaternion.identity;
            public Vector3 Scale { get; set; } = Vector3.one;
        }

        private sealed class StatelMeshMapEntry
        {
            public uint StatelId { get; set; }
            public string MeshName { get; set; }
            public int CountInPlayfield { get; set; }
        }

        private sealed class MeshRotationOverrideEntry
        {
            public string MeshName { get; set; }
            public string NodeName { get; set; }
            public string RotationMode { get; set; }
            public EulerRow RotationEuler { get; set; } = new();
            public VectorRow PositionOffset { get; set; } = new();
        }

        private sealed class EulerRow
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
        }

        private sealed class VectorRow
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
        }

        private enum RotationOverrideMode
        {
            Absolute = 0,
            Offset = 1
        }

        private sealed class MeshRotationOverrideSet
        {
            public Dictionary<string, Vector3> MeshRotationByName { get; } =
                new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, Vector3> NodeRotationByMeshAndNode { get; } =
                new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, RotationOverrideMode> MeshRotationModeByName { get; } =
                new Dictionary<string, RotationOverrideMode>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, RotationOverrideMode> NodeRotationModeByMeshAndNode { get; } =
                new Dictionary<string, RotationOverrideMode>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, Vector3> MeshPositionOffsetByName { get; } =
                new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, Vector3> NodePositionOffsetByMeshAndNode { get; } =
                new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class CollisionOverrideFile
        {
            public List<string> DisableCollisionMeshNameContains { get; set; } = new();
            public List<string> DisableCollisionMeshKeys { get; set; } = new();
            public List<int> DisableCollisionStatelIds { get; set; } = new();
            public List<string> ForceCollisionMeshKeys { get; set; } = new();
            public List<int> ForceCollisionStatelIds { get; set; } = new();
        }

        private sealed class CollisionOverrideSet
        {
            public HashSet<string> DisableCollisionMeshNameContains { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public HashSet<string> DisableCollisionMeshKeys { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public HashSet<int> DisableCollisionStatelIds { get; } = new();

            public HashSet<string> ForceCollisionMeshKeys { get; } =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public HashSet<int> ForceCollisionStatelIds { get; } = new();
        }

        private sealed class PlayfieldRoomsFile
        {
            public int PlayfieldId { get; set; }
            public string Name { get; set; }
            public bool IsIndoor { get; set; }
            public int TilemapId { get; set; }
            public List<RoomTemplateData> Rooms { get; set; } = new();
        }

        private sealed class RoomTemplateData
        {
            public string Name { get; set; }
            public int RotationQuarterTurns { get; set; }
            public Vector3Data TemplatePosition { get; set; }
            public Vector3Data Center { get; set; }
            public short TileX1 { get; set; }
            public short TileY1 { get; set; }
            public short TileX2 { get; set; }
            public short TileY2 { get; set; }
        }

        private sealed class PlayfieldRoomSurfacesFile
        {
            public int PlayfieldId { get; set; }
            public string PlayfieldName { get; set; }
            public List<RoomSurfaceRoomData> Rooms { get; set; } = new();
        }

        private sealed class RoomSurfaceRoomData
        {
            public int Instance { get; set; }
            public string Name { get; set; }
            public List<RoomSurfaceMeshData> SurfaceMeshes { get; set; } = new();
        }

        private sealed class RoomFootprintData
        {
            public int Instance { get; set; }
            public Vector3 CenterAo { get; set; }
            public float WidthAo { get; set; }
            public float LengthAo { get; set; }
            public int RotationQuarterTurns { get; set; }
        }

        private sealed class RoomSurfaceMeshData
        {
            public int VertexCount { get; set; }
            public int TriangleIndexCount { get; set; }
            public Vector3Data Position { get; set; }
            public QuaternionData Rotation { get; set; }
            public Vector3Data Scale { get; set; }
            public List<Vector3Data> Vertices { get; set; } = new();
            public List<int> Triangles { get; set; } = new();
        }

        private enum IndoorSurfaceKind
        {
            Floor,
            Wall,
            Ceiling
        }

        private enum AutoPlayfieldOverrideKind
        {
            None = 0,
            Package = 1,
            MonolithGlb = 2,
            IndoorPfGlb = 3
        }

        private sealed class Vector3Data
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
        }

        private sealed class RuntimeWorldObjectsFile
        {
            public int PlayfieldId { get; set; }
            public string PlayfieldName { get; set; }
            public List<RuntimeWorldObjectData> RuntimeWorldObjects { get; set; } = new();
            public List<RuntimeWorldObjectData> Dynels { get; set; } = new();
        }

        private sealed class TeleportDefaultsFile
        {
            public List<TeleportDefaultEntry> Defaults { get; set; } = new();
        }

        private sealed class TeleportDefaultEntry
        {
            public int PlayfieldId { get; set; }
            public float? AoX { get; set; }
            public float? AoY { get; set; }
            public float? AoZ { get; set; }
            public float? X { get; set; }
            public float? Y { get; set; }
            public float? Z { get; set; }
            public float? Heading { get; set; }
            public string Label { get; set; }
        }

        private sealed class RuntimeWorldObjectData
        {
            public string ObjectType { get; set; }
            public string IdentityType { get; set; }
            public int IdentityInstance { get; set; }
            public string DisplayName { get; set; }
            public string Name { get; set; }
            public int? TemplateId { get; set; }
            public string TemplateName { get; set; }
            public int? MeshId { get; set; }
            public int? Mesh { get; set; }
            public int? CATMesh { get; set; }
            public int? DisplayCATMesh { get; set; }
            public int? LowresMesh { get; set; }
            public int? HeadMesh { get; set; }
            public int? HairMesh { get; set; }
            public int? BackMesh { get; set; }
            public int? ShoulderMesh { get; set; }
            public int? WeaponMesh { get; set; }
            public string MeshName { get; set; }
            public string MeshLookupName { get; set; }
            public Vector3Data Position { get; set; }
            public Vector3Data GlobalPosition { get; set; }
            public QuaternionData Rotation { get; set; }
            public float YawDegrees { get; set; }
            public int? RoomInstance { get; set; }
            public string RoomName { get; set; }
            public string ImportKey { get; set; }
            public int? StaticInstance { get; set; }
            public int? BuildingInstance { get; set; }
            public int? AreaInstance { get; set; }
            public int? SourceStaticInstance { get; set; }
            public int? SourceMesh { get; set; }
            public int? SourceBuildingInstance { get; set; }
            public int? SourceAreaInstance { get; set; }
            public int? MonsterData { get; set; }
            public string SourceFlags { get; set; }
            public bool? IsNpc { get; set; }
            public bool? IsPlayer { get; set; }
            public bool? IsPet { get; set; }
            public bool? IsSimpleChar { get; set; }
            public int? ScaleRaw { get; set; }
            public float? ScaleFactor { get; set; }
            public int? MaxHealth { get; set; }
            public int? MaxNano { get; set; }
            public int? Level { get; set; }
            public string Description { get; set; }
        }

        private sealed class PickCaptureRecord
        {
            public string RecordType { get; set; }
            public int PlayfieldId { get; set; }
            public string Note { get; set; }
            public bool IsMark { get; set; }
            public bool HasRaycastHit { get; set; }
            public Vector3Data MouseWorldPosition { get; set; }
            public Vector3Data ProbePosition { get; set; }
            public Vector3Data RaycastHitPosition { get; set; }
            public Vector3Data RaycastHitNormal { get; set; }
            public PickSurfaceData Surface { get; set; }
        }

        private sealed class PickSurfaceData
        {
            public int? RoomInstance { get; set; }
            public string RoomName { get; set; }
            public int? MeshIndex { get; set; }
        }

        private sealed class IndoorRoomPickPatch
        {
            public Vector3 PositionAo { get; set; }
            public Vector3 NormalAo { get; set; } = Vector3.up;
            public bool HasNormal { get; set; }
            public bool IsMark { get; set; }
            public string Note { get; set; }
        }

        private sealed class IndoorRoomPickRoomData
        {
            public List<IndoorRoomPickPatch> PointPatches { get; } = new();
            public List<List<Vector3>> OutlinePolygonsAo { get; } = new();
        }

        private sealed class QuaternionData
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public float W { get; set; }
        }

        private sealed class ModelInfoEntry
        {
            public int TypeId { get; set; }
            public int InstanceId { get; set; }
            public string Name { get; set; }
        }

        private void Awake()
        {
            _bootStartedAt = Time.realtimeSinceStartup;
            _clientLoadPhase = ClientLoadPhase.Boot;
            // Ensure runtime dynel placeholders get replaced by mesh visuals.
            loadRuntimeObjectVisualsFromGlbFallback = true;
            EnsureRuntimeGlbAnimationSafetyCacheLoaded();

            if (showLaunchLogoOnStartup)
                ShowPlayfieldLoadingOverlay();

            Debug.Log(
                $"PrototypeWorldBootstrap Awake authoritativeNetworking={enableAuthoritativeNetworking} " +
                $"host={authoritativeServerHost}:{authoritativeServerPort} prototypeMovement={enablePrototypeMovement} " +
                $"placeWorldInAoCoordinates={placeWorldInAoCoordinates} centeredMode={UseCenteredPlayfieldCoordinates} scale={EffectivePlayfieldCoordinateScale:0.###} " +
                $"autoSelectOverrideByFolder={autoSelectPlayfieldOverrideByFolder} packageOverride={enablePlayfieldPackageOverride} " +
                $"packageOverridePf={playfieldPackageOverridePlayfieldId} glbOverride={enablePlayfieldGlbOverride} glbOverridePf={playfieldGlbOverridePlayfieldId} " +
                $"glbLoadFlags(statels={loadPlayfieldGlbStatels},terrain={loadPlayfieldGlbTerrain},water={loadPlayfieldGlbWater})");
            AODataManager.EnsureInstance();
            _collisionOverrides = LoadCollisionOverrides();
            _activeCollisionOverrides = _collisionOverrides ?? CreateDefaultCollisionOverrideSet();

            EnsureWorldRoot();
            if (createGroundPlane)
                EnsureGroundPlane();
            SpawnRotationDebugMeshIdentity();

            Vector3 playfieldSpawn = spawnPosition;
            bool allowInitialWorldLoad = loadPlayfieldFromJson && !deferInitialWorldLoadUntilCharacterSelection;
            if (allowInitialWorldLoad && TryLoadPlayfieldJson(playfieldId, out var centerWorld))
                playfieldSpawn = ResolveBestSpawnPosition(centerWorld);

            var bridge = existingBridge != null ? existingBridge : ResolvePrimaryPlayerBridgeInScene();
            if (bridge == null)
            {
                var go = new GameObject("PlayerCharacter");
                go.transform.position = spawnCharacterAtPlayfieldCenter ? playfieldSpawn : spawnPosition;
                bridge = go.AddComponent<CharacterRuntimeBridge>();
            }

            var appearance = bridge.GetComponent<CharacterAppearanceController>();
            if (appearance == null)
                appearance = bridge.gameObject.AddComponent<CharacterAppearanceController>();
            appearance.ConfigureCreateVisualIfMissing(createVisualIfMissing);

            if (bridge.GetComponent<EquippedItemVisualController>() == null)
                bridge.gameObject.AddComponent<EquippedItemVisualController>();

            if (bridge.Character == null)
            {
                var profession = new Profession { Name = "Prototype" };
                var character = new Character(characterName, profession, 0, breedId: 1, professionId: 1);
                bridge.SetCharacter(character);
            }

            appearance.PrewarmCurrentVisual();

            if (spawnCharacterAtPlayfieldCenter)
                bridge.transform.position = playfieldSpawn;

            if (enablePrototypeMovement)
            {
                var controller = bridge.GetComponent<PrototypeWalkerController>();
                if (controller == null)
                    controller = bridge.gameObject.AddComponent<PrototypeWalkerController>();

                if (enableAuthoritativeNetworking)
                {
                    var networkClient = bridge.GetComponent<AuthoritativeNetworkClient>();
                    if (networkClient == null)
                    {
                        networkClient = bridge.gameObject.AddComponent<AuthoritativeNetworkClient>();
                        Debug.Log($"PrototypeWorldBootstrap added AuthoritativeNetworkClient to '{bridge.gameObject.name}'.");
                    }
                    else
                    {
                        Debug.Log($"PrototypeWorldBootstrap found existing AuthoritativeNetworkClient on '{bridge.gameObject.name}'.");
                    }
                    networkClient.Configure(
                        authoritativeServerHost,
                        authoritativeServerPort,
                        autoConnect: true,
                        useAuthoritativeMovement: authoritativeMovement);

                    Debug.Log($"PrototypeWorldBootstrap configured AuthoritativeNetworkClient host={authoritativeServerHost}:{authoritativeServerPort} authoritativeMovement={authoritativeMovement}");
                }
                else
                {
                    Debug.Log("PrototypeWorldBootstrap authoritative networking is disabled, so no network client was attached.");
                }
            }
            else if (alignMainCameraToSpawn && Camera.main != null)
            {
                Camera.main.transform.position = bridge.transform.position + cameraOffset;
                Camera.main.transform.LookAt(bridge.transform.position + Vector3.up * cameraLookAtHeight);
            }

            if (enableZoneTransitions && GetComponent<ZoneTransitionManager>() == null)
                gameObject.AddComponent<ZoneTransitionManager>();

            if (showLaunchLogoOnStartup)
            {
                if (pauseGameplayDuringLaunchLogo)
                {
                    _timeScaleBeforeLaunchLogo = Time.timeScale;
                    Time.timeScale = 0f;
                }

                if (_launchLogoRoutine != null)
                    StopCoroutine(_launchLogoRoutine);
                _launchLogoRoutine = StartCoroutine(HideLaunchLogoAfterDelay());
            }
            else
            {
                HidePlayfieldLoadingOverlay(immediate: true);
                _clientLoadPhase = ClientLoadPhase.CharacterFlowReady;
            }
        }

        public void MarkCharacterFlowReady()
        {
            if (_clientLoadPhase == ClientLoadPhase.Boot || _clientLoadPhase == ClientLoadPhase.CharacterFlowReady)
                _clientLoadPhase = ClientLoadPhase.CharacterFlowReady;
            if (_characterFlowReadyAt <= 0f)
            {
                _characterFlowReadyAt = Time.realtimeSinceStartup;
                if (_bootStartedAt > 0f)
                {
                    float ms = (_characterFlowReadyAt - _bootStartedAt) * 1000f;
                    Debug.Log($"[LoadMetrics] Boot->CharacterFlowReady: {ms:0} ms");
                    LogBudgetIfExceeded(
                        "Boot->CharacterFlowReady",
                        ms,
                        Mathf.Max(budgetBootToCharacterFlowReadyMsWarn, 12000f),
                        Mathf.Max(budgetBootToCharacterFlowReadyMsError, 20000f));
                }
            }
        }

        private void BeginEnterWorldLoading()
        {
            _clientLoadPhase = ClientLoadPhase.EnterWorldLoading;
            _enterWorldLoadingStartedAt = Time.realtimeSinceStartup;
            _spawnReadyAt = -1f;
            _spawnReadyLogged = false;
            ShowPlayfieldLoadingOverlay();
            SetPrimaryPlayerLocalMovementEnabled(false);
        }

        private void CompleteEnterWorldLoadingIfReady(Transform characterTransform)
        {
            if (_clientLoadPhase != ClientLoadPhase.EnterWorldLoading)
                return;
            if (_activePlayfieldGlbLoadInProgress)
                return;
            if (_pendingTransitionCharacter != null)
                return;
            if (characterTransform == null)
                return;
            Vector3 p = characterTransform.position;
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
                return;
            if (!_spawnReadyLogged && _enterWorldLoadingStartedAt > 0f)
            {
                _spawnReadyAt = Time.realtimeSinceStartup;
                _spawnReadyLogged = true;
                float ms = (_spawnReadyAt - _enterWorldLoadingStartedAt) * 1000f;
                Debug.Log($"[LoadMetrics] EnterWorldLoading->SpawnReady: {ms:0} ms");
                LogBudgetIfExceeded(
                    "EnterWorldLoading->SpawnReady",
                    ms,
                    Mathf.Max(budgetEnterWorldToSpawnReadyMsWarn, 4000f),
                    Mathf.Max(budgetEnterWorldToSpawnReadyMsError, 8000f));
            }
            if (!IsNearbyRuntimeContentReadyForEnterWorld(p))
                return;

            _clientLoadPhase = ClientLoadPhase.InWorld;
            float nearbyReadyAt = Time.realtimeSinceStartup;
            if (_spawnReadyAt > 0f)
            {
                float ms = (nearbyReadyAt - _spawnReadyAt) * 1000f;
                Debug.Log($"[LoadMetrics] SpawnReady->NearbyContentReady: {ms:0} ms");
                LogBudgetIfExceeded(
                    "SpawnReady->NearbyContentReady",
                    ms,
                    Mathf.Max(budgetSpawnReadyToNearbyContentReadyMsWarn, 3000f),
                    Mathf.Max(budgetSpawnReadyToNearbyContentReadyMsError, 7000f));
            }
            _enterWorldLoadingStartedAt = -1f;
            HidePlayfieldLoadingOverlay(immediate: true);
            SetPrimaryPlayerLocalMovementEnabled(true);
        }

        private void LogBudgetIfExceeded(string metricName, float valueMs, float warnBudgetMs, float errorBudgetMs)
        {
            if (string.IsNullOrWhiteSpace(metricName) || !float.IsFinite(valueMs))
                return;

            float warn = Mathf.Max(0f, warnBudgetMs);
            float err = Mathf.Max(warn, errorBudgetMs);
            if (valueMs >= err)
            {
                _loadBudgetErrorCount++;
                Debug.LogError($"[LoadBudget] {metricName}={valueMs:0}ms exceeded ERROR budget {err:0}ms.");
                return;
            }

            if (valueMs >= warn)
            {
                _loadBudgetWarnCount++;
                Debug.LogWarning($"[LoadBudget] {metricName}={valueMs:0}ms exceeded WARN budget {warn:0}ms.");
            }
        }

        private bool IsNearbyRuntimeContentReadyForEnterWorld(Vector3 worldPosition)
        {
            if (!requireNearbyRuntimeContentReadyBeforeEnterWorld)
                return true;

            float timeout = Mathf.Max(0.1f, enterWorldNearbyContentTimeoutSeconds);
            if (_enterWorldLoadingStartedAt > 0f && (Time.realtimeSinceStartup - _enterWorldLoadingStartedAt) >= timeout)
                return true;

            if (_activePlayfieldRoot == null)
                return true;

            float radius = Mathf.Max(5f, enterWorldNearbyContentRadius);
            float radiusSq = radius * radius;
            int minReady = Mathf.Max(1, enterWorldNearbyContentMinReadyDynels);

            var dynelMeta = _activePlayfieldRoot.GetComponentsInChildren<RuntimeDynelTargetMetadata>(true);
            if (dynelMeta == null || dynelMeta.Length == 0)
                return true;

            int candidates = 0;
            int ready = 0;
            for (int i = 0; i < dynelMeta.Length; i++)
            {
                var meta = dynelMeta[i];
                if (meta == null)
                    continue;

                var t = meta.transform;
                Vector3 delta = t.position - worldPosition;
                delta.y = 0f;
                if (delta.sqrMagnitude > radiusSq)
                    continue;

                candidates++;
                if (IsRuntimeDynelVisualReady(meta.gameObject))
                    ready++;
            }

            // No nearby dynels to validate in this area: allow enter-world.
            if (candidates == 0)
                return true;

            return ready >= Mathf.Min(minReady, candidates);
        }

        private static bool IsRuntimeDynelVisualReady(GameObject host)
        {
            if (host == null)
                return false;

            var renderers = host.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!IsUsableRuntimeRenderer(renderer))
                    continue;
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (renderer.GetComponentInParent<RuntimePlaceholderVisualMarker>() != null)
                    continue;
                return true;
            }

            return false;
        }

        private void SetPrimaryPlayerLocalMovementEnabled(bool enabled)
        {
            var bridge = existingBridge != null ? existingBridge : ResolvePrimaryPlayerBridgeInScene();
            if (bridge == null)
                return;

            var walker = bridge.GetComponent<PrototypeWalkerController>();
            walker?.SetLocalMovementEnabled(enabled);
        }

        private void OnDestroy()
        {
            // Ensure glTF importers are disposed when exiting play mode or unloading scene,
            // otherwise Unity's leak detector can report persistent NativeArray allocations.
            if (_launchLogoRoutine != null)
            {
                StopCoroutine(_launchLogoRoutine);
                _launchLogoRoutine = null;
            }
            if (pauseGameplayDuringLaunchLogo)
                Time.timeScale = _timeScaleBeforeLaunchLogo;
            HidePlayfieldLoadingOverlay(immediate: true);
            if (_playfieldLoadingOverlayRoot != null)
                Destroy(_playfieldLoadingOverlayRoot);
            ClearActivePlayfield();
            _activeCollisionOverrides = CreateDefaultCollisionOverrideSet();
            SaveRuntimeGlbAnimationSafetyCache();
        }

        private string GetRuntimeGlbAnimationSafetyCachePath()
        {
            try
            {
                string dir = Path.Combine(Application.temporaryCachePath, "AOData", "RuntimeGlbPatched");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "runtime_glb_animation_safety_cache.json");
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetRuntimeGlbAnimationSafetyCachePathInStreamingAssets()
        {
            try
            {
                return Path.Combine(Application.streamingAssetsPath, "AOData", "runtime_glb_animation_safety_cache.json");
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetRuntimeGlbSanitizedMapPathInStreamingAssets()
        {
            try
            {
                return Path.Combine(Application.streamingAssetsPath, "AOData", "runtime_glb_sanitized_map.json");
            }
            catch
            {
                return string.Empty;
            }
        }

        private void MergeRuntimeGlbSanitizedMapFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                var map = JsonConvert.DeserializeObject<RuntimeGlbSanitizedMapFile>(File.ReadAllText(path));
                if (map?.Entries == null)
                    return;

                for (int i = 0; i < map.Entries.Count; i++)
                {
                    var entry = map.Entries[i];
                    if (entry == null
                        || string.IsNullOrWhiteSpace(entry.OriginalPath)
                        || string.IsNullOrWhiteSpace(entry.SanitizedPath))
                    {
                        continue;
                    }

                    string original = NormalizeRuntimeGlbPath(entry.OriginalPath);
                    string sanitized = NormalizeRuntimeGlbPath(entry.SanitizedPath);
                    if (!string.IsNullOrWhiteSpace(original) && !string.IsNullOrWhiteSpace(sanitized))
                        _runtimeGlbSanitizedPathByOriginal[original] = sanitized;
                }
            }
            catch
            {
            }
        }

        private void MergeRuntimeGlbAnimationSafetyCacheFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                var cache = JsonConvert.DeserializeObject<RuntimeGlbAnimationSafetyCacheFile>(File.ReadAllText(path));
                if (cache?.DisableAnimationsPaths == null)
                    return;
                for (int i = 0; i < cache.DisableAnimationsPaths.Count; i++)
                {
                    string p = cache.DisableAnimationsPaths[i];
                    if (!string.IsNullOrWhiteSpace(p))
                        _runtimeGlbDisableAnimationsByPath.Add(NormalizeRuntimeGlbPath(p));
                }
            }
            catch
            {
            }
        }

        private void EnsureRuntimeGlbAnimationSafetyCacheLoaded()
        {
            if (_runtimeGlbAnimationSafetyCacheLoaded)
                return;
            _runtimeGlbAnimationSafetyCacheLoaded = true;

            MergeRuntimeGlbAnimationSafetyCacheFile(GetRuntimeGlbAnimationSafetyCachePathInStreamingAssets());
            MergeRuntimeGlbAnimationSafetyCacheFile(GetRuntimeGlbAnimationSafetyCachePath());
            MergeRuntimeGlbSanitizedMapFile(GetRuntimeGlbSanitizedMapPathInStreamingAssets());
        }

        private void SaveRuntimeGlbAnimationSafetyCache()
        {
            try
            {
                string path = GetRuntimeGlbAnimationSafetyCachePath();
                if (string.IsNullOrWhiteSpace(path))
                    return;
                var cache = new RuntimeGlbAnimationSafetyCacheFile
                {
                    DisableAnimationsPaths = _runtimeGlbDisableAnimationsByPath.OrderBy(x => x).ToList()
                };
                File.WriteAllText(path, JsonConvert.SerializeObject(cache, Formatting.Indented));
            }
            catch
            {
            }
        }

        private bool ShouldDisableAnimationsForRuntimeGlbPath(string glbPath)
        {
            string normalized = NormalizeRuntimeGlbPath(glbPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;
            EnsureRuntimeGlbAnimationSafetyCacheLoaded();
            return _runtimeGlbDisableAnimationsByPath.Contains(normalized);
        }

        private void MarkRuntimeGlbDisableAnimations(string glbPath)
        {
            string normalized = NormalizeRuntimeGlbPath(glbPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;
            EnsureRuntimeGlbAnimationSafetyCacheLoaded();
            if (_runtimeGlbDisableAnimationsByPath.Add(normalized))
                SaveRuntimeGlbAnimationSafetyCache();
        }

        private static string NormalizeRuntimeGlbPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch
            {
                return path.Replace('\\', '/');
            }
        }

        private void EnsureWorldRoot()
        {
            var existing = GameObject.Find("WorldRoot");
            if (existing != null)
            {
                _worldRoot = existing.transform;
                return;
            }

            var root = new GameObject("WorldRoot");
            _worldRoot = root.transform;
        }

        private void EnsureGroundPlane()
        {
            if (_worldRoot == null)
                return;

            var existing = _worldRoot.Find("Ground");
            if (existing != null)
                return;

            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.SetParent(_worldRoot, false);
            plane.transform.position = Vector3.zero;
            plane.transform.localScale = new Vector3(20f, 1f, 20f);
        }

        private void SpawnRotationDebugMeshIdentity()
        {
            if (!spawnRotationDebugMeshAtOrigin || _worldRoot == null)
                return;

            string meshName = string.IsNullOrWhiteSpace(rotationDebugMeshName)
                ? "tree_jungle_medium"
                : rotationDebugMeshName;
            string meshKey = SafeGetFileNameWithoutExtension(meshName);
            string resourcePath = $"{meshPrefabResourcesFolder}/{meshKey}";

            var statel = new StatelData
            {
                StatelId = 0,
                Position = rotationDebugSpawnPosition,
                Rotation = Vector3.zero,
                Scale = 1f
            };

            var prefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            var instance = SpawnStatelObject(
                _worldRoot,
                statel,
                meshName,
                Vector3.zero,
                centerAroundOrigin: false,
                positionScale: 1f,
                placeholderScaleMultiplier: 1f,
                allowPrefabLoad: true,
                resourcesFolder: meshPrefabResourcesFolder,
                prefabCache: prefabCache,
                applyGlobalMeshBasisCorrection: applyGlobalMeshBasisCorrection,
                globalMeshBasisCorrectionEuler: globalMeshBasisCorrectionEuler,
                meshPrefabRotationOffsetEuler: meshPrefabRotationOffsetEuler,
                perMeshRotationEuler: Vector3.zero,
                perMeshRotationIsAbsolute: false,
                perMeshPositionOffset: Vector3.zero,
                meshKey: meshKey,
                meshParts: statel.MeshParts,
                applyMeshPartTransforms: false,
                applyMeshPartPosition: false,
                applyMeshPartRotation: false,
                applyMeshPartScale: false,
                convertMeshPartRotationFromAo: false,
                nodeRotationByMeshAndNode: null,
                nodePositionOffsetByMeshAndNode: null,
                applyBuiltInPlacementCorrection: debugMeshApplyBuiltInPlacementCorrection,
                addColliders: false,
                collidersConvex: false,
                collidersIsTrigger: false,
                maxCollidersPerStatel: 0);

            if (instance == null)
            {
                Debug.LogWarning($"Rotation debug mesh failed to spawn for resourcePath={resourcePath}.");
                return;
            }

            instance.name = $"RotationDebug_{meshKey}";
            if (debugMeshExtraEuler != Vector3.zero)
                instance.transform.rotation = instance.transform.rotation * Quaternion.Euler(debugMeshExtraEuler);
            if (Mathf.Abs(debugMeshExtraYOffset) > 0.0001f)
                instance.transform.position += Vector3.up * debugMeshExtraYOffset;

            Debug.Log(
                $"Spawned rotation debug mesh at identity: " +
                $"name={instance.name}, resourcePath={resourcePath}, position={instance.transform.position}, rotation={instance.transform.rotation.eulerAngles}");
        }

        public Transform ResolvePlayerTransform()
        {
            var bridge = existingBridge != null ? existingBridge : ResolvePrimaryPlayerBridgeInScene();
            return bridge != null ? bridge.transform : null;
        }

        private static CharacterRuntimeBridge ResolvePrimaryPlayerBridgeInScene()
        {
            var bridges = FindObjectsByType<CharacterRuntimeBridge>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (bridges == null || bridges.Length == 0)
                return null;

            static bool IsRuntimeDynelBridge(CharacterRuntimeBridge bridge)
            {
                if (bridge == null)
                    return false;

                if (bridge.GetComponent<RuntimeDynelTargetMetadata>() != null)
                    return true;

                string name = bridge.gameObject != null ? bridge.gameObject.name ?? string.Empty : string.Empty;
                return name.StartsWith("Runtime_", StringComparison.OrdinalIgnoreCase);
            }

            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge == null)
                    continue;
                if (IsRuntimeDynelBridge(bridge))
                    continue;
                if (bridge.Character != null)
                    return bridge;
            }

            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge == null)
                    continue;
                if (IsRuntimeDynelBridge(bridge))
                    continue;
                if (bridge.GetComponent<PrototypeWalkerController>() != null)
                    return bridge;
            }

            // If no clear player bridge exists, return null so bootstrap creates a dedicated player object.
            return null;
        }


        private System.Collections.IEnumerator LoadSinglePlayfieldGlbCoroutine(Transform parent, string glbPath, string nodeName, int loadTicket)
        {
            if (parent == null || string.IsNullOrWhiteSpace(glbPath))
                yield break;

            glbPath = GlbDataUriLoadPathResolver.Resolve(glbPath, true);

            var node = new GameObject(nodeName);
            node.transform.SetParent(parent, false);

            object retainedImporter = null;
            Task<bool> loadTask = TryInstantiateGlbWithReflection(
                glbPath,
                node.transform,
                importer => retainedImporter = importer,
                disableAnimations: true);
            while (!loadTask.IsCompleted)
                yield return null;

            bool success = false;
            if (loadTask.Status == TaskStatus.RanToCompletion)
            {
                success = loadTask.Result;
            }
            else if (loadTask.IsFaulted)
            {
                Debug.LogWarning($"Failed to load playfield GLB '{glbPath}': {loadTask.Exception?.GetBaseException().Message}");
            }

            if (!success)
            {
                Destroy(node);
                if (retainedImporter != null)
                    DisposeImporter(retainedImporter);
                Debug.LogWarning($"Failed to instantiate playfield GLB '{glbPath}'.");
            }
            else
            {
                if (loadTicket != _activePlayfieldGlbLoadTicket
                    || parent != _activePlayfieldRoot)
                {
                    Destroy(node);
                    if (retainedImporter != null)
                        DisposeImporter(retainedImporter);
                    yield break;
                }

                if (nodeName.IndexOf("_Terrain_GLB", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int corrected = ApplyGlbTerrainUvCorrection(
                        node.transform,
                        playfieldGlbTerrainFlipUvU,
                        playfieldGlbTerrainFlipUvV,
                        playfieldGlbTerrainSwapUvAxes);
                    Debug.Log(
                        $"Applied GLB terrain UV correction for '{nodeName}': correctedMeshes={corrected}, " +
                        $"flipU={playfieldGlbTerrainFlipUvU}, flipV={playfieldGlbTerrainFlipUvV}, swapUv={playfieldGlbTerrainSwapUvAxes}.");
                }

                if (retainedImporter != null)
                    _activePlayfieldGlbImporters.Add(retainedImporter);
                Debug.Log($"Loaded playfield GLB '{glbPath}'.");
            }
        }

        private void FinalizePlayfieldGlbOverride(Transform parent)
        {
            if (parent == null)
                return;

            if (playfieldGlbUsesAoUnits && !placeWorldInAoCoordinates)
            {
                float s = Mathf.Max(0.0001f, _activeCoordinateScale);
                parent.localScale = new Vector3(s, s, s);
            }

            var renderers = parent.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                Debug.LogWarning("Playfield GLB override loaded no renderers; content may be missing or far from origin.");
                return;
            }

            bool hasBounds = false;
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
                return;

            if (playfieldGlbAutoCenterHorizontally && _activeUseCenteredCoordinates)
            {
                Vector3 shift = new Vector3(bounds.center.x, 0f, bounds.center.z);
                parent.position -= shift;
                bounds.center -= shift;

                // Keep AO/world conversion in sync with GLB recentering.
                // ConvertWorldToAo uses: ao = world/scale + _activeHorizontalCenter
                // so center must be expressed in AO units.
                float scale = Mathf.Max(0.0001f, _activeCoordinateScale);
                _activeHorizontalCenter = new Vector3(
                    shift.x / Mathf.Max(0.0001f, scale),
                    0f,
                    shift.z / Mathf.Max(0.0001f, scale));
                Debug.Log(
                    $"Playfield GLB override recentered: shiftWorld=({shift.x:F3}, {shift.y:F3}, {shift.z:F3}) " +
                    $"-> aoCenter=({_activeHorizontalCenter.x:F3}, {_activeHorizontalCenter.y:F3}, {_activeHorizontalCenter.z:F3})");
            }
            else if (!_activeUseCenteredCoordinates)
            {
                _activeHorizontalCenter = Vector3.zero;
            }

            if (addCollidersForPlayfieldGlbOverride)
            {
                int added = AddPlayfieldGlbMeshColliders(parent);
                Debug.Log($"Playfield GLB override colliders added: {added}.");
            }

            if (playfieldGlbCreateSafetyGround)
                EnsurePlayfieldGlbSafetyGround(parent, bounds);
        }

        private int AddPlayfieldGlbMeshColliders(Transform root)
        {
            if (root == null)
                return 0;

            int added = 0;
            int triangleLimit = Mathf.Max(1024, maxPlayfieldGlbColliderTriangles);
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null)
                    continue;

                var go = mf.gameObject;
                if (go == null)
                    continue;

                if (go.GetComponent<Collider>() != null)
                    continue;

                int triangleCount = mf.sharedMesh.triangles?.Length ?? 0;
                if (triangleCount <= 0 || triangleCount > triangleLimit)
                    continue;

                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                added++;
            }

            return added;
        }

        private static int ApplyGlbTerrainUvCorrection(Transform root, bool flipU, bool flipV, bool swapUvAxes)
        {
            if (root == null || (!flipU && !flipV && !swapUvAxes))
                return 0;

            int corrected = 0;
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null)
                    continue;

                var src = mf.sharedMesh;
                var srcUvs = src.uv;
                if (srcUvs == null || srcUvs.Length == 0)
                    continue;

                var mesh = UnityEngine.Object.Instantiate(src);
                var uvs = mesh.uv;
                for (int u = 0; u < uvs.Length; u++)
                {
                    float ux = uvs[u].x;
                    float vy = uvs[u].y;
                    if (swapUvAxes)
                    {
                        float t = ux;
                        ux = vy;
                        vy = t;
                    }

                    if (flipU)
                        ux = 1f - ux;
                    if (flipV)
                        vy = 1f - vy;

                    uvs[u] = new Vector2(ux, vy);
                }

                mesh.uv = uvs;
                mf.sharedMesh = mesh;
                corrected++;
            }

            return corrected;
        }

        private void EnsurePlayfieldGlbSafetyGround(Transform parent, Bounds bounds)
        {
            if (parent == null)
                return;

            float padding = Mathf.Max(0f, playfieldGlbSafetyGroundPadding);
            float thickness = Mathf.Max(0.25f, playfieldGlbSafetyGroundThickness);
            float sizeX = Mathf.Max(4f, bounds.size.x + (padding * 2f));
            float sizeZ = Mathf.Max(4f, bounds.size.z + (padding * 2f));
            float y = bounds.min.y - (thickness * 0.5f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "PF_GLB_SafetyGround";
            ground.transform.SetParent(parent, true);
            ground.transform.position = new Vector3(bounds.center.x, y, bounds.center.z);
            ground.transform.localScale = new Vector3(sizeX, thickness, sizeZ);

            var mr = ground.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = false;
        }

        internal static async Task<bool> TryInstantiateGlbWithReflection(
            string fullPath,
            Transform parent,
            Action<object> onImporterLoaded = null,
            bool disableAnimations = true)
        {
            fullPath = GlbDataUriLoadPathResolver.Resolve(fullPath, true);
            if (string.IsNullOrWhiteSpace(fullPath) || parent == null || !File.Exists(fullPath))
                return false;

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
                string uriPath = fullPath.Replace("\\", "/");
                if (!uriPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    uriPath = $"file:///{uriPath}";

                var loadFileMethod = gltfType.GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "LoadFile"
                        && m.GetParameters().Length >= 1
                        && m.GetParameters()[0].ParameterType == typeof(string));
                if (loadFileMethod != null)
                {
                    var args = BuildLoadArgs(loadFileMethod.GetParameters(), fullPath, importSettingsType, importSettings);
                    var taskObj = loadFileMethod.Invoke(importer, args);
                    if (!await AwaitBoolTask(taskObj))
                        return false;
                    bool instantiated = await InstantiateMainScene(importer, parent);
                    if (!instantiated)
                        return false;
                    onImporterLoaded?.Invoke(importer);
                    handedOff = true;
                    return true;
                }

                var loadUriMethod = gltfType.GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "Load"
                        && m.GetParameters().Length >= 1
                        && m.GetParameters()[0].ParameterType == typeof(Uri));
                if (loadUriMethod != null)
                {
                    var uri = new Uri(uriPath);
                    var args = BuildLoadArgs(loadUriMethod.GetParameters(), uri, importSettingsType, importSettings);
                    var taskObj = loadUriMethod.Invoke(importer, args);
                    if (!await AwaitBoolTask(taskObj))
                        return false;
                    bool instantiated = await InstantiateMainScene(importer, parent);
                    if (!instantiated)
                        return false;
                    onImporterLoaded?.Invoke(importer);
                    handedOff = true;
                    return true;
                }

                var loadStringMethod = gltfType.GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "Load"
                        && m.GetParameters().Length >= 1
                        && m.GetParameters()[0].ParameterType == typeof(string));
                if (loadStringMethod != null)
                {
                    var args = BuildLoadArgs(loadStringMethod.GetParameters(), uriPath, importSettingsType, importSettings);
                    var taskObj = loadStringMethod.Invoke(importer, args);
                    if (!await AwaitBoolTask(taskObj))
                    {
                        args = BuildLoadArgs(loadStringMethod.GetParameters(), fullPath, importSettingsType, importSettings);
                        taskObj = loadStringMethod.Invoke(importer, args);
                        if (!await AwaitBoolTask(taskObj))
                            return false;
                    }

                    bool instantiated = await InstantiateMainScene(importer, parent);
                    if (!instantiated)
                        return false;
                    onImporterLoaded?.Invoke(importer);
                    handedOff = true;
                    return true;
                }

                return false;
            }
            finally
            {
                if (!handedOff)
                    DisposeImporter(importer);
            }
        }

        private static async Task<bool> InstantiateMainScene(object importer, Transform parent)
        {
            var importerType = importer.GetType();

            var asyncMainSceneMethod = importerType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "InstantiateMainSceneAsync"
                    && m.GetParameters().Length >= 1
                    && typeof(Transform).IsAssignableFrom(m.GetParameters()[0].ParameterType));
            if (asyncMainSceneMethod != null)
            {
                var asyncMainArgs = BuildInstantiateArgs(asyncMainSceneMethod.GetParameters(), parent);
                return await AwaitBoolTask(asyncMainSceneMethod.Invoke(importer, asyncMainArgs));
            }

            var asyncSceneMethod = importerType.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "InstantiateSceneAsync"
                    && m.GetParameters().Length >= 1);
            if (asyncSceneMethod != null)
            {
                var asyncSceneArgs = BuildSceneInstantiateArgs(asyncSceneMethod.GetParameters(), parent);
                return await AwaitBoolTask(asyncSceneMethod.Invoke(importer, asyncSceneArgs));
            }

            var instantiateMethod = importerType.GetMethods()
                .FirstOrDefault(m =>
                    (m.Name == "InstantiateMainScene" || m.Name == "InstantiateScene")
                    && m.GetParameters().Length >= 1
                    && typeof(Transform).IsAssignableFrom(m.GetParameters()[0].ParameterType));
            if (instantiateMethod == null)
                return false;

            var instantiateArgs = BuildInstantiateArgs(instantiateMethod.GetParameters(), parent);
            object result = instantiateMethod.Invoke(importer, instantiateArgs);
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

        private static object[] BuildSceneInstantiateArgs(ParameterInfo[] parameters, Transform parent)
        {
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (i == 0 && p.ParameterType == typeof(int))
                {
                    args[i] = 0;
                    continue;
                }

                if (typeof(Transform).IsAssignableFrom(p.ParameterType))
                {
                    args[i] = parent;
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
                    args[i] = p.DefaultValue;
                else if (p.ParameterType.IsValueType)
                    args[i] = Activator.CreateInstance(p.ParameterType);
                else
                    args[i] = null;
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
                    args[i] = importSettings;
                else if (p.HasDefaultValue)
                    args[i] = p.DefaultValue;
                else if (p.ParameterType.IsValueType)
                    args[i] = Activator.CreateInstance(p.ParameterType);
                else
                    args[i] = null;
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

        internal static void DisposeImporter(object importer)
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

        private bool TryLoadPlayfieldObjectsFromPackageFolder(
            int pf,
            out PlayfieldData parsed,
            out Dictionary<uint, string> statelMeshMap)
        {
            parsed = null;
            statelMeshMap = null;
            if (!IsPlayfieldPackageOverrideEnabled(pf))
                return false;

            string packageFolder = ResolvePlayfieldPackageFolderPath(pf);
            string objectsPath = Path.Combine(packageFolder, "playfield_objects.json");
            if (!File.Exists(objectsPath))
                return false;

            PlayfieldPackageObjectsFile packageObjects;
            try
            {
                packageObjects = JsonConvert.DeserializeObject<PlayfieldPackageObjectsFile>(File.ReadAllText(objectsPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed parsing playfield package objects {objectsPath}: {ex.Message}");
                return false;
            }

            if (packageObjects?.Objects == null || packageObjects.Objects.Count == 0)
                return false;

            parsed = new PlayfieldData
            {
                PlayfieldId = packageObjects.PlayfieldId > 0 ? packageObjects.PlayfieldId : pf,
                Name = $"PF_{pf}_Package"
            };
            statelMeshMap = new Dictionary<uint, string>();
            var meshNameByMeshId = LoadPlayfieldPackageMeshNameById(packageFolder);

            for (int i = 0; i < packageObjects.Objects.Count; i++)
            {
                var obj = packageObjects.Objects[i];
                if (obj?.Position == null)
                    continue;

                int meshId = obj.MeshId.GetValueOrDefault();
                int statelId = meshId > 0
                    ? meshId
                    : (obj.TemplateId > 0 ? obj.TemplateId : obj.ObjectId);
                if (statelId <= 0)
                    continue;

                var statel = new StatelData
                {
                    StatelId = statelId,
                    Position = new Vector3(obj.Position.X, obj.Position.Y, obj.Position.Z),
                    Rotation = ConvertPackageRotationToEuler(obj.RotationQuaternion),
                    Scale = ConvertPackageScaleToUniform(obj.Scale),
                    ScaleVector = ConvertPackageScaleToVector(obj.Scale),
                    MeshParts = ConvertPackageMeshParts(obj.MeshParts)
                };

                if (!IsFiniteVector3(statel.Position)
                    || !IsFiniteVector3(statel.Rotation)
                    || !float.IsFinite(statel.Scale)
                    || !IsFiniteVector3(statel.ScaleVector))
                    continue;

                parsed.Statels.Add(statel);
                string resolvedMeshName = obj.Name;
                if (string.IsNullOrWhiteSpace(resolvedMeshName) && meshId > 0)
                    meshNameByMeshId.TryGetValue(meshId, out resolvedMeshName);
                if (!string.IsNullOrWhiteSpace(resolvedMeshName))
                    statelMeshMap[(uint)statelId] = SanitizePathLikeString(resolvedMeshName);
            }

            return parsed.Statels.Count > 0;
        }

        private Dictionary<int, string> LoadPlayfieldPackageMeshNameById(string packageFolder)
        {
            var map = new Dictionary<int, string>();
            if (string.IsNullOrWhiteSpace(packageFolder))
                return map;

            string assetMapPath = Path.Combine(packageFolder, "asset_map.json");
            if (!File.Exists(assetMapPath))
                return map;

            try
            {
                var assetMap = JsonConvert.DeserializeObject<PlayfieldPackageAssetMapFile>(File.ReadAllText(assetMapPath));
                if (assetMap?.Meshes == null || assetMap.Meshes.Count == 0)
                    return map;

                for (int i = 0; i < assetMap.Meshes.Count; i++)
                {
                    var entry = assetMap.Meshes[i];
                    if (entry == null || entry.MeshId <= 0)
                        continue;

                    string meshName = !string.IsNullOrWhiteSpace(entry.Name)
                        ? entry.Name
                        : entry.FileName;
                    if (string.IsNullOrWhiteSpace(meshName))
                        continue;

                    map[entry.MeshId] = SanitizePathLikeString(meshName);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to parse package asset_map.json {assetMapPath}: {ex.Message}");
            }

            return map;
        }

        private static Vector3 ConvertPackageRotationToEuler(QuaternionData rotationQuaternion)
        {
            if (rotationQuaternion == null)
                return Vector3.zero;

            var q = new Quaternion(
                rotationQuaternion.X,
                rotationQuaternion.Y,
                rotationQuaternion.Z,
                rotationQuaternion.W);

            float magnitudeSq =
                (q.x * q.x) +
                (q.y * q.y) +
                (q.z * q.z) +
                (q.w * q.w);
            if (magnitudeSq < 0.000001f)
                return Vector3.zero;

            return q.eulerAngles;
        }

        private static float ConvertPackageScaleToUniform(Vector3Data scale)
        {
            if (scale == null)
                return 1f;

            float sx = float.IsFinite(scale.X) ? Mathf.Abs(scale.X) : 1f;
            float sy = float.IsFinite(scale.Y) ? Mathf.Abs(scale.Y) : 1f;
            float sz = float.IsFinite(scale.Z) ? Mathf.Abs(scale.Z) : 1f;
            float avg = (sx + sy + sz) / 3f;
            return avg > 0.0001f ? avg : 1f;
        }

        private static Vector3 ConvertPackageScaleToVector(Vector3Data scale)
        {
            if (scale == null)
                return Vector3.one;

            float sx = float.IsFinite(scale.X) ? Mathf.Abs(scale.X) : 1f;
            float sy = float.IsFinite(scale.Y) ? Mathf.Abs(scale.Y) : 1f;
            float sz = float.IsFinite(scale.Z) ? Mathf.Abs(scale.Z) : 1f;

            sx = sx > 0.0001f ? sx : 1f;
            sy = sy > 0.0001f ? sy : 1f;
            sz = sz > 0.0001f ? sz : 1f;
            return new Vector3(sx, sy, sz);
        }

        private static List<StatelMeshPartData> ConvertPackageMeshParts(List<PlayfieldPackageMeshPartData> parts)
        {
            var result = new List<StatelMeshPartData>();
            if (parts == null || parts.Count == 0)
                return result;

            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null)
                    continue;

                Vector3 position = part.Position != null
                    ? new Vector3(part.Position.X, part.Position.Y, part.Position.Z)
                    : Vector3.zero;

                Quaternion rotation = part.RotationQuaternion != null
                    ? new Quaternion(part.RotationQuaternion.X, part.RotationQuaternion.Y, part.RotationQuaternion.Z, part.RotationQuaternion.W)
                    : Quaternion.identity;
                float rotationMagnitudeSq =
                    (rotation.x * rotation.x) +
                    (rotation.y * rotation.y) +
                    (rotation.z * rotation.z) +
                    (rotation.w * rotation.w);
                if (rotationMagnitudeSq < 0.000001f || !float.IsFinite(rotation.x) || !float.IsFinite(rotation.y) || !float.IsFinite(rotation.z) || !float.IsFinite(rotation.w))
                    rotation = Quaternion.identity;
                else
                    rotation = Quaternion.Normalize(rotation);

                Vector3 scale = ConvertPackageScaleToVector(part.Scale);
                if (!IsFiniteVector3(position) || !IsFiniteVector3(scale))
                    continue;

                result.Add(new StatelMeshPartData
                {
                    SubMeshIndex = part.SubMeshIndex,
                    Position = position,
                    Rotation = rotation,
                    Scale = scale
                });
            }

            return result;
        }

        private bool TryLoadPlayfieldJson(int pf, out Vector3 centerWorld)
        {
            centerWorld = Vector3.zero;
            _activePlayfieldLoadedFromPackage = false;
            bool useCenteredCoordinates = UseCenteredPlayfieldCoordinates;
            float positionScale = EffectivePlayfieldCoordinateScale;
            _activeUseCenteredCoordinates = useCenteredCoordinates;
            _activeCoordinateScale = positionScale;

            if (TryStartPlayfieldGlbOverrideLoad(pf, out centerWorld))
            {
                Debug.Log($"Using playfield GLB override for PF {pf}.");
                return true;
            }
            if (IsPlayfieldGlbStrictMode(pf))
                return false;

            if (TryStartDirectOutdoorPlayfieldLoad(pf, out centerWorld))
                return true;

            PlayfieldData parsed;
            string file = Path.Combine(Application.streamingAssetsPath, playfieldsSubfolder, $"{pf}.json");
            if (TryLoadPlayfieldObjectsFromPackageFolder(pf, out var packageParsed, out var packageMeshMap))
            {
                parsed = packageParsed;
                _packageStatelMeshMapByPlayfield[pf] = packageMeshMap;
                _activePlayfieldLoadedFromPackage = true;
                Debug.Log(
                    $"Using playfield package objects for PF {pf}: " +
                    $"statels={parsed?.Statels?.Count ?? 0}, meshMapEntries={packageMeshMap?.Count ?? 0}.");
            }
            else
            {
                if (IsPlayfieldPackageStrictMode(pf))
                {
                    string packageObjectsPath = Path.Combine(ResolvePlayfieldPackageFolderPath(pf), "playfield_objects.json");
                    Debug.LogError(
                        $"Playfield package strict mode is enabled for PF {pf}, but package objects could not be loaded: {packageObjectsPath}");
                    return false;
                }

                _packageStatelMeshMapByPlayfield.Remove(pf);
                if (!File.Exists(file))
                {
                    if (TryLoadIndoorRoomFootprintsFromAOInstall(pf, out centerWorld))
                        return true;
                    Debug.LogWarning($"Playfield JSON not found: {file}");
                    return false;
                }

                try
                {
                    parsed = JsonConvert.DeserializeObject<PlayfieldData>(File.ReadAllText(file));
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to parse playfield JSON {file}: {ex.Message}");
                    return false;
                }
            }

            if (testUseAoSharpRuntimeObjectsOnly)
            {
                var pfRootRuntimeOnly = new GameObject($"PF_{pf}_{parsed?.Name ?? "Unknown"}");
                pfRootRuntimeOnly.transform.SetParent(_worldRoot, false);

                var runtimeOnlyPrefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
                var runtimeOnlyMeshRotationOverrides = LoadMeshRotationOverrides(pf);
                Vector3 runtimeHorizontalCenter = Vector3.zero;

                _activePlayfieldRoot = pfRootRuntimeOnly.transform;
                _activePlayfieldId = pf;
                _activeHorizontalCenter = runtimeHorizontalCenter;
                _activePlayfieldUsesIndoorRoomSurfaces = false;
                _activeRuntimePrefabCache = runtimeOnlyPrefabCache;
                _activeMeshRotationOverrides = runtimeOnlyMeshRotationOverrides ?? new MeshRotationOverrideSet();

                if (enableAuthoritativeNetworking)
                {
                    centerWorld = Vector3.zero;
                    Debug.Log(
                        $"AOSharp runtime-only test mode local dynel loading is disabled for PF {pf} because authoritative networking is enabled.");
                    return true;
                }

                bool runtimeLoaded = TryLoadRuntimeWorldObjectsJson(
                    pf,
                    pfRootRuntimeOnly.transform,
                    runtimeHorizontalCenter,
                    useCenteredCoordinates,
                    positionScale,
                    runtimeOnlyPrefabCache,
                    runtimeOnlyMeshRotationOverrides);

                centerWorld = Vector3.zero;
                Debug.Log(
                    $"AOSharp runtime-only test mode for playfield {pf}: " +
                    $"runtimeLoaded={runtimeLoaded}. Skipping static statels/terrain/water/indoor imports when runtime objects load.");

                if (runtimeLoaded)
                    return true;

                Debug.LogWarning(
                    $"AOSharp runtime-only test mode for playfield {pf} found no runtime objects. " +
                    "Falling back to normal static playfield loading.");
            }

            if (parsed?.Statels == null || parsed.Statels.Count == 0)
            {
                Debug.LogWarning($"No statels found in playfield JSON: {file}");
                return false;
            }

            var pfRoot = new GameObject($"PF_{pf}_{parsed.Name ?? "Unknown"}");
            pfRoot.transform.SetParent(_worldRoot, false);
            var statelMeshNames = LoadStatelMeshMap(pf);
            var meshRotationOverrides = LoadMeshRotationOverrides(pf);
            var prefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

            var validStatels = new List<StatelData>(parsed.Statels.Count);
            Vector3 sourceCenter = Vector3.zero;
            float safeMaxStatelAbsoluteCoordinate = maxStatelAbsoluteCoordinate > 1f
                ? maxStatelAbsoluteCoordinate
                : 100000f;
            if (maxStatelAbsoluteCoordinate <= 1f)
            {
                Debug.LogWarning(
                    $"PrototypeWorldBootstrap maxStatelAbsoluteCoordinate was {maxStatelAbsoluteCoordinate}, " +
                    $"using safe fallback {safeMaxStatelAbsoluteCoordinate}.");
            }

            for (int i = 0; i < parsed.Statels.Count; i++)
            {
                var statel = parsed.Statels[i];
                if (!IsValidStatelTransform(statel, safeMaxStatelAbsoluteCoordinate))
                    continue;

                validStatels.Add(statel);
                sourceCenter += statel.Position;
            }

            if (validStatels.Count == 0)
            {
                Debug.LogWarning($"No valid statels found in playfield JSON: {file}");
                return false;
            }

            sourceCenter /= validStatels.Count;

            // Keep AO height values intact; only recentre horizontally.
            var horizontalCenter = new Vector3(sourceCenter.x, 0f, sourceCenter.z);
            bool hasRoomSurfaceHorizontalCenter = false;
            Vector3 roomSurfaceHorizontalCenter = Vector3.zero;
            if (loadIndoorRoomSurfacesFromJson
                && TryEstimateIndoorRoomSurfacesHorizontalCenter(pf, out roomSurfaceHorizontalCenter))
            {
                hasRoomSurfaceHorizontalCenter = true;
                float centerDeltaSqr = (new Vector2(horizontalCenter.x, horizontalCenter.z)
                    - new Vector2(roomSurfaceHorizontalCenter.x, roomSurfaceHorizontalCenter.z)).sqrMagnitude;
                const float maxAcceptedCenterDeltaAo = 80f;
                if (centerDeltaSqr > maxAcceptedCenterDeltaAo * maxAcceptedCenterDeltaAo)
                {
                    Debug.LogWarning(
                        $"Playfield {pf} horizontal center mismatch between statels and room surfaces " +
                        $"(deltaAo={Mathf.Sqrt(centerDeltaSqr):F2}). Using room-surface center.");
                    horizontalCenter = roomSurfaceHorizontalCenter;
                }
            }

            // Some indoor exports (for example certain dungeons) can include corrupted/fallback statels
            // far from the actual room-surface center. Cull those so they don't appear in the distance.
            if (hasRoomSurfaceHorizontalCenter
                && indoorStatelCullRadiusAo > 0.01f
                && validStatels.Count > 0)
            {
                float radiusSq = indoorStatelCullRadiusAo * indoorStatelCullRadiusAo;
                int beforeCull = validStatels.Count;
                validStatels = validStatels
                    .Where(s =>
                    {
                        float dx = s.Position.x - horizontalCenter.x;
                        float dz = s.Position.z - horizontalCenter.z;
                        float distSq = (dx * dx) + (dz * dz);
                        return distSq <= radiusSq;
                    })
                    .ToList();

                int culled = beforeCull - validStatels.Count;
                if (culled > 0)
                {
                    Debug.Log(
                        $"Playfield {pf}: culled {culled} distant statels using indoor radius {indoorStatelCullRadiusAo:0.##} AO units.");
                }
            }

            centerWorld = useCenteredCoordinates
                ? Vector3.zero
                : horizontalCenter * positionScale;

            _activePlayfieldRoot = pfRoot.transform;
            _activePlayfieldId = pf;
            _activeHorizontalCenter = horizontalCenter;
            _activePlayfieldUsesIndoorRoomSurfaces = false;
            _activeRuntimePrefabCache = prefabCache;
            _activeMeshRotationOverrides = meshRotationOverrides ?? new MeshRotationOverrideSet();

            if (loadTerrainFromJson)
            {
                bool terrainLoaded = TryLoadTerrainJson(
                    pf,
                    pfRoot.transform,
                    horizontalCenter,
                    useCenteredCoordinates,
                    positionScale);

                if (!terrainLoaded)
                {
                    TryCreatePlayfieldFallbackGround(
                        pf,
                        pfRoot.transform,
                        validStatels,
                        horizontalCenter,
                        useCenteredCoordinates,
                        positionScale);
                }
            }

            if (loadWaterFromJson)
            {
                TryLoadWaterJson(
                    pf,
                    pfRoot.transform,
                    horizontalCenter,
                    useCenteredCoordinates,
                    positionScale);
            }

            // In authoritative mode, runtime dynels must come from server snapshots only.
            // Local runtime JSON spawning can create delayed duplicate waves as deferred queue drains.
            bool shouldLoadRuntimeWorldObjectsFromJson = loadRuntimeWorldObjectsFromJson
                && !enableAuthoritativeNetworking;
            if (shouldLoadRuntimeWorldObjectsFromJson)
            {
                TryLoadRuntimeWorldObjectsJson(
                    pf,
                    pfRoot.transform,
                    horizontalCenter,
                    useCenteredCoordinates,
                    positionScale,
                    prefabCache,
                    meshRotationOverrides);
            }
            else if (!loadRuntimeWorldObjectsFromJson)
            {
                Debug.Log($"Skipping local runtime world object JSON for playfield {pf} because loadRuntimeWorldObjectsFromJson is false.");
            }
            else if (loadRuntimeWorldObjectsFromJson && enableAuthoritativeNetworking)
            {
                Debug.Log(
                    $"Skipping local runtime world object JSON for playfield {pf} because authoritative networking is enabled. " +
                    "Runtime dynels are server authoritative in this mode.");
            }

            bool loadedRoomSurfaces = false;
            if (loadIndoorRoomSurfacesFromJson && !(_activePlayfieldLoadedFromPackage && IsPlayfieldPackageStrictMode(pf)))
            {
                loadedRoomSurfaces = TryLoadIndoorRoomSurfacesJson(
                    pf,
                    pfRoot.transform,
                    horizontalCenter,
                    useCenteredCoordinates,
                    positionScale);
                _activePlayfieldUsesIndoorRoomSurfaces = loadedRoomSurfaces;
            }

            if (loadIndoorRoomFallbackFromJson && !(_activePlayfieldLoadedFromPackage && IsPlayfieldPackageStrictMode(pf)))
            {
                bool buildIndoorHelperFloors = !loadedRoomSurfaces;
                if (TryLoadIndoorRoomsJson(
                    pf,
                    pfRoot.transform,
                    horizontalCenter,
                    useCenteredCoordinates,
                    positionScale,
                    buildIndoorHelperFloors,
                    out var roomCenterWorld))
                {
                    centerWorld = roomCenterWorld;
                }
            }

            int total = validStatels.Count;
            int spawnCount = (maxStatelsToSpawn <= 0 || maxStatelsToSpawn > total)
                ? total
                : maxStatelsToSpawn;
            int mappedNames = 0;
            int prefabResolvable = 0;
            int fallbackNoName = 0;
            int fallbackMissingPrefab = 0;
            int skippedByPlaceholderPolicy = 0;
            int missingLogged = 0;
            var missingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool applyPackageMeshParts = false;

            var staticEntries = new List<PendingStaticStatelSpawnEntry>(spawnCount);
            for (int i = 0; i < spawnCount; i++)
            {
                int sourceIndex = spawnCount == total
                    ? i
                    : Mathf.RoundToInt(i * (total - 1f) / Mathf.Max(1f, spawnCount - 1f));

                var statel = validStatels[sourceIndex];
                string meshName = null;
                if (statel.StatelId > 0)
                    statelMeshNames.TryGetValue((uint)statel.StatelId, out meshName);

                if (string.IsNullOrWhiteSpace(meshName))
                {
                    fallbackNoName++;
                }
                else
                {
                    mappedNames++;
                    if (loadMeshPrefabsFromResources)
                    {
                        string key = SafeGetFileNameWithoutExtension(meshName);
                        if (!ShouldSkipGenericMeshPlaceholder(key))
                        {
                            if (!prefabCache.TryGetValue(key, out var prefab))
                            {
                                prefab = Resources.Load<GameObject>($"{meshPrefabResourcesFolder}/{key}");
                                if (prefab == null && key.StartsWith("mesh_", StringComparison.OrdinalIgnoreCase)
                                    && int.TryParse(key.Substring(5), out int meshId) && meshId > 0)
                                {
                                    string unnamedKey = $"Unnamed_{meshId}";
                                    prefab = Resources.Load<GameObject>($"{meshPrefabResourcesFolder}/{unnamedKey}");
                                    if (prefab != null)
                                        key = unnamedKey;
                                }

                                prefabCache[key] = prefab;
                            }

                            if (prefab != null)
                            {
                                prefabResolvable++;
                            }
                            else
                            {
                                fallbackMissingPrefab++;
                                if (logMissingMeshPrefabs
                                    && missingLogged < Mathf.Max(0, maxMissingMeshPrefabLogs)
                                    && missingKeys.Add(key))
                                {
                                    missingLogged++;
                                    Debug.LogWarning($"Missing mesh prefab in Resources: {meshPrefabResourcesFolder}/{key}");
                                }
                            }
                        }
                    }
                }

                Vector3 perMeshRotationEuler = Vector3.zero;
                bool perMeshRotationIsAbsolute = false;
                Vector3 perMeshPositionOffset = Vector3.zero;
                string meshKey = null;
                if (!string.IsNullOrWhiteSpace(meshName))
                {
                    meshKey = SafeGetFileNameWithoutExtension(meshName);
                    if (!TryGetMeshVectorOverride(meshRotationOverrides.MeshRotationByName, meshKey, out perMeshRotationEuler))
                    {
                        if (enableBuiltInMeshRotationOverrides)
                        {
                            if (TryGetBuiltInMeshRotationOverride(meshKey, out perMeshRotationEuler))
                                perMeshRotationIsAbsolute = false;
                        }
                    }
                    else
                    {
                        perMeshRotationIsAbsolute = TryGetMeshRotationMode(
                            meshRotationOverrides.MeshRotationModeByName,
                            meshKey,
                            out var resolvedMode)
                            ? resolvedMode == RotationOverrideMode.Absolute
                            : true;
                    }
                    TryGetMeshVectorOverride(
                        meshRotationOverrides.MeshPositionOffsetByName,
                        meshKey,
                        out perMeshPositionOffset);

                    if (ShouldSkipGenericMeshPlaceholder(meshKey))
                    {
                        skippedByPlaceholderPolicy++;
                        continue;
                    }
                }

                staticEntries.Add(new PendingStaticStatelSpawnEntry
                {
                    Statel = statel,
                    MeshName = meshName,
                    MeshKey = meshKey,
                    PerMeshRotationEuler = perMeshRotationEuler,
                    PerMeshRotationIsAbsolute = perMeshRotationIsAbsolute,
                    PerMeshPositionOffset = perMeshPositionOffset
                });
            }

            int spawnedNow = 0;
            int seedCount = deferStaticStatelSpawn
                ? Mathf.Clamp(staticStatelImmediateSpawnSeedCount, 0, staticEntries.Count)
                : staticEntries.Count;
            for (int i = 0; i < seedCount; i++)
            {
                if (SpawnPreparedStaticStatel(
                    staticEntries[i],
                    pfRoot.transform,
                    horizontalCenter,
                    useCenteredCoordinates,
                    positionScale,
                    prefabCache,
                    applyPackageMeshParts,
                    meshRotationOverrides,
                    pf))
                {
                    spawnedNow++;
                }
            }

            if (_staticStatelBackgroundSpawnCoroutine != null)
            {
                StopCoroutine(_staticStatelBackgroundSpawnCoroutine);
                _staticStatelBackgroundSpawnCoroutine = null;
            }

            int deferredCount = staticEntries.Count - seedCount;
            if (deferStaticStatelSpawn && deferredCount > 0)
            {
                _staticDeferredQueueCount = deferredCount;
                _staticStatelBackgroundSpawnCoroutine = StartCoroutine(SpawnDeferredStaticStatelsCoroutine(
                    staticEntries,
                    seedCount,
                    pfRoot.transform,
                    horizontalCenter,
                    useCenteredCoordinates,
                    positionScale,
                    prefabCache,
                    applyPackageMeshParts,
                    meshRotationOverrides,
                    pf));
            }
            else
            {
                _staticDeferredQueueCount = 0;
            }

            Debug.Log(
                $"Loaded playfield {pf}: spawnedNow={spawnedNow}/{spawnCount} valid statels " +
                $"(raw={parsed.Statels.Count}). " +
                $"DeferredStatic={Mathf.Max(0, deferredCount)}. " +
                $"MappedNames={mappedNames}, PrefabResolvable={prefabResolvable}, " +
                $"FallbackNoName={fallbackNoName}, FallbackMissingPrefab={fallbackMissingPrefab}, " +
                $"SkippedByPlaceholderPolicy={skippedByPlaceholderPolicy}.");
            return true;
        }

        private bool TryLoadIndoorRoomsJson(
            int pf,
            Transform parent,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            bool buildRoomFloors,
            out Vector3 preferredSpawnWorld)
        {
            preferredSpawnWorld = Vector3.zero;
            string roomsPath = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{roomJsonSuffix}");

            if (!File.Exists(roomsPath))
                return false;

            PlayfieldRoomsFile roomsFile;
            try
            {
                roomsFile = JsonConvert.DeserializeObject<PlayfieldRoomsFile>(File.ReadAllText(roomsPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed parsing rooms JSON {roomsPath}: {ex.Message}");
                return false;
            }

            if (roomsFile?.Rooms == null || roomsFile.Rooms.Count == 0 || !roomsFile.IsIndoor)
                return false;

            GameObject roomsRoot = null;
            Material roomMaterial = null;
            if (buildRoomFloors)
            {
                roomsRoot = new GameObject($"PF_{pf}_Rooms");
                roomsRoot.transform.SetParent(parent, false);
                roomMaterial = BuildIndoorRoomFloorMaterial();
            }

            int built = 0;
            bool hasPreferredSpawn = false;
            Vector3 preferredRoomCenterWorld = Vector3.zero;
            Vector3 firstRoomCenterWorld = Vector3.zero;
            foreach (var room in roomsFile.Rooms)
            {
                if (room?.TemplatePosition == null)
                    continue;

                float widthAo = Mathf.Max(2f, (room.TileX2 - room.TileX1) * 2f);
                float lengthAo = Mathf.Max(2f, (room.TileY2 - room.TileY1) * 2f);
                Vector3 roomOriginAo = new Vector3(room.TemplatePosition.X, room.TemplatePosition.Y, room.TemplatePosition.Z);
                Vector3 roomCenterOffsetAo = room.Center != null
                    ? new Vector3(room.Center.X, 0f, room.Center.Z)
                    : new Vector3(widthAo * 0.5f, 0f, lengthAo * 0.5f);
                Vector3 roomCenterAo = roomOriginAo + roomCenterOffsetAo;
                Vector3 roomCenterWorld = centerAroundOrigin
                    ? (roomCenterAo - horizontalCenter) * positionScale
                    : roomCenterAo * positionScale;

                if (built == 0)
                    firstRoomCenterWorld = roomCenterWorld;

                if (!hasPreferredSpawn && IsPreferredIndoorSpawnRoom(room.Name))
                {
                    preferredRoomCenterWorld = roomCenterWorld;
                    hasPreferredSpawn = true;
                }

                if (buildRoomFloors)
                {
                    var roomGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    roomGo.name = $"RoomFloor_{SanitizeRoomName(room.Name)}";
                    roomGo.transform.SetParent(roomsRoot.transform, false);
                    roomGo.transform.position = roomCenterWorld + Vector3.down * (indoorRoomFloorThickness * 0.5f);
                    roomGo.transform.rotation = Quaternion.Euler(0f, room.RotationQuarterTurns * 90f, 0f);
                    roomGo.transform.localScale = new Vector3(
                        widthAo * positionScale,
                        indoorRoomFloorThickness,
                        lengthAo * positionScale);

                    var renderer = roomGo.GetComponent<MeshRenderer>();
                    if (renderer != null && roomMaterial != null)
                        renderer.sharedMaterial = roomMaterial;
                }

                built++;
            }

            if (built > 0)
            {
                preferredSpawnWorld = hasPreferredSpawn
                    ? preferredRoomCenterWorld
                    : firstRoomCenterWorld;
                Debug.Log($"Loaded indoor room fallback for playfield {pf}: built {built}/{roomsFile.Rooms.Count} room floors.");
            }

            return built > 0;
        }

        private bool TryLoadIndoorRoomSurfacesFromAOInstall(
            int pf,
            AOInstallValidation install,
            AOPlayfieldDefinition definition,
            Transform parent,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            if (install == null || !install.IsValid || definition == null)
                return false;

            string cacheFolder = Path.Combine(
                AOInstallConfiguration.CacheRoot,
                "IndoorSurfaces",
                SanitizeRoomName(install.DatabaseFingerprint));
            string streamPath = Path.Combine(cacheFolder, $"{pf}_v2.aois");
            if (!File.Exists(streamPath))
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string helperPath = Path.Combine(
                    projectRoot, "tools", "AOIndoorExtractor", "AOIndoorExtractor.exe");
                if (!AOIndoorSurfaceExtractor.Extract(
                    install,
                    pf,
                    definition.TilemapId,
                    definition.Rooms.Count,
                    helperPath,
                    streamPath,
                    out string diagnostic))
                {
                    Debug.LogError($"Direct AO indoor extraction failed for PF {pf}: {diagnostic}");
                    return false;
                }
                Debug.Log($"Direct AO indoor extraction completed for PF {pf}: {diagnostic}");
            }

            AOIndoorSurfaceSet extracted;
            try
            {
                extracted = AOIndoorSurfaceStreamDecoder.Decode(streamPath, pf);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Direct AO indoor stream decode failed for PF {pf}: {exception.Message}");
                return false;
            }

            var surfacesFile = new PlayfieldRoomSurfacesFile
            {
                PlayfieldId = pf,
                PlayfieldName = definition.Name
            };
            for (int roomIndex = 0; roomIndex < extracted.Rooms.Count; roomIndex++)
            {
                AOIndoorSurfaceRoom sourceRoom = extracted.Rooms[roomIndex];
                string roomName = sourceRoom.Instance >= 0
                    && sourceRoom.Instance < definition.Rooms.Count
                    ? definition.Rooms[sourceRoom.Instance].Name
                    : $"Room {sourceRoom.Instance}";
                var room = new RoomSurfaceRoomData
                {
                    Instance = sourceRoom.Instance,
                    Name = roomName
                };
                if (sourceRoom.Instance >= 0 && sourceRoom.Instance < definition.Rooms.Count)
                {
                    AOIndoorSurfaceMesh terrain = AOIndoorDungeonTerrainBuilder.Build(
                        definition.Rooms[sourceRoom.Instance], extracted.Tilemap);
                    if (terrain != null)
                        room.SurfaceMeshes.Add(ConvertDirectIndoorMesh(terrain));
                }
                for (int meshIndex = 0; meshIndex < sourceRoom.Meshes.Count; meshIndex++)
                {
                    AOIndoorSurfaceMesh sourceMesh = sourceRoom.Meshes[meshIndex];
                    room.SurfaceMeshes.Add(ConvertDirectIndoorMesh(sourceMesh));
                }
                surfacesFile.Rooms.Add(room);
            }

            bool built = BuildIndoorRoomSurfaces(
                pf, parent, horizontalCenter, centerAroundOrigin, positionScale, surfacesFile,
                addIndoorRoomSurfaceColliders,
                horizontalOnlyColliders: true);
            if (built)
            {
                int navigationRooms = addIndoorRoomSurfaceColliders
                    ? BuildDirectIndoorSharpNavColliders(extracted, parent, horizontalCenter,
                        centerAroundOrigin, positionScale, definition)
                    : 0;
                Debug.Log($"Loaded directly extracted AO indoor surfaces for PF {pf}: "
                    + $"rooms={extracted.Rooms.Count}, sharpNavRooms={navigationRooms}, source={streamPath}.");
            }
            return built;
        }

        private static RoomSurfaceMeshData ConvertDirectIndoorMesh(AOIndoorSurfaceMesh sourceMesh)
        {
            var mesh = new RoomSurfaceMeshData
            {
                VertexCount = sourceMesh.VertexCount,
                TriangleIndexCount = sourceMesh.Triangles.Length,
                Position = new Vector3Data(),
                Rotation = new QuaternionData { W = 1f },
                Scale = new Vector3Data { X = 1f, Y = 1f, Z = 1f },
                Triangles = new List<int>(sourceMesh.Triangles)
            };
            for (int vertex = 0; vertex < sourceMesh.Vertices.Length; vertex += 3)
            {
                mesh.Vertices.Add(new Vector3Data
                {
                    X = sourceMesh.Vertices[vertex],
                    Y = sourceMesh.Vertices[vertex + 1],
                    Z = sourceMesh.Vertices[vertex + 2]
                });
            }
            return mesh;
        }

        private static int BuildDirectIndoorSharpNavColliders(
            AOIndoorSurfaceSet surfaces,
            Transform parent,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            AOPlayfieldDefinition definition)
        {
            var root = new GameObject($"PF_{surfaces.PlayfieldId}_SharpNav");
            root.transform.SetParent(parent, false);
            int built = 0;
            var settings = SharpNav.NavMeshGenerationSettings.CustomDensity(0.3f, 0.5f);
            foreach (AOIndoorSurfaceRoom room in surfaces.Rooms)
            {
                try
                {
                    AOIndoorSurfaceMesh terrain = room.Instance >= 0
                        && room.Instance < definition.Rooms.Count
                        ? AOIndoorDungeonTerrainBuilder.Build(
                            definition.Rooms[room.Instance], surfaces.Tilemap)
                        : null;
                    AOIndoorNavigationMesh navigation = AOIndoorSharpNavBuilder.Build(
                        room, settings, terrain);
                    if (navigation.TriangleCount == 0)
                        continue;

                    var vertices = new Vector3[navigation.VertexCount];
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        int offset = index * 3;
                        Vector3 point = new Vector3(
                            navigation.Vertices[offset],
                            navigation.Vertices[offset + 1],
                            navigation.Vertices[offset + 2]);
                        if (centerAroundOrigin)
                            point -= horizontalCenter;
                        vertices[index] = point * positionScale;
                    }

                    var mesh = new Mesh
                    {
                        name = $"PF_{surfaces.PlayfieldId}_Room_{room.Instance}_SharpNav",
                        indexFormat = vertices.Length > 65535
                            ? UnityEngine.Rendering.IndexFormat.UInt32
                            : UnityEngine.Rendering.IndexFormat.UInt16
                    };
                    mesh.vertices = vertices;
                    mesh.triangles = navigation.Triangles;
                    mesh.RecalculateBounds();
                    var roomObject = new GameObject(mesh.name);
                    roomObject.transform.SetParent(root.transform, false);
                    roomObject.AddComponent<MeshCollider>().sharedMesh = mesh;
                    built++;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"SharpNav bake skipped PF {surfaces.PlayfieldId} room "
                        + $"{room.Instance}: {exception.Message}");
                }
            }
            if (built == 0)
                Destroy(root);
            return built;
        }

        private bool TryLoadIndoorRoomSurfacesJson(
            int pf,
            Transform parent,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            string surfacesPath = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{roomSurfacesJsonSuffix}");

            if (!File.Exists(surfacesPath))
                return false;

            PlayfieldRoomSurfacesFile surfacesFile;
            try
            {
                surfacesFile = JsonConvert.DeserializeObject<PlayfieldRoomSurfacesFile>(File.ReadAllText(surfacesPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed parsing room surfaces JSON {surfacesPath}: {ex.Message}");
                return false;
            }

            if (surfacesFile?.Rooms == null || surfacesFile.Rooms.Count == 0)
                return false;

            return BuildIndoorRoomSurfaces(
                pf, parent, horizontalCenter, centerAroundOrigin, positionScale, surfacesFile);
        }

        private bool BuildIndoorRoomSurfaces(
            int pf,
            Transform parent,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            PlayfieldRoomSurfacesFile surfacesFile,
            bool? addCollidersOverride = null,
            bool horizontalOnlyColliders = false)
        {
            if (surfacesFile?.Rooms == null || surfacesFile.Rooms.Count == 0)
                return false;

            var surfacesRoot = new GameObject($"PF_{pf}_RoomSurfaces");
            surfacesRoot.transform.SetParent(parent, false);
            bool forceSimpleIndoorMaterials = useDistinctIndoorSurfaceColors || pf == 127;
            var roomMaterial = BuildIndoorRoomFloorMaterial(forceSimpleIndoorMaterials);
            var roomWallMaterial = useDistinctIndoorSurfaceColors || pf == 127
                ? BuildIndoorRoomSurfaceMaterial(indoorRoomWallColor, forceSimpleIndoorMaterials)
                : roomMaterial;
            var roomCeilingMaterial = useDistinctIndoorSurfaceColors || pf == 127
                ? BuildIndoorRoomSurfaceMaterial(indoorRoomCeilingColor, forceSimpleIndoorMaterials)
                : roomMaterial;
            var pickDataByRoom = LoadIndoorRoomPickDataByRoom(pf);
            var roomFootprintsByInstance = LoadIndoorRoomFootprintsByInstance(pf);
            bool addSurfaceColliders = addCollidersOverride ?? addIndoorRoomSurfaceColliders;

            int builtRooms = 0;
            int builtSurfaces = 0;
            int builtPickPatches = 0;
            int builtPickOutlines = 0;
            for (int roomIndex = 0; roomIndex < surfacesFile.Rooms.Count; roomIndex++)
            {
                var room = surfacesFile.Rooms[roomIndex];
                if (room == null)
                    continue;

                bool hasSurfaceMeshes = room.SurfaceMeshes != null && room.SurfaceMeshes.Count > 0;
                bool hasPickData = pickDataByRoom != null
                    && pickDataByRoom.TryGetValue(room.Instance, out var roomPickDataExisting)
                    && roomPickDataExisting != null
                    && (roomPickDataExisting.PointPatches.Count > 0 || roomPickDataExisting.OutlinePolygonsAo.Count > 0);

                if (!hasSurfaceMeshes && !hasPickData)
                    continue;

                var floorVertices = new List<Vector3>(2048);
                var floorTriangles = new List<int>(4096);
                var wallVertices = new List<Vector3>(2048);
                var wallTriangles = new List<int>(4096);
                var ceilingVertices = new List<Vector3>(2048);
                var ceilingTriangles = new List<int>(4096);

                if (hasSurfaceMeshes)
                {
                    for (int meshIndex = 0; meshIndex < room.SurfaceMeshes.Count; meshIndex++)
                    {
                        var surface = room.SurfaceMeshes[meshIndex];
                        if (surface?.Vertices == null || surface.Vertices.Count == 0 || surface.Triangles == null || surface.Triangles.Count < 3)
                            continue;

                        Vector3 surfacePositionAo = surface.Position != null
                            ? new Vector3(surface.Position.X, surface.Position.Y, surface.Position.Z)
                            : Vector3.zero;
                        Quaternion surfaceRotation = surface.Rotation != null
                            ? new Quaternion(surface.Rotation.X, surface.Rotation.Y, surface.Rotation.Z, surface.Rotation.W)
                            : Quaternion.identity;
                        Vector3 surfaceScale = surface.Scale != null
                            ? new Vector3(surface.Scale.X, surface.Scale.Y, surface.Scale.Z)
                            : Vector3.one;

                        Matrix4x4 surfaceMatrix = Matrix4x4.TRS(surfacePositionAo, surfaceRotation, surfaceScale);
                        IndoorSurfaceKind kind = ClassifyIndoorSurface(surfaceMatrix, surface);
                        List<Vector3> targetVertices;
                        List<int> targetTriangles;
                        switch (kind)
                        {
                            case IndoorSurfaceKind.Ceiling:
                                targetVertices = ceilingVertices;
                                targetTriangles = ceilingTriangles;
                                break;
                            case IndoorSurfaceKind.Wall:
                                targetVertices = wallVertices;
                                targetTriangles = wallTriangles;
                                break;
                            default:
                                targetVertices = floorVertices;
                                targetTriangles = floorTriangles;
                                break;
                        }

                        int vertexOffset = targetVertices.Count;

                        for (int vertexIndex = 0; vertexIndex < surface.Vertices.Count; vertexIndex++)
                        {
                            var vertex = surface.Vertices[vertexIndex];
                            Vector3 localVertex = new Vector3(vertex.X, vertex.Y, vertex.Z);
                            Vector3 worldAo = surfaceMatrix.MultiplyPoint3x4(localVertex);
                            if (centerAroundOrigin)
                                worldAo -= horizontalCenter;

                            targetVertices.Add(worldAo * positionScale);
                        }

                        for (int triIndex = 0; triIndex < surface.Triangles.Count; triIndex++)
                        {
                            targetTriangles.Add(vertexOffset + surface.Triangles[triIndex]);
                        }

                        builtSurfaces++;
                    }
                }

                if (pf == 127
                    && roomFootprintsByInstance != null
                    && roomFootprintsByInstance.TryGetValue(room.Instance, out var footprint))
                {
                    AppendRoomFootprintFloorPatch(
                        footprint,
                        floorVertices,
                        floorTriangles,
                        wallVertices,
                        horizontalCenter,
                        centerAroundOrigin,
                        positionScale);
                }

                // Captured surfaces remain authoritative; the footprint patch closes holes in
                // rooms whose client surface list does not contain a walkable floor plane.

                if (pickDataByRoom != null
                    && pickDataByRoom.TryGetValue(room.Instance, out var roomPickData)
                    && roomPickData != null)
                {
                    if (roomPickData.PointPatches.Count > 0)
                    {
                        var filledPointPatches = FillAutofloorInterior(roomPickData.PointPatches);
                        var filteredPointPatches = FilterAutofloorNoise(filledPointPatches);
                        builtPickPatches += AppendIndoorRoomPickPatches(
                            filteredPointPatches,
                            floorVertices,
                            floorTriangles,
                            horizontalCenter,
                            centerAroundOrigin,
                            positionScale);
                    }

                    if (roomPickData.OutlinePolygonsAo.Count > 0)
                    {
                        builtPickOutlines += AppendIndoorRoomOutlinePatches(
                            roomPickData.OutlinePolygonsAo,
                            floorVertices,
                            floorTriangles,
                            horizontalCenter,
                            centerAroundOrigin,
                            positionScale);
                    }
                }

                int builtParts = 0;
                builtParts += TryCreateIndoorRoomSurfacePart(
                    surfacesRoot.transform,
                    room,
                    "Floor",
                    floorVertices,
                    floorTriangles,
                    roomMaterial,
                    addSurfaceColliders);
                builtParts += TryCreateIndoorRoomSurfacePart(
                    surfacesRoot.transform,
                    room,
                    "Wall",
                    wallVertices,
                    wallTriangles,
                    roomWallMaterial,
                    addSurfaceColliders && !horizontalOnlyColliders);
                builtParts += TryCreateIndoorRoomSurfacePart(
                    surfacesRoot.transform,
                    room,
                    "Ceiling",
                    ceilingVertices,
                    ceilingTriangles,
                    roomCeilingMaterial,
                    addSurfaceColliders);

                if (builtParts == 0)
                    continue;

                builtRooms++;
            }

            Debug.Log(
                $"Loaded indoor room surfaces for playfield {pf}: builtRooms={builtRooms}/{surfacesFile.Rooms.Count}, " +
                $"builtSurfaceMeshes={builtSurfaces}, builtPickPatches={builtPickPatches}, builtPickOutlines={builtPickOutlines}.");
            return builtRooms > 0;
        }

        private Dictionary<int, RoomFootprintData> LoadIndoorRoomFootprintsByInstance(int pf)
        {
            string roomsPath = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{roomJsonSuffix}");
            if (!File.Exists(roomsPath))
                return null;

            PlayfieldRoomsFile roomsFile;
            try
            {
                roomsFile = JsonConvert.DeserializeObject<PlayfieldRoomsFile>(File.ReadAllText(roomsPath));
            }
            catch
            {
                return null;
            }

            if (roomsFile?.Rooms == null || roomsFile.Rooms.Count == 0)
                return null;

            var byInstance = new Dictionary<int, RoomFootprintData>(roomsFile.Rooms.Count);
            for (int i = 0; i < roomsFile.Rooms.Count; i++)
            {
                var room = roomsFile.Rooms[i];
                if (room?.TemplatePosition == null)
                    continue;

                float widthAo = Mathf.Max(2f, (room.TileX2 - room.TileX1) * 2f);
                float lengthAo = Mathf.Max(2f, (room.TileY2 - room.TileY1) * 2f);
                Vector3 roomOriginAo = new Vector3(room.TemplatePosition.X, room.TemplatePosition.Y, room.TemplatePosition.Z);
                Vector3 roomCenterOffsetAo = room.Center != null
                    ? new Vector3(room.Center.X, 0f, room.Center.Z)
                    : new Vector3(widthAo * 0.5f, 0f, lengthAo * 0.5f);

                byInstance[i] = new RoomFootprintData
                {
                    Instance = i,
                    CenterAo = roomOriginAo + roomCenterOffsetAo,
                    WidthAo = widthAo,
                    LengthAo = lengthAo,
                    RotationQuarterTurns = room.RotationQuarterTurns
                };
            }

            return byInstance;
        }

        private bool TryEstimateIndoorRoomSurfacesHorizontalCenter(int pf, out Vector3 centerAo)
        {
            centerAo = Vector3.zero;

            string surfacesPath = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{roomSurfacesJsonSuffix}");

            if (!File.Exists(surfacesPath))
                return false;

            PlayfieldRoomSurfacesFile surfacesFile;
            try
            {
                surfacesFile = JsonConvert.DeserializeObject<PlayfieldRoomSurfacesFile>(File.ReadAllText(surfacesPath));
            }
            catch
            {
                return false;
            }

            if (surfacesFile?.Rooms == null || surfacesFile.Rooms.Count == 0)
                return false;

            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int roomIndex = 0; roomIndex < surfacesFile.Rooms.Count; roomIndex++)
            {
                var room = surfacesFile.Rooms[roomIndex];
                if (room?.SurfaceMeshes == null || room.SurfaceMeshes.Count == 0)
                    continue;

                for (int meshIndex = 0; meshIndex < room.SurfaceMeshes.Count; meshIndex++)
                {
                    var mesh = room.SurfaceMeshes[meshIndex];
                    if (mesh?.Position == null)
                        continue;

                    var positionAo = new Vector3(mesh.Position.X, mesh.Position.Y, mesh.Position.Z);
                    if (!IsFiniteVector3(positionAo))
                        continue;

                    sum += positionAo;
                    count++;
                }
            }

            if (count <= 0)
                return false;

            Vector3 average = sum / count;
            centerAo = new Vector3(average.x, 0f, average.z);
            return true;
        }

        private Dictionary<int, IndoorRoomPickRoomData> LoadIndoorRoomPickDataByRoom(int pf)
        {
            if (!ApplyPickPatchesToIndoorRooms)
                return null;

            string picksPath = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{IndoorRoomPickJsonlSuffix}");

            if (!File.Exists(picksPath))
                return null;

            var byRoom = new Dictionary<int, IndoorRoomPickRoomData>();
            var activeOutlineByRoom = new Dictionary<int, List<Vector3>>();
            bool pendingOutlineStartWithoutRoom = false;
            int parsedRecords = 0;
            int acceptedPatches = 0;
            int acceptedOutlinePoints = 0;
            int acceptedOutlines = 0;

            static bool IsOutlineStartNote(string note)
            {
                if (string.IsNullOrWhiteSpace(note))
                    return false;

                string trimmed = note.Trim();
                return trimmed.Equals("outline start", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("outline:start", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Equals("/outline start", StringComparison.OrdinalIgnoreCase);
            }

            static bool IsOutlineEndNote(string note)
            {
                if (string.IsNullOrWhiteSpace(note))
                    return false;

                string trimmed = note.Trim();
                return trimmed.Equals("outline end", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("outline:end", StringComparison.OrdinalIgnoreCase)
                    || trimmed.Equals("/outline end", StringComparison.OrdinalIgnoreCase);
            }

            static IndoorRoomPickRoomData EnsureRoomData(
                Dictionary<int, IndoorRoomPickRoomData> byRoomMap,
                int roomInstance)
            {
                if (!byRoomMap.TryGetValue(roomInstance, out var roomData))
                {
                    roomData = new IndoorRoomPickRoomData();
                    byRoomMap[roomInstance] = roomData;
                }

                return roomData;
            }

            void FinalizeOutlineForRoom(int roomInstance)
            {
                if (!activeOutlineByRoom.TryGetValue(roomInstance, out var outline) || outline == null)
                    return;

                if (outline.Count >= 3)
                {
                    EnsureRoomData(byRoom, roomInstance).OutlinePolygonsAo.Add(new List<Vector3>(outline));
                    acceptedOutlines++;
                }

                activeOutlineByRoom.Remove(roomInstance);
            }

            foreach (string line in File.ReadLines(picksPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                PickCaptureRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<PickCaptureRecord>(line);
                }
                catch
                {
                    continue;
                }

                parsedRecords++;
                if (record == null || (record.PlayfieldId > 0 && record.PlayfieldId != pf))
                {
                    continue;
                }

                int? roomInstance = record.Surface?.RoomInstance;
                if (IsOutlineStartNote(record.Note))
                {
                    if (roomInstance.HasValue)
                    {
                        FinalizeOutlineForRoom(roomInstance.Value);
                        activeOutlineByRoom[roomInstance.Value] = new List<Vector3>();
                    }
                    else
                    {
                        pendingOutlineStartWithoutRoom = true;
                    }
                    continue;
                }

                if (IsOutlineEndNote(record.Note))
                {
                    if (roomInstance.HasValue)
                    {
                        FinalizeOutlineForRoom(roomInstance.Value);
                    }
                    else if (activeOutlineByRoom.Count == 1)
                    {
                        int onlyRoom = activeOutlineByRoom.Keys.First();
                        FinalizeOutlineForRoom(onlyRoom);
                    }

                    pendingOutlineStartWithoutRoom = false;
                    continue;
                }

                Vector3Data point = record.RaycastHitPosition ?? record.ProbePosition ?? record.MouseWorldPosition;
                if (point == null || !roomInstance.HasValue)
                    continue;

                Vector3 pointAo = new Vector3(point.X, point.Y, point.Z);

                if (pendingOutlineStartWithoutRoom)
                {
                    FinalizeOutlineForRoom(roomInstance.Value);
                    activeOutlineByRoom[roomInstance.Value] = new List<Vector3>();
                    pendingOutlineStartWithoutRoom = false;
                }

                if (activeOutlineByRoom.TryGetValue(roomInstance.Value, out var activeOutline) && activeOutline != null)
                {
                    bool shouldAddPoint = true;
                    if (activeOutline.Count > 0)
                    {
                        Vector3 last = activeOutline[activeOutline.Count - 1];
                        if ((last - pointAo).sqrMagnitude < 0.0001f)
                            shouldAddPoint = false;
                    }

                    if (shouldAddPoint)
                    {
                        activeOutline.Add(pointAo);
                        acceptedOutlinePoints++;
                    }
                    continue;
                }

                var patch = new IndoorRoomPickPatch
                {
                    PositionAo = pointAo,
                    IsMark = record.IsMark,
                    Note = record.Note
                };

                if (record.RaycastHitNormal != null)
                {
                    var normal = new Vector3(
                        record.RaycastHitNormal.X,
                        record.RaycastHitNormal.Y,
                        record.RaycastHitNormal.Z);
                    if (normal.sqrMagnitude > 0.0001f)
                    {
                        patch.NormalAo = normal.normalized;
                        patch.HasNormal = true;
                    }
                }

                EnsureRoomData(byRoom, roomInstance.Value).PointPatches.Add(patch);
                acceptedPatches++;
            }

            if (activeOutlineByRoom.Count > 0)
            {
                foreach (int roomInstance in activeOutlineByRoom.Keys.ToArray())
                {
                    FinalizeOutlineForRoom(roomInstance);
                }
            }

            if (acceptedPatches <= 0 && acceptedOutlines <= 0)
                return null;

            Debug.Log(
                $"Loaded indoor room pick patches for playfield {pf}: " +
                $"acceptedPointPatches={acceptedPatches}, acceptedOutlines={acceptedOutlines}, acceptedOutlinePoints={acceptedOutlinePoints}, " +
                $"parsedRecords={parsedRecords}, rooms={byRoom.Count}.");
            return byRoom;
        }

        private int AppendIndoorRoomPickPatches(
            List<IndoorRoomPickPatch> roomPickPatches,
            List<Vector3> floorVertices,
            List<int> floorTriangles,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            if (roomPickPatches == null || roomPickPatches.Count == 0)
                return 0;

            float patchYOffset = IndoorRoomPickPatchHeightOffset;
            int added = 0;

            for (int i = 0; i < roomPickPatches.Count; i++)
            {
                var patch = roomPickPatches[i];
                if (patch == null)
                    continue;

                bool isAutofloorLike = !string.IsNullOrWhiteSpace(patch.Note)
                    && patch.Note.StartsWith("autofloor", StringComparison.OrdinalIgnoreCase);
                float minNormalY = isAutofloorLike
                    ? 0.68f
                    : 0.5f;
                if (patch.HasNormal && Mathf.Abs(patch.NormalAo.y) < minNormalY)
                    continue;

                float patchSizeAo = isAutofloorLike
                    ? IndoorRoomPickPatchSizeAo * 1.75f
                    : IndoorRoomPickPatchSizeAo;
                float halfPatchSize = Mathf.Max(0.1f, patchSizeAo * 0.5f);

                Vector3 centerAo = patch.PositionAo + new Vector3(0f, patchYOffset, 0f);
                Vector3 v0Ao = centerAo + new Vector3(-halfPatchSize, 0f, -halfPatchSize);
                Vector3 v1Ao = centerAo + new Vector3(halfPatchSize, 0f, -halfPatchSize);
                Vector3 v2Ao = centerAo + new Vector3(halfPatchSize, 0f, halfPatchSize);
                Vector3 v3Ao = centerAo + new Vector3(-halfPatchSize, 0f, halfPatchSize);

                if (centerAroundOrigin)
                {
                    v0Ao -= horizontalCenter;
                    v1Ao -= horizontalCenter;
                    v2Ao -= horizontalCenter;
                    v3Ao -= horizontalCenter;
                }

                int vertexOffset = floorVertices.Count;
                floorVertices.Add(v0Ao * positionScale);
                floorVertices.Add(v1Ao * positionScale);
                floorVertices.Add(v2Ao * positionScale);
                floorVertices.Add(v3Ao * positionScale);

                floorTriangles.Add(vertexOffset + 0);
                floorTriangles.Add(vertexOffset + 1);
                floorTriangles.Add(vertexOffset + 2);
                floorTriangles.Add(vertexOffset + 0);
                floorTriangles.Add(vertexOffset + 2);
                floorTriangles.Add(vertexOffset + 3);
                added++;
            }

            return added;
        }

        private static List<IndoorRoomPickPatch> FillAutofloorInterior(List<IndoorRoomPickPatch> source)
        {
            if (source == null || source.Count == 0)
                return source ?? new List<IndoorRoomPickPatch>();

            const float gridStepAo = 1f;
            const float yBandStepAo = 1f;
            const int maxBandSpanCells = 160;

            var result = new List<IndoorRoomPickPatch>(source.Count + 4096);
            result.AddRange(source);

            var autofloor = source
                .Where(p => p != null
                    && !string.IsNullOrWhiteSpace(p.Note)
                    && p.Note.StartsWith("autofloor", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (autofloor.Count < 8)
                return result;

            var existing = new HashSet<(int x, int yBand, int z)>();
            foreach (var p in source)
            {
                if (p == null)
                    continue;
                int ex = Mathf.RoundToInt(p.PositionAo.x / gridStepAo);
                int ey = Mathf.RoundToInt(p.PositionAo.y / yBandStepAo);
                int ez = Mathf.RoundToInt(p.PositionAo.z / gridStepAo);
                existing.Add((ex, ey, ez));
            }

            var byBand = autofloor.GroupBy(p => Mathf.RoundToInt(p.PositionAo.y / yBandStepAo));
            foreach (var band in byBand)
            {
                var cells = new HashSet<(int x, int z)>();
                float ySum = 0f;
                int yCount = 0;
                int minX = int.MaxValue, maxX = int.MinValue;
                int minZ = int.MaxValue, maxZ = int.MinValue;

                foreach (var p in band)
                {
                    int ix = Mathf.RoundToInt(p.PositionAo.x / gridStepAo);
                    int iz = Mathf.RoundToInt(p.PositionAo.z / gridStepAo);
                    cells.Add((ix, iz));
                    minX = Mathf.Min(minX, ix);
                    maxX = Mathf.Max(maxX, ix);
                    minZ = Mathf.Min(minZ, iz);
                    maxZ = Mathf.Max(maxZ, iz);
                    ySum += p.PositionAo.y;
                    yCount++;
                }

                if (cells.Count < 8)
                    continue;

                int width = (maxX - minX) + 1;
                int height = (maxZ - minZ) + 1;
                if (width <= 0 || height <= 0 || width > maxBandSpanCells || height > maxBandSpanCells)
                    continue;

                bool[,] occ = new bool[width, height];
                foreach (var c in cells)
                    occ[c.x - minX, c.z - minZ] = true;

                bool[,] outside = new bool[width, height];
                var q = new Queue<(int x, int z)>();

                void EnqueueIfEmpty(int x, int z)
                {
                    if (x < 0 || z < 0 || x >= width || z >= height)
                        return;
                    if (occ[x, z] || outside[x, z])
                        return;
                    outside[x, z] = true;
                    q.Enqueue((x, z));
                }

                for (int x = 0; x < width; x++)
                {
                    EnqueueIfEmpty(x, 0);
                    EnqueueIfEmpty(x, height - 1);
                }

                for (int z = 0; z < height; z++)
                {
                    EnqueueIfEmpty(0, z);
                    EnqueueIfEmpty(width - 1, z);
                }

                while (q.Count > 0)
                {
                    var n = q.Dequeue();
                    EnqueueIfEmpty(n.x + 1, n.z);
                    EnqueueIfEmpty(n.x - 1, n.z);
                    EnqueueIfEmpty(n.x, n.z + 1);
                    EnqueueIfEmpty(n.x, n.z - 1);
                }

                float fillY = yCount > 0 ? (ySum / yCount) : (band.Key * yBandStepAo);
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        if (occ[x, z] || outside[x, z])
                            continue;

                        int wx = x + minX;
                        int wz = z + minZ;
                        var key = (wx, band.Key, wz);
                        if (existing.Contains(key))
                            continue;

                        existing.Add(key);
                        result.Add(new IndoorRoomPickPatch
                        {
                            PositionAo = new Vector3(wx * gridStepAo, fillY, wz * gridStepAo),
                            NormalAo = Vector3.up,
                            HasNormal = true,
                            IsMark = false,
                            Note = "autofloor_fill"
                        });
                    }
                }
            }

            return result;
        }

        private static List<IndoorRoomPickPatch> FilterAutofloorNoise(List<IndoorRoomPickPatch> source)
        {
            if (source == null || source.Count == 0)
                return source ?? new List<IndoorRoomPickPatch>();

            const float gridStepAo = 1f;

            bool IsAutofloorLike(IndoorRoomPickPatch p)
            {
                return p != null
                    && !string.IsNullOrWhiteSpace(p.Note)
                    && p.Note.StartsWith("autofloor", StringComparison.OrdinalIgnoreCase);
            }

            bool IsAutofloorFill(IndoorRoomPickPatch p)
            {
                return p != null
                    && !string.IsNullOrWhiteSpace(p.Note)
                    && p.Note.StartsWith("autofloor_fill", StringComparison.OrdinalIgnoreCase);
            }

            List<IndoorRoomPickPatch> FilterByNeighborhood(
                IEnumerable<IndoorRoomPickPatch> patches,
                float yBandStepAo,
                int minNeighbors)
            {
                var kept = new List<IndoorRoomPickPatch>();
                if (patches == null)
                    return kept;

                foreach (var band in patches.GroupBy(p => Mathf.RoundToInt(p.PositionAo.y / yBandStepAo)))
                {
                    var cellMap = new Dictionary<(int x, int z), List<IndoorRoomPickPatch>>();
                    foreach (var p in band)
                    {
                        int ix = Mathf.RoundToInt(p.PositionAo.x / gridStepAo);
                        int iz = Mathf.RoundToInt(p.PositionAo.z / gridStepAo);
                        var key = (ix, iz);
                        if (!cellMap.TryGetValue(key, out var list))
                        {
                            list = new List<IndoorRoomPickPatch>();
                            cellMap[key] = list;
                        }
                        list.Add(p);
                    }

                    foreach (var kv in cellMap)
                    {
                        int neighbors = 0;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                if (dx == 0 && dz == 0)
                                    continue;
                                if (cellMap.ContainsKey((kv.Key.x + dx, kv.Key.z + dz)))
                                    neighbors++;
                            }
                        }

                        if (neighbors >= minNeighbors)
                            kept.AddRange(kv.Value);
                    }
                }

                return kept;
            }

            var output = new List<IndoorRoomPickPatch>(source.Count);
            var baseAutofloor = source.Where(p => IsAutofloorLike(p) && !IsAutofloorFill(p)).ToList();
            var fillAutofloor = source.Where(IsAutofloorFill).ToList();
            var nonAutofloor = source.Where(p => !IsAutofloorLike(p)).ToList();
            output.AddRange(nonAutofloor);
            // Real autofloor points: light filtering to remove stray wall/door blockers.
            output.AddRange(FilterByNeighborhood(baseAutofloor, yBandStepAo: 1.25f, minNeighbors: 1));
            // Generated fill points: stricter filtering.
            output.AddRange(FilterByNeighborhood(fillAutofloor, yBandStepAo: 0.75f, minNeighbors: 2));

            return output;
        }

        private int AppendIndoorRoomOutlinePatches(
            List<List<Vector3>> outlinePolygonsAo,
            List<Vector3> floorVertices,
            List<int> floorTriangles,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            if (outlinePolygonsAo == null || outlinePolygonsAo.Count == 0)
                return 0;

            int added = 0;
            var triLocal = new List<int>(128);

            for (int polygonIndex = 0; polygonIndex < outlinePolygonsAo.Count; polygonIndex++)
            {
                var polygonAo = outlinePolygonsAo[polygonIndex];
                if (polygonAo == null || polygonAo.Count < 3)
                    continue;

                triLocal.Clear();
                if (!TryTriangulatePolygonXZ(polygonAo, triLocal) || triLocal.Count < 3)
                    continue;

                int vertexOffset = floorVertices.Count;
                for (int i = 0; i < polygonAo.Count; i++)
                {
                    Vector3 ao = polygonAo[i] + new Vector3(0f, IndoorRoomPickPatchHeightOffset, 0f);
                    if (centerAroundOrigin)
                        ao -= horizontalCenter;
                    floorVertices.Add(ao * positionScale);
                }

                for (int i = 0; i < triLocal.Count; i++)
                    floorTriangles.Add(vertexOffset + triLocal[i]);

                added++;
            }

            return added;
        }

        private static bool TryTriangulatePolygonXZ(IReadOnlyList<Vector3> polygonAo, List<int> indicesOut)
        {
            indicesOut.Clear();
            if (polygonAo == null || polygonAo.Count < 3)
                return false;

            static float SignedAreaXZ(IReadOnlyList<Vector3> pts)
            {
                float area = 0f;
                for (int i = 0; i < pts.Count; i++)
                {
                    int j = (i + 1) % pts.Count;
                    area += (pts[i].x * pts[j].z) - (pts[j].x * pts[i].z);
                }

                return area * 0.5f;
            }

            static float Cross2D(Vector3 a, Vector3 b, Vector3 c)
            {
                float abx = b.x - a.x;
                float abz = b.z - a.z;
                float acx = c.x - a.x;
                float acz = c.z - a.z;
                return (abx * acz) - (abz * acx);
            }

            static bool PointInTriangleXZ(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
            {
                float c1 = Cross2D(a, b, p);
                float c2 = Cross2D(b, c, p);
                float c3 = Cross2D(c, a, p);
                bool hasNeg = c1 < -0.0001f || c2 < -0.0001f || c3 < -0.0001f;
                bool hasPos = c1 > 0.0001f || c2 > 0.0001f || c3 > 0.0001f;
                return !(hasNeg && hasPos);
            }

            var verts = new List<int>(polygonAo.Count);
            bool isCounterClockwise = SignedAreaXZ(polygonAo) > 0f;
            if (isCounterClockwise)
            {
                for (int i = 0; i < polygonAo.Count; i++)
                    verts.Add(i);
            }
            else
            {
                for (int i = polygonAo.Count - 1; i >= 0; i--)
                    verts.Add(i);
            }

            int guard = 0;
            int guardLimit = polygonAo.Count * polygonAo.Count;
            while (verts.Count > 3 && guard < guardLimit)
            {
                guard++;
                bool earFound = false;
                for (int i = 0; i < verts.Count; i++)
                {
                    int prev = verts[(i - 1 + verts.Count) % verts.Count];
                    int curr = verts[i];
                    int next = verts[(i + 1) % verts.Count];

                    Vector3 a = polygonAo[prev];
                    Vector3 b = polygonAo[curr];
                    Vector3 c = polygonAo[next];

                    if (Cross2D(a, b, c) <= 0.0001f)
                        continue;

                    bool containsAny = false;
                    for (int t = 0; t < verts.Count; t++)
                    {
                        int test = verts[t];
                        if (test == prev || test == curr || test == next)
                            continue;

                        if (PointInTriangleXZ(polygonAo[test], a, b, c))
                        {
                            containsAny = true;
                            break;
                        }
                    }

                    if (containsAny)
                        continue;

                    indicesOut.Add(prev);
                    indicesOut.Add(curr);
                    indicesOut.Add(next);
                    verts.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                    break;
            }

            if (verts.Count == 3)
            {
                indicesOut.Add(verts[0]);
                indicesOut.Add(verts[1]);
                indicesOut.Add(verts[2]);
            }

            return indicesOut.Count >= 3;
        }

        private bool TryLoadRuntimeWorldObjectsJson(
            int pf,
            Transform parent,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            Dictionary<string, GameObject> prefabCache,
            MeshRotationOverrideSet meshRotationOverrides)
        {
            string runtimePath = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{runtimeWorldObjectsJsonSuffix}");

            if (!File.Exists(runtimePath))
                return false;

            RuntimeWorldObjectsFile runtimeFile;
            try
            {
                runtimeFile = JsonConvert.DeserializeObject<RuntimeWorldObjectsFile>(File.ReadAllText(runtimePath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed parsing runtime world objects JSON {runtimePath}: {ex.Message}");
                return false;
            }

            if (runtimeFile?.RuntimeWorldObjects == null || runtimeFile.RuntimeWorldObjects.Count == 0)
            {
                if (runtimeFile?.Dynels == null || runtimeFile.Dynels.Count == 0)
                    return false;
            }

            List<RuntimeWorldObjectData> runtimeObjects = GetRuntimeObjects(runtimeFile);
            if (runtimeObjects == null || runtimeObjects.Count == 0)
                return false;

            QueueRuntimeGlbTemplatePrewarm(runtimeObjects);

            var runtimeRoot = new GameObject($"PF_{pf}_RuntimeObjects");
            runtimeRoot.transform.SetParent(parent, false);

            var modelNameByInstanceId = LoadGlobalModelNameMapByInstanceId();
            int built = 0;
            int resolvedMeshNames = 0;
            int skippedNoPosition = 0;
            int skippedAuthoritativeDynel = 0;
            int spawnFailed = 0;
            var ring0Entries = new List<PendingRuntimeSpawnEntry>(runtimeObjects.Count);
            var deferredEntries = new List<PendingRuntimeSpawnEntry>(runtimeObjects.Count);
            Transform player = ResolvePlayerTransform();
            Vector3 playerWorld = player != null ? player.position : Vector3.zero;
            bool havePlayer = player != null;
            float ring0Radius = Mathf.Max(5f, runtimeObjectRing0Radius);
            float ring0RadiusSq = ring0Radius * ring0Radius;

            for (int i = 0; i < runtimeObjects.Count; i++)
            {
                var obj = runtimeObjects[i];
                if (obj == null)
                    continue;

                Vector3? aoPosition = ResolveRuntimeWorldObjectAoPosition(obj);
                if (!aoPosition.HasValue)
                {
                    skippedNoPosition++;
                    continue;
                }

                string meshName = ResolveRuntimeWorldObjectMeshName(obj, modelNameByInstanceId);
                if (!string.IsNullOrWhiteSpace(meshName))
                    resolvedMeshNames++;

                string meshKey = string.IsNullOrWhiteSpace(meshName)
                    ? null
                    : SafeGetFileNameWithoutExtension(meshName);

                Vector3 perMeshRotationEuler = Vector3.zero;
                bool perMeshRotationIsAbsolute = false;
                Vector3 perMeshPositionOffset = Vector3.zero;
                if (!string.IsNullOrWhiteSpace(meshKey))
                {
                    if (!TryGetMeshVectorOverride(meshRotationOverrides.MeshRotationByName, meshKey, out perMeshRotationEuler))
                    {
                        if (enableBuiltInMeshRotationOverrides)
                        {
                            if (TryGetBuiltInMeshRotationOverride(meshKey, out perMeshRotationEuler))
                                perMeshRotationIsAbsolute = false;
                        }
                    }
                    else
                    {
                        perMeshRotationIsAbsolute = TryGetMeshRotationMode(
                            meshRotationOverrides.MeshRotationModeByName,
                            meshKey,
                            out var resolvedMode)
                            ? resolvedMode == RotationOverrideMode.Absolute
                            : true;
                    }
                    TryGetMeshVectorOverride(
                        meshRotationOverrides.MeshPositionOffsetByName,
                        meshKey,
                        out perMeshPositionOffset);

                    if (ShouldSkipGenericMeshPlaceholder(meshKey))
                        continue;
                }

                var entry = new PendingRuntimeSpawnEntry
                {
                    Obj = obj,
                    AoPosition = aoPosition.Value,
                    MeshName = meshName,
                    MeshKey = meshKey,
                    PerMeshRotationEuler = perMeshRotationEuler,
                    PerMeshRotationIsAbsolute = perMeshRotationIsAbsolute,
                    PerMeshPositionOffset = perMeshPositionOffset
                };

                if (!enableRuntimeObjectChunkRings || !havePlayer)
                {
                    ring0Entries.Add(entry);
                    continue;
                }

                Vector3 world = ConvertRuntimeAoToWorldPosition(entry.AoPosition, horizontalCenter, centerAroundOrigin, positionScale);
                Vector3 d = world - playerWorld;
                d.y = 0f;
                if (d.sqrMagnitude <= ring0RadiusSq)
                    ring0Entries.Add(entry);
                else
                    deferredEntries.Add(entry);
            }

            int immediateSeedCount = Mathf.Clamp(
                runtimeObjectImmediateSpawnSeedCount,
                0,
                Mathf.Max(0, ring0Entries.Count));
            for (int i = 0; i < immediateSeedCount; i++)
            {
                if (TrySpawnPreparedRuntimeWorldObject(
                    ring0Entries[i],
                    runtimeRoot.transform,
                    horizontalCenter,
                    centerAroundOrigin,
                    positionScale,
                    prefabCache,
                    meshRotationOverrides,
                    pf))
                {
                    built++;
                }
                else
                {
                    spawnFailed++;
                }
            }

            var streamingEntries = new List<PendingRuntimeSpawnEntry>(
                Mathf.Max(0, ring0Entries.Count - immediateSeedCount) + deferredEntries.Count);
            for (int i = immediateSeedCount; i < ring0Entries.Count; i++)
                streamingEntries.Add(ring0Entries[i]);
            streamingEntries.AddRange(deferredEntries);

            if (streamingEntries.Count > 0)
            {
                if (_runtimeObjectBackgroundSpawnCoroutine != null)
                    StopCoroutine(_runtimeObjectBackgroundSpawnCoroutine);

                _deferredRuntimeSpawnStartedAt = Time.unscaledTime;
                _runtimeObjectBackgroundSpawnCoroutine = StartCoroutine(SpawnDeferredRuntimeWorldObjectsCoroutine(
                    streamingEntries,
                    runtimeRoot.transform,
                    horizontalCenter,
                    centerAroundOrigin,
                    positionScale,
                    prefabCache,
                    meshRotationOverrides,
                    pf));
            }
            else
            {
                _deferredRuntimeSpawnStartedAt = -1f;
            }

            Debug.Log(
                $"Loaded runtime world objects for playfield {pf}: built {built}/{runtimeObjects.Count}, " +
                $"ring0={ring0Entries.Count}, deferred={deferredEntries.Count}, immediateSeed={immediateSeedCount}, " +
                $"resolvedMeshNames={resolvedMeshNames}, skippedNoPosition={skippedNoPosition}, " +
                $"skippedAuthoritativeDynel={skippedAuthoritativeDynel}, spawnFailed={spawnFailed}.");
            return built > 0 || streamingEntries.Count > 0;
        }

        private bool ShouldSkipLocalRuntimeObjectInAuthoritativeMode(RuntimeWorldObjectData obj)
        {
            if (obj == null)
                return true;
            if (!enableAuthoritativeNetworking || !allowRuntimeWorldObjectsJsonWhenAuthoritativeNetworking)
                return false;

            bool hasStrongStaticMarkers =
                obj.StaticInstance.HasValue
                || obj.BuildingInstance.HasValue
                || obj.AreaInstance.HasValue
                || obj.SourceStaticInstance.HasValue
                || obj.SourceBuildingInstance.HasValue
                || obj.SourceAreaInstance.HasValue
                || (!string.IsNullOrWhiteSpace(obj.SourceFlags)
                    && (obj.SourceFlags.IndexOf("static", StringComparison.OrdinalIgnoreCase) >= 0
                        || obj.SourceFlags.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0
                        || obj.SourceFlags.IndexOf("area", StringComparison.OrdinalIgnoreCase) >= 0));

            // Hard safety rail: in authoritative mode, local JSON entries with any non-zero
            // identity instance are treated as dynels and must come from server snapshots only.
            if (obj.IdentityInstance != 0)
                return true;

            if (obj.IsNpc == true || obj.IsPlayer == true || obj.IsPet == true || obj.IsSimpleChar == true)
                return true;
            if (obj.MonsterData.HasValue && obj.MonsterData.Value > 0)
                return true;

            string objectType = ResolveRuntimeObjectTypeLabel(obj);
            if (!string.IsNullOrWhiteSpace(objectType))
            {
                string t = objectType.Trim();
                if (t.IndexOf("npc", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("mob", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("monster", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("pet", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("dynel", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                // If type clearly indicates world prop/static structure, allow it.
                if (t.IndexOf("static", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("prop", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("flora", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("scenery", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            // Named character-like entries without explicit static markers are likely server dynels.
            if (!string.IsNullOrWhiteSpace(obj.DisplayName) && !hasStrongStaticMarkers)
                return true;
            if (!string.IsNullOrWhiteSpace(obj.Name) && !hasStrongStaticMarkers)
                return true;

            if (hasStrongStaticMarkers)
                return false;

            return true;
        }

        private bool TryLoadIndoorRoomFootprintsFromAOInstall(int pf,
            out Vector3 centerWorld)
        {
            centerWorld = Vector3.zero;
            AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid)
                return false;

            AOPlayfieldDefinition definition;
            try
            {
                using (var database = new AOResourceDatabase(install.RootPath))
                {
                    if (!database.TryReadRaw(AOResourceTypes.Playfield, pf, out byte[] raw))
                        return false;
                    definition = AOPlayfieldDefinitionDecoder.Decode(raw, pf);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed decoding AO playfield {pf}: {exception.Message}");
                return false;
            }

            if (!definition.IsIndoor || definition.Rooms.Count == 0)
                return false;

            bool useCenteredCoordinates = UseCenteredPlayfieldCoordinates;
            float positionScale = EffectivePlayfieldCoordinateScale;
            Vector3 horizontalCenter = Vector3.zero;
            for (int index = 0; index < definition.Rooms.Count; index++)
            {
                AOIndoorRoom room = definition.Rooms[index];
                horizontalCenter += new Vector3(room.X + room.CenterX, 0f,
                    room.Z + room.CenterZ);
            }
            horizontalCenter /= definition.Rooms.Count;

            var root = new GameObject($"PF_{pf}_{definition.Name}_AOInstall");
            root.transform.SetParent(_worldRoot, false);
            var material = BuildIndoorRoomFloorMaterial();
            bool loadedSurfaces = TryLoadIndoorRoomSurfacesFromAOInstall(
                pf,
                install,
                definition,
                root.transform,
                horizontalCenter,
                useCenteredCoordinates,
                positionScale);
            if (!loadedSurfaces && loadIndoorRoomSurfacesFromJson)
            {
                loadedSurfaces = TryLoadIndoorRoomSurfacesJson(
                    pf,
                    root.transform,
                    horizontalCenter,
                    useCenteredCoordinates,
                    positionScale);
            }
            int built = 0;
            for (int index = 0; index < definition.Rooms.Count; index++)
            {
                AOIndoorRoom room = definition.Rooms[index];
                float widthAo = Mathf.Max(2f, (room.TileX2 - room.TileX1) * 2f);
                float lengthAo = Mathf.Max(2f, (room.TileY2 - room.TileY1) * 2f);
                Vector3 centerAo = new Vector3(room.X + room.CenterX, room.Y,
                    room.Z + room.CenterZ);
                Vector3 center = useCenteredCoordinates
                    ? (centerAo - horizontalCenter) * positionScale
                    : centerAo * positionScale;

                if (!loadedSurfaces)
                {
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floor.name = $"AOInstallRoom_{index}_{SanitizeRoomName(room.Name)}";
                    floor.transform.SetParent(root.transform, false);
                    floor.transform.position = center
                        + Vector3.down * (indoorRoomFloorThickness * 0.5f);
                    floor.transform.rotation = Quaternion.Euler(0f,
                        room.RotationQuarterTurns * 90f, 0f);
                    floor.transform.localScale = new Vector3(widthAo * positionScale,
                        indoorRoomFloorThickness, lengthAo * positionScale);
                    var renderer = floor.GetComponent<MeshRenderer>();
                    if (renderer != null && material != null)
                        renderer.sharedMaterial = material;
                }
                built++;
            }

            Transform placeholder = _worldRoot != null ? _worldRoot.Find("Ground") : null;
            if (placeholder != null) placeholder.gameObject.SetActive(false);

            _activePlayfieldRoot = root.transform;
            _activePlayfieldId = pf;
            _activeHorizontalCenter = horizontalCenter;
            _activeUseCenteredCoordinates = useCenteredCoordinates;
            _activeCoordinateScale = positionScale;
            _activePlayfieldUsesIndoorRoomSurfaces = loadedSurfaces;
            centerWorld = useCenteredCoordinates
                ? Vector3.zero
                : horizontalCenter * positionScale;
            Debug.Log($"Loaded AO-install indoor layout for playfield {pf} "
                + $"({definition.Name}): rooms={built}, tilemap={definition.TilemapId}. "
                + (loadedSurfaces
                    ? " Loaded recovered room surface meshes."
                    : " Room surface meshes are unavailable; rendering AO-derived footprints."));
            return built > 0;
        }

        private System.Collections.IEnumerator SpawnDeferredRuntimeWorldObjectsCoroutine(
            List<PendingRuntimeSpawnEntry> entries,
            Transform runtimeRoot,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            Dictionary<string, GameObject> prefabCache,
            MeshRotationOverrideSet meshRotationOverrides,
            int playfieldId)
        {
            if (entries == null || entries.Count == 0 || runtimeRoot == null)
                yield break;

            int batch = Mathf.Max(1, runtimeObjectBackgroundSpawnBatch);
            int processedInBatch = 0;
            float startedAt = _deferredRuntimeSpawnStartedAt > 0f ? _deferredRuntimeSpawnStartedAt : Time.unscaledTime;
            float dynamicRadius = Mathf.Max(runtimeObjectRing0Radius, runtimeObjectStreamingRadius);
            float dynamicRadiusSq = dynamicRadius * dynamicRadius;
            float maxDefer = Mathf.Max(1f, runtimeObjectStreamingMaxDeferSeconds);
            float frameBudgetMs = Mathf.Max(0.25f, runtimeObjectSpawnFrameBudgetMs);
            var queue = new Queue<PendingRuntimeSpawnEntry>(entries);
            _runtimeDeferredQueueCount = queue.Count;

            while (queue.Count > 0)
            {
                if (runtimeRoot == null || runtimeRoot.gameObject == null)
                    yield break;
                if (_activePlayfieldId != playfieldId)
                    yield break;

                float frameStartedAt = Time.realtimeSinceStartup;
                Transform player = ResolvePlayerTransform();
                bool exceededFrameBudget = false;

                while (queue.Count > 0 && !exceededFrameBudget)
                {
                    var next = queue.Dequeue();
                bool shouldSpawnNow = player == null;
                if (!shouldSpawnNow)
                {
                    Vector3 world = ConvertRuntimeAoToWorldPosition(next.AoPosition, horizontalCenter, centerAroundOrigin, positionScale);
                    Vector3 d = world - player.position;
                    d.y = 0f;
                    shouldSpawnNow = d.sqrMagnitude <= dynamicRadiusSq
                        || (Time.unscaledTime - startedAt) >= maxDefer;
                }

                if (!shouldSpawnNow)
                {
                    queue.Enqueue(next);
                    _runtimeDeferredQueueCount = queue.Count;
                    processedInBatch++;
                    if (processedInBatch >= batch)
                        processedInBatch = 0;
                    float elapsedMs = (Time.realtimeSinceStartup - frameStartedAt) * 1000f;
                    exceededFrameBudget = elapsedMs >= frameBudgetMs;
                    continue;
                }

                TrySpawnPreparedRuntimeWorldObject(
                    next,
                    runtimeRoot,
                    horizontalCenter,
                    centerAroundOrigin,
                    positionScale,
                    prefabCache,
                    meshRotationOverrides,
                    playfieldId);

                processedInBatch++;
                _runtimeDeferredQueueCount = queue.Count;
                if (processedInBatch >= batch)
                    processedInBatch = 0;

                    float spawnElapsedMs = (Time.realtimeSinceStartup - frameStartedAt) * 1000f;
                    exceededFrameBudget = spawnElapsedMs >= frameBudgetMs;
                }

                yield return null;
            }

            _runtimeObjectBackgroundSpawnCoroutine = null;
            _deferredRuntimeSpawnStartedAt = -1f;
            _runtimeDeferredQueueCount = 0;
        }

        private System.Collections.IEnumerator SpawnDeferredStaticStatelsCoroutine(
            List<PendingStaticStatelSpawnEntry> entries,
            int startIndex,
            Transform pfRoot,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            Dictionary<string, GameObject> prefabCache,
            bool applyPackageMeshParts,
            MeshRotationOverrideSet meshRotationOverrides,
            int playfieldId)
        {
            if (entries == null || entries.Count == 0 || pfRoot == null || startIndex >= entries.Count)
                yield break;

            int index = Mathf.Max(0, startIndex);
            int batch = Mathf.Max(1, staticStatelBackgroundSpawnBatch);
            float frameBudgetMs = Mathf.Max(0.25f, staticStatelSpawnFrameBudgetMs);
            while (index < entries.Count)
            {
                if (pfRoot == null || pfRoot.gameObject == null)
                    yield break;
                if (_activePlayfieldId != playfieldId)
                    yield break;

                float frameStartedAt = Time.realtimeSinceStartup;
                int processed = 0;
                while (index < entries.Count && processed < batch)
                {
                    SpawnPreparedStaticStatel(
                        entries[index],
                        pfRoot,
                        horizontalCenter,
                        centerAroundOrigin,
                        positionScale,
                        prefabCache,
                        applyPackageMeshParts,
                        meshRotationOverrides,
                        playfieldId);

                    index++;
                    processed++;
                    _staticDeferredQueueCount = entries.Count - index;
                    float elapsedMs = (Time.realtimeSinceStartup - frameStartedAt) * 1000f;
                    if (elapsedMs >= frameBudgetMs)
                        break;
                }

                yield return null;
            }

            _staticStatelBackgroundSpawnCoroutine = null;
            _staticDeferredQueueCount = 0;
        }

        private bool SpawnPreparedStaticStatel(
            PendingStaticStatelSpawnEntry entry,
            Transform pfRoot,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            Dictionary<string, GameObject> prefabCache,
            bool applyPackageMeshParts,
            MeshRotationOverrideSet meshRotationOverrides,
            int playfieldId)
        {
            if (entry == null || entry.Statel == null || pfRoot == null)
                return false;

            var spawned = SpawnStatelObject(
                pfRoot,
                entry.Statel,
                entry.MeshName,
                horizontalCenter,
                centerAroundOrigin,
                positionScale,
                Mathf.Max(0.1f, placeholderScaleMultiplier),
                loadMeshPrefabsFromResources,
                meshPrefabResourcesFolder,
                prefabCache,
                applyGlobalMeshBasisCorrection,
                globalMeshBasisCorrectionEuler,
                meshPrefabRotationOffsetEuler,
                entry.PerMeshRotationEuler,
                entry.PerMeshRotationIsAbsolute,
                entry.PerMeshPositionOffset,
                entry.MeshKey,
                entry.Statel.MeshParts,
                applyPackageMeshParts,
                applyPackageMeshParts && packageMeshPartApplyPosition,
                applyPackageMeshParts && packageMeshPartApplyRotation,
                applyPackageMeshParts && packageMeshPartApplyScale,
                applyPackageMeshParts && packageMeshPartConvertAoQuaternionToUnity,
                meshRotationOverrides.NodeRotationByMeshAndNode,
                meshRotationOverrides.NodePositionOffsetByMeshAndNode,
                enableBuiltInMeshPlacementCorrection,
                addStatelColliders,
                statelCollidersConvex,
                statelCollidersIsTrigger,
                Mathf.Max(1, maxMeshCollidersPerStatel));

            if (logMeshRotationOverrideApplications
                && spawned != null
                && !string.IsNullOrWhiteSpace(entry.MeshKey)
                && !string.IsNullOrWhiteSpace(meshRotationOverrideDebugFilter)
                && entry.MeshKey.IndexOf(meshRotationOverrideDebugFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log(
                    $"Mesh rotation debug PF {playfieldId} statel={entry.Statel.StatelId} meshKey='{entry.MeshKey}' " +
                    $"statelRot={entry.Statel.Rotation.ToString("F3")} globalBasis={globalMeshBasisCorrectionEuler.ToString("F3")} " +
                    $"meshPrefabOffset={meshPrefabRotationOffsetEuler.ToString("F3")} " +
                    $"perMeshRotation={entry.PerMeshRotationEuler.ToString("F3")} rotationMode={(entry.PerMeshRotationIsAbsolute ? "Absolute" : "Offset")} " +
                    $"finalEuler={spawned.transform.eulerAngles.ToString("F3")}.");
            }

            return spawned != null;
        }

        private bool TrySpawnPreparedRuntimeWorldObject(
            PendingRuntimeSpawnEntry entry,
            Transform runtimeRoot,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            Dictionary<string, GameObject> prefabCache,
            MeshRotationOverrideSet meshRotationOverrides,
            int playfieldId)
        {
            if (entry == null || entry.Obj == null || runtimeRoot == null)
                return false;

            var obj = entry.Obj;
            var runtimeStatel = new StatelData
            {
                StatelId = Mathf.Max(0, obj.TemplateId ?? obj.MeshId ?? obj.Mesh ?? obj.IdentityInstance),
                Position = entry.AoPosition,
                Rotation = ResolveRuntimeWorldObjectEulerAngles(obj),
                Scale = ResolveRuntimeWorldObjectScale(obj)
            };

            GameObject go = SpawnStatelObject(
                runtimeRoot,
                runtimeStatel,
                entry.MeshName,
                horizontalCenter,
                centerAroundOrigin,
                positionScale,
                1f,
                loadMeshPrefabsFromResources,
                meshPrefabResourcesFolder,
                prefabCache,
                applyGlobalMeshBasisCorrection,
                globalMeshBasisCorrectionEuler,
                meshPrefabRotationOffsetEuler,
                entry.PerMeshRotationEuler,
                entry.PerMeshRotationIsAbsolute,
                entry.PerMeshPositionOffset,
                entry.MeshKey,
                runtimeStatel.MeshParts,
                false,
                false,
                false,
                false,
                false,
                meshRotationOverrides.NodeRotationByMeshAndNode,
                meshRotationOverrides.NodePositionOffsetByMeshAndNode,
                enableBuiltInMeshPlacementCorrection,
                addStatelColliders,
                statelCollidersConvex,
                statelCollidersIsTrigger,
                Mathf.Max(1, maxMeshCollidersPerStatel));

            if (go == null)
                return false;

            string runtimeName = !string.IsNullOrWhiteSpace(obj.ImportKey)
                ? obj.ImportKey
                : $"{ResolveRuntimeObjectTypeLabel(obj)}_{obj.TemplateId ?? obj.MeshId ?? obj.Mesh ?? obj.IdentityInstance}";
            go.name = $"Runtime_{runtimeName}";
            ConfigureRuntimeDynelTargetingAndColliders(
                go,
                ResolveRuntimeObjectTypeLabel(obj),
                string.IsNullOrWhiteSpace(obj.DisplayName) ? obj.Name : obj.DisplayName,
                obj,
                playfieldId);
            TryQueueRuntimeObjectGlbVisualFallback(go, obj, entry.MeshName);
            return true;
        }

        private static Vector3 ConvertRuntimeAoToWorldPosition(
            Vector3 aoPosition,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            Vector3 source = centerAroundOrigin
                ? (aoPosition - horizontalCenter)
                : aoPosition;
            return source * positionScale;
        }

        private static List<RuntimeWorldObjectData> GetRuntimeObjects(RuntimeWorldObjectsFile runtimeFile)
        {
            if (runtimeFile == null)
                return null;

            if (runtimeFile.RuntimeWorldObjects != null && runtimeFile.RuntimeWorldObjects.Count > 0)
                return runtimeFile.RuntimeWorldObjects;

            if (runtimeFile.Dynels == null || runtimeFile.Dynels.Count == 0)
                return null;

            for (int i = 0; i < runtimeFile.Dynels.Count; i++)
            {
                var dynel = runtimeFile.Dynels[i];
                if (dynel == null)
                    continue;

                if (string.IsNullOrWhiteSpace(dynel.ObjectType))
                {
                    if (dynel.IsNpc == true)
                        dynel.ObjectType = "NPC";
                    else if (dynel.IsPlayer == true)
                        dynel.ObjectType = "Player";
                    else if (dynel.IsPet == true)
                        dynel.ObjectType = "Pet";
                    else if (!string.IsNullOrWhiteSpace(dynel.IdentityType))
                        dynel.ObjectType = dynel.IdentityType;
                    else
                        dynel.ObjectType = "Dynel";
                }

                if (string.IsNullOrWhiteSpace(dynel.DisplayName))
                    dynel.DisplayName = dynel.Name;
            }

            return runtimeFile.Dynels;
        }

        private static string ResolveRuntimeObjectTypeLabel(RuntimeWorldObjectData obj)
        {
            if (!string.IsNullOrWhiteSpace(obj?.ObjectType))
                return obj.ObjectType;
            if (!string.IsNullOrWhiteSpace(obj?.IdentityType))
                return obj.IdentityType;
            return "RuntimeObject";
        }

        private static Vector3? ResolveRuntimeWorldObjectAoPosition(RuntimeWorldObjectData obj)
        {
            if (obj == null)
                return null;

            Vector3Data source = obj.GlobalPosition ?? obj.Position;
            if (source == null)
                return null;

            return new Vector3(source.X, source.Y, source.Z);
        }

        private static Vector3 ResolveRuntimeWorldObjectEulerAngles(RuntimeWorldObjectData obj)
        {
            if (obj?.Rotation != null)
            {
                var quaternion = new Quaternion(
                    obj.Rotation.X,
                    obj.Rotation.Y,
                    obj.Rotation.Z,
                    obj.Rotation.W);

                if (float.IsFinite(quaternion.x)
                    && float.IsFinite(quaternion.y)
                    && float.IsFinite(quaternion.z)
                    && float.IsFinite(quaternion.w)
                    && ((quaternion.x * quaternion.x)
                        + (quaternion.y * quaternion.y)
                        + (quaternion.z * quaternion.z)
                        + (quaternion.w * quaternion.w)) > 0.0001f)
                {
                    return quaternion.eulerAngles;
                }
            }

            return new Vector3(0f, obj?.YawDegrees ?? 0f, 0f);
        }

        private static float ResolveRuntimeWorldObjectScale(RuntimeWorldObjectData obj)
        {
            if (obj == null)
                return 1f;

            float factor = obj.ScaleFactor ?? 0f;
            if (float.IsFinite(factor) && factor > 0.01f && factor <= 10f)
                return factor;

            int raw = obj.ScaleRaw ?? 0;
            if (raw > 0 && raw <= 1000)
            {
                float scaled = raw / 100f;
                if (float.IsFinite(scaled) && scaled > 0.01f && scaled <= 10f)
                    return scaled;
            }

            return 1f;
        }

        private bool TryCreatePlayfieldFallbackGround(
            int pf,
            Transform parent,
            List<StatelData> validStatels,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            bool haveExtents = false;
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            if (validStatels != null)
            {
                for (int i = 0; i < validStatels.Count; i++)
                {
                    var statel = validStatels[i];
                    var source = centerAroundOrigin
                        ? (statel.Position - horizontalCenter)
                        : statel.Position;

                    var point = source * positionScale;
                    if (!IsFiniteVector3(point))
                        continue;

                    min = Vector3.Min(min, point);
                    max = Vector3.Max(max, point);
                    haveExtents = true;
                }
            }

            float width = Mathf.Max(10f, playfieldFallbackGroundDefaultSize);
            float length = Mathf.Max(10f, playfieldFallbackGroundDefaultSize);
            float minY = -0.5f;
            Vector3 center = new Vector3(0f, minY, 0f);

            if (haveExtents && IsFiniteVector3(min) && IsFiniteVector3(max))
            {
                width = Mathf.Max(10f, (max.x - min.x) + playfieldFallbackGroundPadding * 2f);
                length = Mathf.Max(10f, (max.z - min.z) + playfieldFallbackGroundPadding * 2f);
                minY = Mathf.Min(min.y, max.y);
                center = new Vector3((min.x + max.x) * 0.5f, minY, (min.z + max.z) * 0.5f);
            }

            float thickness = Mathf.Max(0.25f, playfieldFallbackGroundHeight);
            center.y -= thickness * 0.5f;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = $"PF_{pf}_FallbackGround";
            ground.transform.SetParent(parent, false);
            ground.transform.position = center;
            ground.transform.localScale = new Vector3(width, thickness, length);

            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    renderer.sharedMaterial = new Material(shader);
                    if (renderer.sharedMaterial.HasProperty("_BaseColor"))
                        renderer.sharedMaterial.SetColor("_BaseColor", new Color(0.18f, 0.22f, 0.18f, 1f));
                    if (renderer.sharedMaterial.HasProperty("_Color"))
                        renderer.sharedMaterial.SetColor("_Color", new Color(0.18f, 0.22f, 0.18f, 1f));
                }
            }

            Debug.Log(
                $"Created fallback ground for playfield {pf}: " +
                $"center={center:F3} size=({width:F1}, {thickness:F1}, {length:F1}) " +
                $"fromBounds={haveExtents}.");
            return true;
        }

        private Dictionary<int, string> LoadGlobalModelNameMapByInstanceId()
        {
            if (_modelNameByInstanceId != null)
                return _modelNameByInstanceId;

            _modelNameByInstanceId = new Dictionary<int, string>();
            string file = Path.Combine(Application.streamingAssetsPath, "AOData", "model_info_map.json");
            if (!File.Exists(file))
                return _modelNameByInstanceId;

            try
            {
                var entries = JsonConvert.DeserializeObject<List<ModelInfoEntry>>(File.ReadAllText(file));
                if (entries == null)
                    return _modelNameByInstanceId;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null || entry.InstanceId <= 0 || string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    _modelNameByInstanceId[entry.InstanceId] = entry.Name;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse model info map {file}: {ex.Message}");
            }

            return _modelNameByInstanceId;
        }

        private string ResolveAuthoritativeRuntimeMeshName(
            string meshName,
            int? templateId,
            int? meshId,
            int identityInstance,
            string displayName)
        {
            if (!IsIgnorableRuntimeMeshName(meshName))
                return meshName;

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                if (displayName.Equals("Enter The Grid", StringComparison.OrdinalIgnoreCase))
                    return "grid_access_terminal.abiff";
                if (displayName.Equals("Exit the Grid", StringComparison.OrdinalIgnoreCase)
                    || displayName.Equals("To Infested Serenity Island", StringComparison.OrdinalIgnoreCase))
                {
                    return "transport_access_terminal.abiff";
                }
            }

            var modelNameByInstanceId = LoadGlobalModelNameMapByInstanceId();
            var candidateIds = new List<int>(6)
            {
                meshId ?? 0,
                templateId ?? 0,
                identityInstance
            };

            // Runtime extracts often carry negative identity values for dynels.
            // Model map keys are typically positive; try absolute variants too.
            if (identityInstance < 0)
                candidateIds.Add(Math.Abs(identityInstance));
            if (meshId.HasValue && meshId.Value < 0)
                candidateIds.Add(Math.Abs(meshId.Value));
            if (templateId.HasValue && templateId.Value < 0)
                candidateIds.Add(Math.Abs(templateId.Value));

            for (int i = 0; i < candidateIds.Count; i++)
            {
                int id = candidateIds[i];
                if (id <= 0)
                    continue;

                if (!modelNameByInstanceId.TryGetValue(id, out var resolvedMeshName) || string.IsNullOrWhiteSpace(resolvedMeshName))
                    continue;

                string fileName = SafeGetFileNameWithoutExtension(resolvedMeshName);
                if (fileName.StartsWith("collsphere_", StringComparison.OrdinalIgnoreCase))
                    continue;

                return resolvedMeshName;
            }

            return null;
        }

        private static string ResolveRuntimeWorldObjectMeshName(
            RuntimeWorldObjectData obj,
            Dictionary<int, string> modelNameByInstanceId)
        {
            if (obj == null)
                return null;

            if (!IsIgnorableRuntimeMeshName(obj.MeshName))
                return obj.MeshName;
            if (!IsIgnorableRuntimeMeshName(obj.MeshLookupName))
                return obj.MeshLookupName;

            string templateName = obj.TemplateName ?? string.Empty;
            if (templateName.Equals("Enter The Grid", StringComparison.OrdinalIgnoreCase))
                return "grid_access_terminal.abiff";
            if (templateName.Equals("Exit the Grid", StringComparison.OrdinalIgnoreCase))
                return "transport_access_terminal.abiff";

            int[] candidateIds =
            {
                obj.MeshId ?? 0,
                obj.Mesh ?? 0,
                obj.CATMesh ?? 0,
                obj.DisplayCATMesh ?? 0,
                obj.LowresMesh ?? 0,
                obj.HeadMesh ?? 0,
                obj.HairMesh ?? 0,
                obj.BackMesh ?? 0,
                obj.ShoulderMesh ?? 0,
                obj.WeaponMesh ?? 0,
                obj.SourceMesh ?? 0,
                obj.TemplateId ?? 0,
                obj.StaticInstance ?? 0,
                obj.SourceStaticInstance ?? 0,
                obj.BuildingInstance ?? 0,
                obj.SourceBuildingInstance ?? 0,
                obj.AreaInstance ?? 0,
                obj.SourceAreaInstance ?? 0
            };

            for (int i = 0; i < candidateIds.Length; i++)
            {
                int id = candidateIds[i];
                if (id <= 0)
                    continue;

                if (!modelNameByInstanceId.TryGetValue(id, out var meshName) || string.IsNullOrWhiteSpace(meshName))
                    continue;

                string fileName = SafeGetFileNameWithoutExtension(meshName);
                if (fileName.StartsWith("collsphere_", StringComparison.OrdinalIgnoreCase))
                    continue;

                return meshName;
            }

            return null;
        }

        private void TryQueueRuntimeObjectGlbVisualFallback(GameObject host, RuntimeWorldObjectData obj, string resolvedMeshName)
        {
            if (obj == null)
                return;

            TryQueueRuntimeObjectGlbVisualFallback(
                host,
                ResolveRuntimeObjectTypeLabel(obj),
                string.IsNullOrWhiteSpace(obj.DisplayName) ? obj.Name : obj.DisplayName,
                resolvedMeshName,
                obj.IdentityInstance);
        }

        private void TryQueueRuntimeObjectGlbVisualFallback(
            GameObject host,
            string objectType,
            string displayName,
            string resolvedMeshName,
            int identityInstance)
        {
            if (!loadRuntimeObjectVisualsFromGlbFallback || host == null)
                return;
            if (!HostNeedsRuntimeGlbFallback(host))
                return;

            List<string> candidates = ResolveRuntimeObjectGlbCandidatePaths(objectType, resolvedMeshName);
            var state = host.GetComponent<RuntimeDynelGlbFallbackState>();
            if (state == null)
                state = host.AddComponent<RuntimeDynelGlbFallbackState>();
            state.CandidatePaths.Clear();
            state.CandidatePaths.AddRange(candidates);
            state.NextRetryAt = Time.unscaledTime + UnityEngine.Random.Range(0.05f, Mathf.Max(0.5f, runtimeDynelGlbRetryIntervalSeconds));
            state.RetryCount = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                string path = candidates[i];
                if (!File.Exists(path))
                    continue;

                if (logRuntimeObjectGlbFallback)
                {
                    Debug.Log(
                        $"Runtime GLB fallback for {host.name}: objectType={objectType}, displayName='{displayName}', " +
                        $"identity={identityInstance}, path='{path}'.");
                }

                // Presentation preference: if we know a real mesh file exists, hide primitive
                // placeholders while streaming that mesh in. Keep placeholders only when no mesh
                // candidate exists at all.
                if (hideRuntimePlaceholdersWhenMeshCandidateExists)
                    HideRuntimePlaceholderVisuals(host);

                if (TryAttachCachedRuntimeGlbVisual(host, path))
                {
                    Destroy(state);
                    return;
                }

                if (state.RetryCount >= Mathf.Max(1, runtimeDynelGlbMaxRetriesPerHost))
                {
                    // Keep placeholder visuals visible when retry budget is exhausted.
                    ShowRuntimePlaceholderVisuals(host);
                    Destroy(state);
                    return;
                }

                if (IsRuntimeGlbPathTemporarilyBlocked(path))
                {
                    ShowRuntimePlaceholderVisuals(host);
                    state.NextRetryAt = Time.unscaledTime + Mathf.Max(1f, runtimeDynelGlbRetryIntervalSeconds);
                    state.RetryCount++;
                    return;
                }

                int loadTicket = _activePlayfieldGlbLoadTicket;
                Transform expectedRoot = _activePlayfieldRoot;
                EnqueueRuntimeGlbLoad(host, path, loadTicket, expectedRoot);
                return;
            }
        }


        private static void HideRuntimePlaceholderVisuals(GameObject host)
        {
            if (host == null)
                return;

            var placeholders = host.GetComponentsInChildren<RuntimePlaceholderVisualMarker>(true);
            for (int i = 0; i < placeholders.Length; i++)
            {
                var marker = placeholders[i];
                if (marker == null)
                    continue;

                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.enabled = false;
            }
        }

        private static void ShowRuntimePlaceholderVisuals(GameObject host)
        {
            if (host == null)
                return;

            var placeholders = host.GetComponentsInChildren<RuntimePlaceholderVisualMarker>(true);
            for (int i = 0; i < placeholders.Length; i++)
            {
                var marker = placeholders[i];
                if (marker == null)
                    continue;

                var renderer = marker.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.enabled = true;
            }
        }

        private bool IsRuntimeGlbPathTemporarilyBlocked(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (_runtimeGlbPathBlockedUntil.TryGetValue(path, out float blockedUntil))
            {
                if (Time.unscaledTime < blockedUntil)
                    return true;
                _runtimeGlbPathBlockedUntil.Remove(path);
            }
            return false;
        }

        private void MarkRuntimeGlbPathTemporarilyBlocked(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            if (runtimeGlbBlockFailedPathForSession)
            {
                _runtimeGlbPathBlockedUntil[path] = float.MaxValue;
                return;
            }

            _runtimeGlbPathBlockedUntil[path] = Time.unscaledTime + Mathf.Max(5f, runtimeGlbFailureCooldownSeconds);
        }

        private void EnqueueRuntimeGlbLoad(GameObject host, string path, int loadTicket, Transform expectedRoot)
        {
            if (host == null || string.IsNullOrWhiteSpace(path))
                return;

            if (TryAttachCachedRuntimeGlbVisual(host, path))
                return;

            if (_runtimeGlbPathsInFlight.Contains(path))
            {
                if (!_runtimeGlbWaitersByPath.TryGetValue(path, out var waiters) || waiters == null)
                {
                    waiters = new List<GameObject>();
                    _runtimeGlbWaitersByPath[path] = waiters;
                }
                if (!waiters.Contains(host))
                    waiters.Add(host);
                return;
            }

            _runtimeGlbLoadQueue.Enqueue(new RuntimeGlbLoadRequest
            {
                Host = host,
                Path = path,
                LoadTicket = loadTicket,
                ExpectedRoot = expectedRoot,
                PrewarmOnly = false
            });

            if (_runtimeGlbLoadPumpCoroutine == null)
                _runtimeGlbLoadPumpCoroutine = StartCoroutine(RuntimeGlbLoadPumpCoroutine());
        }

        private void EnqueueRuntimeGlbPrewarm(string path, int loadTicket, Transform expectedRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            if (_runtimeGlbVisualTemplateByPath.ContainsKey(path) || _runtimeGlbPathsInFlight.Contains(path))
                return;

            _runtimeGlbLoadQueue.Enqueue(new RuntimeGlbLoadRequest
            {
                Host = null,
                Path = path,
                LoadTicket = loadTicket,
                ExpectedRoot = expectedRoot,
                PrewarmOnly = true
            });

            if (_runtimeGlbLoadPumpCoroutine == null)
                _runtimeGlbLoadPumpCoroutine = StartCoroutine(RuntimeGlbLoadPumpCoroutine());
        }

        private System.Collections.IEnumerator RuntimeGlbLoadPumpCoroutine()
        {
            while (_runtimeGlbLoadQueue.Count > 0 || _runtimeGlbLoadsInFlight > 0)
            {
                int startedThisFrame = 0;
                int maxConcurrent = Mathf.Max(1, maxConcurrentRuntimeGlbLoads);
                int maxStarts = Mathf.Max(1, maxRuntimeGlbStartsPerFrame);

                while (_runtimeGlbLoadQueue.Count > 0
                    && _runtimeGlbLoadsInFlight < maxConcurrent
                    && startedThisFrame < maxStarts)
                {
                    RuntimeGlbLoadRequest request = _runtimeGlbLoadQueue.Dequeue();
                    if (request == null || string.IsNullOrWhiteSpace(request.Path))
                        continue;
                    if (!request.PrewarmOnly && request.Host == null)
                        continue;
                    if (_runtimeGlbPathsInFlight.Contains(request.Path))
                        continue;

                    _runtimeGlbLoadsInFlight++;
                    _runtimeGlbPathsInFlight.Add(request.Path);
                    startedThisFrame++;
                    StartCoroutine(LoadRuntimeObjectGlbVisualCoroutine(
                        request.Host,
                        request.Path,
                        request.LoadTicket,
                        request.ExpectedRoot,
                        request.PrewarmOnly));
                }

                yield return null;
            }

            _runtimeGlbLoadPumpCoroutine = null;
        }

        private void QueueRuntimeGlbTemplatePrewarm(List<RuntimeWorldObjectData> runtimeObjects)
        {
            if (!prewarmRuntimeGlbTemplates || runtimeObjects == null || runtimeObjects.Count == 0)
                return;

            int queued = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < runtimeObjects.Count; i++)
            {
                if (queued >= Mathf.Max(1, maxRuntimeGlbTemplatePrewarmPerPlayfield))
                    break;

                var obj = runtimeObjects[i];
                if (obj == null)
                    continue;
                if (ShouldSkipLocalRuntimeObjectInAuthoritativeMode(obj))
                    continue;

                var candidates = ResolveRuntimeObjectGlbCandidatePaths(
                    ResolveRuntimeObjectTypeLabel(obj),
                    ResolveRuntimeWorldObjectMeshName(obj, _modelNameByInstanceId ?? LoadGlobalModelNameMapByInstanceId()));
                string selected = null;
                for (int c = 0; c < candidates.Count; c++)
                {
                    string path = candidates[c];
                    if (!File.Exists(path))
                        continue;
                    selected = path;
                    break;
                }

                if (string.IsNullOrWhiteSpace(selected))
                    continue;
                if (!seen.Add(selected))
                    continue;
                if (_runtimeGlbVisualTemplateByPath.ContainsKey(selected) || _runtimeGlbPathsInFlight.Contains(selected))
                    continue;

                EnqueueRuntimeGlbPrewarm(selected, _activePlayfieldGlbLoadTicket, _activePlayfieldRoot);
                queued++;
            }
        }

        private List<string> ResolveRuntimeObjectGlbCandidatePaths(string objectType, string resolvedMeshName)
        {
            var paths = new List<string>();
            var keys = new List<string>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddKey(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;

                string normalized = SafeGetFileNameWithoutExtension(key.Trim());
                if (string.IsNullOrWhiteSpace(normalized) || IsIgnorableRuntimeMeshName(normalized))
                    return;

                if (seenKeys.Add(normalized))
                    keys.Add(normalized);
            }

            AddKey(resolvedMeshName);

            bool preferCharacterMeshes = IsCharacterLikeRuntimeObject(objectType, resolvedMeshName);
            string[] folders = preferCharacterMeshes
                ? new[] { "CharacterMeshes", "ItemMeshes", "Meshes" }
                : new[] { "ItemMeshes", "Meshes", "CharacterMeshes" };

            for (int i = 0; i < folders.Length; i++)
            {
                string folder = folders[i];
                for (int k = 0; k < keys.Count; k++)
                {
                    string key = keys[k];
                    paths.Add(Path.Combine(Application.streamingAssetsPath, "AOData", folder, $"{key}.glb"));
                }
            }

            return paths;
        }

        private static bool IsCharacterLikeRuntimeObject(string objectType, string resolvedMeshName)
        {
            string type = objectType?.Trim() ?? string.Empty;
            string mesh = resolvedMeshName?.Trim() ?? string.Empty;

            if (mesh.EndsWith(".cir", StringComparison.OrdinalIgnoreCase))
                return true;

            return type.Equals("NPC", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Player", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Pet", StringComparison.OrdinalIgnoreCase)
                || type.Equals("SimpleChar", StringComparison.OrdinalIgnoreCase);
        }

        private System.Collections.IEnumerator LoadRuntimeObjectGlbVisualCoroutine(
            GameObject host,
            string glbPath,
            int loadTicket,
            Transform expectedRoot,
            bool prewarmOnly)
        {
            bool success = false;
            bool sawDuplicateKeyframeError = false;
            bool logHandlerSubscribed = false;
            void LogHandler(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Error)
                    return;
                if (condition != null
                    && condition.IndexOf("ACCESSOR_ANIMATION_INPUT_NON_INCREASING", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sawDuplicateKeyframeError = true;
                }
                else if (condition != null
                    && condition.IndexOf("Time of subsequent animation keyframes is not increasing", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    sawDuplicateKeyframeError = true;
                }
            }
            try
            {
                if (string.IsNullOrWhiteSpace(glbPath))
                    yield break;
                if (!prewarmOnly && host == null)
                    yield break;
                if (!File.Exists(glbPath))
                    yield break;

                string runtimeLoadPath = ResolveRuntimeObjectGlbLoadPath(glbPath);
                var node = new GameObject("RuntimeVisual_GLB");
                if (!prewarmOnly && host != null)
                    node.transform.SetParent(host.transform, false);

                bool disableAnimations = ShouldDisableRuntimeFallbackAnimations(host, runtimeLoadPath, prewarmOnly);
                object retainedImporter = null;

                Application.logMessageReceivedThreaded += LogHandler;
                logHandlerSubscribed = true;
                Task<bool> loadTask = TryInstantiateGlbWithReflection(
                    runtimeLoadPath,
                    node.transform,
                    importer => retainedImporter = importer,
                    disableAnimations: disableAnimations);

                while (!loadTask.IsCompleted)
                    yield return null;
                Application.logMessageReceivedThreaded -= LogHandler;
                logHandlerSubscribed = false;

                if (loadTask.Status == TaskStatus.RanToCompletion)
                {
                    success = loadTask.Result;
                }
                else if (loadTask.IsFaulted && logRuntimeObjectGlbFallback)
                {
                    string hostName = host != null ? host.name : "prewarm";
                    Debug.LogWarning($"Runtime GLB fallback failed for '{hostName}' path='{glbPath}': {loadTask.Exception?.GetBaseException().Message}");
                }

                if (!disableAnimations && sawDuplicateKeyframeError)
                {
                    MarkRuntimeGlbDisableAnimations(glbPath);
                }

                if (!success && !disableAnimations)
                {
                    // Retry once in animation-safe mode for problematic GLBs.
                    if (retainedImporter != null)
                        DisposeImporter(retainedImporter);
                    Destroy(node);
                    retainedImporter = null;
                    sawDuplicateKeyframeError = false;

                    node = new GameObject("RuntimeVisual_GLB");
                    if (!prewarmOnly && host != null)
                        node.transform.SetParent(host.transform, false);

                    Application.logMessageReceivedThreaded += LogHandler;
                    logHandlerSubscribed = true;
                    loadTask = TryInstantiateGlbWithReflection(
                        runtimeLoadPath,
                        node.transform,
                        importer => retainedImporter = importer,
                        disableAnimations: true);
                    while (!loadTask.IsCompleted)
                        yield return null;
                    Application.logMessageReceivedThreaded -= LogHandler;
                    logHandlerSubscribed = false;

                    if (loadTask.Status == TaskStatus.RanToCompletion)
                        success = loadTask.Result;
                    else
                        success = false;

                    if (success)
                        MarkRuntimeGlbDisableAnimations(glbPath);
                }

                if (!success)
                {
                    if (!prewarmOnly)
                        MarkRuntimeGlbPathTemporarilyBlocked(glbPath);
                    Destroy(node);
                    if (retainedImporter != null)
                        DisposeImporter(retainedImporter);
                    yield break;
                }

                if (!HasRenderableGeometry(node))
                {
                    success = false;
                    Destroy(node);
                    if (retainedImporter != null)
                        DisposeImporter(retainedImporter);
                    yield break;
                }
                ForceEnableRenderableGeometry(node);

                if (loadTicket != _activePlayfieldGlbLoadTicket || expectedRoot != _activePlayfieldRoot)
                {
                    Destroy(node);
                    if (retainedImporter != null)
                        DisposeImporter(retainedImporter);
                    yield break;
                }

                CacheRuntimeGlbVisualTemplate(glbPath, node);
                TryApplyRuntimeGeneralTextureFallback(node);
                if (prewarmOnly)
                {
                    Destroy(node);
                    if (retainedImporter != null)
                        DisposeImporter(retainedImporter);
                }
                else if (host != null)
                {
                    EnsureRuntimeGlbIdlePlayback(node);
                    EnsureNonBlockingDynelCollidersIfNeeded(host);
                    if (retainedImporter != null)
                        _activePlayfieldGlbImporters.Add(retainedImporter);
                    var state = host.GetComponent<RuntimeDynelGlbFallbackState>();
                    if (state != null)
                        Destroy(state);

                    // Hide primitive fallback mesh after a successful GLB visual load.
                    var hostRenderers = host.GetComponentsInChildren<Renderer>(true);
                    for (int i = 0; i < hostRenderers.Length; i++)
                    {
                        var renderer = hostRenderers[i];
                        if (renderer == null)
                            continue;

                        // Preserve world-space labels/nameplates attached under the host.
                        if (renderer.GetComponent<TextMesh>() != null)
                            continue;

                        // Keep newly loaded GLB visual renderers enabled.
                        if (renderer.transform.IsChildOf(node.transform))
                            continue;
                        // For runtime dynels, hide all non-GLB renderers on the host tree to avoid
                        // duplicate frozen mesh (base host) + animated GLB double-visuals.
                        if (!ShouldHideRuntimeHostRendererAfterGlbAttach(host, renderer))
                            continue;
                        renderer.enabled = false;
                    }
                }
            }
            finally
            {
                if (logHandlerSubscribed)
                    Application.logMessageReceivedThreaded -= LogHandler;
                _runtimeGlbPathsInFlight.Remove(glbPath);
                _runtimeGlbLoadsInFlight = Mathf.Max(0, _runtimeGlbLoadsInFlight - 1);
                FlushRuntimeGlbWaiters(glbPath, success);
            }
        }

        private void FlushRuntimeGlbWaiters(string glbPath, bool success)
        {
            if (string.IsNullOrWhiteSpace(glbPath))
                return;
            if (!_runtimeGlbWaitersByPath.TryGetValue(glbPath, out var waiters) || waiters == null || waiters.Count == 0)
                return;
            _runtimeGlbWaitersByPath.Remove(glbPath);

            if (!success)
                return;

            for (int i = 0; i < waiters.Count; i++)
            {
                var host = waiters[i];
                if (host == null)
                    continue;
                if (IsRuntimeDynelDeadOrCorpse(host))
                    continue;
                _runtimeGlbAttachQueue.Enqueue(new RuntimeGlbAttachRequest
                {
                    Host = host,
                    Path = glbPath
                });
            }
        }

        private sealed class RuntimeGlbLoadRequest
        {
            public GameObject Host;
            public string Path;
            public int LoadTicket;
            public Transform ExpectedRoot;
            public bool PrewarmOnly;
        }

        private sealed class RuntimeGlbAttachRequest
        {
            public GameObject Host;
            public string Path;
        }

        [Serializable]
        private sealed class RuntimeGlbAnimationSafetyCacheFile
        {
            public int Version = 1;
            public List<string> DisableAnimationsPaths = new();
        }

        [Serializable]
        private sealed class RuntimeGlbSanitizedMapFile
        {
            public int Version = 1;
            public List<RuntimeGlbSanitizedMapEntry> Entries = new();
        }

        [Serializable]
        private sealed class RuntimeGlbSanitizedMapEntry
        {
            public string OriginalPath;
            public string SanitizedPath;
        }

        private bool TryAttachCachedRuntimeGlbVisual(GameObject host, string glbPath)
        {
            if (host == null || string.IsNullOrWhiteSpace(glbPath))
                return false;
            if (IsRuntimeDynelDeadOrCorpse(host))
                return false;

            if (!_runtimeGlbVisualTemplateByPath.TryGetValue(glbPath, out var template) || template == null)
                return false;

            var clone = Instantiate(template, host.transform, false);
            clone.name = "RuntimeVisual_GLB";
            clone.SetActive(true);
            TryApplyRuntimeGeneralTextureFallback(clone);
            ForceEnableRenderableGeometry(clone);
            EnsureRuntimeGlbIdlePlayback(clone);
            if (!HasRenderableGeometry(clone))
            {
                Destroy(clone);
                return false;
            }
            EnsureNonBlockingDynelCollidersIfNeeded(host);

            var hostRenderers = host.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < hostRenderers.Length; i++)
            {
                var renderer = hostRenderers[i];
                if (renderer == null)
                    continue;
                if (renderer.GetComponent<TextMesh>() != null)
                    continue;
                if (renderer.transform.IsChildOf(clone.transform))
                    continue;
                if (!ShouldHideRuntimeHostRendererAfterGlbAttach(host, renderer))
                    continue;
                renderer.enabled = false;
            }

            var state = host.GetComponent<RuntimeDynelGlbFallbackState>();
            if (state != null)
                Destroy(state);

            return true;
        }

        private static void EnsureRuntimeGlbIdlePlayback(GameObject root)
        {
            if (root == null)
                return;

            var animators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;
                animator.enabled = true;
                animator.speed = 1f;
            }

            var legacyAnimations = root.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < legacyAnimations.Length; i++)
            {
                var legacy = legacyAnimations[i];
                if (legacy == null)
                    continue;
                if (legacy.isPlaying)
                    continue;

                string selected = null;
                foreach (AnimationState state in legacy)
                {
                    if (state == null || state.clip == null)
                        continue;
                    selected ??= state.name;
                    string n = state.name ?? string.Empty;
                    if (n.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        selected = state.name;
                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(selected))
                    continue;
                if (legacy.GetClip(selected) == null)
                    continue;

                var clip = legacy.GetClip(selected);
                if (clip != null)
                    clip.wrapMode = WrapMode.Loop;

                var st = legacy[selected];
                if (st != null)
                    st.wrapMode = WrapMode.Loop;

                legacy.wrapMode = WrapMode.Loop;
                legacy.Play(selected);
            }
        }

        private static bool ShouldHideRuntimeHostRendererAfterGlbAttach(GameObject host, Renderer renderer)
        {
            if (host == null || renderer == null)
                return false;

            // Hide direct host fallback renderer.
            if (renderer.gameObject == host)
                return true;

            // Hide explicit placeholder visuals only; preserve established animated visuals.
            if (renderer.GetComponent<RuntimePlaceholderVisualMarker>() != null)
                return true;
            if (renderer.GetComponentInParent<RuntimePlaceholderVisualMarker>() != null)
                return true;

            return false;
        }

        private static bool IsRuntimeDynelDeadOrCorpse(GameObject host)
        {
            if (host == null)
                return false;

            var combatState = host.GetComponent<RuntimeDynelCombatState>();
            if (combatState != null && combatState.IsDead)
                return true;

            var bridge = host.GetComponent<CharacterRuntimeBridge>();
            if (bridge != null && !string.IsNullOrWhiteSpace(bridge.DisplayNameOverride))
            {
                string n = bridge.DisplayNameOverride.Trim();
                if (n.StartsWith("Remains of ", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool HasRenderableGeometry(GameObject root)
        {
            if (root == null)
                return false;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (renderer.GetComponent<TextMesh>() != null)
                    continue;
                if (!IsUsableRuntimeRenderer(renderer))
                    continue;
                return true;
            }

            return false;
        }

        private static void ForceEnableRenderableGeometry(GameObject root)
        {
            if (root == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (renderer.GetComponent<TextMesh>() != null)
                    continue;
                if (!IsUsableRuntimeRenderer(renderer))
                    continue;
                renderer.enabled = true;
            }
        }

        private static bool IsUsableRuntimeRenderer(Renderer renderer)
        {
            if (renderer == null)
                return false;

            if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    return false;
            }
            else if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null)
                    return false;
            }

            return true;
        }

        private static bool HostNeedsRuntimeGlbFallback(GameObject host)
        {
            if (host == null)
                return false;

            // If we already have an attached runtime GLB visual, no further fallback is needed.
            var t = host.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child != null && string.Equals(child.name, "RuntimeVisual_GLB", StringComparison.Ordinal))
                    return false;
            }

            bool hasPlaceholder = host.GetComponentInChildren<RuntimePlaceholderVisualMarker>(true) != null;
            if (hasPlaceholder)
                return true;

            // Runtime dynels (NPC/Mob/etc) should still try GLB fallback even if prefab visuals exist.
            if (host.GetComponent<RuntimeDynelQuestIdentity>() != null || host.GetComponent<RuntimeDynelCombatState>() != null)
                return true;

            // If host already has non-text renderers, don't override with runtime GLB fallback.
            return !HasRenderableGeometry(host);
        }

        private bool ShouldDisableRuntimeFallbackAnimations(GameObject host, string glbPath, bool prewarmOnly)
        {
            if (prewarmOnly)
                return true;

            // Combat dynels need embedded clip import for attack/death one-shots.
            if (host != null && host.GetComponent<RuntimeDynelCombatState>() != null)
            {
                // Always allow clip import for combat dynels so death/attack one-shots
                // (e.g. reet/snake variants) can play reliably.
                return false;
            }

            if (ShouldDisableAnimationsForRuntimeGlbPath(glbPath))
                return true;

            return true;
        }

        private void CacheRuntimeGlbVisualTemplate(string glbPath, GameObject sourceNode)
        {
            if (string.IsNullOrWhiteSpace(glbPath) || sourceNode == null)
                return;
            if (_runtimeGlbVisualTemplateByPath.ContainsKey(glbPath))
                return;

            Transform templateRoot = GetOrCreateRuntimeGlbTemplateRoot();
            if (templateRoot == null)
                return;

            var template = Instantiate(sourceNode, templateRoot, false);
            template.name = $"Template_{SafeGetFileNameWithoutExtension(glbPath)}";
            template.SetActive(false);
            _runtimeGlbVisualTemplateByPath[glbPath] = template;
        }

        private Transform GetOrCreateRuntimeGlbTemplateRoot()
        {
            if (_runtimeGlbVisualTemplateRoot != null)
                return _runtimeGlbVisualTemplateRoot;

            Transform parent = _worldRoot != null ? _worldRoot : transform;
            var go = new GameObject("RuntimeGlbVisualTemplates");
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            _runtimeGlbVisualTemplateRoot = go.transform;
            return _runtimeGlbVisualTemplateRoot;
        }

        private void ConfigureRuntimeDynelTargetingAndColliders(
            GameObject host,
            string objectType,
            string displayName,
            RuntimeWorldObjectData runtimeObj = null,
            int playfieldId = -1)
        {
            if (host == null)
                return;

            var bridge = host.GetComponent<CharacterRuntimeBridge>();
            if (bridge == null)
                bridge = host.AddComponent<CharacterRuntimeBridge>();
            bridge.DisplayNameOverride = string.IsNullOrWhiteSpace(displayName) ? host.name : displayName.Trim();

            bool useNonBlocking = runtimeMobDynelsUseNonBlockingSelectionColliders
                && ShouldUseNonBlockingDynelCollider(objectType);
            var meta = host.GetComponent<RuntimeDynelTargetMetadata>();
            if (meta == null)
                meta = host.AddComponent<RuntimeDynelTargetMetadata>();
            meta.UseNonBlockingColliders = useNonBlocking;
            meta.ObjectType = objectType ?? string.Empty;

            var questIdentity = host.GetComponent<RuntimeDynelQuestIdentity>();
            if (questIdentity == null)
                questIdentity = host.AddComponent<RuntimeDynelQuestIdentity>();
            questIdentity.PlayfieldId = playfieldId >= 0 ? playfieldId : _activePlayfieldId;
            questIdentity.DynelName = string.IsNullOrWhiteSpace(displayName) ? host.name : displayName.Trim();
            questIdentity.IdentityInstance = runtimeObj?.IdentityInstance ?? 0;
            questIdentity.TemplateId = runtimeObj?.TemplateId ?? 0;
            questIdentity.StaticInstance = runtimeObj?.StaticInstance ?? 0;
            questIdentity.MonsterData = runtimeObj?.MonsterData ?? 0;
            questIdentity.Level = runtimeObj?.Level ?? 0;
            questIdentity.Description = runtimeObj?.Description ?? string.Empty;

            if (ShouldAttachRuntimeCombatState(objectType))
            {
                var combatState = host.GetComponent<RuntimeDynelCombatState>();
                if (combatState == null)
                    combatState = host.AddComponent<RuntimeDynelCombatState>();
                int resolvedMaxHealth = Mathf.Max(1, runtimeObj?.MaxHealth ?? 120);
                combatState.Initialize(resolvedMaxHealth, 0);
            }

            EnsureNonBlockingDynelCollidersIfNeeded(host);
        }

        private static bool ShouldAttachRuntimeCombatState(string objectType)
        {
            string normalized = (objectType ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "npc"
                || normalized == "monster"
                || normalized == "mob"
                || normalized == "creature"
                || normalized == "simplechar"
                || normalized == "dynel"
                || normalized == "pet";
        }

        private void EnsureNonBlockingDynelCollidersIfNeeded(GameObject host)
        {
            if (host == null)
                return;

            var meta = host.GetComponent<RuntimeDynelTargetMetadata>();
            if (meta == null || !meta.UseNonBlockingColliders)
                return;

            var colliders = host.GetComponentsInChildren<Collider>(true);
            SphereCollider hostSelectionTrigger = host.GetComponent<SphereCollider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null)
                    continue;
                if (collider is CharacterController)
                    continue;

                bool isHostSelectionTrigger = collider.transform == host.transform
                    && collider is SphereCollider
                    && collider == hostSelectionTrigger;
                if (isHostSelectionTrigger)
                    continue;

                Destroy(collider);
            }

            var trigger = hostSelectionTrigger;
            if (trigger == null)
                trigger = host.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.enabled = true;
            trigger.radius = EstimateRuntimeSelectionRadius(host);
            trigger.center = Vector3.up * trigger.radius;
        }

        private bool ShouldUseNonBlockingDynelCollider(string objectType)
        {
            string type = objectType?.Trim() ?? string.Empty;

            bool isMobLike = type.Equals("NPC", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Pet", StringComparison.OrdinalIgnoreCase)
                || type.Equals("SimpleChar", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Dynel", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Monster", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Mob", StringComparison.OrdinalIgnoreCase)
                || type.Equals("Player", StringComparison.OrdinalIgnoreCase);

            return isMobLike;
        }

        private float EstimateRuntimeSelectionRadius(GameObject host)
        {
            float fallback = Mathf.Max(0.1f, runtimeMobDynelSelectionColliderMinRadius);
            if (host == null)
                return fallback;

            var renderers = host.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return fallback;

            Bounds merged = default;
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;

                if (!hasBounds)
                {
                    merged = r.bounds;
                    hasBounds = true;
                }
                else
                {
                    merged.Encapsulate(r.bounds);
                }
            }

            if (!hasBounds)
                return fallback;

            float radius = Mathf.Max(merged.extents.x, merged.extents.z);
            if (!float.IsFinite(radius) || radius <= 0.01f)
                return fallback;

            return Mathf.Max(fallback, radius);
        }

        private void TryApplyRuntimeGeneralTextureFallback(GameObject root)
        {
            if (root == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var materials = renderer.materials;
                bool changed = false;
                for (int m = 0; m < materials.Length; m++)
                {
                    var mat = materials[m];
                    if (mat == null)
                        continue;

                    if (!TryResolveGeneralTextureIdFromMaterialName(mat.name, out int textureId))
                        continue;
                    if (!TryGetRuntimeGeneralTexture(textureId, out var texture))
                        continue;

                    if (mat.HasProperty("_BaseMap"))
                        mat.SetTexture("_BaseMap", texture);
                    if (mat.HasProperty("_MainTex"))
                        mat.SetTexture("_MainTex", texture);
                    mat.mainTexture = texture;
                    changed = true;
                }

                if (changed)
                    renderer.materials = materials;
            }
        }

        private static bool TryResolveGeneralTextureIdFromMaterialName(string materialName, out int textureId)
        {
            textureId = 0;
            if (string.IsNullOrWhiteSpace(materialName))
                return false;

            string name = materialName.Trim();
            int cloneIdx = name.IndexOf(" (", StringComparison.Ordinal);
            if (cloneIdx > 0)
                name = name.Substring(0, cloneIdx);

            int underscore = name.LastIndexOf('_');
            if (underscore < 0 || underscore >= name.Length - 1)
                return false;

            string digits = name.Substring(underscore + 1);
            return int.TryParse(digits, out textureId) && textureId > 0;
        }

        private bool TryGetRuntimeGeneralTexture(int textureId, out Texture2D texture)
        {
            texture = null;
            if (textureId <= 0)
                return false;

            if (_runtimeGeneralTextureCache.TryGetValue(textureId, out var cached) && cached != null)
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

            try
            {
                var bytes = File.ReadAllBytes(path);
                var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!loaded.LoadImage(bytes))
                {
                    Destroy(loaded);
                    return false;
                }

                loaded.wrapMode = TextureWrapMode.Repeat;
                loaded.filterMode = FilterMode.Point;
                _runtimeGeneralTextureCache[textureId] = loaded;
                texture = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ClearRuntimeGeneralTextureCache()
        {
            foreach (var tex in _runtimeGeneralTextureCache.Values)
            {
                if (tex != null)
                    Destroy(tex);
            }

            _runtimeGeneralTextureCache.Clear();
        }

        private string ResolveRuntimeObjectGlbLoadPath(string glbPath)
        {
            if (string.IsNullOrWhiteSpace(glbPath))
                return glbPath;

            EnsureRuntimeGlbAnimationSafetyCacheLoaded();
            string normalizedOriginal = NormalizeRuntimeGlbPath(glbPath);

            if (_runtimeGlbSanitizedPathByOriginal.TryGetValue(normalizedOriginal, out var sanitizedPath)
                && !string.IsNullOrWhiteSpace(sanitizedPath)
                && File.Exists(sanitizedPath))
            {
                glbPath = sanitizedPath;
            }

            if (_runtimeGlbLoadPathCache.TryGetValue(glbPath, out var cachedPath)
                && !string.IsNullOrWhiteSpace(cachedPath)
                && File.Exists(cachedPath))
            {
                return cachedPath;
            }

            string resolved = glbPath;
            if (normalizeRuntimeGlbDataUriMimeTypes
                && glbPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                && File.Exists(glbPath)
                && TryWriteRuntimeGlbMimeNormalizedCopy(glbPath, out string patchedPath))
            {
                resolved = patchedPath;
            }

            resolved = GlbDataUriLoadPathResolver.Resolve(resolved, true);
            _runtimeGlbLoadPathCache[glbPath] = resolved;
            return resolved;
        }

        private static bool TryWriteRuntimeGlbMimeNormalizedCopy(string sourcePath, out string patchedPath)
        {
            patchedPath = null;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return false;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(sourcePath);
            }
            catch
            {
                return false;
            }

            if (bytes.Length < 20)
                return false;

            const uint GlbMagic = 0x46546C67; // "glTF"
            const uint JsonChunkType = 0x4E4F534A; // "JSON"
            uint magic = BitConverter.ToUInt32(bytes, 0);
            if (magic != GlbMagic)
                return false;

            uint jsonChunkLength = BitConverter.ToUInt32(bytes, 12);
            uint jsonChunkType = BitConverter.ToUInt32(bytes, 16);
            if (jsonChunkType != JsonChunkType)
                return false;

            int jsonOffset = 20;
            int jsonLength = (int)jsonChunkLength;
            if (jsonOffset + jsonLength > bytes.Length)
                return false;

            string json = Encoding.UTF8.GetString(bytes, jsonOffset, jsonLength)
                .TrimEnd('\0', ' ', '\t', '\r', '\n');

            if (string.IsNullOrWhiteSpace(json))
                return false;

            string updatedJson = json
                .Replace("data:image/png;base64,/9j/", "data:image/jpeg;base64,/9j/")
                .Replace("data:image/jpeg;base64,iVBORw0KGgo", "data:image/png;base64,iVBORw0KGgo");

            if (string.Equals(updatedJson, json, StringComparison.Ordinal))
                return false;

            byte[] updatedJsonBytes = Encoding.UTF8.GetBytes(updatedJson);
            int paddedJsonLength = (updatedJsonBytes.Length + 3) & ~3;
            byte[] paddedJsonBytes = new byte[paddedJsonLength];
            Buffer.BlockCopy(updatedJsonBytes, 0, paddedJsonBytes, 0, updatedJsonBytes.Length);
            for (int i = updatedJsonBytes.Length; i < paddedJsonLength; i++)
                paddedJsonBytes[i] = 0x20;

            int remainderOffset = jsonOffset + jsonLength;
            int remainderLength = bytes.Length - remainderOffset;
            byte[] output = new byte[20 + paddedJsonLength + remainderLength];

            Buffer.BlockCopy(bytes, 0, output, 0, 12);
            Buffer.BlockCopy(BitConverter.GetBytes((uint)paddedJsonLength), 0, output, 12, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(JsonChunkType), 0, output, 16, 4);
            Buffer.BlockCopy(paddedJsonBytes, 0, output, 20, paddedJsonLength);
            if (remainderLength > 0)
                Buffer.BlockCopy(bytes, remainderOffset, output, 20 + paddedJsonLength, remainderLength);

            Buffer.BlockCopy(BitConverter.GetBytes((uint)output.Length), 0, output, 8, 4);

            try
            {
                string cacheRoot = Path.Combine(Application.temporaryCachePath, "AOData", "RuntimeGlbPatched");
                Directory.CreateDirectory(cacheRoot);
                string baseName = SafeGetFileNameWithoutExtension(sourcePath);
                string suffix = Math.Abs(sourcePath.GetHashCode()).ToString("x8");
                patchedPath = Path.Combine(cacheRoot, $"{baseName}_{suffix}.glb");
                File.WriteAllBytes(patchedPath, output);
                return true;
            }
            catch
            {
                patchedPath = null;
                return false;
            }
        }

        private Material BuildIndoorRoomFloorMaterial(bool forceSimpleShader = false)
        {
            return BuildIndoorRoomSurfaceMaterial(indoorRoomFloorColor, forceSimpleShader);
        }

        private Material BuildIndoorRoomSurfaceMaterial(Color color, bool forceSimpleShader = false)
        {
            Material source = null;
            if (!forceSimpleShader)
            {
                source = indoorRoomFloorMaterialTemplate != null
                    ? indoorRoomFloorMaterialTemplate
                    : terrainMaterialTemplate;
            }

            Material mat;
            if (source != null)
            {
                mat = new Material(source);
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Sprites/Default")
                    ?? Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                    ?? Shader.Find("Standard");
                mat = shader != null ? new Material(shader) : null;
            }

            if (mat != null)
            {
                if (mat.shader == null || !mat.shader.isSupported)
                {
                    var fallbackShader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Color")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                        ?? Shader.Find("Standard");
                    mat = fallbackShader != null ? new Material(fallbackShader) : mat;
                }

                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", null);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", null);
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 0f);

                if (mat.HasProperty("_Color"))
                    mat.color = color;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", color);
            }

            return mat;
        }

        private static int TryCreateIndoorRoomSurfacePart(
            Transform parent,
            RoomSurfaceRoomData room,
            string suffix,
            List<Vector3> vertices,
            List<int> triangles,
            Material material,
            bool addColliders)
        {
            if (vertices == null || triangles == null || vertices.Count == 0 || triangles.Count < 3)
                return 0;

            var roomGo = new GameObject($"RoomSurface_{SanitizeRoomName(room.Name)}_{room.Instance}_{suffix}");
            roomGo.transform.SetParent(parent, false);

            var meshFilter = roomGo.AddComponent<MeshFilter>();
            var meshRenderer = roomGo.AddComponent<MeshRenderer>();
            var mesh = new Mesh
            {
                indexFormat = vertices.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(BuildDoubleSidedTriangles(triangles.ToArray()), 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;

            if (material != null)
                meshRenderer.sharedMaterial = material;

            if (addColliders)
            {
                var collider = roomGo.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
            }

            return 1;
        }

        private static void AppendHeuristicIndoorFloorPatch(
            RoomSurfaceRoomData room,
            List<Vector3> floorVertices,
            List<int> floorTriangles,
            List<Vector3> wallVertices)
        {
            if (wallVertices == null || wallVertices.Count < 3)
                return;

            float minY = float.MaxValue;
            for (int i = 0; i < wallVertices.Count; i++)
                minY = Mathf.Min(minY, wallVertices[i].y);

            if (!float.IsFinite(minY))
                return;

            const float edgeTolerance = 0.08f;
            var boundaryPoints = new List<Vector2>(256);
            for (int i = 0; i < wallVertices.Count; i++)
            {
                Vector3 v = wallVertices[i];
                if (Mathf.Abs(v.y - minY) > edgeTolerance)
                    continue;

                var candidate = new Vector2(v.x, v.z);
                bool duplicate = false;
                for (int j = 0; j < boundaryPoints.Count; j++)
                {
                    if ((boundaryPoints[j] - candidate).sqrMagnitude <= 0.0004f)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                    boundaryPoints.Add(candidate);
            }

            if (boundaryPoints.Count < 3)
                return;

            var hull = BuildConvexHull(boundaryPoints);
            if (hull.Count < 3)
                return;

            int vertexOffset = floorVertices.Count;
            float patchY = minY + 0.01f;
            for (int i = 0; i < hull.Count; i++)
                floorVertices.Add(new Vector3(hull[i].x, patchY, hull[i].y));

            for (int i = 1; i < hull.Count - 1; i++)
            {
                floorTriangles.Add(vertexOffset);
                floorTriangles.Add(vertexOffset + i);
                floorTriangles.Add(vertexOffset + i + 1);
            }
        }

        private static void AppendRoomFootprintFloorPatch(
            RoomFootprintData footprint,
            List<Vector3> floorVertices,
            List<int> floorTriangles,
            List<Vector3> wallVertices,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            if (footprint == null)
                return;

            float minY = float.MaxValue;
            if (wallVertices != null)
            {
                for (int i = 0; i < wallVertices.Count; i++)
                    minY = Mathf.Min(minY, wallVertices[i].y);
            }

            Vector3 center = centerAroundOrigin
                ? (footprint.CenterAo - horizontalCenter) * positionScale
                : footprint.CenterAo * positionScale;
            float halfWidth = Mathf.Max(0.1f, (footprint.WidthAo * positionScale) * 0.5f);
            float halfLength = Mathf.Max(0.1f, (footprint.LengthAo * positionScale) * 0.5f);
            float y = float.IsFinite(minY)
                ? minY + Mathf.Max(0.005f, IndoorRoomPickPatchHeightOffset * 0.5f)
                : center.y + Mathf.Max(0.005f, IndoorRoomPickPatchHeightOffset * 0.5f);

            Quaternion rot = Quaternion.Euler(0f, footprint.RotationQuarterTurns * 90f, 0f);
            Vector3 c0 = center + rot * new Vector3(-halfWidth, 0f, -halfLength);
            Vector3 c1 = center + rot * new Vector3(halfWidth, 0f, -halfLength);
            Vector3 c2 = center + rot * new Vector3(halfWidth, 0f, halfLength);
            Vector3 c3 = center + rot * new Vector3(-halfWidth, 0f, halfLength);
            c0.y = y;
            c1.y = y;
            c2.y = y;
            c3.y = y;

            int vo = floorVertices.Count;
            floorVertices.Add(c0);
            floorVertices.Add(c1);
            floorVertices.Add(c2);
            floorVertices.Add(c3);

            floorTriangles.Add(vo + 0);
            floorTriangles.Add(vo + 1);
            floorTriangles.Add(vo + 2);
            floorTriangles.Add(vo + 0);
            floorTriangles.Add(vo + 2);
            floorTriangles.Add(vo + 3);
        }

        private static bool ShouldApplyHeuristicIndoorFloorPatch(
            int playfieldId,
            List<Vector3> floorVertices,
            List<int> floorTriangles,
            List<Vector3> wallVertices)
        {
            if (wallVertices == null || wallVertices.Count < 12)
                return false;

            // Keep this constrained to the problematic dungeon for now.
            if (playfieldId != 127)
                return false;

            if (floorTriangles == null || floorTriangles.Count < 12 || floorVertices == null || floorVertices.Count < 4)
                return true;

            if (!TryComputeXZBoundsArea(floorVertices, out float floorArea)
                || !TryComputeXZBoundsArea(wallVertices, out float wallArea)
                || wallArea <= 0.0001f)
            {
                return true;
            }

            float coverage = floorArea / wallArea;
            return coverage < 0.92f;
        }

        private static bool TryComputeXZBoundsArea(List<Vector3> vertices, out float area)
        {
            area = 0f;
            if (vertices == null || vertices.Count == 0)
                return false;

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            bool hasFinite = false;

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 v = vertices[i];
                if (!float.IsFinite(v.x) || !float.IsFinite(v.z))
                    continue;

                hasFinite = true;
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minZ = Mathf.Min(minZ, v.z);
                maxZ = Mathf.Max(maxZ, v.z);
            }

            if (!hasFinite)
                return false;

            float width = Mathf.Max(0f, maxX - minX);
            float length = Mathf.Max(0f, maxZ - minZ);
            area = width * length;
            return area > 0.0001f;
        }

        private static List<Vector2> BuildConvexHull(List<Vector2> points)
        {
            var sorted = points
                .OrderBy(p => p.x)
                .ThenBy(p => p.y)
                .ToList();

            if (sorted.Count <= 3)
                return sorted;

            var lower = new List<Vector2>();
            for (int i = 0; i < sorted.Count; i++)
            {
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], sorted[i]) <= 0f)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(sorted[i]);
            }

            var upper = new List<Vector2>();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], sorted[i]) <= 0f)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(sorted[i]);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }


        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            return (ab.x * ac.y) - (ab.y * ac.x);
        }

        private static IndoorSurfaceKind ClassifyIndoorSurface(Matrix4x4 surfaceMatrix, RoomSurfaceMeshData surface)
        {
            if (surface?.Vertices == null || surface.Vertices.Count == 0 || surface.Triangles == null || surface.Triangles.Count < 3)
                return IndoorSurfaceKind.Wall;

            Vector3 accumulatedNormal = Vector3.zero;
            int samples = 0;
            int triangleLimit = Mathf.Min(surface.Triangles.Count, 24);

            for (int triIndex = 0; triIndex + 2 < triangleLimit; triIndex += 3)
            {
                int ia = surface.Triangles[triIndex];
                int ib = surface.Triangles[triIndex + 1];
                int ic = surface.Triangles[triIndex + 2];
                if (ia < 0 || ib < 0 || ic < 0
                    || ia >= surface.Vertices.Count
                    || ib >= surface.Vertices.Count
                    || ic >= surface.Vertices.Count)
                {
                    continue;
                }

                Vector3 a = surfaceMatrix.MultiplyPoint3x4(ToVector3(surface.Vertices[ia]));
                Vector3 b = surfaceMatrix.MultiplyPoint3x4(ToVector3(surface.Vertices[ib]));
                Vector3 c = surfaceMatrix.MultiplyPoint3x4(ToVector3(surface.Vertices[ic]));
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude < 0.0001f)
                    continue;

                accumulatedNormal += normal.normalized;
                samples++;
            }

            if (samples == 0)
                return IndoorSurfaceKind.Wall;

            Vector3 averageNormal = (accumulatedNormal / samples).normalized;
            float upDot = Vector3.Dot(averageNormal, Vector3.up);
            if (upDot >= 0.65f)
                return IndoorSurfaceKind.Floor;
            if (upDot <= -0.65f)
                return IndoorSurfaceKind.Ceiling;

            return IndoorSurfaceKind.Wall;
        }

        private static Vector3 ToVector3(Vector3Data value)
        {
            return value == null ? Vector3.zero : new Vector3(value.X, value.Y, value.Z);
        }

        private static string SanitizeRoomName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Room";

            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
                    chars[i] = '_';
            }

            return new string(chars);
        }

        private static bool IsPreferredIndoorSpawnRoom(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string normalized = name.Trim();
            return normalized.IndexOf("entry", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("entrance", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("start", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("arrival", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("lobby", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryLoadTerrainJson(
            int pf,
            Transform parent,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            string terrainPath = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{terrainJsonSuffix}");
            bool packageOverride = IsPlayfieldPackageOverrideEnabled(pf);
            bool packageStrict = IsPlayfieldPackageStrictMode(pf);
            bool usingPackageGeometry = false;

            if (packageOverride)
            {
                string packageTerrainPath = Path.Combine(ResolvePlayfieldPackageFolderPath(pf), "playfield_geometry.json");
                if (File.Exists(packageTerrainPath))
                {
                    terrainPath = packageTerrainPath;
                    usingPackageGeometry = true;
                }
                else if (packageStrict)
                {
                    Debug.LogError($"Playfield package strict mode missing terrain file: {packageTerrainPath}");
                    return false;
                }
            }

            if (!File.Exists(terrainPath))
            {
                Debug.LogWarning($"Terrain JSON not found: {terrainPath}");
                return false;
            }

            if (maxTerrainJsonMegabytes > 0)
            {
                try
                {
                    long byteLimit = (long)maxTerrainJsonMegabytes * 1024L * 1024L;
                    long fileSize = new FileInfo(terrainPath).Length;
                    if (fileSize > byteLimit)
                    {
                        Debug.LogWarning(
                            $"Terrain JSON for playfield {pf} is {fileSize / (1024f * 1024f):F1} MB " +
                            $"which exceeds maxTerrainJsonMegabytes={maxTerrainJsonMegabytes}. " +
                            "Attempting streaming terrain load.");
                    }
                }
                catch (Exception sizeEx)
                {
                    Debug.LogWarning($"Failed to inspect terrain JSON size for {terrainPath}: {sizeEx.Message}");
                }
            }

            string packageAtlasPath = null;
            if (packageOverride
                && string.Equals(Path.GetFileName(terrainPath), "playfield_geometry.json", StringComparison.OrdinalIgnoreCase))
            {
                string terrainDir = Path.GetDirectoryName(terrainPath) ?? string.Empty;
                string[] atlasCandidates =
                {
                    // New package exporter output (preferred).
                    Path.Combine(terrainDir, $"terrain_atlas_{pf}.png"),
                    Path.Combine(terrainDir, $"{pf}_terrain_atlas.png"),
                    // Older package layout.
                    Path.GetFullPath(Path.Combine(terrainDir, "..", "textures", $"{pf}_terrain_atlas.png")),
                    Path.GetFullPath(Path.Combine(terrainDir, "..", "textures", $"terrain_atlas_{pf}.png"))
                };

                for (int i = 0; i < atlasCandidates.Length; i++)
                {
                    string candidate = atlasCandidates[i];
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        packageAtlasPath = candidate;
                        break;
                    }
                }
            }

            bool effectiveFlipV = usingPackageGeometry ? flipPackageTerrainV : true;
            bool effectiveFlipU = usingPackageGeometry ? flipPackageTerrainU : false;
            bool effectiveSwapUv = usingPackageGeometry ? swapPackageTerrainUvAxes : false;
            bool forcedAogltfFlip = false;
            if (usingPackageGeometry
                && forceFlipVForAogltfManifestTerrain
                && IsAogltfManifestTerrainPackage(terrainPath))
            {
                effectiveFlipV = true;
                forcedAogltfFlip = true;
            }

            Material terrainMat = BuildTerrainMaterial(pf, packageAtlasPath);

            var terrainRoot = new GameObject($"PF_{pf}_Terrain");
            terrainRoot.transform.SetParent(parent, false);

            int built = 0;
            int totalChunks = 0;
            bool foundChunksProperty = false;
            try
            {
                using var file = File.OpenText(terrainPath);
                using var reader = new JsonTextReader(file);
                var serializer = JsonSerializer.CreateDefault();
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.PropertyName)
                        continue;

                    string property = reader.Value?.ToString();
                    if (!string.Equals(property, "Chunks", StringComparison.Ordinal))
                    {
                        if (string.Equals(property, "Terrain", StringComparison.Ordinal))
                        {
                            if (!reader.Read())
                                break;
                            continue;
                        }

                        if (!reader.Read())
                            break;
                        reader.Skip();
                        continue;
                    }

                    foundChunksProperty = true;
                    if (!reader.Read() || reader.TokenType != JsonToken.StartArray)
                    {
                        Debug.LogWarning($"Terrain JSON Chunks property is not an array: {terrainPath}");
                        continue;
                    }

                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonToken.EndArray)
                            break;
                        if (reader.TokenType != JsonToken.StartObject)
                            continue;

                        var chunk = serializer.Deserialize<PlayfieldTerrainChunk>(reader);
                        int chunkIndex = totalChunks;
                        totalChunks++;
                        if (TryBuildTerrainChunk(
                            terrainRoot.transform,
                            chunk,
                            chunkIndex,
                            terrainMat,
                            horizontalCenter,
                            centerAroundOrigin,
                            positionScale,
                            effectiveFlipV,
                            effectiveFlipU,
                            effectiveSwapUv))
                        {
                            built++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed parsing terrain JSON {terrainPath}: {ex.Message}");
                if (terrainRoot != null)
                    Destroy(terrainRoot);
                return false;
            }

            if (!foundChunksProperty || totalChunks == 0)
            {
                Debug.LogWarning($"Terrain has no chunks: {terrainPath}");
                if (terrainRoot != null)
                    Destroy(terrainRoot);
                return false;
            }

            Debug.Log(
                $"Loaded terrain for playfield {pf}: built {built}/{totalChunks} chunks. " +
                $"source={(usingPackageGeometry ? "package" : "legacy")} flipV={effectiveFlipV} flipU={effectiveFlipU} swapUv={effectiveSwapUv} forcedAogltfFlip={forcedAogltfFlip}");
            return built > 0;
        }

        private static bool IsAogltfManifestTerrainPackage(string terrainPath)
        {
            try
            {
                string dir = Path.GetDirectoryName(terrainPath);
                if (string.IsNullOrWhiteSpace(dir))
                    return false;

                string metaPath = Path.Combine(dir, "playfield_meta.json");
                if (!File.Exists(metaPath))
                    return false;

                JObject meta = JObject.Parse(File.ReadAllText(metaPath));
                string sourceVersion = meta?["SourceVersion"]?.ToString();
                return !string.IsNullOrWhiteSpace(sourceVersion)
                    && sourceVersion.StartsWith("aogltf_manifest_", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool TryBuildTerrainChunk(
            Transform terrainRoot,
            PlayfieldTerrainChunk chunk,
            int chunkIndex,
            Material terrainMat,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale,
            bool flipV,
            bool flipU,
            bool swapUv)
        {
            if (chunk == null || chunk.Vertices == null || chunk.Triangles == null || chunk.Vertices.Count == 0 || chunk.Triangles.Count == 0)
                return false;

            var go = new GameObject($"TerrainChunk_{chunkIndex}");
            go.transform.SetParent(terrainRoot, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();

            var mesh = new Mesh();
            mesh.indexFormat = chunk.Vertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            var verts = new Vector3[chunk.Vertices.Count];
            for (int v = 0; v < chunk.Vertices.Count; v++)
            {
                var p = chunk.Vertices[v];
                var src = new Vector3(p.X, p.Y, p.Z);
                if (centerAroundOrigin)
                    src -= horizontalCenter;
                verts[v] = src * positionScale;
            }

            var hasNormals = chunk.Normals != null && chunk.Normals.Count == chunk.Vertices.Count;
            var norms = hasNormals ? new Vector3[chunk.Normals.Count] : null;
            if (hasNormals)
            {
                for (int n = 0; n < chunk.Normals.Count; n++)
                {
                    var no = chunk.Normals[n];
                    norms[n] = new Vector3(no.X, no.Y, no.Z);
                }
            }

            var hasUvs = chunk.UVs != null && chunk.UVs.Count == chunk.Vertices.Count;
            var uvs = hasUvs ? new Vector2[chunk.UVs.Count] : null;
            if (hasUvs)
            {
                for (int u = 0; u < chunk.UVs.Count; u++)
                {
                    var uv = chunk.UVs[u];
                    float ux = uv.X;
                    float vy = uv.Y;
                    if (swapUv)
                    {
                        float tmp = ux;
                        ux = vy;
                        vy = tmp;
                    }

                    if (flipU)
                        ux = 1f - ux;
                    if (flipV)
                        vy = 1f - vy;

                    uvs[u] = new Vector2(ux, vy);
                }
            }

            mesh.vertices = verts;
            mesh.triangles = chunk.Triangles.ToArray();

            if (hasNormals)
                mesh.normals = norms;
            else
                mesh.RecalculateNormals();

            if (hasUvs)
                mesh.uv = uvs;

            mesh.RecalculateBounds();

            mf.sharedMesh = mesh;
            mr.sharedMaterial = terrainMat;
            if (addTerrainColliders)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
            }

            return true;
        }

        private bool TryLoadWaterJson(
            int pf,
            Transform parent,
            Vector3 horizontalCenter,
            bool centerAroundOrigin,
            float positionScale)
        {
            string waterPath = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{waterJsonSuffix}");
            bool packageOverride = IsPlayfieldPackageOverrideEnabled(pf);
            bool packageStrict = IsPlayfieldPackageStrictMode(pf);

            if (packageOverride)
            {
                string packageWaterPath = Path.Combine(ResolvePlayfieldPackageFolderPath(pf), "playfield_water.json");
                if (File.Exists(packageWaterPath))
                {
                    waterPath = packageWaterPath;
                }
                else if (packageStrict)
                {
                    Debug.LogError($"Playfield package strict mode missing water file: {packageWaterPath}");
                    return false;
                }
            }

            if (!File.Exists(waterPath))
            {
                Debug.LogWarning($"Water JSON not found: {waterPath}");
                return false;
            }

            PlayfieldWaterFile waterFile;
            try
            {
                waterFile = JsonConvert.DeserializeObject<PlayfieldWaterFile>(File.ReadAllText(waterPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed parsing water JSON {waterPath}: {ex.Message}");
                return false;
            }

            if (waterFile?.Water == null || waterFile.Water.Count == 0)
            {
                Debug.LogWarning($"Water has no meshes: {waterPath}");
                return false;
            }

            var waterRoot = new GameObject($"PF_{pf}_Water");
            waterRoot.transform.SetParent(parent, false);
            var waterMat = BuildWaterMaterial();

            int built = 0;
            for (int i = 0; i < waterFile.Water.Count; i++)
            {
                var w = waterFile.Water[i];
                if (w.Vertices == null || w.Triangles == null || w.Vertices.Count == 0 || w.Triangles.Count == 0)
                    continue;

                var go = new GameObject($"WaterMesh_{i}");
                go.transform.SetParent(waterRoot.transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();

                var mesh = new Mesh();
                mesh.indexFormat = w.Vertices.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;

                var verts = new Vector3[w.Vertices.Count];
                for (int v = 0; v < w.Vertices.Count; v++)
                {
                    var p = w.Vertices[v];
                    var src = new Vector3(p.X, p.Y, p.Z);
                    if (centerAroundOrigin)
                        src -= horizontalCenter;
                    verts[v] = src * positionScale;
                }

                mesh.vertices = verts;
                mesh.triangles = BuildDoubleSidedTriangles(w.Triangles.ToArray());
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                mf.sharedMesh = mesh;
                mr.sharedMaterial = waterMat;
                built++;
            }

            Debug.Log($"Loaded water for playfield {pf}: built {built}/{waterFile.Water.Count} meshes.");
            return true;
        }

        private Material BuildTerrainMaterial(int pf, string explicitAtlasPath = null)
        {
            string atlasPath = !string.IsNullOrWhiteSpace(explicitAtlasPath)
                ? explicitAtlasPath
                : Path.Combine(
                    Application.streamingAssetsPath,
                    playfieldsSubfolder,
                    $"{pf}{terrainAtlasSuffix}");

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Texture");

            if (shader == null)
            {
                Debug.LogError("No compatible terrain shader found (URP Lit / Standard / Unlit/Texture).");
                return new Material(Shader.Find("Sprites/Default"));
            }

            Material mat = terrainMaterialTemplate != null
                ? new Material(terrainMaterialTemplate)
                : new Material(shader);

            if (mat.shader == null || !mat.shader.isSupported)
            {
                Debug.LogWarning($"Terrain template shader unsupported: '{mat.shader?.name ?? "null"}'. Falling back to '{shader.name}'.");
                mat = new Material(shader);
            }

            if (!File.Exists(atlasPath))
            {
                Debug.LogWarning($"Terrain atlas not found: {atlasPath}");
                return mat;
            }

            byte[] atlasBytes = File.ReadAllBytes(atlasPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(atlasBytes))
            {
                Debug.LogWarning($"Failed to decode terrain atlas: {atlasPath}");
                return mat;
            }

            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", tex); // URP/HDRP-style
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", tex); // Built-in
            mat.mainTexture = tex;

            Debug.Log($"Terrain atlas loaded: {atlasPath}");
            Debug.Log($"Terrain material shader: {mat.shader.name}");
            return mat;
        }

        private Material BuildWaterMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");

            Material mat = waterMaterialTemplate != null
                ? new Material(waterMaterialTemplate)
                : new Material(shader);

            if (mat.shader == null || !mat.shader.isSupported)
                mat = new Material(shader);

            var c = defaultWaterColor;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", c);

            // URP transparency path.
            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", 0f); // Double-sided water surface.

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            // Built-in Standard fallback.
            if (mat.shader != null && mat.shader.name == "Standard")
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            }

            mat.renderQueue = 3000;
            return mat;
        }

        private static int[] BuildDoubleSidedTriangles(int[] src)
        {
            if (src == null || src.Length == 0)
                return Array.Empty<int>();

            var dst = new int[src.Length * 2];
            Array.Copy(src, dst, src.Length);

            int offset = src.Length;
            for (int i = 0; i < src.Length; i += 3)
            {
                dst[offset + i + 0] = src[i + 0];
                dst[offset + i + 1] = src[i + 2];
                dst[offset + i + 2] = src[i + 1];
            }

            return dst;
        }

        private Dictionary<uint, string> LoadStatelMeshMap(int pf)
        {
            var map = new Dictionary<uint, string>();
            if (!loadStatelMeshMap)
                return map;

            if (_packageStatelMeshMapByPlayfield.TryGetValue(pf, out var packageMap)
                && packageMap != null
                && packageMap.Count > 0)
            {
                Debug.Log($"Using playfield package statel mesh map for playfield {pf}: {packageMap.Count} entries.");
                return new Dictionary<uint, string>(packageMap);
            }
            if (IsPlayfieldPackageStrictMode(pf))
            {
                Debug.LogWarning($"Playfield package strict mode active for PF {pf}: no package statel mesh map entries found.");
                return map;
            }

            string file = Path.Combine(Application.streamingAssetsPath, playfieldsSubfolder, $"{pf}_statel_mesh_map.json");
            if (!File.Exists(file))
            {
                Debug.LogWarning($"Statel mesh map not found: {file}");
                return map;
            }

            try
            {
                var entries = JsonConvert.DeserializeObject<List<StatelMeshMapEntry>>(File.ReadAllText(file));
                if (entries == null)
                    return map;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (!string.IsNullOrWhiteSpace(entry.MeshName))
                        map[entry.StatelId] = SanitizePathLikeString(entry.MeshName);
                }

                Debug.Log($"Loaded statel mesh map for playfield {pf}: {map.Count} entries.");
                return map;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse statel mesh map {file}: {ex.Message}");
                return map;
            }
        }

        private MeshRotationOverrideSet LoadMeshRotationOverrides(int pf)
        {
            var set = new MeshRotationOverrideSet();
            if (!loadMeshRotationOverrides)
                return set;

            bool loadedGlobal = false;
            bool loadedPlayfield = false;

            if (!string.IsNullOrWhiteSpace(globalMeshRotationOverridesFile))
            {
                string globalFile = Path.Combine(
                    Application.streamingAssetsPath,
                    playfieldsSubfolder,
                    globalMeshRotationOverridesFile);
                loadedGlobal = TryAppendMeshRotationOverridesFromFile(globalFile, set);
            }

            string playfieldFile = Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"{pf}{meshRotationOverridesSuffix}");
            loadedPlayfield = TryAppendMeshRotationOverridesFromFile(playfieldFile, set);

            if (loadedGlobal || loadedPlayfield)
            {
                Debug.Log(
                    $"Loaded mesh rotation overrides for playfield {pf}: " +
                    $"global={loadedGlobal}, playfield={loadedPlayfield}, " +
                    $"meshRot={set.MeshRotationByName.Count}, nodeRot={set.NodeRotationByMeshAndNode.Count}, " +
                    $"meshPos={set.MeshPositionOffsetByName.Count}, nodePos={set.NodePositionOffsetByMeshAndNode.Count}.");
            }

            return set;
        }

        private CollisionOverrideSet LoadCollisionOverrides()
        {
            var set = CreateDefaultCollisionOverrideSet();
            if (!loadGlobalCollisionOverrides)
                return set;

            try
            {
                string file = Path.Combine(
                    Application.streamingAssetsPath,
                    playfieldsSubfolder,
                    globalCollisionOverridesFile);

                if (!File.Exists(file))
                {
                    Debug.Log($"Collision overrides file not found; using defaults: {file}");
                    return set;
                }

                var parsed = JsonConvert.DeserializeObject<CollisionOverrideFile>(File.ReadAllText(file));
                if (parsed == null)
                    return set;

                AppendCollisionOverrideStrings(set.DisableCollisionMeshNameContains, parsed.DisableCollisionMeshNameContains, normalizeAsMeshKey: false);
                AppendCollisionOverrideStrings(set.DisableCollisionMeshKeys, parsed.DisableCollisionMeshKeys, normalizeAsMeshKey: true);
                AppendCollisionOverrideInts(set.DisableCollisionStatelIds, parsed.DisableCollisionStatelIds);
                AppendCollisionOverrideStrings(set.ForceCollisionMeshKeys, parsed.ForceCollisionMeshKeys, normalizeAsMeshKey: true);
                AppendCollisionOverrideInts(set.ForceCollisionStatelIds, parsed.ForceCollisionStatelIds);

                Debug.Log(
                    $"Loaded collision overrides: disableContains={set.DisableCollisionMeshNameContains.Count}, " +
                    $"disableMeshKeys={set.DisableCollisionMeshKeys.Count}, disableStatels={set.DisableCollisionStatelIds.Count}, " +
                    $"forceMeshKeys={set.ForceCollisionMeshKeys.Count}, forceStatels={set.ForceCollisionStatelIds.Count}.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse collision overrides: {ex.Message}");
            }

            return set;
        }

        private static CollisionOverrideSet CreateDefaultCollisionOverrideSet()
        {
            return new CollisionOverrideSet();
        }

        private static void AppendCollisionOverrideStrings(
            HashSet<string> target,
            List<string> source,
            bool normalizeAsMeshKey)
        {
            if (target == null || source == null || source.Count == 0)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                string raw = source[i];
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string value = normalizeAsMeshKey
                    ? NormalizeMeshOverrideKey(raw)
                    : raw.Trim();

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                target.Add(value);
            }
        }

        private static void AppendCollisionOverrideInts(HashSet<int> target, List<int> source)
        {
            if (target == null || source == null || source.Count == 0)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                int value = source[i];
                if (value > 0)
                    target.Add(value);
            }
        }

        private bool TryAppendMeshRotationOverridesFromFile(string file, MeshRotationOverrideSet set)
        {
            if (string.IsNullOrWhiteSpace(file) || set == null || !File.Exists(file))
                return false;

            try
            {
                var entries = JsonConvert.DeserializeObject<List<MeshRotationOverrideEntry>>(File.ReadAllText(file));
                if (entries == null || entries.Count == 0)
                    return false;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (string.IsNullOrWhiteSpace(entry.MeshName))
                        continue;

                    string key = SafeGetFileNameWithoutExtension(entry.MeshName);
                    RotationOverrideMode rotationMode = ParseRotationOverrideMode(entry.RotationMode);
                    Vector3 euler = entry.RotationEuler != null
                        ? new Vector3(entry.RotationEuler.X, entry.RotationEuler.Y, entry.RotationEuler.Z)
                        : Vector3.zero;
                    Vector3 positionOffset = entry.PositionOffset != null
                        ? new Vector3(entry.PositionOffset.X, entry.PositionOffset.Y, entry.PositionOffset.Z)
                        : Vector3.zero;
                    string normalizedKey = NormalizeMeshOverrideKey(key);
                    if (!string.IsNullOrWhiteSpace(entry.NodeName))
                    {
                        string nodeName = entry.NodeName.Trim();
                        string nodeKey = $"{key}|{nodeName}";
                        set.NodeRotationByMeshAndNode[nodeKey] = euler;
                        set.NodeRotationModeByMeshAndNode[nodeKey] = rotationMode;
                        set.NodePositionOffsetByMeshAndNode[nodeKey] = positionOffset;
                        if (!string.IsNullOrWhiteSpace(normalizedKey)
                            && !string.Equals(normalizedKey, key, StringComparison.OrdinalIgnoreCase))
                        {
                            string normalizedNodeKey = $"{normalizedKey}|{nodeName}";
                            set.NodeRotationByMeshAndNode[normalizedNodeKey] = euler;
                            set.NodeRotationModeByMeshAndNode[normalizedNodeKey] = rotationMode;
                            set.NodePositionOffsetByMeshAndNode[normalizedNodeKey] = positionOffset;
                        }
                    }
                    else
                    {
                        set.MeshRotationByName[key] = euler;
                        set.MeshRotationModeByName[key] = rotationMode;
                        set.MeshPositionOffsetByName[key] = positionOffset;
                        if (!string.IsNullOrWhiteSpace(normalizedKey)
                            && !string.Equals(normalizedKey, key, StringComparison.OrdinalIgnoreCase))
                        {
                            set.MeshRotationByName[normalizedKey] = euler;
                            set.MeshRotationModeByName[normalizedKey] = rotationMode;
                            set.MeshPositionOffsetByName[normalizedKey] = positionOffset;
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse mesh rotation overrides {file}: {ex.Message}");
                return false;
            }
        }

        private static GameObject SpawnStatelObject(
            Transform parent,
            StatelData statel,
            string meshName,
            Vector3 sourceCenter,
            bool centerAroundOrigin,
            float positionScale,
            float placeholderScaleMultiplier,
            bool allowPrefabLoad,
            string resourcesFolder,
            Dictionary<string, GameObject> prefabCache,
            bool applyGlobalMeshBasisCorrection,
            Vector3 globalMeshBasisCorrectionEuler,
            Vector3 meshPrefabRotationOffsetEuler,
            Vector3 perMeshRotationEuler,
            bool perMeshRotationIsAbsolute,
            Vector3 perMeshPositionOffset,
            string meshKey,
            List<StatelMeshPartData> meshParts,
            bool applyMeshPartTransforms,
            bool applyMeshPartPosition,
            bool applyMeshPartRotation,
            bool applyMeshPartScale,
            bool convertMeshPartRotationFromAo,
            Dictionary<string, Vector3> nodeRotationByMeshAndNode,
            Dictionary<string, Vector3> nodePositionOffsetByMeshAndNode,
            bool applyBuiltInPlacementCorrection,
            bool addColliders,
            bool collidersConvex,
            bool collidersIsTrigger,
            int maxCollidersPerStatel)
        {
            var sourcePosition = centerAroundOrigin
                ? (statel.Position - sourceCenter)
                : statel.Position;

            if (!IsFiniteVector3(sourcePosition))
                return null;

            var position = sourcePosition * positionScale;
            if (!IsFiniteVector3(position))
                return null;

            var statelRotation = IsFiniteVector3(statel.Rotation) ? statel.Rotation : Vector3.zero;
            var rotation = Quaternion.Euler(statelRotation);
            var globalBasisRotation = applyGlobalMeshBasisCorrection
                ? Quaternion.Euler(globalMeshBasisCorrectionEuler)
                : Quaternion.identity;
            var meshOffsetRotation = Quaternion.Euler(meshPrefabRotationOffsetEuler);
            var perMeshRotation = Quaternion.Euler(perMeshRotationEuler);
            Vector3 baseScale = IsFiniteVector3(statel.ScaleVector) ? statel.ScaleVector : Vector3.one;
            if (Mathf.Approximately(baseScale.x, 1f)
                && Mathf.Approximately(baseScale.y, 1f)
                && Mathf.Approximately(baseScale.z, 1f)
                && float.IsFinite(statel.Scale)
                && !Mathf.Approximately(statel.Scale, 1f)
                && statel.Scale > 0.0001f)
            {
                baseScale = new Vector3(statel.Scale, statel.Scale, statel.Scale);
            }

            if (baseScale.x <= 0.0001f || baseScale.y <= 0.0001f || baseScale.z <= 0.0001f)
            {
                float fallback = statel.Scale <= 0f ? 1f : statel.Scale;
                baseScale = new Vector3(fallback, fallback, fallback);
            }

            var scaled = new Vector3(
                Mathf.Clamp(baseScale.x * placeholderScaleMultiplier, 0.25f, 12f),
                Mathf.Clamp(baseScale.y * placeholderScaleMultiplier, 0.25f, 12f),
                Mathf.Clamp(baseScale.z * placeholderScaleMultiplier, 0.25f, 12f));

            GameObject go = null;
            if (allowPrefabLoad && !string.IsNullOrWhiteSpace(meshName))
            {
                var prefab = TryLoadMeshPrefab(
                    resourcesFolder,
                    prefabCache,
                    meshName,
                    statel.StatelId,
                    out _);

                if (prefab != null)
                    go = UnityEngine.Object.Instantiate(prefab);
            }

            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var renderer = go.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = ColorFromName(meshName);
                if (go.GetComponent<RuntimePlaceholderVisualMarker>() == null)
                    go.AddComponent<RuntimePlaceholderVisualMarker>();
            }

            go.name = string.IsNullOrWhiteSpace(meshName)
                ? $"Statel_{statel.StatelId}"
                : $"Statel_{statel.StatelId}_{SafeGetFileNameWithoutExtension(meshName)}";

            go.transform.SetParent(parent, false);
            go.transform.position = position + perMeshPositionOffset;
            go.transform.rotation = perMeshRotationIsAbsolute
                ? perMeshRotation
                : rotation * globalBasisRotation * meshOffsetRotation * perMeshRotation;
            go.transform.localScale = scaled;
            if (applyMeshPartTransforms && meshParts != null && meshParts.Count > 0)
            {
                ApplyMeshPartTransforms(
                    go.transform,
                    meshParts,
                    applyMeshPartPosition,
                    applyMeshPartRotation,
                    applyMeshPartScale,
                    convertMeshPartRotationFromAo);
            }

            if (applyBuiltInPlacementCorrection)
                ApplyBuiltInMeshPlacementCorrection(go.transform, meshKey);

            if (!string.IsNullOrWhiteSpace(meshKey)
                && nodeRotationByMeshAndNode != null
                && nodeRotationByMeshAndNode.Count > 0)
            {
                ApplyNodeRotationOverrides(go.transform, meshKey, nodeRotationByMeshAndNode);
            }
            if (!string.IsNullOrWhiteSpace(meshKey)
                && nodePositionOffsetByMeshAndNode != null
                && nodePositionOffsetByMeshAndNode.Count > 0)
            {
                ApplyNodePositionOverrides(go.transform, meshKey, nodePositionOffsetByMeshAndNode);
            }

            int statelId = statel != null ? statel.StatelId : 0;
            bool forceCollisionForStatel = ShouldForceCollisionForStatel(statelId, meshKey);
            bool disableCollisionByName = ShouldDisableCollisionForMeshName(meshName, meshKey, statelId);
            if (addColliders && forceCollisionForStatel)
            {
                EnsureSolidColliderForForcedStatel(go, collidersConvex, maxCollidersPerStatel);
            }
            else if (addColliders && !disableCollisionByName)
            {
                AddStatelCollidersIfMissing(go, collidersConvex, collidersIsTrigger, maxCollidersPerStatel);
            }

            return go;
        }

        private static GameObject TryLoadMeshPrefab(
            string resourcesFolder,
            Dictionary<string, GameObject> prefabCache,
            string meshName,
            int statelId,
            out string resolvedKey)
        {
            resolvedKey = string.Empty;
            if (string.IsNullOrWhiteSpace(resourcesFolder) || string.IsNullOrWhiteSpace(meshName))
                return null;

            string baseKey = SafeGetFileNameWithoutExtension(meshName);
            var candidates = new List<string>();

            void AddCandidate(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                    return;
                if (!candidates.Contains(key, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(key);
            }

            AddCandidate(baseKey);

            if (statelId > 0)
            {
                AddCandidate($"{baseKey}_{statelId}");
                AddCandidate($"Unnamed_{statelId}");
                AddCandidate($"UnnamedRecord_{statelId}");
            }

            if (baseKey.StartsWith("mesh_", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(baseKey.Substring(5), out int meshId)
                && meshId > 0)
            {
                AddCandidate($"Unnamed_{meshId}");
                AddCandidate($"UnnamedRecord_{meshId}");
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string key = candidates[i];
                if (!prefabCache.TryGetValue(key, out var prefab))
                {
                    prefab = Resources.Load<GameObject>($"{resourcesFolder}/{key}");
                    prefabCache[key] = prefab;
                }

                if (prefab != null)
                {
                    resolvedKey = key;
                    return prefab;
                }
            }

            return null;
        }

        private static bool IsIgnorableRuntimeMeshName(string meshName)
        {
            if (string.IsNullOrWhiteSpace(meshName))
                return true;

            string trimmed = meshName.Trim();
            return trimmed.Equals("NoName", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("None", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("null", StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyMeshPartTransforms(
            Transform root,
            List<StatelMeshPartData> meshParts,
            bool applyPosition,
            bool applyRotation,
            bool applyScale,
            bool convertRotationFromAo)
        {
            if (root == null || meshParts == null || meshParts.Count == 0)
                return;

            for (int i = 0; i < meshParts.Count; i++)
            {
                var part = meshParts[i];
                if (part == null || part.SubMeshIndex < 0)
                    continue;

                Transform target = FindMeshPartTransform(root, part.SubMeshIndex);
                if (target == null)
                    continue;

                if (applyPosition)
                    target.localPosition = part.Position;

                if (applyRotation)
                {
                    Quaternion rotation = part.Rotation;
                    if (convertRotationFromAo)
                        rotation = ConvertAoQuaternionToUnity(rotation);
                    target.localRotation = rotation;
                }

                if (applyScale)
                    target.localScale = part.Scale;
            }
        }

        private static Quaternion ConvertAoQuaternionToUnity(Quaternion source)
        {
            if (!float.IsFinite(source.x)
                || !float.IsFinite(source.y)
                || !float.IsFinite(source.z)
                || !float.IsFinite(source.w))
            {
                return Quaternion.identity;
            }

            var converted = new Quaternion(-source.x, source.y, -source.z, source.w);
            float magnitudeSq =
                (converted.x * converted.x) +
                (converted.y * converted.y) +
                (converted.z * converted.z) +
                (converted.w * converted.w);
            if (magnitudeSq < 0.000001f)
                return Quaternion.identity;

            return Quaternion.Normalize(converted);
        }

        private static Transform FindMeshPartTransform(Transform root, int subMeshIndex)
        {
            if (root == null || subMeshIndex < 0)
                return null;

            string expectedName = $"Mesh_{subMeshIndex}";
            var all = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == root)
                    continue;

                if (string.Equals(t.name, expectedName, StringComparison.OrdinalIgnoreCase)
                    || t.name.StartsWith(expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    return t;
                }
            }

            // Fallback: apply by mesh-bearing child order when names are not preserved.
            var meshChildren = root.GetComponentsInChildren<MeshFilter>(true)
                .Select(x => x != null ? x.transform : null)
                .Where(x => x != null && x != root)
                .ToList();
            if (subMeshIndex >= 0 && subMeshIndex < meshChildren.Count)
                return meshChildren[subMeshIndex];

            return null;
        }

        private static bool TryGetBuiltInMeshRotationOverride(string meshKey, out Vector3 euler)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
            {
                euler = Vector3.zero;
                return false;
            }

            if (meshKey.Equals("jungle_tree_thin2", StringComparison.OrdinalIgnoreCase))
            {
                euler = Vector3.zero;
                return true;
            }

            if (meshKey.Equals("mcclean_tree01", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("penumbra_jungletree", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("tree_jungle", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("jungle_tree", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("jungletree", StringComparison.OrdinalIgnoreCase)
                || meshKey.IndexOf("_tree_jungle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                euler = new Vector3(180f, 0f, 0f);
                return true;
            }

            euler = Vector3.zero;
            return false;
        }

        private static bool ShouldForceCollisionForStatel(int statelId, string meshKey)
        {
            var overrides = _activeCollisionOverrides ?? CreateDefaultCollisionOverrideSet();
            if (statelId > 0 && overrides.ForceCollisionStatelIds.Contains(statelId))
                return true;

            if (string.IsNullOrWhiteSpace(meshKey))
                return false;

            string normalizedKey = NormalizeMeshOverrideKey(meshKey);
            if (string.IsNullOrWhiteSpace(normalizedKey))
                return false;

            return overrides.ForceCollisionMeshKeys.Contains(normalizedKey);
        }

        private static bool ShouldDisableCollisionForMeshName(string meshName, string meshKey, int statelId)
        {
            var overrides = _activeCollisionOverrides ?? CreateDefaultCollisionOverrideSet();
            if (statelId > 0 && overrides.ForceCollisionStatelIds.Contains(statelId))
                return false;
            if (statelId > 0 && overrides.DisableCollisionStatelIds.Contains(statelId))
                return true;

            string normalizedKey = NormalizeMeshOverrideKey(meshKey);
            if (!string.IsNullOrWhiteSpace(normalizedKey))
            {
                if (overrides.ForceCollisionMeshKeys.Contains(normalizedKey))
                    return false;
                if (overrides.DisableCollisionMeshKeys.Contains(normalizedKey))
                    return true;
            }

            string haystack = string.Join(" ", meshName ?? string.Empty, meshKey ?? string.Empty);
            if (string.IsNullOrWhiteSpace(haystack))
                return false;

            foreach (string token in overrides.DisableCollisionMeshNameContains)
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;
                if (haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static void ApplyBuiltInMeshPlacementCorrection(Transform root, string meshKey)
        {
            if (root == null || !NeedsBuiltInTreeGroundLift(meshKey))
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float liftMultiplier = GetBuiltInTreeGroundLiftMultiplier(meshKey);
            if (liftMultiplier <= 0f)
                return;

            float lift = bounds.size.y * liftMultiplier;
            if (!float.IsFinite(lift) || lift <= 0.01f)
                return;

            root.position += Vector3.up * lift;
        }

        private static bool NeedsBuiltInTreeGroundLift(string meshKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
                return false;

            return meshKey.StartsWith("tree_jungle", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("jungle_tree", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("jungletree", StringComparison.OrdinalIgnoreCase)
                || meshKey.IndexOf("_tree_jungle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float GetBuiltInTreeGroundLiftMultiplier(string meshKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
                return 0f;

            if (meshKey.StartsWith("jungle_tree_thin", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("jungle_tree_thin2", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("jungletree_thinnest", StringComparison.OrdinalIgnoreCase))
            {
                return meshKey.Equals("jungle_tree_thin2", StringComparison.OrdinalIgnoreCase) ? 0.2f : 0.5f;
            }

            if (meshKey.StartsWith("tree_jungle_medium", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("tree_jungle_medium2", StringComparison.OrdinalIgnoreCase))
            {
                return 0.65f;
            }

            return 0.6f;
        }

        private static bool ShouldSkipGenericMeshPlaceholder(string meshKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
                return false;

            if (meshKey.StartsWith("mesh_", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(meshKey.Substring(5), out int meshId) && meshId > 0)
                    return false;
                return true;
            }

            return meshKey.StartsWith("invisible_", StringComparison.OrdinalIgnoreCase)
                || meshKey.StartsWith("startup_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidStatelTransform(StatelData statel, float maxAbsCoordinate)
        {
            if (statel == null)
                return false;

            if (statel.StatelId <= 0)
                return false;

            if (!IsFiniteVector3(statel.Position) || !IsFiniteVector3(statel.Rotation))
                return false;

            return Mathf.Abs(statel.Position.x) <= maxAbsCoordinate
                && Mathf.Abs(statel.Position.y) <= maxAbsCoordinate
                && Mathf.Abs(statel.Position.z) <= maxAbsCoordinate;
        }

        private static bool IsFiniteVector3(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string SafeGetFileNameWithoutExtension(string pathLike)
        {
            string sanitized = SanitizePathLikeString(pathLike);
            if (string.IsNullOrWhiteSpace(sanitized))
                return string.Empty;

            try
            {
                return Path.GetFileNameWithoutExtension(sanitized);
            }
            catch
            {
                string manual = sanitized.Replace('\\', '/');
                int slash = manual.LastIndexOf('/');
                if (slash >= 0 && slash + 1 < manual.Length)
                    manual = manual.Substring(slash + 1);

                int dot = manual.LastIndexOf('.');
                if (dot > 0)
                    manual = manual.Substring(0, dot);

                return manual.Trim();
            }
        }

        private static string SanitizePathLikeString(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new System.Text.StringBuilder(value.Length);
            var invalidPath = Path.GetInvalidPathChars();
            var invalidFile = Path.GetInvalidFileNameChars();

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\0')
                    continue;

                if (Array.IndexOf(invalidPath, c) >= 0 || Array.IndexOf(invalidFile, c) >= 0)
                {
                    if (c == '\\' || c == '/')
                        sb.Append(c);
                    else
                        sb.Append('_');
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString().Trim();
        }

        private static string NormalizeMeshOverrideKey(string meshKey)
        {
            string key = SafeGetFileNameWithoutExtension(meshKey);
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            int suffixSplit = key.LastIndexOf('_');
            if (suffixSplit > 0 && suffixSplit + 1 < key.Length)
            {
                bool allDigits = true;
                for (int i = suffixSplit + 1; i < key.Length; i++)
                {
                    if (!char.IsDigit(key[i]))
                    {
                        allDigits = false;
                        break;
                    }
                }

                if (allDigits)
                    key = key.Substring(0, suffixSplit);
            }

            return key;
        }

        private static bool TryGetMeshVectorOverride(
            Dictionary<string, Vector3> map,
            string meshKey,
            out Vector3 value)
        {
            value = Vector3.zero;
            if (map == null || string.IsNullOrWhiteSpace(meshKey))
                return false;

            if (map.TryGetValue(meshKey, out value))
                return true;

            string normalized = NormalizeMeshOverrideKey(meshKey);
            if (!string.IsNullOrWhiteSpace(normalized)
                && !string.Equals(normalized, meshKey, StringComparison.OrdinalIgnoreCase)
                && map.TryGetValue(normalized, out value))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetMeshRotationMode(
            Dictionary<string, RotationOverrideMode> map,
            string meshKey,
            out RotationOverrideMode mode)
        {
            mode = RotationOverrideMode.Absolute;
            if (map == null || string.IsNullOrWhiteSpace(meshKey))
                return false;

            if (map.TryGetValue(meshKey, out mode))
                return true;

            string normalized = NormalizeMeshOverrideKey(meshKey);
            if (!string.IsNullOrWhiteSpace(normalized)
                && !string.Equals(normalized, meshKey, StringComparison.OrdinalIgnoreCase)
                && map.TryGetValue(normalized, out mode))
            {
                return true;
            }

            return false;
        }

        private static RotationOverrideMode ParseRotationOverrideMode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return RotationOverrideMode.Absolute;

            string trimmed = raw.Trim();
            if (trimmed.Equals("offset", StringComparison.OrdinalIgnoreCase))
                return RotationOverrideMode.Offset;

            if (trimmed.Equals("absolute", StringComparison.OrdinalIgnoreCase))
                return RotationOverrideMode.Absolute;

            return RotationOverrideMode.Absolute;
        }

        private static bool TryGetNodeVectorOverride(
            Dictionary<string, Vector3> map,
            string meshKey,
            string nodeName,
            out Vector3 value)
        {
            value = Vector3.zero;
            if (map == null || string.IsNullOrWhiteSpace(meshKey) || string.IsNullOrWhiteSpace(nodeName))
                return false;

            string key = $"{meshKey}|{nodeName}";
            if (map.TryGetValue(key, out value))
                return true;

            string normalized = NormalizeMeshOverrideKey(meshKey);
            if (!string.IsNullOrWhiteSpace(normalized)
                && !string.Equals(normalized, meshKey, StringComparison.OrdinalIgnoreCase))
            {
                string normalizedKey = $"{normalized}|{nodeName}";
                if (map.TryGetValue(normalizedKey, out value))
                    return true;
            }

            // Fallback for exporters that append numeric/node suffixes (e.g. leaves, leaves001).
            string meshPrefix = $"{meshKey}|";
            string normalizedPrefix = !string.IsNullOrWhiteSpace(normalized)
                ? $"{normalized}|"
                : null;
            foreach (var kvp in map)
            {
                string candidateKey = kvp.Key;
                if (string.IsNullOrWhiteSpace(candidateKey))
                    continue;

                bool sameMesh = candidateKey.StartsWith(meshPrefix, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(normalizedPrefix)
                        && candidateKey.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase));
                if (!sameMesh)
                    continue;

                int sep = candidateKey.IndexOf('|');
                if (sep < 0 || sep + 1 >= candidateKey.Length)
                    continue;

                string overrideNode = candidateKey.Substring(sep + 1);
                if (nodeName.IndexOf(overrideNode, StringComparison.OrdinalIgnoreCase) >= 0
                    || overrideNode.IndexOf(nodeName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    value = kvp.Value;
                    return true;
                }
            }

            return false;
        }

        private static void ApplyNodeRotationOverrides(
            Transform root,
            string meshKey,
            Dictionary<string, Vector3> nodeRotationByMeshAndNode)
        {
            if (root == null || string.IsNullOrWhiteSpace(meshKey))
                return;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == root)
                    continue;

                if (!TryGetNodeVectorOverride(nodeRotationByMeshAndNode, meshKey, t.name, out var euler))
                    continue;

                t.localRotation = Quaternion.Euler(euler);
            }
        }

        private static void ApplyNodePositionOverrides(
            Transform root,
            string meshKey,
            Dictionary<string, Vector3> nodePositionOffsetByMeshAndNode)
        {
            if (root == null || string.IsNullOrWhiteSpace(meshKey))
                return;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == root)
                    continue;

                if (!TryGetNodeVectorOverride(nodePositionOffsetByMeshAndNode, meshKey, t.name, out var offset))
                    continue;

                t.localPosition += offset;
            }
        }

        private static void AddStatelCollidersIfMissing(
            GameObject root,
            bool convex,
            bool isTrigger,
            int maxColliders)
        {
            const int maxSafeMeshColliderTriangles = 2097152;

            if (root == null)
                return;

            // Respect authored colliders on imported prefabs.
            if (root.GetComponentsInChildren<Collider>(true).Length > 0)
                return;

            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            int added = 0;

            for (int i = 0; i < meshFilters.Length; i++)
            {
                if (added >= maxColliders)
                    break;

                var mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null)
                    continue;

                var target = mf.gameObject;
                if (target.GetComponent<Collider>() != null)
                    continue;

                int triangleCount = 0;
                try
                {
                    triangleCount = mf.sharedMesh.triangles.Length / 3;
                }
                catch
                {
                    triangleCount = 0;
                }

                if (triangleCount > maxSafeMeshColliderTriangles)
                {
                    var box = target.AddComponent<BoxCollider>();
                    box.center = mf.sharedMesh.bounds.center;
                    box.size = mf.sharedMesh.bounds.size;
                    box.isTrigger = isTrigger;
                    added++;
                    continue;
                }

                var mc = target.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = convex;
                mc.isTrigger = isTrigger;
                added++;
            }
        }

        private static void EnsureSolidColliderForForcedStatel(GameObject root, bool convex, int maxColliders)
        {
            if (root == null)
                return;

            var colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders != null && colliders.Length > 0)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (c == null)
                        continue;

                    c.isTrigger = false;
                    if (c is MeshCollider mc)
                        mc.convex = convex;
                }

                return;
            }

            AddStatelCollidersIfMissing(root, convex, isTrigger: false, maxColliders: maxColliders);
        }

        private static Color ColorFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new Color(0.7f, 0.7f, 0.7f, 1f);

            unchecked
            {
                int hash = name.GetHashCode();
                byte r = (byte)((hash >> 16) & 0xFF);
                byte g = (byte)((hash >> 8) & 0xFF);
                byte b = (byte)(hash & 0xFF);
                return new Color(0.25f + (r / 255f) * 0.6f, 0.25f + (g / 255f) * 0.6f, 0.25f + (b / 255f) * 0.6f, 1f);
            }
        }
    }
}




