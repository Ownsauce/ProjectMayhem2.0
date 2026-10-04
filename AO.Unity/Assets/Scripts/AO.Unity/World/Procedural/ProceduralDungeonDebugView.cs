using WorldGen.UnityIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using WorldGen.Contracts;
using WorldGen.Dungeons;
using WorldGen.Spatial;
using WorldGen.Geometry;
using WorldGen.Content;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.World.Procedural
{
    /// <summary>Optional client adapter for RDB or other authored dungeon kit assets.</summary>
    public interface IProceduralDungeonAssetProvider
    {
        bool TryBuildRoom(DungeonLayout layout, DungeonRoom room, Transform parent);
        bool TryBuildCorridor(DungeonLayout layout, DungeonCorridor corridor, Transform parent);
    }

    public static class ProceduralDungeonAssetProviderRegistry
    {
        public static IProceduralDungeonAssetProvider RdbProvider { get; set; }
    }

    /// <summary>
    /// Opt-in presentation probe for a shared deterministic dungeon layout.
    /// It is not attached to a scene automatically and owns no gameplay state.
    /// </summary>
    public sealed class ProceduralDungeonDebugView : MonoBehaviour
    {
        private const float WorldUnitsPerUnityUnit = 1000f;
        private const float DoorVisualClearHeight = 2.8f;
        private const float MinimumWalkableFloorThickness = 0.75f;

        [SerializeField] private long seed = 123;
        [SerializeField, Range(3, 64)] private int roomCount = 12;
        [SerializeField] private float floorThickness = 0.2f;
        [SerializeField] private float wallThickness = 0.25f;
        [SerializeField] private bool addColliders = true;
        [SerializeField] private float doorAnimationDegreesPerSecond = 240f;
        [SerializeField] private float doorInteractionRange = 3.25f;
        [SerializeField] private float automaticTempleDoorOpenRange = 4.25f;
        [SerializeField] private float automaticTempleDoorCloseRange = 5.75f;
        [SerializeField, Min(1)] private int constructionUnitsPerFrame = 8;
        [SerializeField, Min(0.1f)] private float constructionFrameBudgetMs = 2f;
        [SerializeField] private AOGameServerSession serverSession;
        [SerializeField] private DungeonModuleCatalog authoredModuleCatalog;
        [SerializeField] private bool standaloneTemplePreview;

        private Transform generatedRoot;
        private readonly Dictionary<string, List<DoorLeafPresentation>> doorLeaves =
            new Dictionary<string, List<DoorLeafPresentation>>(StringComparer.Ordinal);
        private readonly Dictionary<Collider, string> interactionDoors =
            new Dictionary<Collider, string>();
        private readonly Dictionary<string, int> doorIndices =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> roomRoots =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, GameObject> corridorRoots =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private DungeonLayout activeLayout;
        private Transform buildParent;
        private bool suppressVisualCollidersForCoreBake;
        private Transform poolRoot;
        private readonly Stack<GameObject> cubePool = new Stack<GameObject>();
        private readonly List<GameObject> activeCubes = new List<GameObject>();
        private readonly List<Mesh> activeAuthoredMeshes = new List<Mesh>();
        private Material proceduralSurfaceMaterial;
        private Material caveGroundMaterial;
        private Texture2D proceduralSurfaceTexture;
        private Texture2D caveGroundTexture;
        private DungeonLayoutProfile activeProfile = DungeonLayoutProfile.Facility;
        private DungeonVisualTheme activeTheme = DungeonVisualTheme.Industrial;
        private DungeonAssetSource activeAssetSource = DungeonAssetSource.Procedural;
        private DungeonPrefabModuleProvider prefabModuleProvider;
        private TempleDungeonKit templeDungeonKit;
        private SubwayDungeonKit subwayDungeonKit;
        private WorldGenPrefabCatalog worldGenPrefabCatalog;
        private int templePiecesPlaced;
        public int TemplePiecesPlaced => templePiecesPlaced;

        public void ConfigureAsStandaloneTemplePreview()
        {
            standaloneTemplePreview = true;
            transform.position = new Vector3(1000f, 0f, 0f);
        }
        public void ConfigureAsStandaloneSubwayPreview()
        {
            standaloneTemplePreview = true;
            transform.position = new Vector3(2000f, 0f, 0f);
        }

        [ContextMenu("Rebuild Subway Dungeon")]
        public void RebuildSubway()
        {
            var manifest = new GenerationManifest("mission-dungeon", "5.13.0", 2,
                unchecked((ulong)seed), "unity-subway-" + seed, "development");
            manifest.Parameters["roomCount"] = roomCount.ToString(CultureInfo.InvariantCulture);
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] = DungeonLayoutProfile.Subway.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] = DungeonVisualTheme.Subway.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] = DungeonAssetSource.Procedural.ToString();
            Build(new DungeonGenerator().Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(roomCount, DungeonLayoutProfile.Subway)));
        }

        private int constructionTicket;
        private DungeonLocation currentLocation;
        private bool buildingAuthoredCaveChamber;
        private bool visibilityDirty;
        private string highlightedDoorId = string.Empty;
        private float nextInteractionAt;

        private sealed class DoorLeafPresentation
        {
            public Transform Pivot;
            public Transform LeafTransform;
            public Renderer Renderer;
            public Collider Collider;
            public float OpenAngle;
            public Vector3 ClosedLocalPosition;
            public Vector3 OpenLocalPosition;
            public bool AutomaticSliding;
            public bool IsOpen;
            public Color StateColor;
            public string RoomId;
            public string ConnectionId;
            public GameObject Root;
        }

        private readonly SubwayLightingController subwayLighting = new SubwayLightingController();
        private void ApplySubwayEnvironment() => subwayLighting.Activate();
        private void RestoreSubwayEnvironment() => subwayLighting.Restore();

        private void OnEnable()
        {
            if (serverSession == null) serverSession = GetComponentInParent<AOGameServerSession>();
            if (serverSession != null)
                serverSession.ProceduralDoorStatesChanged += HandleDoorStatesChanged;
        }

        private void OnDisable()
        {
            RestoreSubwayEnvironment();
            if (serverSession != null)
                serverSession.ProceduralDoorStatesChanged -= HandleDoorStatesChanged;
        }

        private float nextFixtureShadowUpdate;

        private void UpdateFixtureShadows()
        {
            if (activeTheme != DungeonVisualTheme.Subway || generatedRoot == null
                || Time.unscaledTime < nextFixtureShadowUpdate) return;
            nextFixtureShadowUpdate = Time.unscaledTime + .25f;
            Transform player = serverSession != null ? serverSession.ControlledPlayerTransform : null;
            Camera camera = Camera.main;
            if (player == null && camera == null) return;
            Vector3 position = player != null ? player.position : camera.transform.position;
            RefreshFixtureShadows(position);
        }

        public void RefreshFixtureShadows(Vector3 position)
        {
            if (activeTheme != DungeonVisualTheme.Subway || generatedRoot == null) return;
            subwayLighting.RefreshShadows(generatedRoot, position);
        }

        private void Update()
        {
            UpdateFixtureShadows();
            float step = doorAnimationDegreesPerSecond * Time.deltaTime;
            Transform player = serverSession != null
                ? serverSession.ControlledPlayerTransform : null;
            foreach (List<DoorLeafPresentation> leaves in doorLeaves.Values)
            {
                for (int i = 0; i < leaves.Count; i++)
                {
                    DoorLeafPresentation leaf = leaves[i];
                    if (leaf?.Pivot == null) continue;
                    if (leaf.AutomaticSliding)
                    {
                        float distance = player != null
                            ? Vector3.Distance(player.position, leaf.Pivot.position)
                            : float.MaxValue;
                        leaf.IsOpen = leaf.IsOpen
                            ? distance < automaticTempleDoorCloseRange
                            : distance < automaticTempleDoorOpenRange;
                        if (leaf.LeafTransform != null)
                        {
                            Vector3 targetPosition = leaf.IsOpen
                                ? leaf.OpenLocalPosition : leaf.ClosedLocalPosition;
                            leaf.LeafTransform.localPosition = Vector3.MoveTowards(
                                leaf.LeafTransform.localPosition, targetPosition,
                                doorAnimationDegreesPerSecond * 0.004f * Time.deltaTime);
                        }
                        if (leaf.Collider != null)
                            leaf.Collider.enabled = addColliders && !leaf.IsOpen;
                        continue;
                    }
                    Quaternion target = Quaternion.Euler(0f, leaf.IsOpen ? leaf.OpenAngle : 0f, 0f);
                    leaf.Pivot.localRotation = Quaternion.RotateTowards(
                        leaf.Pivot.localRotation, target, step);
                }
            }
            UpdateDoorInteraction();
            UpdateRoomVisibility();
        }

        [ContextMenu("Rebuild Procedural Dungeon")]
        public void Rebuild()
        {
            Clear();
            var manifest = new GenerationManifest(
                "mission-dungeon", "5.9.0", 2, unchecked((ulong)seed),
                "unity-debug-" + seed, "development");
            manifest.Parameters["roomCount"] = roomCount.ToString(CultureInfo.InvariantCulture);
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                DungeonLayoutProfile.Facility.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
                DungeonVisualTheme.Industrial.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] =
                DungeonAssetSource.Procedural.ToString();
            DungeonLayout layout = new DungeonGenerator().Generate(
                manifest, DungeonGenerationProfileCatalog.CreateParameters(
                    roomCount, DungeonLayoutProfile.Facility));
            Build(layout);
        }

        [ContextMenu("Rebuild Temple Dungeon")]
        public void RebuildTemple()
        {
            Clear();
            var manifest = new GenerationManifest(
                "temple-dungeon", "5.9.0", 2, unchecked((ulong)seed),
                "unity-temple-" + seed, "development");
            manifest.Parameters["roomCount"] = roomCount.ToString(CultureInfo.InvariantCulture);
            manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] =
                DungeonLayoutProfile.Temple.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.ThemeParameter] =
                DungeonVisualTheme.Temple.ToString();
            manifest.Parameters[DungeonGenerationProfileCatalog.AssetSourceParameter] =
                DungeonAssetSource.Procedural.ToString();
            Build(new DungeonGenerator().Generate(manifest,
                DungeonGenerationProfileCatalog.CreateParameters(roomCount,
                    DungeonLayoutProfile.Temple)));
        }

        public bool IsPresentationReady { get; private set; }

        public void Build(DungeonLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            Clear();
            Transform[] roots = FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < roots.Length; i++)
            {
                Transform candidate = roots[i];
                if (candidate == null
                    || !candidate.name.StartsWith("Generated Dungeon ", StringComparison.Ordinal))
                    continue;
                candidate.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(candidate.gameObject);
                else DestroyImmediate(candidate.gameObject);
            }
            generatedRoot = new GameObject("Generated Dungeon " + layout.Manifest.WorldId).transform;
            PrototypeWorldBootstrap world = FindFirstObjectByType<PrototypeWorldBootstrap>();
            generatedRoot.SetParent(!standaloneTemplePreview && world != null && world.ActivePlayfieldRoot != null
                ? world.ActivePlayfieldRoot : transform, false);
            activeLayout = layout;
            ConfigureVisualKit(layout);
            buildParent = generatedRoot;
            int ticket = constructionTicket;
            if (Application.isPlaying)
                StartCoroutine(BuildPresentationCoroutine(layout, ticket));
            else
                BuildPresentationImmediately(layout);
        }

        [ContextMenu("Clear Procedural Dungeon")]
        public void Clear()
        {
            IsPresentationReady = false;
            RestoreSubwayEnvironment();
            constructionTicket++;
            templePiecesPlaced = 0;
            for (int i = activeCubes.Count - 1; i >= 0; i--)
                ReleaseCube(activeCubes[i]);
            activeCubes.Clear();
            for (int i = 0; i < activeAuthoredMeshes.Count; i++)
                if (activeAuthoredMeshes[i] != null)
                {
                    if (Application.isPlaying) Destroy(activeAuthoredMeshes[i]);
                    else DestroyImmediate(activeAuthoredMeshes[i]);
                }
            activeAuthoredMeshes.Clear();
            doorLeaves.Clear();
            interactionDoors.Clear();
            doorIndices.Clear();
            roomRoots.Clear();
            corridorRoots.Clear();
            activeLayout = null;
            buildParent = null;
            currentLocation = null;
            visibilityDirty = false;
            highlightedDoorId = string.Empty;
            if (generatedRoot == null) return;
            generatedRoot.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(generatedRoot.gameObject);
            else DestroyImmediate(generatedRoot.gameObject);
            generatedRoot = null;
        }

        private void OnDestroy()
        {
            if (poolRoot != null)
            {
                if (Application.isPlaying) Destroy(poolRoot.gameObject);
                else DestroyImmediate(poolRoot.gameObject);
                poolRoot = null;
                cubePool.Clear();
            }
            if (proceduralSurfaceMaterial != null) Destroy(proceduralSurfaceMaterial);
            if (caveGroundMaterial != null) Destroy(caveGroundMaterial);
            if (proceduralSurfaceTexture != null) Destroy(proceduralSurfaceTexture);
        }

        private IEnumerator BuildPresentationCoroutine(DungeonLayout layout, int ticket)
        {
            var frameWatch = System.Diagnostics.Stopwatch.StartNew();
            int unitsThisFrame = 0;
            for (int i = 0; i < layout.Rooms.Count; i++)
            {
                if (ticket != constructionTicket) yield break;
                BuildRoom(layout, layout.Rooms[i]);
                if (ShouldYieldConstruction(frameWatch, ++unitsThisFrame))
                { yield return null; unitsThisFrame = 0; frameWatch.Restart(); }
            }
            for (int i = 0; i < layout.Corridors.Count; i++)
            {
                if (ticket != constructionTicket) yield break;
                BuildCorridor(layout.Corridors[i], i);
                if (ShouldYieldConstruction(frameWatch, ++unitsThisFrame))
                { yield return null; unitsThisFrame = 0; frameWatch.Restart(); }
            }
            IReadOnlyList<DungeonDoor> doors = DungeonDoorFactory.Create(layout);
            for (int i = 0; i < doors.Count; i++)
            {
                if (ticket != constructionTicket) yield break;
                BuildDoor(doors[i], i);
                if (ShouldYieldConstruction(frameWatch, ++unitsThisFrame))
                { yield return null; unitsThisFrame = 0; frameWatch.Restart(); }
            }
            for (int i = 0; i < layout.SpawnPoints.Count; i++)
            {
                if (ticket != constructionTicket) yield break;
                BuildSpawn(layout.SpawnPoints[i], i);
                if (ShouldYieldConstruction(frameWatch, ++unitsThisFrame))
                { yield return null; unitsThisFrame = 0; frameWatch.Restart(); }
            }
            if (ticket != constructionTicket) yield break;
            FinishPresentationBuild();
        }

        private void BuildPresentationImmediately(DungeonLayout layout)
        {
            for (int i = 0; i < layout.Rooms.Count; i++) BuildRoom(layout, layout.Rooms[i]);
            for (int i = 0; i < layout.Corridors.Count; i++) BuildCorridor(layout.Corridors[i], i);
            IReadOnlyList<DungeonDoor> doors = DungeonDoorFactory.Create(layout);
            for (int i = 0; i < doors.Count; i++) BuildDoor(doors[i], i);
            for (int i = 0; i < layout.SpawnPoints.Count; i++) BuildSpawn(layout.SpawnPoints[i], i);
            FinishPresentationBuild();
        }

        private bool ShouldYieldConstruction(System.Diagnostics.Stopwatch frameWatch,
            int unitsThisFrame)
        {
            return unitsThisFrame >= Mathf.Max(1, constructionUnitsPerFrame)
                   || frameWatch.Elapsed.TotalMilliseconds
                   >= Mathf.Max(0.1f, constructionFrameBudgetMs);
        }

        private void FinishPresentationBuild()
        {
            Physics.SyncTransforms();
            IsPresentationReady = true;
            if (activeTheme == DungeonVisualTheme.Temple)
                Debug.Log("Temple dungeon kit: " + templePiecesPlaced +
                    " authored pieces placed.", this);
            HandleDoorStatesChanged(serverSession != null
                ? serverSession.ProceduralDoorStates
                : new Dictionary<string, DungeonDoorStateSnapshot>());
            visibilityDirty = true;
            UpdateRoomVisibility();
            if (activeTheme == DungeonVisualTheme.Subway)
            {
                int pieces = 0;
                foreach (Transform piece in generatedRoot.GetComponentsInChildren<Transform>(true))
                    if (piece.name.EndsWith("[Subway GLB]", StringComparison.Ordinal)) pieces++;
                string message = "Subway presentation ready: GLB pieces=" + pieces
                    + ", kit=" + (subwayDungeonKit != null ? "loaded" : "unavailable");
                Debug.Log("[WorldGen] " + message, this);
                AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(message);
                return;
            }
            AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                $"Procedural presentation ready: rooms={roomRoots.Count}, "
                + $"corridors={corridorRoots.Count}, pooled={cubePool.Count}.");
        }

        private void BuildDoor(DungeonDoor door, int index)
        {
            doorIndices[door.Id] = index;
            var leaves = new List<DoorLeafPresentation>(1)
            {
                BuildDoorPortal(door.Id, door.Portal, 0)
            };
            doorLeaves.Add(door.Id, leaves);
        }

        private DoorLeafPresentation BuildDoorPortal(string doorId,
            DungeonDoorPortal portal, int side)
        {
            var portalRoot = new GameObject($"Door {doorId} Side {side} Root");
            portalRoot.transform.SetParent(generatedRoot, false);
            Transform previousBuildParent = buildParent;
            buildParent = portalRoot.transform;
            WorldBounds bounds = portal.ClosedBlockingBounds;
            bool xWall = portal.Facing == DungeonPortalFacing.NegativeX
                         || portal.Facing == DungeonPortalFacing.PositiveX;
            float minY = ToUnity(bounds.Minimum.Y);
            float availableHeight = ToUnity(bounds.Maximum.Y - bounds.Minimum.Y);
            float height = Mathf.Min(DoorVisualClearHeight,
                Mathf.Max(0.5f, availableHeight - 0.1f));
            float width = ToUnity(xWall
                ? bounds.Maximum.Z - bounds.Minimum.Z
                : bounds.Maximum.X - bounds.Minimum.X);
            float thickness = Mathf.Max(0.08f, ToUnity(xWall
                ? bounds.Maximum.X - bounds.Minimum.X
                : bounds.Maximum.Z - bounds.Minimum.Z));
            Vector3 center = BoundsCenter(bounds);
            float postWidth = Mathf.Min(0.15f, width * 0.15f);
            float headerHeight = 0.18f;

            Vector3 firstPost = xWall
                ? new Vector3(center.x, minY + height * 0.5f,
                    ToUnity(bounds.Minimum.Z) - postWidth * 0.5f)
                : new Vector3(ToUnity(bounds.Minimum.X) - postWidth * 0.5f,
                    minY + height * 0.5f, center.z);
            Vector3 secondPost = xWall
                ? new Vector3(center.x, minY + height * 0.5f,
                    ToUnity(bounds.Maximum.Z) + postWidth * 0.5f)
                : new Vector3(ToUnity(bounds.Maximum.X) + postWidth * 0.5f,
                    minY + height * 0.5f, center.z);
            Vector3 postScale = xWall
                ? new Vector3(thickness + 0.08f, height, postWidth)
                : new Vector3(postWidth, height, thickness + 0.08f);
            Vector3 headerPosition = new Vector3(center.x,
                minY + height + headerHeight * 0.5f, center.z);
            Vector3 headerScale = xWall
                ? new Vector3(thickness + 0.08f, headerHeight, width + postWidth * 2f)
                : new Vector3(width + postWidth * 2f, headerHeight, thickness + 0.08f);
            Color frameColor = activeProfile == DungeonLayoutProfile.CaveDungeon
                ? new Color(0.23f, 0.20f, 0.17f)
                : new Color(0.16f, 0.2f, 0.24f);
            if (!TryBuildCatalogDoorway(doorId, side, portal.Facing, center, minY, width, height))
            {
                CreateCube($"Door {doorId} Side {side} Frame A", firstPost, postScale,
                    frameColor, collidable: false);
                CreateCube($"Door {doorId} Side {side} Frame B", secondPost, postScale,
                    frameColor, collidable: false);
                CreateCube($"Door {doorId} Side {side} Header", headerPosition, headerScale,
                    frameColor, collidable: false);
            }
            BuildDoorwayFacade(doorId, portal, xWall, minY, height, thickness, frameColor);

            var pivot = new GameObject($"Door {doorId} Side {side} Hinge").transform;
            pivot.SetParent(buildParent, false);
            pivot.localPosition = xWall
                ? new Vector3(center.x, minY + height * 0.5f, ToUnity(bounds.Minimum.Z))
                : new Vector3(ToUnity(bounds.Minimum.X), minY + height * 0.5f, center.z);
            GameObject leaf = CreateCubeObject($"Door {doorId} Side {side} Leaf",
                Vector3.zero,
                xWall ? new Vector3(thickness, height, width)
                      : new Vector3(width, height, thickness),
                new Color(0.22f, 0.48f, 0.68f), collidable: true, pivot);
            leaf.transform.localPosition = xWall
                ? new Vector3(0f, 0f, width * 0.5f)
                : new Vector3(width * 0.5f, 0f, 0f);
            bool automaticTempleDoor = activeProfile == DungeonLayoutProfile.Temple
                || activeProfile == DungeonLayoutProfile.TempleReference;
            Vector3 closedLocalPosition = leaf.transform.localPosition;
            Vector3 openLocalPosition = closedLocalPosition + (xWall
                ? new Vector3(0f, 0f, width * 0.92f)
                : new Vector3(width * 0.92f, 0f, 0f));
            if (automaticTempleDoor)
                SetRendererColor(leaf.GetComponent<Renderer>(),
                    new Color(0.38f, 0.30f, 0.19f));
            if (activeProfile == DungeonLayoutProfile.Subway)
            {
                if (subwayDungeonKit != null && subwayDungeonKit.Place("door", "Door " + doorId + " Metal Leaf",
                    leaf.transform, Vector3.zero, Vector3.one, Quaternion.Euler(0, xWall ? 90 : 0, 0)))
                {
                    // Parent to the existing leaf so hinge rotation, collision, and door states remain shared.
                    leaf.GetComponent<Renderer>().forceRenderingOff = true;
                }
                else BuildAuthoredSubwayDoorLeaf(doorId, pivot, xWall, width, height, thickness);
            }
            Collider leafCollider = leaf.GetComponent<Collider>();
            if (leafCollider != null) interactionDoors.Add(leafCollider, doorId);
            var interactionObject = new GameObject($"Door {doorId} Side {side} Interaction");
            interactionObject.transform.SetParent(buildParent, false);
            interactionObject.transform.localPosition = new Vector3(center.x,
                minY + height * 0.5f, center.z);
            var interactionCollider = interactionObject.AddComponent<BoxCollider>();
            interactionCollider.size = xWall
                ? new Vector3(Mathf.Max(0.5f, thickness), height, width)
                : new Vector3(width, height, Mathf.Max(0.5f, thickness));
            interactionCollider.isTrigger = true;
            interactionDoors.Add(interactionCollider, doorId);
            buildParent = previousBuildParent;
            return new DoorLeafPresentation
            {
                Pivot = pivot,
                LeafTransform = leaf.transform,
                Renderer = leaf.GetComponent<Renderer>(),
                Collider = leaf.GetComponent<Collider>(),
                OpenAngle = portal.Facing == DungeonPortalFacing.PositiveX
                            || portal.Facing == DungeonPortalFacing.NegativeZ ? 90f : -90f,
                IsOpen = true,
                StateColor = new Color(0.22f, 0.48f, 0.68f),
                ClosedLocalPosition = closedLocalPosition,
                OpenLocalPosition = openLocalPosition,
                AutomaticSliding = automaticTempleDoor,
                RoomId = portal.RoomId,
                ConnectionId = portal.ConnectionId,
                Root = portalRoot
            };
        }

        private bool TryBuildCatalogDoorway(string doorId, int side, DungeonPortalFacing facing,
            Vector3 center, float floor, float width, float height)
        {
            if (activeTheme == DungeonVisualTheme.Subway && subwayDungeonKit != null
                && subwayDungeonKit.PlaceMetalDoorFrame("Door " + doorId + " Metal Frame " + side,
                    buildParent, new Vector3(center.x, floor, center.z), width, height,
                    Quaternion.Euler(0, facing == DungeonPortalFacing.PositiveX ? 90
                        : facing == DungeonPortalFacing.NegativeX ? 270 : facing == DungeonPortalFacing.NegativeZ ? 180 : 0, 0)))
                return true;
            if (worldGenPrefabCatalog == null) return false;
            string theme = activeProfile == DungeonLayoutProfile.Temple
                || activeProfile == DungeonLayoutProfile.TempleReference ? "temple"
                : activeProfile == DungeonLayoutProfile.Subway ? "subway" : string.Empty;
            int yaw = facing == DungeonPortalFacing.PositiveX ? 90
                : facing == DungeonPortalFacing.NegativeX ? 270
                : facing == DungeonPortalFacing.NegativeZ ? 180 : 0;
            WorldGenPrefabCatalog.Entry entry = worldGenPrefabCatalog.ResolveDoorway(
                theme, width, height, yaw, doorId + "/" + side);
            if (entry == null) return false;
            GameObject frame = Instantiate(entry.Prefab, buildParent, false);
            frame.name = "Door " + doorId + " Frame " + entry.Asset.id;
            frame.transform.localPosition = new Vector3(center.x, floor, center.z);
            frame.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return true;
        }

        private void BuildAuthoredSubwayDoorLeaf(string doorId, Transform pivot,
            bool xWall, float width, float height, float thickness)
        {
            Color inset = new Color(0.075f, 0.10f, 0.11f);
            Color glass = new Color(0.10f, 0.31f, 0.39f);
            Color warning = new Color(0.94f, 0.64f, 0.06f);
            float face = thickness * 0.56f;
            Vector3 panelPosition = xWall
                ? new Vector3(face, 0.18f, width * 0.50f)
                : new Vector3(width * 0.50f, 0.18f, face);
            Vector3 panelScale = xWall
                ? new Vector3(0.035f, height * 0.58f, width * 0.72f)
                : new Vector3(width * 0.72f, height * 0.58f, 0.035f);
            CreateCubeObject("Door " + doorId + " Recessed Panel", panelPosition,
                panelScale, inset, false, pivot);
            Vector3 windowPosition = xWall
                ? new Vector3(face + 0.022f, height * 0.25f, width * 0.50f)
                : new Vector3(width * 0.50f, height * 0.25f, face + 0.022f);
            Vector3 windowScale = xWall
                ? new Vector3(0.04f, height * 0.22f, width * 0.42f)
                : new Vector3(width * 0.42f, height * 0.22f, 0.04f);
            CreateCubeObject("Door " + doorId + " Window", windowPosition,
                windowScale, glass, false, pivot);
            Vector3 stripePosition = xWall
                ? new Vector3(face + 0.026f, -height * 0.26f, width * 0.50f)
                : new Vector3(width * 0.50f, -height * 0.26f, face + 0.026f);
            Vector3 stripeScale = xWall
                ? new Vector3(0.045f, 0.13f, width * 0.78f)
                : new Vector3(width * 0.78f, 0.13f, 0.045f);
            CreateCubeObject("Door " + doorId + " Safety Stripe", stripePosition,
                stripeScale, warning, false, pivot);
        }

        private void BuildDoorwayFacade(string doorId, DungeonDoorPortal portal, bool xWall,
            float minY, float leafHeight, float thickness, Color color)
        {
            DungeonCorridor corridor = null;
            for (int i = 0; activeLayout != null && i < activeLayout.Corridors.Count; i++)
                if (activeLayout.Corridors[i].ConnectionId == portal.ConnectionId)
                { corridor = activeLayout.Corridors[i]; break; }
            if (corridor == null) return;

            WorldBounds door = portal.ClosedBlockingBounds;
            WorldBounds hall = corridor.WalkableBounds;
            float fixedAxis = ToUnity(xWall ? portal.Center.X : portal.Center.Z);
            float doorStart = ToUnity(xWall ? door.Minimum.Z : door.Minimum.X);
            float doorEnd = ToUnity(xWall ? door.Maximum.Z : door.Maximum.X);
            float hallStart = ToUnity(xWall ? hall.Minimum.Z : hall.Minimum.X);
            float hallEnd = ToUnity(xWall ? hall.Maximum.Z : hall.Maximum.X);
            float fullHeight = ToUnity(door.Maximum.Y - door.Minimum.Y);
            bool authoredCaveEntrance = activeLayout != null
                && activeLayout.Rooms.Count > 0
                && DungeonCaveChamber.IsEntranceModule(activeLayout, activeLayout.Rooms[0])
                && portal.RoomId == activeLayout.Rooms[0].Id;
            if (authoredCaveEntrance) fullHeight = Mathf.Min(fullHeight, 3.30f);

            CreateFacadeSpan("Left", hallStart, doorStart, fullHeight);
            CreateFacadeSpan("Right", doorEnd, hallEnd, fullHeight);
            float lintelHeight = Mathf.Max(0f, fullHeight - leafHeight);
            if (lintelHeight > 0.05f)
            {
                float center = (doorStart + doorEnd) * 0.5f;
                Vector3 position = xWall
                    ? new Vector3(fixedAxis, minY + leafHeight + lintelHeight * 0.5f, center)
                    : new Vector3(center, minY + leafHeight + lintelHeight * 0.5f, fixedAxis);
                Vector3 scale = xWall
                    ? new Vector3(thickness, lintelHeight, doorEnd - doorStart)
                    : new Vector3(doorEnd - doorStart, lintelHeight, thickness);
                CreateCube("Door " + doorId + " Upper Infill", position, scale, color, false);
                float signWidth = Mathf.Min(0.78f, (doorEnd - doorStart) * 0.62f);
                Vector3 signPosition = xWall
                    ? new Vector3(fixedAxis, minY + leafHeight + Mathf.Min(0.3f, lintelHeight * 0.5f), center)
                    : new Vector3(center, minY + leafHeight + Mathf.Min(0.3f, lintelHeight * 0.5f), fixedAxis);
                Vector3 signScale = xWall
                    ? new Vector3(thickness + 0.05f, 0.16f, signWidth)
                    : new Vector3(signWidth, 0.16f, thickness + 0.05f);
                if (!authoredCaveEntrance)
                    CreateCube("Door " + doorId + " Header Sign", signPosition, signScale,
                        new Color(0.12f, 0.58f, 0.72f), false);
            }

            void CreateFacadeSpan(string label, float start, float end, float facadeHeight)
            {
                float span = end - start;
                if (span <= 0.01f) return;
                float center = (start + end) * 0.5f;
                Vector3 position = xWall
                    ? new Vector3(fixedAxis, minY + facadeHeight * 0.5f, center)
                    : new Vector3(center, minY + facadeHeight * 0.5f, fixedAxis);
                Vector3 scale = xWall
                    ? new Vector3(thickness, facadeHeight, span)
                    : new Vector3(span, facadeHeight, thickness);
                CreateCube("Door " + doorId + " " + label + " Infill", position, scale, color);
            }
        }

        private void HandleDoorStatesChanged(
            IReadOnlyDictionary<string, DungeonDoorStateSnapshot> states)
        {
            foreach (KeyValuePair<string, List<DoorLeafPresentation>> pair in doorLeaves)
            {
                DungeonDoorStateSnapshot state = null;
                states?.TryGetValue(pair.Key, out state);
                bool open = state == null || state.State == DungeonDoorState.Open;
                Color color = state?.State == DungeonDoorState.Locked
                    ? new Color(0.82f, 0.56f, 0.12f)
                    : state?.State == DungeonDoorState.Sealed
                        ? new Color(0.68f, 0.16f, 0.2f)
                        : new Color(0.22f, 0.48f, 0.68f);
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    DoorLeafPresentation leaf = pair.Value[i];
                    if (leaf.AutomaticSliding)
                    {
                        leaf.StateColor = new Color(0.38f, 0.30f, 0.19f);
                        SetRendererColor(leaf.Renderer, leaf.StateColor);
                        continue;
                    }
                    leaf.IsOpen = open;
                    leaf.StateColor = color;
                    if (leaf.Collider != null) leaf.Collider.enabled = addColliders && !open;
                    SetRendererColor(leaf.Renderer,
                        pair.Key == highlightedDoorId ? HighlightColor(color) : color);
                }
            }
            visibilityDirty = true;
        }

        private void UpdateDoorInteraction()
        {
            if (WorldGenLayoutDebugOverlay.IsOpen)
                return;
            if (activeProfile == DungeonLayoutProfile.Temple
                || activeProfile == DungeonLayoutProfile.TempleReference)
            {
                if (!string.IsNullOrEmpty(highlightedDoorId))
                {
                    highlightedDoorId = string.Empty;
                    RefreshDoorColors();
                }
                return;
            }
            string aimedDoor = ResolveAimedDoor();
            if (!string.Equals(aimedDoor, highlightedDoorId, StringComparison.Ordinal))
            {
                highlightedDoorId = aimedDoor;
                RefreshDoorColors();
            }
            if (string.IsNullOrEmpty(aimedDoor) || Time.unscaledTime < nextInteractionAt
                || IsUiInputActive())
                return;
            bool interact = WasInteractKeyPressedThisFrame();
            if (!interact && WasRightClickPressedThisFrame())
                interact = EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject();
            if (!interact || serverSession == null
                || !serverSession.ProceduralDoorStates.TryGetValue(
                    aimedDoor, out DungeonDoorStateSnapshot state))
                return;
            nextInteractionAt = Time.unscaledTime + 0.2f;
            int doorIndex = doorIndices.TryGetValue(aimedDoor, out int resolvedIndex)
                ? resolvedIndex : -1;
            AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                doorIndex >= 0
                    ? $"Using procedural door {doorIndex}: {aimedDoor}"
                    : $"Using procedural door: {aimedDoor}");
            serverSession.RequestProceduralDoorUse(aimedDoor, state.Revision);
        }

        private string ResolveAimedDoor()
        {
            Camera camera = Camera.main;
            Transform player = serverSession != null ? serverSession.ControlledPlayerTransform : null;
            if (camera == null || player == null) return string.Empty;
            Vector2 pointer = ReadPointerPosition();
            if (Cursor.lockState == CursorLockMode.Locked)
                pointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Ray ray = camera.ScreenPointToRay(pointer);
            RaycastHit[] hits = Physics.RaycastAll(ray, 12f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null)
                    continue;
                if (!interactionDoors.TryGetValue(collider, out string doorId))
                {
                    if (!collider.isTrigger) return string.Empty;
                    continue;
                }
                if (Vector3.Distance(player.position, collider.bounds.center) <= doorInteractionRange)
                    return doorId;
            }
            return string.Empty;
        }

        private void RefreshDoorColors()
        {
            foreach (KeyValuePair<string, List<DoorLeafPresentation>> pair in doorLeaves)
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    DoorLeafPresentation leaf = pair.Value[i];
                    SetRendererColor(leaf.Renderer, pair.Key == highlightedDoorId
                        ? HighlightColor(leaf.StateColor) : leaf.StateColor);
                }
        }

        private static Color HighlightColor(Color color) => Color.Lerp(color, Color.white, 0.45f);

        private static bool IsUiInputActive()
        {
            GameObject selected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponentInParent<InputField>() != null;
        }

        private static bool WasInteractKeyPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.E);
#endif
        }

        private static bool WasRightClickPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        private static Vector2 ReadPointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
            return Input.mousePosition;
#endif
        }

        private void BuildRoom(DungeonLayout layout, DungeonRoom room)
        {
            var root = new GameObject("Room " + room.Index + " " + room.Id);
            root.transform.SetParent(generatedRoot, false);
            roomRoots.Add(room.Id, root);
            Transform previousBuildParent = buildParent;
            bool previousSuppressVisualColliders = suppressVisualCollidersForCoreBake;
            buildParent = root.transform;
            suppressVisualCollidersForCoreBake =
                activeProfile != DungeonLayoutProfile.CaveDungeon;
            if (TryBuildCatalogRoom(room, root.transform))
            {
                suppressVisualCollidersForCoreBake = previousSuppressVisualColliders;
                buildParent = previousBuildParent;
                return;
            }
            if (TryBuildExternalRoom(layout, room, root.transform))
            {
                suppressVisualCollidersForCoreBake = previousSuppressVisualColliders;
                buildParent = previousBuildParent;
                return;
            }
            if (DungeonCaveChamber.IsEntranceModule(layout, room))
            {
                BuildAuthoredCaveChamber(layout, room);
                suppressVisualCollidersForCoreBake = previousSuppressVisualColliders;
                buildParent = previousBuildParent;
                return;
            }
            IReadOnlyList<WorldBounds> floorSections = DungeonRoomGeometry.FloorSections(room);
            int subwayStepStart = DungeonRoomGeometry.IsSubwayTrackRoom(room)
                ? floorSections.Count - DungeonRoomGeometry.SubwayTrackAccessSteps(room).Count : floorSections.Count;
            for (int i = 0; i < floorSections.Count; i++)
            {
                WorldBounds section = floorSections[i];
                float walkableThickness = DungeonRoomGeometry.IsSubwayTrackRoom(room)
                    ? floorThickness : Mathf.Max(floorThickness, MinimumWalkableFloorThickness);
                if (activeProfile == DungeonLayoutProfile.CaveDungeon)
                {
                    Vector3 floorSize = BoundsSize(section);
                    floorSize.y = walkableThickness;
                    Vector3 floorCenter = BoundsCenter(section);
                    floorCenter.y = ToUnity(section.Minimum.Y) - walkableThickness * 0.5f;
                    CreateInvisibleCollisionBox("Cave Floor Support", floorCenter, floorSize);
                    BuildCaveFloorMesh(room, section, i);
                }
                else if (activeTheme == DungeonVisualTheme.Subway && i >= subwayStepStart)
                    BuildSubwayStep("Room " + room.Index + " Stair " + i, section,
                        room.Size.X < room.Size.Z, ToUnity(DungeonRoomGeometry.SubwayTrackFloorY(room)) - .2f, .2f);
                else BuildBox("Room Floor " + room.Index + " Section " + i,
                    section, walkableThickness, RoomFloorColor(room));
                if (activeTheme == DungeonVisualTheme.Temple)
                    BuildTempleHorizontalTiles(section, false, TempleSurfaceId(false, room.Index));
                Vector3 sectionSize = BoundsSize(section);
                Vector3 sectionCenter = BoundsCenter(section);
                Vector3 ceilingCenter = new Vector3(sectionCenter.x, ToUnity(section.Maximum.Y), sectionCenter.z);
                Vector3 ceilingSize = new Vector3(sectionSize.x, floorThickness, sectionSize.z);
                if (activeProfile == DungeonLayoutProfile.CaveDungeon)
                {
                    CreateInvisibleCollisionBox("Cave Ceiling Support", ceilingCenter, ceilingSize);
                    BuildCaveCeilingMesh(room, section, i);
                }
                else CreateCube("Room " + room.Index + " Ceiling Section " + i,
                    ceilingCenter, ceilingSize, RoomWallColor(room), assetRole: DungeonAssetRole.Ceiling);
                if (activeTheme == DungeonVisualTheme.Temple)
                    BuildTempleHorizontalTiles(section, true, TempleSurfaceId(true, room.Index));
            }
            if (DungeonRoomGeometry.IsSubwayTrackRoom(room))
            {
                for (int side = 0; side < 2; side++)
                    for (int segment = 0; segment < 2; segment++)
                    {
                        WorldBounds platformEdge =
                            DungeonRoomGeometry.SubwayPlatformEdgeSegmentBounds(
                                room, side == 1, segment == 1);
                        CreateCube("Room " + room.Index + " Platform Edge " + side
                            + " Segment " + segment, BoundsCenter(platformEdge),
                            BoundsSize(platformEdge), RoomWallColor(room));
                    }
                for (int platform = 0; platform < 2; platform++)
                    for (int stairSide = 0; stairSide < 2; stairSide++)
                    {
                        WorldBounds cheek = DungeonRoomGeometry.SubwayTrackStairSideBounds(
                            room, platform == 1, stairSide == 1);
                        BuildSubwayPlatformHandrail(room, cheek, platform, stairSide);
                        CreateCube("Room " + room.Index + " Stair Cheek " + platform
                            + "/" + stairSide, BoundsCenter(cheek), BoundsSize(cheek),
                            RoomWallColor(room));
                    }
                // Train-tunnel headers are collision-only here. Their visible surface is
                // the fitted arch infill built with the room details; drawing both in the
                // same plane produces dark z-fighting above the portal.
            }
            IReadOnlyList<DungeonRoomWallSection> wallSections = DungeonRoomGeometry.WallSections(room);
            for (int i = 0; i < wallSections.Count; i++)
                BuildWall(room, wallSections[i], layout.DoorPortals);
            if (activeTheme == DungeonVisualTheme.Subway) BuildSubwayWallCorners(room, wallSections);
            BuildRoomModuleDetails(room, layout.DoorPortals);
            if (suppressVisualCollidersForCoreBake)
            {
                IReadOnlyList<DungeonCollisionBox> collision =
                    DungeonCollisionBaker.BakeRoom(layout, room);
                for (int i = 0; i < collision.Count; i++)
                    CreateInvisibleCollisionBox("Core Collision " + collision[i].Id,
                        BoundsCenter(collision[i].Bounds), BoundsSize(collision[i].Bounds));
            }
            suppressVisualCollidersForCoreBake = previousSuppressVisualColliders;
            buildParent = previousBuildParent;
        }

        private void BuildCorridor(DungeonCorridor corridor, int index)
        {
            var root = new GameObject("Corridor " + index + " " + corridor.Id);
            root.transform.SetParent(generatedRoot, false);
            corridorRoots.Add(corridor.ConnectionId, root);
            Transform previousBuildParent = buildParent;
            bool previousSuppressVisualColliders = suppressVisualCollidersForCoreBake;
            buildParent = root.transform;
            suppressVisualCollidersForCoreBake =
                activeProfile != DungeonLayoutProfile.CaveDungeon;
            if (TryBuildCatalogPassage(corridor, root.transform))
            {
                suppressVisualCollidersForCoreBake = previousSuppressVisualColliders;
                buildParent = previousBuildParent; return;
            }
            if (TryBuildExternalCorridor(activeLayout, corridor, root.transform))
            {
                suppressVisualCollidersForCoreBake = previousSuppressVisualColliders;
                buildParent = previousBuildParent; return;
            }
            if (DungeonCaveTunnel.IsFirstBend(activeLayout, corridor)
                || DungeonCaveTunnel.IsWideDescent(activeLayout, corridor))
            {
                BuildAuthoredCaveBend(corridor, index);
                suppressVisualCollidersForCoreBake = previousSuppressVisualColliders;
                buildParent = previousBuildParent;
                return;
            }
            IReadOnlyList<WorldBounds> corridorFloorSections =
                DungeonCorridorGeometry.FloorSections(activeLayout, corridor);
            for (int section = 0; section < corridorFloorSections.Count; section++)
            {
                float corridorWalkableThickness = Mathf.Max(floorThickness,
                    ToUnity(DungeonCorridorGeometry.FloorSupportDepth(
                        corridor, corridorFloorSections[section])));
                if (activeProfile == DungeonLayoutProfile.CaveDungeon)
                {
                    WorldBounds floorSection = corridorFloorSections[section];
                    Vector3 floorSize = BoundsSize(floorSection);
                    floorSize.y = corridorWalkableThickness;
                    Vector3 floorCenter = BoundsCenter(floorSection);
                    floorCenter.y = ToUnity(floorSection.Minimum.Y) - corridorWalkableThickness * 0.5f;
                    CreateInvisibleCollisionBox("Cave Tunnel Floor Support", floorCenter, floorSize);
                    BuildCaveFloorMesh(null, floorSection, index + 1000);
                }
                else if (activeTheme == DungeonVisualTheme.Subway && corridor.ChangesElevation)
                    BuildSubwayStep("Corridor " + index + " Stair " + section, corridorFloorSections[section],
                        DungeonCorridorGeometry.IsAlongX(activeLayout, corridor),
                        ToUnity(corridor.WalkableBounds.Minimum.Y) - .2f,
                        Mathf.Max(.05f, ToUnity(Math.Abs(corridor.ToFloorY - corridor.FromFloorY)) / Mathf.Max(1, corridorFloorSections.Count - 1)));
                else BuildBox("Corridor Floor " + index + " Section " + section,
                    corridorFloorSections[section], corridorWalkableThickness,
                    ThemeFloorColor());
                if (activeTheme == DungeonVisualTheme.Temple)
                    BuildTempleHorizontalTiles(corridorFloorSections[section], false, "temple-floor-tile-1");
            }
            bool authoredSubwayShell = activeProfile == DungeonLayoutProfile.Subway
                                       && BuildAuthoredSubwayPassage(corridor, index);
            bool caveShell = activeProfile == DungeonLayoutProfile.CaveDungeon;
            if (caveShell) BuildCaveTunnelMesh(corridor, index);
            else if (!authoredSubwayShell) BuildCorridorSideWalls(corridor, index);
            Vector3 corridorSize = BoundsSize(corridor.WalkableBounds);
            Vector3 corridorCenter = BoundsCenter(corridor.WalkableBounds);
            if (caveShell)
                CreateInvisibleCollisionBox("Cave Tunnel Ceiling Support",
                    new Vector3(corridorCenter.x, ToUnity(corridor.WalkableBounds.Maximum.Y), corridorCenter.z),
                    new Vector3(corridorSize.x, floorThickness, corridorSize.z));
            else if (!authoredSubwayShell)
            {
                CreateCube("Corridor " + index + " Ceiling",
                    new Vector3(corridorCenter.x, ToUnity(corridor.WalkableBounds.Maximum.Y), corridorCenter.z),
                    new Vector3(corridorSize.x, floorThickness, corridorSize.z),
                    ThemeWallColor());
                if (activeTheme == DungeonVisualTheme.Temple)
                    BuildTempleHorizontalTiles(corridor.WalkableBounds, true, "temple-ceiling-tile-1");
                if (activeProfile == DungeonLayoutProfile.Subway)
                    BuildSubwayTunnelDetails(corridor, index);
                else if (activeProfile == DungeonLayoutProfile.GroupDungeon)
                    BuildKeepCorridorDetails(corridor, index);
                else if (activeProfile == DungeonLayoutProfile.Temple
                    || activeProfile == DungeonLayoutProfile.TempleReference)
                    BuildTempleCorridorDetails(corridor, index);
            }
            if (suppressVisualCollidersForCoreBake)
            {
                IReadOnlyList<DungeonCollisionBox> collision =
                    DungeonCollisionBaker.BakeCorridor(activeLayout, corridor);
                for (int i = 0; i < collision.Count; i++)
                    CreateInvisibleCollisionBox("Core Collision " + collision[i].Id,
                        BoundsCenter(collision[i].Bounds), BoundsSize(collision[i].Bounds));
            }
            suppressVisualCollidersForCoreBake = previousSuppressVisualColliders;
            buildParent = previousBuildParent;
        }

        private void BuildAuthoredCaveBend(DungeonCorridor corridor, int index)
        {
            bool wide = DungeonCaveTunnel.IsWideDescent(activeLayout, corridor);
            int ledgeSign = wide ? DungeonCaveTunnel.LedgeCrossSign(activeLayout, corridor) : 0;
            WorldBounds bounds = corridor.WalkableBounds;
            Vector3 center = BoundsCenter(bounds);
            Vector3 size = BoundsSize(bounds);
            float floor = ToUnity(bounds.Minimum.Y);
            float ceiling = ToUnity(bounds.Maximum.Y);
            IReadOnlyList<WorldBounds> floorSections =
                DungeonCorridorGeometry.FloorSections(activeLayout, corridor);
            foreach (WorldBounds section in floorSections)
            {
                float depth = Mathf.Max(floorThickness,
                    ToUnity(DungeonCorridorGeometry.FloorSupportDepth(corridor, section)));
                Vector3 sectionCenter = BoundsCenter(section);
                Vector3 sectionSize = BoundsSize(section);
                CreateInvisibleCollisionBox("Cave Bend Floor Support",
                    new Vector3(sectionCenter.x, ToUnity(section.Minimum.Y) - depth * 0.5f,
                        sectionCenter.z),
                    new Vector3(sectionSize.x, depth, sectionSize.z));
            }
            if (wide)
            {
                DungeonCaveTunnel.LedgeSegment[] ledgeSegments =
                    DungeonCaveTunnel.LedgeSegments(activeLayout, corridor);
                for (int ledgeIndex = 0; ledgeIndex < ledgeSegments.Length; ledgeIndex++)
                {
                    DungeonCaveTunnel.LedgeSegment ledge = ledgeSegments[ledgeIndex];
                    Vector3 start = new Vector3(ToUnity(ledge.Start.X), 0f,
                        ToUnity(ledge.Start.Z));
                    Vector3 end = new Vector3(ToUnity(ledge.End.X), 0f,
                        ToUnity(ledge.End.Z));
                    Vector3 tangent = end - start;
                    if (!addColliders || ledge.Width <= 0
                        || tangent.sqrMagnitude < 0.01f) continue;
                    var support = new GameObject("Raised Cave Ledge Solid Support " + ledgeIndex);
                    support.transform.SetParent(buildParent, false);
                    float depth = ToUnity(ledge.FloorY - ledge.BaseFloorY);
                    support.transform.localPosition = (start + end) * 0.5f
                        + Vector3.up * (ToUnity(ledge.FloorY) - depth * 0.5f);
                    support.transform.localRotation = Quaternion.Euler(0f,
                        Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg, 0f);
                    support.AddComponent<BoxCollider>().size = new Vector3(
                        ToUnity(ledge.Width), depth, tangent.magnitude + 0.5f);
                }
            }
            CreateInvisibleCollisionBox("Cave Bend Ceiling Support",
                new Vector3(center.x, ceiling, center.z),
                new Vector3(size.x, floorThickness, size.z));
            bool alongX = DungeonCorridorGeometry.IsAlongX(activeLayout, corridor);
            if (alongX)
            {
                CreateInvisibleCollisionBox("Cave Bend Outer Wall A",
                    new Vector3(center.x, center.y, ToUnity(bounds.Minimum.Z)),
                    new Vector3(size.x, size.y, wallThickness));
                CreateInvisibleCollisionBox("Cave Bend Outer Wall B",
                    new Vector3(center.x, center.y, ToUnity(bounds.Maximum.Z)),
                    new Vector3(size.x, size.y, wallThickness));
            }
            else
            {
                CreateInvisibleCollisionBox("Cave Bend Outer Wall A",
                    new Vector3(ToUnity(bounds.Minimum.X), center.y, center.z),
                    new Vector3(wallThickness, size.y, size.z));
                CreateInvisibleCollisionBox("Cave Bend Outer Wall B",
                    new Vector3(ToUnity(bounds.Maximum.X), center.y, center.z),
                    new Vector3(wallThickness, size.y, size.z));
            }

            DungeonCaveTunnel.Section[] sections =
                DungeonCaveTunnel.Sections(activeLayout, corridor);
            const int archPoints = 17;
            var floorVertices = new Vector3[sections.Length * 3];
            var floorTriangles = new int[(sections.Length - 1) * 12];
            var shellVertices = new Vector3[sections.Length * archPoints];
            var shellTriangles = new int[(sections.Length - 1) * (archPoints - 1) * 6];
            DungeonCaveSocketProfile startSocket = default, endSocket = default;
            int minimumSocketAxis = int.MaxValue, maximumSocketAxis = int.MinValue;
            foreach (DungeonDoorPortal portal in activeLayout.DoorPortals)
            {
                if (portal.ConnectionId != corridor.ConnectionId) continue;
                int portalAxis = alongX ? portal.Center.X : portal.Center.Z;
                if (portalAxis < minimumSocketAxis)
                { minimumSocketAxis = portalAxis; startSocket = DungeonCaveSockets.Resolve(portal); }
                if (portalAxis > maximumSocketAxis)
                { maximumSocketAxis = portalAxis; endSocket = DungeonCaveSockets.Resolve(portal); }
            }
            var crownHeights = new float[sections.Length];
            for (int ring = 0; ring < sections.Length; ring++)
                crownHeights[ring] = CaveBendCrown(sections, ring, wide,
                    startSocket, endSocket, ceiling);
            if (wide) BuildRaisedCaveLedge(corridor, index, sections, ledgeSign,
                alongX, crownHeights);
            for (int ring = 0; ring < sections.Length; ring++)
            {
                DungeonCaveTunnel.Section section = sections[ring];
                float ringFloor = ToUnity(section.Center.Y);
                Vector3 left = new Vector3(ToUnity(section.Left.X), ringFloor,
                    ToUnity(section.Left.Z));
                Vector3 right = new Vector3(ToUnity(section.Right.X), ringFloor,
                    ToUnity(section.Right.Z));
                Vector3 middle = new Vector3(ToUnity(section.Center.X), ringFloor,
                    ToUnity(section.Center.Z));
                float u = ring / (float)(sections.Length - 1);
                float crown = crownHeights[ring];
                floorVertices[ring * 3] = left + Vector3.up * 0.012f;
                floorVertices[ring * 3 + 1] = middle + Vector3.up
                    * (0.018f + 0.025f * Mathf.Sin(Mathf.PI * u));
                floorVertices[ring * 3 + 2] = right + Vector3.up * 0.012f;
                for (int point = 0; point < archPoints; point++)
                {
                    float normalizedCross = (float)DungeonCaveSockets.NormalizedCross(
                        point, archPoints - 1);
                    float heightFactor = (float)DungeonCaveSockets.NormalizedHeight(
                        point, archPoints - 1);
                    float span = (normalizedCross + 1f) * 0.5f;
                    Vector3 position = Vector3.Lerp(left, right, span);
                    float relief = point == 0 || point == archPoints - 1 ? 0f
                        : 0.22f * Mathf.Sin(Mathf.PI * u)
                        * CaveNoise(ring * 0.32f, point * 0.42f, index + 17);
                    position.y = ringFloor + 0.012f
                        + (crown - ringFloor - 0.012f) * heightFactor + relief;
                    shellVertices[ring * archPoints + point] = position;
                    if (ring == sections.Length - 1 || point == archPoints - 1
                        || (wide && (ledgeSign > 0 ? point >= 8 : point < 8))) continue;
                    int t = (ring * (archPoints - 1) + point) * 6;
                    int a = ring * archPoints + point;
                    shellTriangles[t] = a;
                    shellTriangles[t + 1] = a + archPoints;
                    shellTriangles[t + 2] = a + 1;
                    shellTriangles[t + 3] = a + 1;
                    shellTriangles[t + 4] = a + archPoints;
                    shellTriangles[t + 5] = a + archPoints + 1;
                }
                if (ring == sections.Length - 1) continue;
                for (int lane = 0; lane < 2; lane++)
                {
                    int t = ring * 12 + lane * 6;
                    int a = ring * 3 + lane;
                    floorTriangles[t] = a;
                    floorTriangles[t + 1] = a + 3;
                    floorTriangles[t + 2] = a + 1;
                    floorTriangles[t + 3] = a + 1;
                    floorTriangles[t + 4] = a + 3;
                    floorTriangles[t + 5] = a + 4;
                }
                Vector3 nextLeft = new Vector3(ToUnity(sections[ring + 1].Left.X),
                    floor, ToUnity(sections[ring + 1].Left.Z));
                Vector3 nextRight = new Vector3(ToUnity(sections[ring + 1].Right.X),
                    floor, ToUnity(sections[ring + 1].Right.Z));
                if (!wide || ledgeSign > 0)
                    CreateCaveSegmentCollision("Cave Bend Inner Wall A " + ring,
                        new Vector3(left.x, floor, left.z), nextLeft, size.y);
                if (!wide || ledgeSign < 0)
                    CreateCaveSegmentCollision("Cave Bend Inner Wall B " + ring,
                        new Vector3(right.x, floor, right.z), nextRight, size.y);
                if (wide)
                {
                    Vector3 outer = ledgeSign > 0 ? right : left;
                    Vector3 nextOuter = ledgeSign > 0 ? nextRight : nextLeft;
                    float upperRouteWidth = ToUnity(DungeonCaveTunnel.UpperRouteWidthAt(
                        activeLayout, corridor, ring / (double)(sections.Length - 1)));
                    float nextUpperRouteWidth = ToUnity(DungeonCaveTunnel.UpperRouteWidthAt(
                        activeLayout, corridor, (ring + 1) / (double)(sections.Length - 1)));
                    Vector3 offset = alongX
                        ? new Vector3(0f, 0f, ledgeSign * upperRouteWidth)
                        : new Vector3(ledgeSign * upperRouteWidth, 0f, 0f);
                    Vector3 nextOffset = alongX
                        ? new Vector3(0f, 0f, ledgeSign * nextUpperRouteWidth)
                        : new Vector3(ledgeSign * nextUpperRouteWidth, 0f, 0f);
                    CreateCaveSegmentCollision("Raised Cave Ledge Outer Wall " + ring,
                        new Vector3(outer.x + offset.x, floor, outer.z + offset.z),
                        new Vector3(nextOuter.x + nextOffset.x, floor,
                            nextOuter.z + nextOffset.z),
                        size.y);
                }
            }
            CreateCaveMesh("Authored Cave Bend Floor " + index,
                floorVertices, floorTriangles, ThemeFloorColor());
            CreateCaveMesh("Authored Cave Bend Rock Shell " + index,
                shellVertices, shellTriangles, ThemeWallColor());
            if (wide)
            {
                var wallVertices = new Vector3[sections.Length * 2];
                var wallTriangles = new int[(sections.Length - 1) * 12];
                for (int ring = 0; ring < sections.Length; ring++)
                {
                    DungeonCaveTunnel.Section section = sections[ring];
                    WorldVector3 edge = ledgeSign > 0 ? section.Right : section.Left;
                    float upperRouteWidth = ToUnity(DungeonCaveTunnel.UpperRouteWidthAt(
                        activeLayout, corridor, ring / (double)(sections.Length - 1)));
                    Vector3 offset = alongX
                        ? new Vector3(0f, 0f, ledgeSign * upperRouteWidth)
                        : new Vector3(ledgeSign * upperRouteWidth, 0f, 0f);
                    Vector3 position = new Vector3(ToUnity(edge.X),
                        ToUnity(section.Center.Y) - 0.8f,
                        ToUnity(edge.Z)) + offset;
                    wallVertices[ring * 2] = position;
                    wallVertices[ring * 2 + 1] = new Vector3(position.x,
                        Mathf.Min(ToUnity(bounds.Maximum.Y) - 0.25f,
                            ToUnity(section.Center.Y) + 7.8f),
                        position.z);
                    if (ring == sections.Length - 1) continue;
                    int a = ring * 2, t = ring * 12;
                    wallTriangles[t] = a;
                    wallTriangles[t + 1] = a + 2;
                    wallTriangles[t + 2] = a + 1;
                    wallTriangles[t + 3] = a + 1;
                    wallTriangles[t + 4] = a + 2;
                    wallTriangles[t + 5] = a + 3;
                    wallTriangles[t + 6] = a + 1;
                    wallTriangles[t + 7] = a + 2;
                    wallTriangles[t + 8] = a;
                    wallTriangles[t + 9] = a + 3;
                    wallTriangles[t + 10] = a + 2;
                    wallTriangles[t + 11] = a + 1;
                }
                CreateCaveMesh("Raised Cave Ledge Outer Rock Wall " + index,
                    wallVertices, wallTriangles, ThemeWallColor());
            }
            BuildAuthoredCavePassageLighting(sections, crownHeights, index, wide);
        }

        private static float CaveBendCrown(DungeonCaveTunnel.Section[] sections,
            int ring, bool wide, DungeonCaveSocketProfile startSocket,
            DungeonCaveSocketProfile endSocket, float ceiling)
        {
            float u = ring / (float)(sections.Length - 1);
            float floor = ToUnity(sections[ring].Center.Y);
            float endpointCrown = Mathf.Lerp(ToUnity(startSocket.CrownHeight),
                ToUnity(endSocket.CrownHeight), u);
            return Mathf.Min(ceiling - 0.25f, floor + endpointCrown
                + (wide ? 10.5f : 0.88f) * Mathf.Sin(Mathf.PI * u));
        }

        private void BuildAuthoredCavePassageLighting(
            DungeonCaveTunnel.Section[] sections, float[] crownHeights,
            int corridorIndex, bool wide)
        {
            int spacing = wide ? 9 : 8;
            for (int ring = spacing / 2; ring < sections.Length; ring += spacing)
            {
                DungeonCaveTunnel.Section section = sections[ring];
                Vector3 position = new Vector3(ToUnity(section.Center.X),
                    crownHeights[ring] - 0.48f,
                    ToUnity(section.Center.Z));
                Color glow = (ring / spacing) % 2 == 0
                    ? new Color(1f, 0.62f, 0.32f)
                    : new Color(0.42f, 0.68f, 0.72f);
                CreateCube("Cave Passage " + corridorIndex + " Light Stone " + ring,
                    position + Vector3.up * 0.38f, new Vector3(0.32f, 0.20f, 0.32f),
                    Color.Lerp(glow, Color.white, 0.2f), false);
                var lightObject = new GameObject(
                    "Cave Passage " + corridorIndex + " Pool Light " + ring);
                lightObject.transform.SetParent(buildParent, false);
                lightObject.transform.localPosition = position;
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = glow;
                light.range = wide ? 16f : 12f;
                light.intensity = wide ? 1.25f : 1.05f;
                light.shadows = LightShadows.None;
            }
        }

        private void BuildRaisedCaveLedge(DungeonCorridor corridor, int index,
            DungeonCaveTunnel.Section[] sections, int ledgeSign, bool alongX,
            float[] crownHeights)
        {
            // One continuous, closed rock mass hides the conservative stair-step
            // collision slabs. The inner face is the visible 6 m drop to the path.
            var vertices = new Vector3[sections.Length * 5];
            var triangles = new List<int>((sections.Length - 1) * 60 + 24);
            for (int ring = 0; ring < sections.Length; ring++)
            {
                DungeonCaveTunnel.Section section = sections[ring];
                WorldVector3 edge = ledgeSign > 0 ? section.Right : section.Left;
                Vector3 inner = new Vector3(ToUnity(edge.X), 0f, ToUnity(edge.Z));
                float upperRouteWidth = ToUnity(DungeonCaveTunnel.UpperRouteWidthAt(
                    activeLayout, corridor, ring / (double)(sections.Length - 1)));
                Vector3 outward = alongX
                    ? new Vector3(0f, 0f, ledgeSign * upperRouteWidth)
                    : new Vector3(ledgeSign * upperRouteWidth, 0f, 0f);
                float floorY = ToUnity(section.Center.Y);
                float lift = ToUnity(DungeonCaveTunnel.LedgeLift(activeLayout, corridor,
                    ring / (double)(sections.Length - 1)));
                float top = floorY + lift + 0.018f;
                float bottom = floorY - 0.82f;
                int v = ring * 5;
                vertices[v] = new Vector3(inner.x, top, inner.z);
                vertices[v + 1] = new Vector3(inner.x + outward.x * 0.5f,
                    top + 0.025f * Mathf.Sin(ring * 0.19f), inner.z + outward.z * 0.5f);
                vertices[v + 2] = new Vector3(inner.x + outward.x, top,
                    inner.z + outward.z);
                vertices[v + 3] = new Vector3(inner.x, bottom, inner.z);
                vertices[v + 4] = new Vector3(inner.x + outward.x, bottom,
                    inner.z + outward.z);
                if (ring == sections.Length - 1) continue;
                int next = v + 5;
                AddCaveDoubleSidedQuad(triangles, v, next, v + 1, next + 1);
                AddCaveDoubleSidedQuad(triangles, v + 1, next + 1, v + 2, next + 2);
                AddCaveDoubleSidedQuad(triangles, v + 3, next + 3, v, next);
                AddCaveDoubleSidedQuad(triangles, v + 2, next + 2, v + 4, next + 4);
                AddCaveDoubleSidedQuad(triangles, v + 4, next + 4, v + 3, next + 3);
            }
            CreateCaveMesh("Raised Cave Ledge Solid Rock " + index,
                vertices, triangles.ToArray(), ThemeWallColor());

            // Bridge the missing half of the vault above the ledge. Without this
            // surface the ledge sees straight through to the empty world outside.
            var roof = new Vector3[sections.Length * 3];
            var roofTriangles = new List<int>((sections.Length - 1) * 24);
            float roofLimit = ToUnity(corridor.WalkableBounds.Maximum.Y) - 0.25f;
            for (int ring = 0; ring < sections.Length; ring++)
            {
                DungeonCaveTunnel.Section section = sections[ring];
                WorldVector3 edge = ledgeSign > 0 ? section.Right : section.Left;
                Vector3 center = new Vector3(ToUnity(section.Center.X), 0f,
                    ToUnity(section.Center.Z));
                Vector3 outer = new Vector3(ToUnity(edge.X), 0f, ToUnity(edge.Z));
                float upperRouteWidth = ToUnity(DungeonCaveTunnel.UpperRouteWidthAt(
                    activeLayout, corridor, ring / (double)(sections.Length - 1)));
                outer += alongX ? new Vector3(0f, 0f, ledgeSign * upperRouteWidth)
                    : new Vector3(ledgeSign * upperRouteWidth, 0f, 0f);
                float floorY = ToUnity(section.Center.Y);
                // Meet the remaining half of the authored tunnel at its exact
                // generated crown. A separately guessed height left a visible
                // crescent-shaped hole through the bend.
                float u = ring / (float)(sections.Length - 1);
                center.y = crownHeights[ring]
                    + 0.22f * Mathf.Sin(Mathf.PI * u)
                    * CaveNoise(ring * 0.32f, 8f * 0.42f, index + 17);
                outer.y = Mathf.Min(roofLimit, floorY + 7.8f);
                Vector3 shoulder = Vector3.Lerp(center, outer, 0.55f);
                shoulder.y = Mathf.Min(roofLimit,
                    Mathf.Max(center.y, outer.y) + 0.35f);
                int v = ring * 3;
                roof[v] = center;
                roof[v + 1] = shoulder;
                roof[v + 2] = outer;
                if (ring == sections.Length - 1) continue;
                AddCaveDoubleSidedQuad(roofTriangles, v, v + 3, v + 1, v + 4);
                AddCaveDoubleSidedQuad(roofTriangles, v + 1, v + 4, v + 2, v + 5);
            }
            CreateCaveMesh("Raised Cave Ledge Closing Vault " + index,
                roof, roofTriangles.ToArray(), ThemeWallColor());

        }

        private static void AddCaveDoubleSidedQuad(List<int> triangles,
            int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(c); triangles.Add(b); triangles.Add(d);
            triangles.Add(c); triangles.Add(b); triangles.Add(a);
            triangles.Add(d); triangles.Add(b); triangles.Add(c);
        }

        private void CreateCaveSegmentCollision(string name, Vector3 start,
            Vector3 end, float height)
        {
            if (!addColliders) return;
            Vector3 tangent = end - start;
            float length = tangent.magnitude;
            if (length < 0.05f) return;
            var collision = new GameObject(name);
            collision.transform.SetParent(buildParent, false);
            collision.transform.localPosition = (start + end) * 0.5f
                + Vector3.up * height * 0.5f;
            collision.transform.localRotation = Quaternion.Euler(0f,
                Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg, 0f);
            collision.AddComponent<BoxCollider>().size =
                new Vector3(0.30f, height, length + 0.12f);
        }

        private void BuildCorridorSideWalls(DungeonCorridor corridor, int index)
        {
            WorldBounds bounds = corridor.WalkableBounds;
            float widthX = ToUnity(bounds.Maximum.X - bounds.Minimum.X);
            float widthZ = ToUnity(bounds.Maximum.Z - bounds.Minimum.Z);
            float minX = ToUnity(bounds.Minimum.X);
            float maxX = ToUnity(bounds.Maximum.X);
            float minZ = ToUnity(bounds.Minimum.Z);
            float maxZ = ToUnity(bounds.Maximum.Z);
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float corridorHeight = ToUnity(bounds.Maximum.Y - bounds.Minimum.Y);
            float centerY = ToUnity(bounds.Minimum.Y) + corridorHeight * 0.5f;
            Color color = ThemeWallColor();

            if (DungeonCorridorGeometry.IsAlongX(activeLayout, corridor))
            {
                CreateCube("Corridor " + index + " Wall NegativeZ",
                    new Vector3(centerX, centerY, minZ),
                    new Vector3(widthX, corridorHeight, wallThickness), color, assetRole: DungeonAssetRole.Wall, wallFacing: DungeonPortalFacing.NegativeZ);
                CreateCube("Corridor " + index + " Wall PositiveZ",
                    new Vector3(centerX, centerY, maxZ),
                    new Vector3(widthX, corridorHeight, wallThickness), color, assetRole: DungeonAssetRole.Wall, wallFacing: DungeonPortalFacing.PositiveZ);
                if (activeTheme == DungeonVisualTheme.Temple)
                {
                    float floorY = ToUnity(bounds.Minimum.Y);
                    BuildTempleWallTiles(index + 1000, floorY, DungeonPortalFacing.NegativeZ,
                        false, minZ, minX, maxX, corridorHeight);
                    BuildTempleWallTiles(index + 1000, floorY, DungeonPortalFacing.PositiveZ,
                        false, maxZ, minX, maxX, corridorHeight);
                }
            }
            else
            {
                CreateCube("Corridor " + index + " Wall NegativeX",
                    new Vector3(minX, centerY, centerZ),
                    new Vector3(wallThickness, corridorHeight, widthZ), color, assetRole: DungeonAssetRole.Wall, wallFacing: DungeonPortalFacing.NegativeX);
                CreateCube("Corridor " + index + " Wall PositiveX",
                    new Vector3(maxX, centerY, centerZ),
                    new Vector3(wallThickness, corridorHeight, widthZ), color, assetRole: DungeonAssetRole.Wall, wallFacing: DungeonPortalFacing.PositiveX);
                if (activeTheme == DungeonVisualTheme.Temple)
                {
                    float floorY = ToUnity(bounds.Minimum.Y);
                    BuildTempleWallTiles(index + 1000, floorY, DungeonPortalFacing.NegativeX,
                        true, minX, minZ, maxZ, corridorHeight);
                    BuildTempleWallTiles(index + 1000, floorY, DungeonPortalFacing.PositiveX,
                        true, maxX, minZ, maxZ, corridorHeight);
                }
            }
        }

        private void BuildWall(DungeonRoom room, DungeonRoomWallSection section,
            IReadOnlyList<DungeonDoorPortal> portals)
        {
            DungeonPortalFacing facing = section.Facing;
            bool xWall = facing == DungeonPortalFacing.NegativeX || facing == DungeonPortalFacing.PositiveX;
            float fixedAxis = ToUnity(section.FixedAxis);
            float start = ToUnity(section.Start);
            float end = ToUnity(section.End);
            var openings = new List<Vector2>();
            for (int i = 0; section.AcceptsPortals && i < portals.Count; i++)
            {
                DungeonDoorPortal portal = portals[i];
                if (portal.RoomId != room.Id || portal.Facing != facing) continue;
                DungeonCaveSocketProfile caveSocket = activeProfile == DungeonLayoutProfile.CaveDungeon
                    ? DungeonCaveSockets.Resolve(portal) : default;
                float openingStart = ToUnity(xWall
                    ? portal.ClosedBlockingBounds.Minimum.Z : portal.ClosedBlockingBounds.Minimum.X);
                float openingEnd = ToUnity(xWall
                    ? portal.ClosedBlockingBounds.Maximum.Z : portal.ClosedBlockingBounds.Maximum.X);
                if (activeProfile == DungeonLayoutProfile.CaveDungeon)
                {
                    openingStart += ToUnity(caveSocket.SideInset);
                    openingEnd -= ToUnity(caveSocket.SideInset);
                    if (!buildingAuthoredCaveChamber)
                        BuildCaveRoomSocketCrown(room, portal, caveSocket,
                            xWall, fixedAxis, openingStart, openingEnd);
                }
                openings.Add(new Vector2(Mathf.Max(start, openingStart), Mathf.Min(end, openingEnd)));
            }
            openings.Sort((left, right) => left.x.CompareTo(right.x));
            float cursor = start;
            for (int i = 0; i < openings.Count; i++)
            {
                if (openings[i].y <= openings[i].x) continue;
                CreateWallSegment(room, facing, xWall, fixedAxis, cursor, openings[i].x);
                cursor = Mathf.Max(cursor, openings[i].y);
            }
            CreateWallSegment(room, facing, xWall, fixedAxis, cursor, end);
        }

        private void BuildCaveRoomSocketCrown(DungeonRoom room,
            DungeonDoorPortal portal, DungeonCaveSocketProfile socket,
            bool xWall, float fixedAxis, float openingStart, float openingEnd)
        {
            int points = socket.ArchSegments + 1;
            float floor = ToUnity(room.Bounds.Minimum.Y);
            float top = ToUnity(room.Bounds.Maximum.Y) - 0.08f;
            float crown = Mathf.Min(top - 0.20f, floor + ToUnity(socket.CrownHeight));
            float inward = portal.Facing == DungeonPortalFacing.NegativeX
                || portal.Facing == DungeonPortalFacing.NegativeZ ? 1f : -1f;
            var vertices = new Vector3[points * 2];
            var triangles = new List<int>((points - 1) * 12);
            for (int point = 0; point < points; point++)
            {
                float t = point / (float)(points - 1);
                float normalizedCross = (float)DungeonCaveSockets.NormalizedCross(
                    point, points - 1);
                float normalizedHeight = (float)DungeonCaveSockets.NormalizedHeight(
                    point, points - 1);
                float cross = Mathf.Lerp(openingStart, openingEnd,
                    (normalizedCross + 1f) * 0.5f);
                float archY = floor + normalizedHeight * (crown - floor);
                float lowerAxis = fixedAxis + inward * 0.06f;
                float upperAxis = fixedAxis + inward * (0.12f
                    + 0.16f * CaveNoise(point * 0.41f, room.Index, (int)socket.Kind));
                vertices[point] = xWall
                    ? new Vector3(lowerAxis, archY, cross)
                    : new Vector3(cross, archY, lowerAxis);
                vertices[points + point] = xWall
                    ? new Vector3(upperAxis, top, cross)
                    : new Vector3(cross, top, upperAxis);
                if (point == points - 1) continue;
                AddCaveDoubleSidedQuad(triangles, point, point + 1,
                    points + point, points + point + 1);
            }
            CreateCaveMesh("Room " + room.Index + " Cave Opening " + socket.Kind,
                vertices, triangles.ToArray(), RoomWallColor(room));
        }

        private void CreateWallSegment(DungeonRoom room, DungeonPortalFacing facing, bool xWall,
            float fixedAxis, float start, float end)
        {
            float length = end - start;
            if (length <= 0.01f) return;
            int minimumY = DungeonRoomGeometry.IsSubwayTrackRoom(room)
                ? DungeonRoomGeometry.SubwayTrackFloorY(room)
                : room.Bounds.Minimum.Y;
            float height = ToUnity(room.Bounds.Maximum.Y - minimumY);
            float centerY = ToUnity(minimumY) + height * 0.5f;
            Vector3 position = xWall
                ? new Vector3(fixedAxis, centerY, (start + end) * 0.5f)
                : new Vector3((start + end) * 0.5f, centerY, fixedAxis);
            Vector3 scale = xWall
                ? new Vector3(wallThickness, height, length)
                : new Vector3(length, height, wallThickness);
            if (activeProfile == DungeonLayoutProfile.CaveDungeon)
            {
                CreateInvisibleCollisionBox("Cave Wall Support", position, scale);
                if (!buildingAuthoredCaveChamber)
                    BuildCaveWallMesh(room, facing, xWall, fixedAxis, start, end);
            }
            else
            {
                if (activeTheme == DungeonVisualTheme.Temple)
                    BuildTempleWallTiles(room, facing, xWall, fixedAxis, start, end, height);
                CreateCube("Room " + room.Index + " Wall " + facing, position, scale,
                    RoomWallColor(room), assetRole: DungeonAssetRole.Wall, wallFacing: facing);
            }
        }

        private void BuildTempleWallTiles(DungeonRoom room, DungeonPortalFacing facing,
            bool xWall, float fixedAxis, float start, float end, float height)
        {
            BuildTempleWallTiles(room.Index, ToUnity(room.Bounds.Minimum.Y),
                facing, xWall, fixedAxis, start, end, height, true);
        }

        private void BuildTempleWallTiles(int variationIndex, float floorY,
            DungeonPortalFacing facing, bool xWall, float fixedAxis,
            float start, float end, float height, bool accentRoom = false)
        {
            if (templeDungeonKit == null) return;
            const float panelSize = 1.5f;
            int columns = Mathf.Max(1, Mathf.CeilToInt((end - start) / panelSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt(height / panelSize));
            float cellWidth = (end - start) / columns;
            float cellHeight = height / rows;
            Vector3 inward = facing == DungeonPortalFacing.NegativeX ? Vector3.right
                : facing == DungeonPortalFacing.PositiveX ? Vector3.left
                : facing == DungeonPortalFacing.NegativeZ ? Vector3.forward : Vector3.back;
            float inwardOffset = wallThickness * .5f + .12f;
            Quaternion rotation = Quaternion.LookRotation(inward, Vector3.up);
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    float along = start + (column + .5f) * cellWidth;
                    Vector3 center = xWall
                        ? new Vector3(fixedAxis, floorY + (row + .5f) * cellHeight, along)
                        : new Vector3(along, floorY + (row + .5f) * cellHeight, fixedAxis);
                    center += inward * inwardOffset;
                    string id = "temple-flat-plain-wall-tile-2";
                    if (accentRoom && rows >= 3 && row == rows / 2)
                    {
                        string[] accents =
                        {
                            "temple-flat-wall-tile-1", "temple-flat-maze-wall-tile-4",
                            "temple-flat-rough-wall-tile-3", "temple-pointed-wall-tile-1"
                        };
                        id = accents[Mathf.Abs(variationIndex) % accents.Length];
                        if (templeDungeonKit.Resolve(id) == null)
                            id = "temple-flat-plain-wall-tile-2";
                    }
                    TryPlaceTempleAssetFitted(id, center, rotation,
                        new Vector3(cellWidth + .035f, cellHeight + .035f, .18f));
                }
        }

        private static string TempleSurfaceId(bool ceiling, int roomIndex)
        {
            int count = ceiling ? 2 : 6;
            int index = (int)((uint)roomIndex * 2654435761u % (uint)count) + 1;
            return "temple-" + (ceiling ? "ceiling" : "floor") + "-tile-" + index;
        }

        private void BuildTempleHorizontalTiles(WorldBounds section, bool ceiling, string id)
        {
            if (templeDungeonKit?.Resolve(id) == null) return;
            float minX = ToUnity(section.Minimum.X);
            float maxX = ToUnity(section.Maximum.X);
            float minZ = ToUnity(section.Minimum.Z);
            float maxZ = ToUnity(section.Maximum.Z);
            const float panelSize = 1.5f;
            int columns = Mathf.Max(1, Mathf.CeilToInt((maxX - minX) / panelSize));
            int rows = Mathf.Max(1, Mathf.CeilToInt((maxZ - minZ) / panelSize));
            float cellWidth = (maxX - minX) / columns;
            float cellDepth = (maxZ - minZ) / rows;
            float surfaceY = ceiling
                ? ToUnity(section.Maximum.Y) - floorThickness * .5f - .02f
                : ToUnity(section.Minimum.Y) + .02f;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.forward,
                ceiling ? Vector3.down : Vector3.up);
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                    TryPlaceTempleAssetFitted(id,
                        new Vector3(minX + (column + .5f) * cellWidth, surfaceY,
                            minZ + (row + .5f) * cellDepth), rotation,
                        new Vector3(cellWidth + .035f, cellDepth + .035f, .04f));
        }

        private bool TryPlaceTempleAssetFitted(string id, Vector3 center,
            Quaternion rotation, Vector3 targetSize)
        {
            GameObject prefab = templeDungeonKit?.Resolve(id);
            if (prefab == null) return false;
            if (!TryGetTempleVisualBounds(prefab, out Bounds bounds))
                return TryPlaceTempleAsset(id, center, rotation, Vector3.one);
            Vector3 size = bounds.size;
            Vector3 scale = new Vector3(
                targetSize.x / Mathf.Max(size.x, .001f),
                targetSize.y / Mathf.Max(size.y, .001f),
                targetSize.z / Mathf.Max(size.z, .001f));
            Vector3 position = center - rotation * Vector3.Scale(bounds.center, scale);
            return TryPlaceTempleAsset(id, position, rotation, scale);
        }

        private static bool TryGetTempleVisualBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Bounds meshBounds = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = toRoot.MultiplyPoint3x4(meshBounds.center +
                        Vector3.Scale(meshBounds.extents, new Vector3(x, y, z)));
                    if (!found) { bounds = new Bounds(corner, Vector3.zero); found = true; }
                    else bounds.Encapsulate(corner);
                }
            }
            return found;
        }

        private bool TryPlaceTempleAsset(string id, Vector3 position,
            Quaternion rotation, Vector3 scale)
        {
            GameObject prefab = templeDungeonKit?.Resolve(id);
            if (prefab == null || buildParent == null) return false;
            GameObject instance = Instantiate(prefab, buildParent, false);
            instance.name = id;
            instance.transform.localPosition = position;
            instance.transform.localRotation = rotation;
            instance.transform.localScale = scale;
            templePiecesPlaced++;
            return true;
        }

        private void BuildRoomModuleDetails(DungeonRoom room,
            IReadOnlyList<DungeonDoorPortal> portals)
        {
            float minX = ToUnity(room.Bounds.Minimum.X);
            float maxX = ToUnity(room.Bounds.Maximum.X);
            float minZ = ToUnity(room.Bounds.Minimum.Z);
            float maxZ = ToUnity(room.Bounds.Maximum.Z);
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float width = maxX - minX;
            float depth = maxZ - minZ;
            float height = ToUnity(room.Bounds.Maximum.Y - room.Bounds.Minimum.Y);
            Color accent = RoomAccentColor(room);

            // Low perimeter bands use the same cuts as walls, never crossing a doorway.
            IReadOnlyList<DungeonRoomWallSection> walls = DungeonRoomGeometry.WallSections(room);
            if (activeProfile != DungeonLayoutProfile.CaveDungeon)
                for (int i = 0; i < walls.Count; i++) BuildWallTrim(room, walls[i], portals, accent);

            if (activeProfile == DungeonLayoutProfile.Subway)
            {
                BuildCorePresentation(room.Id);
                BuildSubwayCeilingFixtureGrid(room);
                BuildSubwayRoomDetails(room, portals, minX, maxX, minZ, maxZ,
                    ToUnity(room.Bounds.Minimum.Y), height);
                return;
            }
            if (activeProfile == DungeonLayoutProfile.GroupDungeon)
            {
                BuildKeepRoomDetails(room, minX, maxX, minZ, maxZ, height, accent);
                return;
            }
            if (activeProfile == DungeonLayoutProfile.Temple
                || activeProfile == DungeonLayoutProfile.TempleReference)
            {
                BuildTempleRoomDetails(room, minX, maxX, minZ, maxZ, height, accent);
                return;
            }
            if (activeProfile == DungeonLayoutProfile.CaveDungeon)
            {
                BuildCaveRoomLighting(room);
                return;
            }

            if (room.Shape != DungeonRoomShape.LShaped) switch (room.Index % 3)
            {
                case 0:
                    float inset = 0.48f;
                    Vector3 pillarScale = new Vector3(0.32f, height - 0.3f, 0.32f);
                    CreateCube("Room " + room.Index + " Pillar NW",
                        new Vector3(minX + inset, height * 0.5f, minZ + inset), pillarScale, accent, false);
                    CreateCube("Room " + room.Index + " Pillar SE",
                        new Vector3(maxX - inset, height * 0.5f, maxZ - inset), pillarScale, accent, false);
                    break;
                case 1:
                    CreateCube("Room " + room.Index + " Ceiling Beam",
                        new Vector3(centerX, height - 0.18f, centerZ),
                        new Vector3(width - 0.6f, 0.22f, 0.28f), accent, false);
                    break;
                default:
                    CreateCube("Room " + room.Index + " Ceiling Beam A",
                        new Vector3(centerX, height - 0.18f, centerZ),
                        new Vector3(0.26f, 0.22f, depth - 0.6f), accent, false);
                    CreateCube("Room " + room.Index + " Ceiling Beam B",
                        new Vector3(centerX, height - 0.16f, centerZ),
                        new Vector3(width - 0.6f, 0.18f, 0.26f), accent, false);
                    break;
            }

            CreateCube("Room " + room.Index + " Light Fixture",
                new Vector3(centerX, height - 0.32f, centerZ),
                new Vector3(0.75f, 0.12f, 0.75f), Color.Lerp(accent, Color.white, 0.55f), false);
            if (room.Index % 4 == 0 || room.Role == DungeonRoomRole.Objective
                || room.Role == DungeonRoomRole.Boss || room.Role == DungeonRoomRole.Treasure)
            {
                var lightObject = new GameObject("Room " + room.Index + " Practical Light");
                lightObject.transform.SetParent(buildParent, false);
                lightObject.transform.localPosition = new Vector3(centerX, height - 0.5f, centerZ);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = Color.Lerp(accent, new Color(1f, 0.86f, 0.68f), 0.55f);
                light.range = Mathf.Min(9f, Mathf.Max(width, depth) * 0.75f);
                light.intensity = room.Role == DungeonRoomRole.Boss ? 2.2f : 1.35f;
                light.shadows = LightShadows.None;
            }
        }

        private void BuildAuthoredCaveChamber(DungeonLayout layout, DungeonRoom room)
        {
            WorldVector3[] outlineData = DungeonCaveChamber.Outline(room,
                layout.DoorPortals);
            var outline = new Vector3[outlineData.Length];
            for (int i = 0; i < outline.Length; i++)
                outline[i] = new Vector3(ToUnity(outlineData[i].X),
                    ToUnity(outlineData[i].Y), ToUnity(outlineData[i].Z));
            float floor = ToUnity(room.Bounds.Minimum.Y);
            float upper = ToUnity(room.Bounds.Maximum.Y);
            Vector3 center = new Vector3(ToUnity(room.Center.X), floor, ToUnity(room.Center.Z));

            // A flat authoritative slab backs the organic visible floor. No raised
            // mesh collider is introduced at this stage; the portal remains flush.
            Vector3 supportSize = BoundsSize(room.Bounds);
            supportSize.y = Mathf.Max(floorThickness, MinimumWalkableFloorThickness);
            CreateInvisibleCollisionBox("Authored Cave Floor Support",
                new Vector3(center.x, floor - supportSize.y * 0.5f, center.z), supportSize);
            CreateInvisibleCollisionBox("Authored Cave Ceiling Support",
                new Vector3(center.x, upper, center.z),
                new Vector3(supportSize.x, floorThickness, supportSize.z));

            const int vaultRings = 3;
            var roofVertices = new Vector3[outline.Length * vaultRings + 1];
            var roofTriangles = new int[outline.Length * ((vaultRings - 1) * 6 + 3)];
            float rimHeight = Mathf.Min(upper - 0.36f, floor + 3.05f);
            for (int i = 0; i < outline.Length; i++)
            {
                int next = (i + 1) % outline.Length;
                for (int ring = 0; ring < vaultRings; ring++)
                {
                    float radius = ring == 0 ? 1f : ring == 1 ? 0.68f : 0.34f;
                    Vector3 position = Vector3.Lerp(center, outline[i], radius);
                    float rise = (upper - 0.16f - rimHeight) * (1f - radius * radius);
                    position.y = rimHeight + rise
                        + (0.16f + 0.70f * (1f - radius))
                        * CaveNoise(i * 0.08f, ring * 0.5f, room.Index)
                        + 0.07f * CaveNoise(i * 0.45f, ring, room.Index + 3);
                    roofVertices[ring * outline.Length + i] = position;
                    if (ring == vaultRings - 1) continue;
                    int t = (ring * outline.Length + i) * 6;
                    int outer = ring * outline.Length + i;
                    int outerNext = ring * outline.Length + next;
                    int inner = (ring + 1) * outline.Length + i;
                    int innerNext = (ring + 1) * outline.Length + next;
                    roofTriangles[t] = outer;
                    roofTriangles[t + 1] = inner;
                    roofTriangles[t + 2] = outerNext;
                    roofTriangles[t + 3] = outerNext;
                    roofTriangles[t + 4] = inner;
                    roofTriangles[t + 5] = innerNext;
                }
                int fan = (vaultRings - 1) * outline.Length * 6 + i * 3;
                roofTriangles[fan] = (vaultRings - 1) * outline.Length + i;
                roofTriangles[fan + 1] = roofVertices.Length - 1;
                roofTriangles[fan + 2] = (vaultRings - 1) * outline.Length + next;
            }
            roofVertices[roofVertices.Length - 1] =
                new Vector3(center.x, upper - 0.16f, center.z);
            BuildAuthoredCaveChamberFloor(room, outline, center);
            CreateCaveMesh("Authored Cave Chamber Vault", roofVertices,
                roofTriangles, RoomWallColor(room));
            for (int portalIndex = 0; portalIndex < layout.DoorPortals.Count; portalIndex++)
            {
                DungeonDoorPortal portal = layout.DoorPortals[portalIndex];
                if (portal.RoomId != room.Id) continue;
                bool xFacing = portal.Facing == DungeonPortalFacing.NegativeX
                    || portal.Facing == DungeonPortalFacing.PositiveX;
                float axis = ToUnity(xFacing ? portal.Center.X : portal.Center.Z);
                float cross = ToUnity(xFacing ? portal.Center.Z : portal.Center.X);
                float inset = portal.Facing == DungeonPortalFacing.NegativeX
                    || portal.Facing == DungeonPortalFacing.NegativeZ ? 3.6f : -3.6f;
                float half = 2.30f;
                float y = floor + 0.020f;
                Vector3[] landing = xFacing
                    ? new[] { new Vector3(axis, y, cross - half),
                        new Vector3(axis + inset, y, cross - half),
                        new Vector3(axis + inset, y, cross + half),
                        new Vector3(axis, y, cross + half) }
                    : new[] { new Vector3(cross - half, y, axis),
                        new Vector3(cross + half, y, axis),
                        new Vector3(cross + half, y, axis + inset),
                        new Vector3(cross - half, y, axis + inset) };
                CreateCaveMesh("Authored Cave Door Landing " + portalIndex,
                    landing, new[] { 0, 3, 1, 1, 3, 2 }, RoomFloorColor(room));
                BuildAuthoredCaveOpeningShoulders(room, portal, portalIndex,
                    floor, rimHeight);
            }

            // Preserve the authoritative rectangular collision hull, but leave its
            // flat presentation hidden behind the organic chamber surfaces.
            buildingAuthoredCaveChamber = true;
            try
            {
                IReadOnlyList<DungeonRoomWallSection> outerWalls =
                    DungeonRoomGeometry.WallSections(room);
                for (int i = 0; i < outerWalls.Count; i++)
                    BuildWall(room, outerWalls[i], layout.DoorPortals);
            }
            finally { buildingAuthoredCaveChamber = false; }

            IReadOnlyList<DungeonCaveChamber.WallSegment> segments =
                DungeonCaveChamber.WallSegments(room, layout.DoorPortals);
            for (int i = 0; i < segments.Count; i++)
            {
                Vector3 a = new Vector3(ToUnity(segments[i].Start.X), floor,
                    ToUnity(segments[i].Start.Z));
                Vector3 b = new Vector3(ToUnity(segments[i].End.X), floor,
                    ToUnity(segments[i].End.Z));
                Vector3 tangent = b - a;
                float length = tangent.magnitude;
                if (length < 0.1f) continue;
                Vector3 midpoint = (a + b) * 0.5f;
                const int strata = 6;
                var wallVertices = new Vector3[strata * 2];
                var wallTriangles = new int[(strata - 1) * 6];
                for (int level = 0; level < strata; level++)
                {
                    float v = level / (float)(strata - 1);
                    for (int endpoint = 0; endpoint < 2; endpoint++)
                    {
                        Vector3 point = endpoint == 0 ? a : b;
                        Vector3 radial = new Vector3(point.x - center.x, 0f,
                            point.z - center.z).normalized;
                        float rough = CaveNoise(point.x * 0.6f,
                            point.z * 0.6f + v, room.Index + 11);
                        float bulge = 0.30f * v * v * Mathf.Sin(Mathf.PI * v) * rough;
                        point += radial * bulge;
                        point.y = Mathf.Lerp(floor, rimHeight + 0.16f, v)
                            + 0.10f * Mathf.Sin(Mathf.PI * v) * rough;
                        wallVertices[level * 2 + endpoint] = point;
                    }
                    if (level == strata - 1) continue;
                    int t = level * 6, n = level * 2;
                    wallTriangles[t] = n;
                    wallTriangles[t + 1] = n + 2;
                    wallTriangles[t + 2] = n + 1;
                    wallTriangles[t + 3] = n + 1;
                    wallTriangles[t + 4] = n + 2;
                    wallTriangles[t + 5] = n + 3;
                }
                CreateCaveMesh("Authored Cave Chamber Rock Wall " + i,
                    wallVertices, wallTriangles, RoomWallColor(room));
                if (addColliders)
                {
                    var collision = new GameObject("Authored Cave Wall Collision " + i);
                    collision.transform.SetParent(buildParent, false);
                    collision.transform.localPosition = new Vector3(midpoint.x,
                        floor + (upper - floor) * 0.5f, midpoint.z);
                    collision.transform.localRotation = Quaternion.Euler(0f,
                        Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg, 0f);
                    collision.AddComponent<BoxCollider>().size =
                        new Vector3(0.30f, upper - floor, length + 0.12f);
                }
            }
            BuildCaveRoomLighting(room);
            for (int lightIndex = 0; lightIndex < 2; lightIndex++)
            {
                var lightObject = new GameObject("Authored Cave Chamber Bounce Light " + lightIndex);
                lightObject.transform.SetParent(buildParent, false);
                lightObject.transform.localPosition = center
                    + new Vector3(lightIndex == 0 ? -4.2f : 4.2f,
                        3.4f, lightIndex == 0 ? 2.8f : -2.8f);
                Light bounce = lightObject.AddComponent<Light>();
                bounce.type = LightType.Point;
                bounce.color = new Color(0.82f, 0.63f, 0.44f);
                bounce.range = 12f;
                bounce.intensity = 0.90f;
                bounce.shadows = LightShadows.None;
            }
        }

        private void BuildAuthoredCaveOpeningShoulders(DungeonRoom room,
            DungeonDoorPortal portal, int portalIndex, float floor, float rimHeight)
        {
            DungeonCaveSocketProfile socket = DungeonCaveSockets.Resolve(portal);
            bool xFacing = portal.Facing == DungeonPortalFacing.NegativeX
                || portal.Facing == DungeonPortalFacing.PositiveX;
            float axis = ToUnity(xFacing ? portal.Center.X : portal.Center.Z);
            float crossCenter = ToUnity(xFacing ? portal.Center.Z : portal.Center.X);
            float halfWidth = ToUnity(socket.VisualWidth) * 0.5f;
            float crown = Mathf.Min(rimHeight - 0.035f,
                floor + ToUnity(socket.CrownHeight));
            int points = socket.ArchSegments + 1;
            var vertices = new Vector3[points * 2];
            var triangles = new List<int>((points - 1) * 12);
            for (int point = 0; point < points; point++)
            {
                float normalizedCross = (float)DungeonCaveSockets.NormalizedCross(
                    point, socket.ArchSegments);
                float normalizedHeight = (float)DungeonCaveSockets.NormalizedHeight(
                    point, socket.ArchSegments);
                float cross = crossCenter + normalizedCross * halfWidth;
                float archY = floor + normalizedHeight * (crown - floor);
                float rimY = rimHeight + 0.035f
                    * Mathf.Sin(Mathf.PI * point / socket.ArchSegments)
                    * CaveNoise(point * 0.31f, room.Index + 17, portalIndex);
                vertices[point] = xFacing
                    ? new Vector3(axis, archY, cross)
                    : new Vector3(cross, archY, axis);
                vertices[points + point] = xFacing
                    ? new Vector3(axis, rimY, cross)
                    : new Vector3(cross, rimY, axis);
                if (point == points - 1) continue;
                AddCaveDoubleSidedQuad(triangles, point, point + 1,
                    points + point, points + point + 1);
            }
            CreateCaveMesh("Authored Cave Opening Shoulders " + socket.Kind + " "
                + portalIndex, vertices, triangles.ToArray(), RoomWallColor(room));
        }

        private void BuildCaveSocketJunction(DungeonRoom room,
            DungeonDoorPortal portal, int portalIndex)
        {
            bool xFacing = portal.Facing == DungeonPortalFacing.NegativeX
                || portal.Facing == DungeonPortalFacing.PositiveX;
            float socketAxis = ToUnity(xFacing ? portal.Center.X : portal.Center.Z);
            float crossCenter = ToUnity(xFacing ? portal.Center.Z : portal.Center.X);
            float inwardSign = portal.Facing == DungeonPortalFacing.NegativeX
                || portal.Facing == DungeonPortalFacing.NegativeZ ? 1f : -1f;
            DungeonCaveSocketProfile profile = DungeonCaveSockets.Resolve(portal);
            // First isolated module slice: do not manufacture transition geometry
            // for every entrance portal until this standard S-bend junction is
            // visually approved.
            float halfWidth = ToUnity(xFacing
                ? portal.ClosedBlockingBounds.Maximum.Z - portal.ClosedBlockingBounds.Minimum.Z
                : portal.ClosedBlockingBounds.Maximum.X - portal.ClosedBlockingBounds.Minimum.X) * 0.5f
                - ToUnity(profile.SideInset);
            float floor = ToUnity(room.Bounds.Minimum.Y) + 0.018f;
            float crownHeight = ToUnity(profile.CrownHeight);
            int archPoints = profile.ArchSegments + 1;
            WorldVector3[] outline = DungeonCaveChamber.Outline(room);
            float centerWallAxis = ResolveCaveWallAxis(outline, xFacing,
                socketAxis, crossCenter, inwardSign, 6f);
            float leftWallAxis = Mathf.Clamp(ResolveCaveWallAxis(outline, xFacing,
                socketAxis, crossCenter - halfWidth, inwardSign,
                6f),
                centerWallAxis - 0.75f, centerWallAxis + 0.75f);
            float rightWallAxis = Mathf.Clamp(ResolveCaveWallAxis(outline, xFacing,
                socketAxis, crossCenter + halfWidth, inwardSign,
                6f),
                centerWallAxis - 0.75f, centerWallAxis + 0.75f);
            var vertices = new Vector3[archPoints * 2];
            var triangles = new List<int>((archPoints - 1) * 12);
            for (int point = 0; point < archPoints; point++)
            {
                float angle = Mathf.PI * point / (archPoints - 1);
                float normalizedCross = -Mathf.Cos(angle);
                float cross = crossCenter + normalizedCross * halfWidth;
                float y = floor + Mathf.Sin(angle) * crownHeight;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.Abs(normalizedCross));
                float wallAxis = Mathf.Lerp(centerWallAxis,
                    normalizedCross < 0f ? leftWallAxis : rightWallAxis, blend);
                vertices[point] = xFacing
                    ? new Vector3(socketAxis, y, cross)
                    : new Vector3(cross, y, socketAxis);
                vertices[archPoints + point] = xFacing
                    ? new Vector3(wallAxis, y, cross)
                    : new Vector3(cross, y, wallAxis);
                if (point == archPoints - 1) continue;
                AddCaveDoubleSidedQuad(triangles, point,
                    archPoints + point, point + 1, archPoints + point + 1);
            }
            AddCaveDoubleSidedQuad(triangles, 0, archPoints,
                archPoints - 1, archPoints * 2 - 1);
            CreateCaveMesh("Cave Junction " + profile.Kind + " "
                + portalIndex, vertices, triangles.ToArray(), RoomWallColor(room));
        }

        private void BuildCaveSocketSpandrels(DungeonRoom room,
            IReadOnlyList<DungeonDoorPortal> portals)
        {
            for (int portalIndex = 0; portalIndex < portals.Count; portalIndex++)
            {
                DungeonDoorPortal portal = portals[portalIndex];
                if (portal.RoomId != room.Id) continue;
                DungeonCaveSocketProfile profile = DungeonCaveSockets.Resolve(portal);
                bool xFacing = portal.Facing == DungeonPortalFacing.NegativeX
                    || portal.Facing == DungeonPortalFacing.PositiveX;
                float axis = ToUnity(xFacing ? portal.Center.X : portal.Center.Z);
                float crossCenter = ToUnity(xFacing ? portal.Center.Z : portal.Center.X);
                float inward = portal.Facing == DungeonPortalFacing.NegativeX
                    || portal.Facing == DungeonPortalFacing.NegativeZ ? 1f : -1f;
                float portalHalfWidth = ToUnity(xFacing
                    ? portal.ClosedBlockingBounds.Maximum.Z
                        - portal.ClosedBlockingBounds.Minimum.Z
                    : portal.ClosedBlockingBounds.Maximum.X
                        - portal.ClosedBlockingBounds.Minimum.X) * 0.5f;
                float archHalfWidth = portalHalfWidth - ToUnity(profile.SideInset);
                float floor = ToUnity(room.Bounds.Minimum.Y) + 0.02f;
                float top = ToUnity(room.Bounds.Maximum.Y) - 0.12f;
                float crown = Mathf.Min(top - 0.35f, floor + ToUnity(profile.CrownHeight));
                int points = profile.ArchSegments + 1;
                var vertices = new Vector3[points * 2 + 8];
                var triangles = new List<int>((points - 1) * 12 + 24);
                for (int point = 0; point < points; point++)
                {
                    float t = point / (float)(points - 1);
                    float angle = Mathf.PI * t;
                    float cross = crossCenter - Mathf.Cos(angle) * archHalfWidth;
                    float archY = floor + Mathf.Sin(angle) * (crown - floor);
                    float lowerInset = inward * (0.08f
                        + 0.16f * Mathf.Sin(angle) * CaveNoise(point, room.Index, portalIndex));
                    float upperInset = inward * (0.20f
                        + 0.30f * CaveNoise(point * 0.37f, room.Index + 5, portalIndex));
                    vertices[point] = xFacing
                        ? new Vector3(axis + lowerInset, archY, cross)
                        : new Vector3(cross, archY, axis + lowerInset);
                    vertices[points + point] = xFacing
                        ? new Vector3(axis + upperInset, top, cross)
                        : new Vector3(cross, top, axis + upperInset);
                    if (point == points - 1) continue;
                    AddCaveDoubleSidedQuad(triangles, point, point + 1,
                        points + point, points + point + 1);
                }
                for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                {
                    float inner = crossCenter + (sideIndex == 0 ? -archHalfWidth : archHalfWidth);
                    float outer = crossCenter + (sideIndex == 0 ? -portalHalfWidth : portalHalfWidth);
                    int first = points * 2 + sideIndex * 4;
                    vertices[first] = xFacing ? new Vector3(axis, floor, inner)
                        : new Vector3(inner, floor, axis);
                    vertices[first + 1] = xFacing ? new Vector3(axis, floor, outer)
                        : new Vector3(outer, floor, axis);
                    vertices[first + 2] = xFacing ? new Vector3(axis + inward * 0.16f, top, inner)
                        : new Vector3(inner, top, axis + inward * 0.16f);
                    vertices[first + 3] = xFacing ? new Vector3(axis + inward * 0.16f, top, outer)
                        : new Vector3(outer, top, axis + inward * 0.16f);
                    AddCaveDoubleSidedQuad(triangles, first, first + 1,
                        first + 2, first + 3);
                }
                CreateCaveMesh("Cave Socket Spandrel " + profile.Kind + " " + portalIndex,
                    vertices, triangles.ToArray(), RoomWallColor(room));
            }
        }

        private static float ResolveCaveWallAxis(WorldVector3[] outline,
            bool xFacing, float socketAxis, float cross, float inwardSign,
            float maximumDepth)
        {
            float wallAxis = socketAxis + inwardSign * 3.6f;
            float nearestDepth = float.MaxValue;
            for (int i = 0; i < outline.Length; i++)
            {
                int next = (i + 1) % outline.Length;
                float aCross = ToUnity(xFacing ? outline[i].Z : outline[i].X);
                float bCross = ToUnity(xFacing ? outline[next].Z : outline[next].X);
                float span = bCross - aCross;
                if (Mathf.Abs(span) < 0.0001f) continue;
                float t = (cross - aCross) / span;
                if (t < 0f || t > 1f) continue;
                float aAxis = ToUnity(xFacing ? outline[i].X : outline[i].Z);
                float bAxis = ToUnity(xFacing ? outline[next].X : outline[next].Z);
                float candidate = Mathf.Lerp(aAxis, bAxis, t);
                float depth = (candidate - socketAxis) * inwardSign;
                if (depth <= 0f || depth >= nearestDepth) continue;
                nearestDepth = depth;
                wallAxis = candidate + inwardSign * 0.10f;
            }
            float clampedDepth = Mathf.Clamp((wallAxis - socketAxis) * inwardSign,
                0.25f, maximumDepth);
            return socketAxis + inwardSign * clampedDepth;
        }

        private void BuildAuthoredCaveChamberFloor(DungeonRoom room,
            Vector3[] outline, Vector3 center)
        {
            const int rings = 4;
            int count = outline.Length;
            var vertices = new Vector3[count * rings + 1];
            var triangles = new int[count * ((rings - 1) * 6 + 3)];
            for (int ring = 0; ring < rings; ring++)
            {
                float radius = 1f - ring * 0.245f;
                for (int i = 0; i < count; i++)
                {
                    Vector3 point = Vector3.Lerp(center, outline[i], radius);
                    float variation = 0.034f + 0.025f
                        * CaveNoise(i * 0.35f, ring * 1.3f, room.Index);
                    float fade = Mathf.Sin(Mathf.PI * (1f - radius));
                    point.y = center.y + 0.012f + fade * variation;
                    vertices[ring * count + i] = point;
                    if (ring == rings - 1) continue;
                    int next = (i + 1) % count;
                    int t = (ring * count + i) * 6;
                    int outer = ring * count + i;
                    int outerNext = ring * count + next;
                    int inner = (ring + 1) * count + i;
                    int innerNext = (ring + 1) * count + next;
                    triangles[t] = outer;
                    triangles[t + 1] = outerNext;
                    triangles[t + 2] = inner;
                    triangles[t + 3] = outerNext;
                    triangles[t + 4] = innerNext;
                    triangles[t + 5] = inner;
                }
            }
            vertices[vertices.Length - 1] = center + Vector3.up * 0.012f;
            for (int i = 0; i < count; i++)
            {
                int t = (rings - 1) * count * 6 + i * 3;
                triangles[t] = (rings - 1) * count + i;
                triangles[t + 1] = (rings - 1) * count + (i + 1) % count;
                triangles[t + 2] = vertices.Length - 1;
            }
            CreateCaveMesh("Authored Cave Chamber Undulating Floor",
                vertices, triangles, RoomFloorColor(room));
        }

        private void CreateInvisibleCollisionBox(string name, Vector3 center, Vector3 size)
        {
            if (!addColliders) return;
            var support = new GameObject(name);
            support.transform.SetParent(buildParent, false);
            support.transform.localPosition = center;
            support.AddComponent<BoxCollider>().size = size;
        }

        private void CreateCaveMesh(string name, Vector3[] vertices, int[] triangles,
            Color color)
        {
            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                uv[i] = new Vector2((vertices[i].x + vertices[i].y * 0.37f) * 0.16f,
                    (vertices[i].z + vertices[i].y * 0.61f) * 0.16f);
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            activeAuthoredMeshes.Add(mesh);
            var surface = new GameObject(name);
            surface.transform.SetParent(buildParent, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = surface.AddComponent<MeshRenderer>();
            bool ground = activeTheme == DungeonVisualTheme.Cavern
                && (name.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("Landing", StringComparison.OrdinalIgnoreCase) >= 0);
            renderer.sharedMaterial = ground
                ? EnsureCaveGroundMaterial() : EnsureProceduralSurfaceMaterial();
            SetRendererColor(renderer, color);
        }

        private static float CaveNoise(float a, float b, float seed)
        {
            return Mathf.Sin(a * 1.73f + b * 2.91f + seed * 0.67f)
                 * Mathf.Cos(a * 0.83f - b * 1.41f + seed * 1.31f);
        }

        private void BuildCaveFloorMesh(DungeonRoom room, WorldBounds section, int sectionIndex)
        {
            int seed = room == null ? sectionIndex : room.Index;
            bool protectCenter = room != null
                && section.Minimum.X <= room.Center.X && room.Center.X <= section.Maximum.X
                && section.Minimum.Z <= room.Center.Z && room.Center.Z <= section.Maximum.Z;
            DungeonCaveSurface.MeshData surface = DungeonCaveSurface.BuildFloor(
                section, seed, protectCenter);
            var vertices = new Vector3[surface.Vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(ToUnity(surface.Vertices[i].X),
                    ToUnity(surface.Vertices[i].Y), ToUnity(surface.Vertices[i].Z));
            CreateCaveMesh("Cave " + (room == null ? "Tunnel" : "Room " + room.Index)
                + " Walkable Floor " + sectionIndex, vertices, surface.Triangles,
                room == null ? ThemeFloorColor() : RoomFloorColor(room));
        }

        private void BuildCaveCeilingMesh(DungeonRoom room, WorldBounds section, int sectionIndex)
        {
            const int cells = 8;
            var vertices = new Vector3[(cells + 1) * (cells + 1)];
            var triangles = new int[cells * cells * 6];
            float minX = ToUnity(section.Minimum.X), maxX = ToUnity(section.Maximum.X);
            float minZ = ToUnity(section.Minimum.Z), maxZ = ToUnity(section.Maximum.Z);
            float roof = ToUnity(section.Maximum.Y) - 0.20f;
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                float u = x / (float)cells, v = z / (float)cells;
                float edge = Mathf.Min(u, 1f - u, v, 1f - v) * 2f;
                float rise = Mathf.Clamp01(edge) * (0.35f + 0.30f * CaveNoise(x, z, room.Index));
                vertices[z * (cells + 1) + x] = new Vector3(
                    Mathf.Lerp(minX, maxX, u), roof + rise, Mathf.Lerp(minZ, maxZ, v));
            }
            for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                int n = (z * cells + x) * 6, a = z * (cells + 1) + x;
                triangles[n] = a; triangles[n + 1] = a + cells + 1; triangles[n + 2] = a + 1;
                triangles[n + 3] = a + 1; triangles[n + 4] = a + cells + 1;
                triangles[n + 5] = a + cells + 2;
            }
            CreateCaveMesh("Cave Room " + room.Index + " Roof " + sectionIndex,
                vertices, triangles, RoomWallColor(room));
        }

        private void BuildCaveWallMesh(DungeonRoom room, DungeonPortalFacing facing,
            bool xWall, float fixedAxis, float start, float end)
        {
            int cells = Mathf.Clamp(Mathf.CeilToInt((end - start) / 2.5f), 2, 16);
            const int levels = 5;
            var vertices = new Vector3[(cells + 1) * levels];
            var triangles = new int[cells * (levels - 1) * 6];
            float floor = ToUnity(room.Bounds.Minimum.Y);
            float height = ToUnity(room.Bounds.Maximum.Y - room.Bounds.Minimum.Y);
            float inward = facing == DungeonPortalFacing.NegativeX
                           || facing == DungeonPortalFacing.NegativeZ ? 1f : -1f;
            for (int i = 0; i <= cells; i++)
            for (int level = 0; level < levels; level++)
            {
                float u = i / (float)cells, v = level / (float)(levels - 1);
                float endTaper = Mathf.Sin(Mathf.PI * u);
                float inset = (0.10f + 0.28f * CaveNoise(i, level, room.Index + (int)facing))
                              * endTaper * Mathf.Sin(Mathf.PI * v);
                float axis = fixedAxis + inward * inset;
                float y = floor + v * height;
                vertices[i * levels + level] = xWall
                    ? new Vector3(axis, y, Mathf.Lerp(start, end, u))
                    : new Vector3(Mathf.Lerp(start, end, u), y, axis);
            }
            for (int i = 0; i < cells; i++)
            for (int level = 0; level < levels - 1; level++)
            {
                int n = (i * (levels - 1) + level) * 6, a = i * levels + level;
                triangles[n] = a; triangles[n + 1] = a + 1; triangles[n + 2] = a + levels;
                triangles[n + 3] = a + levels; triangles[n + 4] = a + 1;
                triangles[n + 5] = a + levels + 1;
            }
            CreateCaveMesh("Cave Room " + room.Index + " Wall " + facing,
                vertices, triangles, RoomWallColor(room));
        }

        private void BuildCaveTunnelMesh(DungeonCorridor corridor, int index)
        {
            WorldBounds bounds = corridor.WalkableBounds;
            bool alongX = DungeonCorridorGeometry.IsAlongX(activeLayout, corridor);
            Vector3 center = BoundsCenter(bounds), size = BoundsSize(bounds);
            float length = alongX ? size.x : size.z;
            float width = alongX ? size.z : size.x;
            float floor = ToUnity(bounds.Minimum.Y), height = size.y;
            int rings = Mathf.Clamp(Mathf.CeilToInt(length / 2.5f), 2, 24);
            const int archPoints = 17;
            var vertices = new Vector3[(rings + 1) * archPoints];
            var triangles = new int[rings * (archPoints - 1) * 6];
            DungeonCaveSocketProfile startSocket = default, endSocket = default;
            int minimumSocketAxis = int.MaxValue, maximumSocketAxis = int.MinValue;
            float startSocketCross = alongX ? center.z : center.x;
            float endSocketCross = startSocketCross;
            foreach (DungeonDoorPortal portal in activeLayout.DoorPortals)
            {
                if (portal.ConnectionId != corridor.ConnectionId) continue;
                int portalAxis = alongX ? portal.Center.X : portal.Center.Z;
                if (portalAxis < minimumSocketAxis)
                {
                    minimumSocketAxis = portalAxis;
                    startSocket = DungeonCaveSockets.Resolve(portal);
                    startSocketCross = ToUnity(alongX ? portal.Center.Z : portal.Center.X);
                }
                if (portalAxis > maximumSocketAxis)
                {
                    maximumSocketAxis = portalAxis;
                    endSocket = DungeonCaveSockets.Resolve(portal);
                    endSocketCross = ToUnity(alongX ? portal.Center.Z : portal.Center.X);
                }
            }
            for (int ring = 0; ring <= rings; ring++)
            for (int point = 0; point < archPoints; point++)
            {
                float u = ring / (float)rings;
                float normalizedCross = (float)DungeonCaveSockets.NormalizedCross(
                    point, archPoints - 1);
                float arch = (float)DungeonCaveSockets.NormalizedHeight(
                    point, archPoints - 1);
                float shoulder = Mathf.Abs(normalizedCross);
                float taper = Mathf.Sin(Mathf.PI * u);
                float socketWidth = Mathf.Lerp(ToUnity(startSocket.VisualWidth),
                    ToUnity(endSocket.VisualWidth), u);
                float ringWidth = Mathf.Lerp(socketWidth, width,
                    Mathf.Sin(Mathf.PI * u));
                float halfWidth = ringWidth * 0.5f;
                float shift = 0.16f * taper * CaveNoise(ring, 1f, index);
                float socketCenterCross = Mathf.Lerp(startSocketCross, endSocketCross, u);
                float boundsCross = alongX ? center.z : center.x;
                float ringCross = Mathf.Lerp(socketCenterCross, boundsCross, taper);
                float side = normalizedCross * halfWidth
                    * (1f - 0.08f * taper * (1f - shoulder)) + shift;
                float crown = Mathf.Min(height - 0.30f,
                    Mathf.Lerp(ToUnity(startSocket.CrownHeight),
                        ToUnity(endSocket.CrownHeight), u)
                    + 0.45f * Mathf.Sin(Mathf.PI * u));
                float y = floor + Mathf.Lerp(0.05f, crown, arch)
                    + arch * taper * 0.24f * CaveNoise(ring, point, index);
                float along = (u - 0.5f) * length;
                vertices[ring * archPoints + point] = alongX
                    ? new Vector3(center.x + along, y, ringCross + side)
                    : new Vector3(ringCross + side, y, center.z + along);
            }
            for (int ring = 0; ring < rings; ring++)
            for (int point = 0; point < archPoints - 1; point++)
            {
                int n = (ring * (archPoints - 1) + point) * 6;
                int a = ring * archPoints + point;
                triangles[n] = a; triangles[n + 1] = a + archPoints; triangles[n + 2] = a + 1;
                triangles[n + 3] = a + 1; triangles[n + 4] = a + archPoints;
                triangles[n + 5] = a + archPoints + 1;
            }
            CreateCaveMesh("Cave Tunnel " + index + " Irregular Shell",
                vertices, triangles, ThemeWallColor());
            // The mesh is presentation-only. Conservative box collision remains aligned
            // with the server's corridor envelope and cannot open a falling seam.
            float wallY = floor + height * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = alongX
                    ? new Vector3(center.x, wallY, center.z + side * width * 0.5f)
                    : new Vector3(center.x + side * width * 0.5f, wallY, center.z);
                Vector3 scale = alongX
                    ? new Vector3(length, height, wallThickness)
                    : new Vector3(wallThickness, height, length);
                CreateInvisibleCollisionBox("Cave Tunnel Wall Support", position, scale);
            }
        }

        private void BuildCaveRoomLighting(DungeonRoom room)
        {
            Vector3 center = BoundsCenter(room.Bounds);
            Color glow = room.Role == DungeonRoomRole.Boss
                ? new Color(0.82f, 0.39f, 0.16f) : new Color(0.40f, 0.70f, 0.54f);
            var lightObject = new GameObject("Cave Room " + room.Index + " Ambient Light");
            lightObject.transform.SetParent(buildParent, false);
            lightObject.transform.localPosition = center + Vector3.up * 1.2f;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = glow;
            light.range = Mathf.Min(14f, Mathf.Max(BoundsSize(room.Bounds).x, BoundsSize(room.Bounds).z) * 0.65f);
            light.intensity = room.Role == DungeonRoomRole.Boss ? 2.3f
                : room.Role == DungeonRoomRole.Objective ? 1.55f : 0.95f;
            light.shadows = LightShadows.None;
        }


        private void BuildTempleRoomDetails(DungeonRoom room, float minX, float maxX,
            float minZ, float maxZ, float height, Color accent)
        {
            float floorY = ToUnity(room.Bounds.Minimum.Y);
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float width = maxX - minX;
            float depth = maxZ - minZ;
            Color stone = new Color(0.43f, 0.34f, 0.22f);
            Color bronze = new Color(0.52f, 0.30f, 0.12f);
            Color cloth = room.Role == DungeonRoomRole.Boss
                ? new Color(0.35f, 0.035f, 0.055f)
                : new Color(0.24f, 0.06f, 0.16f);

            if (room.Shape != DungeonRoomShape.LShaped)
            {
                for (int xSide = -1; xSide <= 1; xSide += 2)
                for (int zSide = -1; zSide <= 1; zSide += 2)
                {
                    Vector3 pillarCenter = new Vector3(
                        centerX + xSide * (width * 0.5f - 0.85f),
                        floorY + height * 0.5f,
                        centerZ + zSide * (depth * 0.5f - 0.85f));
                    if (!TryPlaceTempleAssetFitted("temple-pillar-front", pillarCenter,
                        Quaternion.identity, new Vector3(.9f, height - .2f, .9f)))
                        CreateCube("Temple Room " + room.Index + " Pillar " + xSide + "/" + zSide,
                            pillarCenter, new Vector3(0.62f, height - 0.2f, 0.62f), stone, false);
                }
                CreateCube("Temple Room " + room.Index + " Vault Rib X",
                    new Vector3(centerX, floorY + height - 0.30f, centerZ),
                    new Vector3(Mathf.Max(1f, width - 0.8f), 0.38f, 0.44f),
                    stone, false);
                CreateCube("Temple Room " + room.Index + " Vault Rib Z",
                    new Vector3(centerX, floorY + height - 0.28f, centerZ),
                    new Vector3(0.44f, 0.38f, Mathf.Max(1f, depth - 0.8f)),
                    stone, false);
            }

            bool sacred = room.ModuleKind == DungeonModuleKind.NaveHub
                || room.ModuleKind == DungeonModuleKind.InnerGate
                || room.ModuleKind == DungeonModuleKind.HighSanctuary
                || room.ModuleKind == DungeonModuleKind.ProfaneSanctum
                || room.ModuleKind == DungeonModuleKind.RitualChapel;
            if (sacred)
            {
                for (int side = -1; side <= 1; side += 2)
                    CreateCube("Temple Room " + room.Index + " Banner " + side,
                        new Vector3(centerX + side * width * 0.27f,
                            floorY + height - 1.55f, minZ + 0.17f),
                        new Vector3(0.92f, 2.15f, 0.07f), cloth, false);
                if (!TryPlaceTempleAsset("temple-altar-01",
                    new Vector3(centerX, floorY + 0.6f, centerZ), Quaternion.identity,
                    Vector3.one))
                    CreateCube("Temple Room " + room.Index + " Altar",
                        new Vector3(centerX, floorY + 0.48f, centerZ),
                        new Vector3(2.25f, 0.78f, 1.15f), bronze, false);
            }

            for (int side = -1; side <= 1; side += 2)
                CreateCube("Temple Room " + room.Index + " Brazier " + side,
                    new Vector3(centerX + side * Mathf.Min(2.8f, width * 0.27f),
                        floorY + 0.72f, centerZ),
                    new Vector3(0.42f, 1.05f, 0.42f), accent, false);
            var lightObject = new GameObject("Temple Room " + room.Index + " Firelight");
            lightObject.transform.SetParent(buildParent, false);
            lightObject.transform.localPosition =
                new Vector3(centerX, floorY + Mathf.Min(height - 0.7f, 3.2f), centerZ);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = room.Role == DungeonRoomRole.Boss
                ? new Color(0.95f, 0.22f, 0.10f) : new Color(1f, 0.58f, 0.24f);
            light.range = Mathf.Min(16f, Mathf.Max(width, depth) * 0.78f);
            light.intensity = room.Role == DungeonRoomRole.Boss ? 2.6f : 1.55f;
            light.shadows = LightShadows.None;
        }

        private void BuildTempleCorridorDetails(DungeonCorridor corridor, int index)
        {
            WorldBounds bounds = corridor.WalkableBounds;
            bool alongX = DungeonCorridorGeometry.IsAlongX(activeLayout, corridor);
            Vector3 center = BoundsCenter(bounds);
            Vector3 size = BoundsSize(bounds);
            float length = alongX ? size.x : size.z;
            float width = alongX ? size.z : size.x;
            int bays = Mathf.Clamp(Mathf.RoundToInt(length / 4.5f), 2, 12);
            Color stone = new Color(0.45f, 0.36f, 0.23f);
            for (int bay = 0; bay < bays; bay++)
            {
                float along = Mathf.Lerp(-length * 0.43f, length * 0.43f,
                    bays == 1 ? 0.5f : bay / (float)(bays - 1));
                Vector3 rib = alongX
                    ? new Vector3(center.x + along, ToUnity(bounds.Maximum.Y) - 0.32f,
                        center.z)
                    : new Vector3(center.x, ToUnity(bounds.Maximum.Y) - 0.32f,
                        center.z + along);
                CreateCube("Temple Corridor " + index + " Arch " + bay, rib,
                    alongX ? new Vector3(0.42f, 0.34f, width - 0.22f)
                        : new Vector3(width - 0.22f, 0.34f, 0.42f), stone, false);
                if (bay % 2 != 0) continue;
                CreateCube("Temple Corridor " + index + " Sconce " + bay,
                    rib - Vector3.up * 1.25f, new Vector3(0.28f, 0.38f, 0.28f),
                    new Color(0.95f, 0.38f, 0.10f), false);
            }
        }

        private void BuildKeepRoomDetails(DungeonRoom room, float minX, float maxX,
            float minZ, float maxZ, float height, Color accent)
        {
            float floorY = ToUnity(room.Bounds.Minimum.Y);
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float width = maxX - minX;
            float depth = maxZ - minZ;
            Color stone = new Color(0.42f, 0.44f, 0.46f);
            Color banner = room.Role == DungeonRoomRole.Boss
                ? new Color(0.48f, 0.12f, 0.14f)
                : new Color(0.18f, 0.28f, 0.43f);

            // Freestanding corner supports imply a vaulted keep without constricting
            // the centered door lane. L-shaped rooms retain their unobstructed floor.
            if (room.Shape != DungeonRoomShape.LShaped)
                for (int xSide = -1; xSide <= 1; xSide += 2)
                for (int zSide = -1; zSide <= 1; zSide += 2)
                    CreateCube("Keep Room " + room.Index + " Corner Pier "
                               + xSide + "/" + zSide,
                        new Vector3(centerX + xSide * (width * 0.5f - 0.85f),
                            floorY + height * 0.5f,
                            centerZ + zSide * (depth * 0.5f - 0.85f)),
                        new Vector3(0.55f, height - 0.25f, 0.55f), stone, false);

            // Crossing vault ribs and wall banners give major rooms a distinct
            // silhouette and navigation landmark without inserting floor blockers.
            if (room.Shape != DungeonRoomShape.LShaped)
            {
                CreateCube("Keep Room " + room.Index + " Vault Rib X",
                    new Vector3(centerX, floorY + height - 0.30f, centerZ),
                    new Vector3(Mathf.Max(1f, width - 0.7f), 0.34f, 0.40f), stone, false);
                CreateCube("Keep Room " + room.Index + " Vault Rib Z",
                    new Vector3(centerX, floorY + height - 0.28f, centerZ),
                    new Vector3(0.40f, 0.34f, Mathf.Max(1f, depth - 0.7f)), stone, false);
            }
            if (room.Role == DungeonRoomRole.Boss || room.Role == DungeonRoomRole.Objective
                || room.Shape == DungeonRoomShape.GrandChamber)
            {
                for (int side = -1; side <= 1; side += 2)
                    CreateCube("Keep Room " + room.Index + " Hanging Banner " + side,
                        new Vector3(centerX + side * width * 0.27f,
                            floorY + height - 1.35f, minZ + 0.20f),
                        new Vector3(0.80f, 1.70f, 0.06f), banner, false);
            }
            CreateCube("Keep Room " + room.Index + " Lantern",
                new Vector3(centerX, floorY + height - 0.62f, centerZ),
                new Vector3(0.48f, 0.40f, 0.48f), accent, false);
            if (room.Role == DungeonRoomRole.Boss || room.Role == DungeonRoomRole.Objective
                || room.Index % 3 == 0)
            {
                var lightObject = new GameObject("Keep Room " + room.Index + " Warm Light");
                lightObject.transform.SetParent(buildParent, false);
                lightObject.transform.localPosition =
                    new Vector3(centerX, floorY + height - 0.8f, centerZ);
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.72f, 0.40f);
                light.range = Mathf.Min(15f, Mathf.Max(width, depth) * 0.72f);
                light.intensity = room.Role == DungeonRoomRole.Boss ? 2.3f : 1.5f;
                light.shadows = LightShadows.None;
            }
        }

        private void BuildKeepCorridorDetails(DungeonCorridor corridor, int index)
        {
            WorldBounds bounds = corridor.WalkableBounds;
            bool alongX = DungeonCorridorGeometry.IsAlongX(activeLayout, corridor);
            Vector3 center = BoundsCenter(bounds);
            Vector3 size = BoundsSize(bounds);
            float length = alongX ? size.x : size.z;
            float width = alongX ? size.z : size.x;
            int bays = Mathf.Clamp(Mathf.RoundToInt(length / 5f), 2, 10);
            Color stone = new Color(0.44f, 0.45f, 0.46f);
            for (int bay = 0; bay < bays; bay++)
            {
                float along = Mathf.Lerp(-length * 0.42f, length * 0.42f,
                    bays == 1 ? 0.5f : bay / (float)(bays - 1));
                Vector3 rib = alongX
                    ? new Vector3(center.x + along, ToUnity(bounds.Maximum.Y) - 0.30f, center.z)
                    : new Vector3(center.x, ToUnity(bounds.Maximum.Y) - 0.30f, center.z + along);
                CreateCube("Keep Corridor " + index + " Vault Arch " + bay, rib,
                    alongX ? new Vector3(0.40f, 0.30f, width - 0.25f)
                        : new Vector3(width - 0.25f, 0.30f, 0.40f), stone, false);
                if (bay % 2 != 0) continue;
                CreateCube("Keep Corridor " + index + " Sconce " + bay,
                    rib - Vector3.up * 1.1f,
                    new Vector3(0.32f, 0.42f, 0.32f),
                    new Color(0.95f, 0.55f, 0.18f), false);
            }
        }

        private void BuildSubwayRoomDetails(DungeonRoom room,
            IReadOnlyList<DungeonDoorPortal> portals, float minX, float maxX,
            float minZ, float maxZ, float floorY, float height)
        {
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            float width = maxX - minX;
            float depth = maxZ - minZ;
            bool tracksAlongX = width >= depth;
            Color concrete = new Color(0.62f, 0.66f, 0.69f);
            Color trackBed = new Color(0.055f, 0.065f, 0.075f);
            Color rail = new Color(0.58f, 0.62f, 0.65f);
            Color safety = new Color(0.96f, 0.70f, 0.08f);
            Color sign = new Color(0.08f, 0.23f, 0.42f);

            if (room.ModuleKind == DungeonModuleKind.Entrance)
            {
                // Keep this as a legible circulation L rather than scattering
                // concourse props through the intentionally absent inside corner.
                BuildSubwayEntranceDoor(room, portals, floorY, height, sign, safety);
                return;
            }
            if (room.ModuleKind == DungeonModuleKind.ServiceRoom
                || room.ModuleKind == DungeonModuleKind.TreasureRoom)
            {
                BuildSubwayServiceRoom(room.Index, centerX, centerZ, width, depth, height,
                    floorY, concrete, safety);
                return;
            }
            if (room.ModuleKind == DungeonModuleKind.StationConcourse)
            {
                BuildSubwayConcourse(room.Index, centerX, centerZ, width, depth, height,
                    floorY, concrete, sign, safety);
                return;
            }
            if (room.ModuleKind == DungeonModuleKind.StationPlatform
                || room.ModuleKind == DungeonModuleKind.Terminal)
            {
                // These are safe, full-floor circulation rooms. Recessed rail geometry
                // belongs only to the dedicated, oversized TrainChamber.
                BuildSubwayConcourse(room.Index, centerX, centerZ, width, depth, height,
                    floorY, concrete, sign, safety);
                return;
            }
            if (room.ModuleKind == DungeonModuleKind.TrackTunnel)
            {
                BuildSubwayRailPortalInfills(room);
                BuildSubwayTrackRoom(room.Index, centerX, centerZ, width, depth,
                    height, floorY, ToUnity(DungeonRoomGeometry.SubwayTrackFloorY(room)),
                    trackBed, rail, concrete, sign, safety);
                return;
            }
            if (room.ModuleKind == DungeonModuleKind.TrackJunction)
            {
                BuildSubwayTrackJunction(room, portals, floorY, trackBed, rail,
                    concrete, safety);
                return;
            }
            if (room.ModuleKind == DungeonModuleKind.GrandHall)
            {
                BuildSubwayGrandHall(room.Index, centerX, centerZ, width, depth,
                    height, floorY, concrete, sign);
                return;
            }
            if (room.ModuleKind == DungeonModuleKind.Restroom)
            {
                if (activeLayout.Manifest.GeneratorVersion != "5.13.0")
                    BuildSubwayRestroom(room, portals, centerX, centerZ, width, depth, height,
                        floorY, concrete, sign);
                return;
            }

            // A flush, non-colliding track bed and rails make station rooms immediately readable
            // while retaining the original room floor as the authoritative walkable surface.
            float trackFloorY = ToUnity(DungeonRoomGeometry.SubwayTrackFloorY(room));
            float crossSize = tracksAlongX ? depth : width;
            float trackCrossCenter = 0f;
            Vector3 bedScale = tracksAlongX
                ? new Vector3(Mathf.Max(2f, width - 0.2f), 0.055f, 5f)
                : new Vector3(5f, 0.055f, Mathf.Max(2f, depth - 0.2f));
            Vector3 bedPosition = tracksAlongX
                ? new Vector3(centerX, trackFloorY + 0.018f, centerZ + trackCrossCenter)
                : new Vector3(centerX + trackCrossCenter, trackFloorY + 0.018f, centerZ);
            CreateCube("Subway Room " + room.Index + " Track Bed",
                bedPosition, bedScale, trackBed, false);
            const float railOffset = 0.90f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 railPosition = tracksAlongX
                    ? new Vector3(centerX, trackFloorY + 0.075f,
                        bedPosition.z + side * railOffset)
                    : new Vector3(bedPosition.x + side * railOffset,
                        trackFloorY + 0.075f, centerZ);
                Vector3 railScale = tracksAlongX
                    ? new Vector3(bedScale.x, 0.07f, 0.075f)
                    : new Vector3(0.075f, 0.07f, bedScale.z);
                CreateCube("Subway Room " + room.Index + " Rail " + side,
                    railPosition, railScale, rail, false, assetRole: DungeonAssetRole.Rail);
            }
            float stationTrackLength = tracksAlongX ? bedScale.x : bedScale.z;
            int stationSleeperCount = Mathf.Clamp(
                Mathf.FloorToInt(stationTrackLength / 1.15f), 8, 40);
            for (int sleeper = 0; sleeper < stationSleeperCount; sleeper++)
            {
                float offset = Mathf.Lerp(-stationTrackLength * 0.48f,
                    stationTrackLength * 0.48f,
                    stationSleeperCount == 1 ? 0.5f
                        : sleeper / (float)(stationSleeperCount - 1));
                Vector3 sleeperPosition = tracksAlongX
                    ? new Vector3(centerX + offset, trackFloorY + 0.035f, centerZ)
                    : new Vector3(centerX, trackFloorY + 0.035f, centerZ + offset);
                Vector3 sleeperScale = tracksAlongX
                    ? new Vector3(0.18f, 0.045f, 4.8f)
                    : new Vector3(4.8f, 0.045f, 0.18f);
                CreateCube("Subway Room " + room.Index + " Sleeper " + sleeper,
                    sleeperPosition, sleeperScale, concrete, false);
            }

            // Keep the safety line just outside the actual platform retaining wall.
            // The train chamber has a wider track bed in the newer layout.
            float safetyLineOffset = ToUnity(
                DungeonRoomGeometry.SubwayTrackWidthForRoom(room)) * 0.5f + 0.15f;
            float longitudinal = tracksAlongX ? width : depth;
            const float stationStairHalfWidth = 3.5f;
            float lineSegmentLength = Mathf.Max(0.2f,
                (longitudinal - stationStairHalfWidth * 2f) * 0.5f);
            Vector3 lineScale = tracksAlongX
                ? new Vector3(lineSegmentLength, 0.025f, 0.16f)
                : new Vector3(0.16f, 0.025f, lineSegmentLength);
            for (int platformSide = -1; platformSide <= 1; platformSide += 2)
            {
                for (int segment = -1; segment <= 1; segment += 2)
                {
                    float alongOffset = segment
                        * (stationStairHalfWidth + lineSegmentLength * 0.5f);
                    Vector3 linePosition = tracksAlongX
                        ? new Vector3(centerX + alongOffset, floorY + 0.026f,
                            centerZ + platformSide * safetyLineOffset)
                        : new Vector3(centerX + platformSide * safetyLineOffset,
                            floorY + 0.026f, centerZ + alongOffset);
                    CreateCube("Subway Room " + room.Index + " Platform Safety Line "
                        + platformSide + "/" + segment,
                        linePosition, lineScale, safety, false, assetRole: DungeonAssetRole.Safety);
                }
            }

            // Columns, hanging route signs, and a continuous light strip distinguish stations
            // from ordinary facility chambers. Keep the center aisle and every doorway clear.
            int columnPairs = Mathf.Clamp(Mathf.FloorToInt(longitudinal / 3.2f), 2, 4);
            for (int i = 0; i < columnPairs; i++)
            {
                float along = Mathf.Lerp(-longitudinal * 0.32f, longitudinal * 0.32f,
                    columnPairs == 1 ? 0.5f : i / (float)(columnPairs - 1));
                for (int side = -1; side <= 1; side += 2)
                {
                    float cross = (tracksAlongX ? depth : width) * 0.34f * side;
                    Vector3 position = tracksAlongX
                        ? new Vector3(centerX + along, floorY + height * 0.5f, centerZ + cross)
                        : new Vector3(centerX + cross, floorY + height * 0.5f, centerZ + along);
                    CreateCube("Subway Room " + room.Index + " Platform Column " + i + " " + side,
                        position, new Vector3(0.28f, height - 0.25f, 0.28f), concrete, false, assetRole: DungeonAssetRole.Pillar);
                }
            }
            Vector3 lightScale = tracksAlongX
                ? new Vector3(Mathf.Max(1f, width - 1.2f), 0.10f, 0.24f)
                : new Vector3(0.24f, 0.10f, Mathf.Max(1f, depth - 1.2f));
            CreateCube("Subway Room " + room.Index + " Platform Light Strip",
                new Vector3(centerX, floorY + height - 0.25f, centerZ), lightScale,
                new Color(0.86f, 0.92f, 0.88f), false, assetRole: DungeonAssetRole.Light);
            Vector3 signScale = tracksAlongX
                ? new Vector3(1.65f, 0.48f, 0.10f) : new Vector3(0.10f, 0.48f, 1.65f);
            CreateCube("Subway Room " + room.Index + " Route Sign",
                new Vector3(centerX, floorY + height - 0.82f, centerZ), signScale, sign, false);
            if (room.ModuleKind == DungeonModuleKind.TrainChamber)
            {
                BuildSubwayRailPortalInfills(room);
                BuildStoppedSubwayTrain(room);
            }
        }

        private void BuildCorePresentation(string ownerId)
        {
            if (activeLayout == null) return;
            bool subwayBathroom = activeTheme == DungeonVisualTheme.Subway
                && activeLayout.Manifest.GeneratorVersion != "5.13.0"
                && activeLayout.Rooms.Any(room => room.Id == ownerId && room.ModuleKind == DungeonModuleKind.Restroom);
            for (int i = 0; i < activeLayout.Presentation.Count; i++)
            {
                DungeonPresentationElement element = activeLayout.Presentation[i];
                if (element.OwnerId != ownerId) continue;
                if (activeLayout.Manifest.GeneratorVersion == "5.13.0"
                    && (element.FixtureRole == DungeonFixtureRole.Toilet || element.FixtureRole == DungeonFixtureRole.Sink))
                {
                    Vector3 size = BoundsSize(element.Bounds);
                    if (element.YawDegrees == 90 || element.YawDegrees == 270) size = new Vector3(size.z, size.y, size.x);
                    PlaceSubwayBathroomProp(element.FixtureRole == DungeonFixtureRole.Toilet ? "toilet" : "sink",
                        "Resolved Fixture " + element.Id, BoundsCenter(element.Bounds), size,
                        Quaternion.Euler(0, element.YawDegrees, 0) * Vector3.forward, Color.white);
                    continue;
                }
                // The client builds the updated GLB fixture bank once, with open stalls.
                // Keep the shared room lighting and non-bathroom presentation elements.
                if (subwayBathroom && (element.MaterialRole == DungeonMaterialRole.Porcelain
                    || element.MaterialRole == DungeonMaterialRole.RestroomPartition)) continue;
                bool hasConstructiveRecipe = false;
                for (int recipeIndex = 0; recipeIndex < activeLayout.ConstructiveRecipes.Count;
                    recipeIndex++)
                    if (activeLayout.ConstructiveRecipes[recipeIndex].FixtureId == element.Id)
                    { hasConstructiveRecipe = true; break; }
                if (hasConstructiveRecipe) continue;
                Color color;
                switch (element.MaterialRole)
                {
                    case DungeonMaterialRole.ModernPanelMetal:
                        color = new Color(0.62f, 0.66f, 0.69f); break;
                    case DungeonMaterialRole.DarkStructuralMetal:
                        color = new Color(0.12f, 0.15f, 0.18f); break;
                    case DungeonMaterialRole.WayfindingBlue:
                        color = new Color(0.06f, 0.30f, 0.55f); break;
                    case DungeonMaterialRole.CoolWhiteLight:
                        color = new Color(0.88f, 0.95f, 1.0f); break;
                    case DungeonMaterialRole.CoolRailLight:
                        color = new Color(0.68f, 0.78f, 0.82f); break;
                    case DungeonMaterialRole.RestroomPartition:
                        color = new Color(0.30f, 0.34f, 0.35f); break;
                    case DungeonMaterialRole.Porcelain:
                        color = new Color(0.82f, 0.84f, 0.80f); break;
                    default: color = Color.white; break;
                }
                if (activeTheme == DungeonVisualTheme.Subway
                    && (element.MaterialRole == DungeonMaterialRole.CoolWhiteLight
                        || element.MaterialRole == DungeonMaterialRole.CoolRailLight)
                    && subwayDungeonKit != null
                    && subwayDungeonKit.Place("light", "Core Presentation " + element.Id, buildParent,
                        BoundsCenter(element.Bounds), BoundsSize(element.Bounds), Quaternion.identity)) continue;
                CreateCube("Core Presentation " + element.Id,
                    BoundsCenter(element.Bounds), BoundsSize(element.Bounds), color, false);
            }
            for (int i = 0; i < activeLayout.ConstructiveRecipes.Count; i++)
            {
                DungeonPrimitiveRecipe recipe = activeLayout.ConstructiveRecipes[i];
                if (recipe.OwnerId != ownerId) continue;
                if (subwayBathroom && (recipe.FixtureId.Contains("/toilet-") || recipe.FixtureId.Contains("/sink-"))) continue;
                GeometryMeshData data = ConstructiveMeshBuilder.Build(recipe);
                var mesh = new Mesh { name = "WorldGen " + recipe.Id };
                var vertices = new Vector3[data.Vertices.Length];
                var normals = new Vector3[data.Normals.Length];
                var uv = new Vector2[data.UV.Length];
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    vertices[vertex] = new Vector3(data.Vertices[vertex].X,
                        data.Vertices[vertex].Y, data.Vertices[vertex].Z)
                        / WorldUnitsPerUnityUnit;
                    normals[vertex] = new Vector3(data.Normals[vertex].X,
                        data.Normals[vertex].Y, data.Normals[vertex].Z);
                    uv[vertex] = new Vector2(data.UV[vertex].X, data.UV[vertex].Y);
                }
                mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv;
                mesh.triangles = data.Indices; mesh.RecalculateBounds();
                activeAuthoredMeshes.Add(mesh);
                var child = new GameObject("Core Constructive " + recipe.Id);
                child.transform.SetParent(buildParent, false);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                Renderer renderer = child.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = EnsureProceduralSurfaceMaterial();
                SetRendererColor(renderer, recipe.MaterialRole == DungeonMaterialRole.Porcelain
                    ? new Color(0.88f, 0.90f, 0.88f)
                    : recipe.MaterialRole == DungeonMaterialRole.ModernPanelMetal
                        ? new Color(0.62f, 0.66f, 0.69f)
                        : new Color(0.12f, 0.15f, 0.18f));
            }
        }

        private void BuildModernSubwayArchitecture(DungeonRoom room,
            IReadOnlyList<DungeonDoorPortal> portals, float floorY, float height,
            Color panelMetal, Color routeBlue)
        {
            Color darkMetal = new Color(0.12f, 0.15f, 0.18f);
            IReadOnlyList<DungeonRoomWallSection> walls =
                DungeonRoomGeometry.WallSections(room);
            for (int wallIndex = 0; wallIndex < walls.Count; wallIndex++)
            {
                DungeonRoomWallSection wall = walls[wallIndex];
                bool xWall = wall.Facing == DungeonPortalFacing.NegativeX
                    || wall.Facing == DungeonPortalFacing.PositiveX;
                float fixedAxis = ToUnity(wall.FixedAxis);
                float start = ToUnity(wall.Start) + 0.35f;
                float end = ToUnity(wall.End) - 0.35f;
                float length = end - start;
                if (length <= 0.3f) continue;

                // A continuous upper datum ties differently sized modules together.
                Vector3 bandPosition = xWall
                    ? new Vector3(fixedAxis, floorY + height * 0.70f,
                        (start + end) * 0.5f)
                    : new Vector3((start + end) * 0.5f,
                        floorY + height * 0.70f, fixedAxis);
                Vector3 bandScale = xWall
                    ? new Vector3(0.10f, 0.12f, length)
                    : new Vector3(length, 0.12f, 0.10f);
                CreateCube("Modern Subway Room " + room.Index + " Upper Band " + wallIndex,
                    bandPosition, bandScale, routeBlue, false);

                int bays = Mathf.Max(1, Mathf.FloorToInt(length / 3.2f));
                for (int bay = 1; bay < bays; bay++)
                {
                    float axis = Mathf.Lerp(start, end, bay / (float)bays);
                    bool crossesOpening = false;
                    for (int portalIndex = 0; portalIndex < portals.Count; portalIndex++)
                    {
                        DungeonDoorPortal portal = portals[portalIndex];
                        if (portal.RoomId != room.Id || portal.Facing != wall.Facing) continue;
                        float openingStart = ToUnity(xWall
                            ? portal.ClosedBlockingBounds.Minimum.Z
                            : portal.ClosedBlockingBounds.Minimum.X) - 0.20f;
                        float openingEnd = ToUnity(xWall
                            ? portal.ClosedBlockingBounds.Maximum.Z
                            : portal.ClosedBlockingBounds.Maximum.X) + 0.20f;
                        if (axis >= openingStart && axis <= openingEnd)
                        { crossesOpening = true; break; }
                    }
                    if (crossesOpening) continue;
                    Vector3 mullionPosition = xWall
                        ? new Vector3(fixedAxis, floorY + height * 0.5f, axis)
                        : new Vector3(axis, floorY + height * 0.5f, fixedAxis);
                    Vector3 mullionScale = xWall
                        ? new Vector3(0.11f, Mathf.Max(1f, height - 0.35f), 0.08f)
                        : new Vector3(0.08f, Mathf.Max(1f, height - 0.35f), 0.11f);
                    CreateCube("Modern Subway Room " + room.Index + " Mullion "
                        + wallIndex + "/" + bay, mullionPosition, mullionScale,
                        darkMetal, false);
                }
            }

            // Ceiling cross-members follow the actual floor sections, including
            // both legs of the authored L, rather than filling its absent quadrant.
            IReadOnlyList<WorldBounds> sections = DungeonRoomGeometry.FloorSections(room);
            if (!DungeonRoomGeometry.IsSubwayTrackRoom(room))
                for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
                {
                    WorldBounds section = sections[sectionIndex];
                    Vector3 center = BoundsCenter(section);
                    Vector3 size = BoundsSize(section);
                    center.y = floorY + height - 0.18f;
                    bool longX = size.x >= size.z;
                    Vector3 beamScale = longX
                        ? new Vector3(Mathf.Max(0.6f, size.x - 0.35f), 0.16f, 0.18f)
                        : new Vector3(0.18f, 0.16f, Mathf.Max(0.6f, size.z - 0.35f));
                    CreateCube("Modern Subway Room " + room.Index + " Ceiling Beam "
                        + sectionIndex, center, beamScale, darkMetal, false);
                }

            BuildModernSubwayLighting(room, floorY, height);
            if (room.ModuleKind == DungeonModuleKind.Entrance)
                BuildModernSubwayOpenPortalFrames(room, portals, floorY,
                    darkMetal, panelMetal);
        }

        private void BuildSubwayCeilingFixtureGrid(DungeonRoom room)
        {
            bool train = room.ModuleKind == DungeonModuleKind.TrainChamber;
            float spacing = train ? 4f : 6f;
            // Track floor sections include individual stair treads. Use one ceiling
            // envelope there; other rooms use their actual footprint (including the L).
            IReadOnlyList<WorldBounds> sections = DungeonRoomGeometry.IsSubwayTrackRoom(room)
                ? new[] { room.Bounds } : DungeonRoomGeometry.FloorSections(room);
            var fixtureRoot = new GameObject("Room " + room.Index + " Ceiling Fixture Grid");
            fixtureRoot.transform.SetParent(buildParent, false);
            Transform previous = buildParent;
            buildParent = fixtureRoot.transform;
            try
            {
                for (int section = 0; section < sections.Count; section++)
                {
                    WorldBounds bounds = sections[section];
                    Vector3 size = BoundsSize(bounds);
                    Vector3 center = BoundsCenter(bounds);
                    center.y = ToUnity(room.Bounds.Maximum.Y) - .38f;
                    int columns = Mathf.Max(1, Mathf.CeilToInt(size.x / spacing));
                    int rows = Mathf.Max(1, Mathf.CeilToInt(size.z / spacing));
                    for (int row = 0; row < rows; row++)
                        for (int column = 0; column < columns; column++)
                        {
                            Vector3 position = center + new Vector3(
                                -size.x * .5f + (column + .5f) * size.x / columns, 0,
                                -size.z * .5f + (row + .5f) * size.z / rows);
                            CreateCube("Ceiling Lamp " + section + "/" + row + "/" + column,
                                position, new Vector3(1.8f, .12f, .48f), Color.white,
                                false, assetRole: DungeonAssetRole.Light);
                        }
                }
                if (train)
                    foreach (SubwayFixtureLight fixture in fixtureRoot.GetComponentsInChildren<SubwayFixtureLight>())
                    {
                        fixture.Source.intensity *= 2f;
                        fixture.Source.range = Mathf.Max(fixture.Source.range,
                            ToUnity(room.Bounds.Maximum.Y - room.Bounds.Minimum.Y) + 8f);
                    }
            }
            finally { buildParent = previous; }
        }

        private void BuildModernSubwayLighting(DungeonRoom room, float floorY, float height)
        {
            if (room.ModuleKind != DungeonModuleKind.Entrance
                && room.ModuleKind != DungeonModuleKind.Restroom
                && room.ModuleKind != DungeonModuleKind.TrackJunction) return;
            Color light = room.ModuleKind == DungeonModuleKind.TrackJunction
                ? new Color(0.68f, 0.78f, 0.82f)
                : new Color(0.88f, 0.95f, 1.0f);
            IReadOnlyList<WorldBounds> sections = DungeonRoomGeometry.FloorSections(room);
            for (int i = 0; i < sections.Count; i++)
            {
                Vector3 center = BoundsCenter(sections[i]);
                Vector3 size = BoundsSize(sections[i]);
                center.y = floorY + height - 0.29f;
                CreateCube("Modern Subway Room " + room.Index + " Light " + i,
                    center, size.x >= size.z
                        ? new Vector3(Mathf.Min(5f, size.x * 0.42f), 0.08f, 0.26f)
                        : new Vector3(0.26f, 0.08f, Mathf.Min(5f, size.z * 0.42f)),
                    light, false, assetRole: DungeonAssetRole.Light);
            }
        }

        private void BuildModernSubwayOpenPortalFrames(DungeonRoom room,
            IReadOnlyList<DungeonDoorPortal> portals, float floorY,
            Color darkMetal, Color panelMetal)
        {
            for (int i = 0; i < portals.Count; i++)
            {
                DungeonDoorPortal portal = portals[i];
                if (portal.RoomId != room.Id) continue;
                bool xWall = portal.Facing == DungeonPortalFacing.NegativeX
                    || portal.Facing == DungeonPortalFacing.PositiveX;
                float width = ToUnity(xWall
                    ? portal.ClosedBlockingBounds.Maximum.Z
                        - portal.ClosedBlockingBounds.Minimum.Z
                    : portal.ClosedBlockingBounds.Maximum.X
                        - portal.ClosedBlockingBounds.Minimum.X);
                Vector3 center = ToUnity(portal.Center);
                center.y = floorY;
                const float frameHeight = 3.25f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 post = xWall
                        ? new Vector3(center.x, floorY + frameHeight * 0.5f,
                            center.z + side * width * 0.5f)
                        : new Vector3(center.x + side * width * 0.5f,
                            floorY + frameHeight * 0.5f, center.z);
                    CreateCube("Modern Subway Entrance Frame Post " + i + "/" + side,
                        post, xWall ? new Vector3(0.20f, frameHeight, 0.24f)
                            : new Vector3(0.24f, frameHeight, 0.20f), darkMetal, false);
                }
                Vector3 header = center + Vector3.up * frameHeight;
                CreateCube("Modern Subway Entrance Frame Header " + i, header,
                    xWall ? new Vector3(0.22f, 0.30f, width + 0.48f)
                        : new Vector3(width + 0.48f, 0.30f, 0.22f), panelMetal, false);
            }
        }

        private void BuildSubwayEntranceDoor(DungeonRoom room,
            IReadOnlyList<DungeonDoorPortal> portals, float floorY, float height,
            Color doorColor, Color warning)
        {
            DungeonDoorPortal interior = null;
            for (int i = 0; i < portals.Count; i++)
                if (portals[i].RoomId == room.Id) { interior = portals[i]; break; }
            // The subway entrance is an authored L. Its route leaves through the
            // centered +X end cap and the public entrance sits at the other leg's
            // centered +Z end cap; it is not the opposite wall of a rectangle.
            DungeonPortalFacing exterior = DungeonPortalFacing.PositiveZ;
            float x = ToUnity(room.Center.X);
            float z = ToUnity(room.Center.Z);
            float edgeX = exterior == DungeonPortalFacing.NegativeX
                ? ToUnity(room.Bounds.Minimum.X) + 0.08f
                : ToUnity(room.Bounds.Maximum.X) - 0.08f;
            float edgeZ = exterior == DungeonPortalFacing.NegativeZ
                ? ToUnity(room.Bounds.Minimum.Z) + 0.08f
                : ToUnity(room.Bounds.Maximum.Z) - 0.08f;
            bool xWall = exterior == DungeonPortalFacing.NegativeX
                || exterior == DungeonPortalFacing.PositiveX;
            Vector3 scale = xWall ? new Vector3(0.16f, 3.5f, 6.4f)
                : new Vector3(6.4f, 3.5f, 0.16f);
            Vector3 position = xWall
                ? new Vector3(edgeX, floorY + 1.75f, z)
                : new Vector3(x, floorY + 1.75f, edgeZ);
            bool metalEntrance = subwayDungeonKit != null && subwayDungeonKit.Place("door", "Subway Entrance Locked Metal Door",
                buildParent, position, new Vector3(6.4f, 3.5f, .16f), Quaternion.Euler(0, xWall ? 90 : 0, 0));
            if (metalEntrance) subwayDungeonKit.PlaceMetalDoorFrame("Subway Entrance Metal Frame", buildParent,
                position - Vector3.up * 1.75f, 6.4f, 3.5f, Quaternion.Euler(0, xWall ? 90 : 0, 0));
            else CreateCube("Subway Entrance Locked Sliding Door", position, scale, doorColor, false);
            Vector3 seamScale = xWall ? new Vector3(0.18f, 3.0f, 0.06f)
                : new Vector3(0.06f, 3.0f, 0.18f);
            if (!metalEntrance) CreateCube("Subway Entrance Door Seam", position, seamScale, warning, false);
            CreateCube("Subway Entrance Locked Indicator",
                position + Vector3.up * 0.8f + (xWall ? Vector3.forward : Vector3.right) * 1.7f,
                new Vector3(0.18f, 0.18f, 0.18f), new Color(0.85f, 0.08f, 0.06f), false);
        }

        private void BuildSubwayTrackJunction(DungeonRoom room,
            IReadOnlyList<DungeonDoorPortal> portals, float floorY, Color bed,
            Color rail, Color sleeper, Color safety)
        {
            DungeonDoorPortal incoming = null;
            for (int i = 0; i < portals.Count; i++)
                if (portals[i].RoomId == room.Id) { incoming = portals[i]; break; }
            DungeonPortalFacing facing = incoming == null
                ? DungeonPortalFacing.NegativeX : incoming.Facing;
            bool alongX = facing == DungeonPortalFacing.NegativeX
                || facing == DungeonPortalFacing.PositiveX;
            float cx = ToUnity(room.Center.X), cz = ToUnity(room.Center.Z);
            float width = ToUnity(room.Size.X), depth = ToUnity(room.Size.Z);
            CreateCube("Subway Junction Track Bed", new Vector3(cx, floorY + 0.02f, cz),
                new Vector3(width - 0.2f, 0.06f, depth - 0.2f), bed, false);

            float length = alongX ? width : depth;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = alongX ? new Vector3(cx, floorY + 0.08f, cz + side * 0.9f)
                    : new Vector3(cx + side * 0.9f, floorY + 0.08f, cz);
                Vector3 s = alongX ? new Vector3(length, 0.07f, 0.075f)
                    : new Vector3(0.075f, 0.07f, length);
                CreateCube("Subway Junction Straight Rail " + side, p, s, rail, false, assetRole: DungeonAssetRole.Rail);
            }
            int turn = (room.Index & 1) == 0 ? 1 : -1;
            for (int i = 0; i < portals.Count; i++)
            {
                DungeonDoorPortal outlet = portals[i];
                if (outlet.RoomId != room.Id) continue;
                bool outletAlongX = outlet.Facing == DungeonPortalFacing.NegativeX
                    || outlet.Facing == DungeonPortalFacing.PositiveX;
                if (outletAlongX == alongX) continue;
                turn = outlet.Facing == DungeonPortalFacing.PositiveX
                    || outlet.Facing == DungeonPortalFacing.PositiveZ ? 1 : -1;
                break;
            }
            int forward = facing == DungeonPortalFacing.NegativeX
                || facing == DungeonPortalFacing.NegativeZ ? 1 : -1;
            const int pieces = 9;
            for (int piece = 0; piece < pieces; piece++)
            {
                float t = (piece + 0.5f) / pieces;
                float forwardOffset = Mathf.Lerp(-length * 0.48f * forward, 0f, t);
                float sideOffset = turn * t * t * (alongX ? depth : width) * 0.46f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = alongX
                        ? new Vector3(cx + forwardOffset, floorY + 0.09f,
                            cz + sideOffset + side * 0.9f)
                        : new Vector3(cx + sideOffset + side * 0.9f,
                            floorY + 0.09f, cz + forwardOffset);
                    Vector3 s = alongX
                        ? new Vector3(length / pieces + 0.18f, 0.065f, 0.075f)
                        : new Vector3(0.075f, 0.065f, length / pieces + 0.18f);
                    CreateCube("Subway Junction Curved Rail " + piece + " " + side,
                        p, s, rail, false, assetRole: DungeonAssetRole.Rail);
                }
            }
            CreateCube("Subway Junction Direction Marker",
                new Vector3(cx, floorY + 0.12f, cz),
                alongX ? new Vector3(2.8f, 0.025f, 0.18f)
                    : new Vector3(0.18f, 0.025f, 2.8f), safety, false);
        }

        private void BuildSubwayRailPortalInfills(DungeonRoom room)
        {
            bool alongX = room.Size.X >= room.Size.Z;
            float trackFloor = ToUnity(DungeonRoomGeometry.SubwayTrackFloorY(room));
            float top = ToUnity(room.Bounds.Maximum.Y) + 0.20f;
            float crossCenter = ToUnity(alongX ? room.Center.Z : room.Center.X);
            const float radius = 2.5f;
            const float springHeight = 2.25f;
            const int arcSegments = 14;
            int portalNumber = 0;
            for (int portalIndex = 0; activeLayout != null
                 && portalIndex < activeLayout.DoorPortals.Count; portalIndex++)
            {
                DungeonDoorPortal portal = activeLayout.DoorPortals[portalIndex];
                if (portal.RoomId != room.Id) continue;
                bool portalAlongX = portal.Facing == DungeonPortalFacing.NegativeX
                    || portal.Facing == DungeonPortalFacing.PositiveX;
                if (portalAlongX != alongX) continue;
                float fixedAxis = ToUnity(alongX ? portal.Center.X : portal.Center.Z);
                int surfaceVertexCount = (arcSegments + 1) * 2;
                var vertices = new Vector3[surfaceVertexCount * 2];
                var triangles = new int[arcSegments * 12];
                for (int arc = 0; arc <= arcSegments; arc++)
                {
                    float angle = Mathf.PI - arc * Mathf.PI / arcSegments;
                    float cross = crossCenter + Mathf.Cos(angle) * (radius + 0.08f);
                    float archY = trackFloor + springHeight + Mathf.Sin(angle) * radius;
                    vertices[arc * 2] = alongX
                        ? new Vector3(fixedAxis, archY, cross)
                        : new Vector3(cross, archY, fixedAxis);
                    vertices[arc * 2 + 1] = alongX
                        ? new Vector3(fixedAxis, top, cross)
                        : new Vector3(cross, top, fixedAxis);
                    vertices[surfaceVertexCount + arc * 2] = vertices[arc * 2];
                    vertices[surfaceVertexCount + arc * 2 + 1] = vertices[arc * 2 + 1];
                    if (arc == arcSegments) continue;
                    int vertex = arc * 2;
                    int triangle = arc * 12;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = vertex + 1;
                    triangles[triangle + 2] = vertex + 2;
                    triangles[triangle + 3] = vertex + 2;
                    triangles[triangle + 4] = vertex + 1;
                    triangles[triangle + 5] = vertex + 3;
                    int back = surfaceVertexCount + vertex;
                    triangles[triangle + 6] = back + 2;
                    triangles[triangle + 7] = back + 1;
                    triangles[triangle + 8] = back;
                    triangles[triangle + 9] = back + 3;
                    triangles[triangle + 10] = back + 1;
                    triangles[triangle + 11] = back + 2;
                }
                var mesh = new Mesh
                {
                    name = "Subway Rail Portal Infill " + room.Index + "/" + portalNumber,
                    vertices = vertices,
                    triangles = triangles
                };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                activeAuthoredMeshes.Add(mesh);
                var infill = new GameObject(mesh.name);
                infill.transform.SetParent(buildParent, false);
                infill.AddComponent<MeshFilter>().sharedMesh = mesh;
                Renderer renderer = infill.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = EnsureProceduralSurfaceMaterial();
                SetRendererColor(renderer, RoomWallColor(room));
                portalNumber++;
            }
        }

        private void BuildSubwayTrackRoom(int roomIndex, float centerX, float centerZ,
            float width, float depth, float height, float platformY, float trackFloorY,
            Color trackBed, Color rail, Color concrete, Color routeColor, Color safety)
        {
            bool alongX = width >= depth;
            float length = alongX ? width : depth;
            float cross = alongX ? depth : width;
            Vector3 bedScale = alongX
                ? new Vector3(width - 0.30f, 0.035f, 5f)
                : new Vector3(5f, 0.035f, depth - 0.30f);
            CreateCube("Subway Track Room " + roomIndex + " Bed",
                new Vector3(centerX, trackFloorY + 0.012f, centerZ), bedScale, trackBed, false);

            const float railOffset = 0.90f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 railPosition = alongX
                    ? new Vector3(centerX, trackFloorY + 0.075f, centerZ + side * railOffset)
                    : new Vector3(centerX + side * railOffset, trackFloorY + 0.075f, centerZ);
                Vector3 railScale = alongX
                    ? new Vector3(width - 0.18f, 0.07f, 0.075f)
                    : new Vector3(0.075f, 0.07f, depth - 0.18f);
                CreateCube("Subway Track Room " + roomIndex + " Rail " + side,
                    railPosition, railScale, rail, false, assetRole: DungeonAssetRole.Rail);
            }

            int sleeperCount = Mathf.Clamp(Mathf.FloorToInt(length / 1.15f), 8, 28);
            for (int i = 0; i < sleeperCount; i++)
            {
                float offset = Mathf.Lerp(-length * 0.47f, length * 0.47f,
                    sleeperCount == 1 ? 0.5f : i / (float)(sleeperCount - 1));
                Vector3 position = alongX
                    ? new Vector3(centerX + offset, trackFloorY + 0.035f, centerZ)
                    : new Vector3(centerX, trackFloorY + 0.035f, centerZ + offset);
                Vector3 scale = alongX
                    ? new Vector3(0.18f, 0.045f, 4.8f)
                    : new Vector3(4.8f, 0.045f, 0.18f);
                CreateCube("Subway Track Room " + roomIndex + " Sleeper " + i,
                    position, scale, concrete, false);
            }

            int ribCount = Mathf.Clamp(Mathf.CeilToInt(length / 3.5f), 3, 10);
            for (int i = 0; i < ribCount; i++)
            {
                float offset = Mathf.Lerp(-length * 0.43f, length * 0.43f,
                    ribCount == 1 ? 0.5f : i / (float)(ribCount - 1));
                Vector3 position = alongX
                    ? new Vector3(centerX + offset, platformY + height - 0.24f, centerZ)
                    : new Vector3(centerX, platformY + height - 0.24f, centerZ + offset);
                Vector3 scale = alongX
                    ? new Vector3(0.20f, 0.22f, cross - 0.18f)
                    : new Vector3(cross - 0.18f, 0.22f, 0.20f);
                CreateCube("Subway Track Room " + roomIndex + " Roof Rib " + i,
                    position, scale, concrete, false);
                if ((i & 1) == 0)
                    CreateCube("Subway Track Room " + roomIndex + " Light " + i,
                        position + Vector3.down * 0.16f,
                        alongX ? new Vector3(0.32f, 0.08f, 0.85f)
                            : new Vector3(0.85f, 0.08f, 0.32f),
                        new Color(0.86f, 0.92f, 0.88f), false, assetRole: DungeonAssetRole.Light);
            }

            // Low route stripes keep the visual line readable without spanning a
            // lateral doorway or adding collision across a service-room opening.
            const float tunnelStairHalfWidth = 1.7f;
            float stripeSegmentLength = Mathf.Max(0.2f,
                (length - tunnelStairHalfWidth * 2f) * 0.5f);
            Vector3 routeScale = alongX
                ? new Vector3(stripeSegmentLength, 0.025f, 0.10f)
                : new Vector3(0.10f, 0.025f, stripeSegmentLength);
            for (int side = -1; side <= 1; side += 2)
            {
                for (int segment = -1; segment <= 1; segment += 2)
                {
                    float alongOffset = segment
                        * (tunnelStairHalfWidth + stripeSegmentLength * 0.5f);
                    Vector3 position = alongX
                        ? new Vector3(centerX + alongOffset, platformY + 0.026f,
                            centerZ + side * cross * 0.34f)
                        : new Vector3(centerX + side * cross * 0.34f,
                            platformY + 0.026f, centerZ + alongOffset);
                    CreateCube("Subway Track Room " + roomIndex + " Route Stripe "
                        + side + "/" + segment, position, routeScale, routeColor, false);
                    Vector3 safetyPosition = alongX
                        ? new Vector3(centerX + alongOffset, platformY + 0.028f,
                            centerZ + side * 2.65f)
                        : new Vector3(centerX + side * 2.65f,
                            platformY + 0.028f, centerZ + alongOffset);
                    CreateCube("Subway Track Room " + roomIndex + " Platform Edge "
                        + side + "/" + segment,
                        safetyPosition, routeScale, safety, false);
                }
            }
        }

        private void BuildSubwayServiceRoom(int roomIndex, float centerX, float centerZ,
            float width, float depth, float height, float floorY, Color concrete, Color safety)
        {
            Color equipment = new Color(0.15f, 0.20f, 0.20f);
            for (int side = -1; side <= 1; side += 2)
            {
                CreateCube("Subway Service " + roomIndex + " Equipment Bank " + side,
                    new Vector3(centerX + side * (width * 0.34f), floorY + 0.75f, centerZ),
                    new Vector3(Mathf.Min(1.15f, width * 0.18f), 1.5f,
                        Mathf.Max(1.2f, depth * 0.48f)), equipment, false);
                CreateCube("Subway Service " + roomIndex + " Hazard Stripe " + side,
                    new Vector3(centerX + side * (width * 0.23f), floorY + 0.035f, centerZ),
                    new Vector3(0.13f, 0.025f, Mathf.Max(1.2f, depth * 0.70f)), safety, false);
            }
            for (int pipe = -1; pipe <= 1; pipe++)
                CreateCube("Subway Service " + roomIndex + " Ceiling Conduit " + pipe,
                    new Vector3(centerX + pipe * 0.32f, floorY + height - 0.22f, centerZ),
                    new Vector3(0.12f, 0.12f, Mathf.Max(1f, depth - 0.7f)),
                    pipe == 0 ? safety : concrete, false);
        }

        private void BuildSubwayConcourse(int roomIndex, float centerX, float centerZ,
            float width, float depth, float height, float floorY,
            Color concrete, Color sign, Color safety)
        {
            CreateCube("Subway Concourse " + roomIndex + " Overhead Route Board",
                new Vector3(centerX, floorY + height - 0.72f, centerZ),
                new Vector3(Mathf.Min(4.2f, width * 0.55f), 0.65f, 0.12f), sign, false);
            CreateCube("Subway Concourse " + roomIndex + " Ceiling Light A",
                new Vector3(centerX, floorY + height - 0.24f, centerZ - depth * 0.24f),
                new Vector3(Mathf.Max(1f, width - 1.1f), 0.09f, 0.20f), Color.white, false, assetRole: DungeonAssetRole.Light);
            CreateCube("Subway Concourse " + roomIndex + " Ceiling Light B",
                new Vector3(centerX, floorY + height - 0.24f, centerZ + depth * 0.24f),
                new Vector3(Mathf.Max(1f, width - 1.1f), 0.09f, 0.20f), Color.white, false, assetRole: DungeonAssetRole.Light);
        }

        private void BuildSubwayGrandHall(int roomIndex, float centerX, float centerZ,
            float width, float depth, float height, float floorY,
            Color concrete, Color sign)
        {
            // Intentionally unobstructed: this is a flat station-scale encounter room.
            for (int row = -1; row <= 1; row += 2)
                for (int light = -2; light <= 2; light++)
                    CreateCube("Subway Grand Hall " + roomIndex + " Light " + row + "/" + light,
                        new Vector3(centerX + light * width * 0.17f,
                            floorY + height - 0.22f, centerZ + row * depth * 0.25f),
                        new Vector3(2.8f, 0.10f, 0.28f), Color.white, false, assetRole: DungeonAssetRole.Light);
            CreateCube("Subway Grand Hall " + roomIndex + " Route Board",
                new Vector3(centerX, floorY + height - 0.75f, centerZ),
                new Vector3(6.5f, 0.70f, 0.14f), sign, false);
        }

        private void BuildSubwayRestroom(DungeonRoom room,
            IReadOnlyList<DungeonDoorPortal> portals, float centerX, float centerZ,
            float width, float depth, float height, float floorY,
            Color concrete, Color sign)
        {
            Color partition = new Color(0.30f, 0.34f, 0.35f);
            Color porcelain = new Color(0.82f, 0.84f, 0.80f);
            bool negativeX = false, positiveX = false, negativeZ = false, positiveZ = false;
            DungeonPortalFacing primary = DungeonPortalFacing.NegativeX;
            bool foundPrimary = false;
            for (int i = 0; i < portals.Count; i++)
            {
                if (portals[i].RoomId != room.Id) continue;
                if (!foundPrimary) { primary = portals[i].Facing; foundPrimary = true; }
                switch (portals[i].Facing)
                {
                    case DungeonPortalFacing.NegativeX: negativeX = true; break;
                    case DungeonPortalFacing.PositiveX: positiveX = true; break;
                    case DungeonPortalFacing.NegativeZ: negativeZ = true; break;
                    case DungeonPortalFacing.PositiveZ: positiveZ = true; break;
                }
            }
            // Prefer a doorway-free wall perpendicular to the first entrance so
            // the stall doors and toilets never sit on its direct sight/travel line.
            bool primaryAlongX = primary == DungeonPortalFacing.NegativeX
                || primary == DungeonPortalFacing.PositiveX;
            DungeonPortalFacing fixtureWall;
            if (primaryAlongX && !positiveZ) fixtureWall = DungeonPortalFacing.PositiveZ;
            else if (primaryAlongX && !negativeZ) fixtureWall = DungeonPortalFacing.NegativeZ;
            else if (!primaryAlongX && !positiveX) fixtureWall = DungeonPortalFacing.PositiveX;
            else if (!primaryAlongX && !negativeX) fixtureWall = DungeonPortalFacing.NegativeX;
            else if (!positiveZ) fixtureWall = DungeonPortalFacing.PositiveZ;
            else if (!negativeZ) fixtureWall = DungeonPortalFacing.NegativeZ;
            else if (!positiveX) fixtureWall = DungeonPortalFacing.PositiveX;
            else fixtureWall = DungeonPortalFacing.NegativeX;

            bool wallAlongX = fixtureWall == DungeonPortalFacing.PositiveZ
                || fixtureWall == DungeonPortalFacing.NegativeZ;
            float wallSign = fixtureWall == DungeonPortalFacing.PositiveX
                || fixtureWall == DungeonPortalFacing.PositiveZ ? 1f : -1f;
            float wallAxis = wallAlongX
                ? centerZ + wallSign * depth * 0.5f
                : centerX + wallSign * width * 0.5f;
            float stallFront = wallAlongX
                ? centerZ + wallSign * depth * 0.10f
                : centerX + wallSign * width * 0.10f;
            float dividerDepth = Mathf.Abs(wallAxis - stallFront);
            float dividerCenter = (wallAxis + stallFront) * 0.5f;
            for (int stall = -2; stall <= 2; stall++)
            {
                float lateral = stall * 1.45f;
                Vector3 dividerPosition = wallAlongX
                    ? new Vector3(centerX + lateral - 0.70f, floorY + 1.15f,
                        dividerCenter)
                    : new Vector3(dividerCenter, floorY + 1.15f,
                        centerZ + lateral - 0.70f);
                Vector3 dividerScale = wallAlongX
                    ? new Vector3(0.08f, 2.30f, dividerDepth)
                    : new Vector3(dividerDepth, 2.30f, 0.08f);
                Vector3 toiletPosition = wallAlongX
                    ? new Vector3(centerX + lateral, floorY + 0.30f,
                        wallAxis - wallSign * 0.72f)
                    : new Vector3(wallAxis - wallSign * 0.72f, floorY + 0.30f,
                        centerZ + lateral);
                CreateCube("Subway Restroom " + room.Index + " Stall Divider " + stall,
                    dividerPosition, dividerScale, partition, false);

                PlaceSubwayBathroomProp("toilet", "Subway Restroom " + room.Index + " Toilet " + stall,
                    toiletPosition + Vector3.up * .12f, new Vector3(.62f, .84f, .85f),
                    wallAlongX ? new Vector3(0, 0, -wallSign) : new Vector3(-wallSign, 0, 0), porcelain);

            }
            // A continuous rear infill closes the stall bank against the room
            // shell, even when the visual wall thickness leaves a small tolerance.
            CreateCube("Subway Restroom " + room.Index + " Stall Rear Infill",
                wallAlongX
                    ? new Vector3(centerX, floorY + 1.15f,
                        wallAxis - wallSign * 0.06f)
                    : new Vector3(wallAxis - wallSign * 0.06f,
                        floorY + 1.15f, centerZ),
                wallAlongX ? new Vector3(7.4f, 2.30f, 0.14f)
                    : new Vector3(0.14f, 2.30f, 7.4f), partition, false);
            for (int sink = -1; sink <= 1; sink++)
                PlaceSubwayBathroomProp("sink", "Subway Restroom " + room.Index + " Sink " + sink,
                    wallAlongX
                        ? new Vector3(centerX + sink * 1.8f, floorY + .90f, centerZ - wallSign * (depth * .5f - .38f))
                        : new Vector3(centerX - wallSign * (width * .5f - .38f), floorY + .90f, centerZ + sink * 1.8f),
                    new Vector3(1.15f, .72f, .62f),
                    wallAlongX ? new Vector3(0, 0, wallSign) : new Vector3(wallSign, 0, 0), porcelain);
            CreateCube("Subway Restroom " + room.Index + " Sign",
                new Vector3(centerX, floorY + height - 0.55f, centerZ),
                new Vector3(2.2f, 0.55f, 0.08f), sign, false);
        }

        private void PlaceSubwayBathroomProp(string role, string label, Vector3 center, Vector3 size, Vector3 forward, Color fallback)
        {
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            if (subwayDungeonKit != null && subwayDungeonKit.Place(role, label, buildParent, center, size, rotation)) return;
            CreateCube(label, center, Mathf.Abs(forward.x) > .5f ? new Vector3(size.z, size.y, size.x) : size, fallback, false);
        }

        private void BuildStoppedSubwayTrain(DungeonRoom room)
        {
            bool alongX = room.Size.X >= room.Size.Z;
            Color bodyColor = new Color(0.58f, 0.61f, 0.59f);
            Color glass = new Color(0.07f, 0.18f, 0.24f);
            for (int car = 0; car < 3; car++)
            {
                WorldBounds bounds = DungeonRoomGeometry.SubwayTrainCarBounds(room, car);
                Vector3 center = BoundsCenter(bounds);
                Vector3 size = BoundsSize(bounds);
                if (subwayDungeonKit != null && subwayDungeonKit.Place("train",
                    "Subway Train Car " + car, buildParent,
                    new Vector3(center.x, ToUnity(DungeonRoomGeometry.SubwayTrackFloorY(room)) + .11f + size.y * .5f, center.z),
                    alongX ? new Vector3(size.z, size.y, size.x) : size,
                    Quaternion.Euler(0, alongX ? 90 : 0, 0))) continue;
                CreateCube("Subway Room " + room.Index + " Train Car " + car,
                    center, size, bodyColor);
                float trackFloor = ToUnity(DungeonRoomGeometry.SubwayTrackFloorY(room));
                Vector3 undercarriageScale = alongX
                    ? new Vector3(size.x * 0.72f, 0.22f, 1.75f)
                    : new Vector3(1.75f, 0.22f, size.z * 0.72f);
                CreateCube("Subway Room " + room.Index + " Car Undercarriage " + car,
                    new Vector3(center.x, trackFloor + 0.54f, center.z),
                    undercarriageScale, new Color(0.10f, 0.11f, 0.11f), false);
                for (int bogie = -1; bogie <= 1; bogie += 2)
                {
                    float alongPosition = (alongX ? center.x : center.z)
                        + bogie * (alongX ? size.x : size.z) * 0.30f;
                    for (int wheelSide = -1; wheelSide <= 1; wheelSide += 2)
                    {
                        float wheelCross = (alongX ? center.z : center.x)
                            + wheelSide * 0.90f;
                        Vector3 wheelStart = alongX
                            ? new Vector3(alongPosition, trackFloor + 0.32f,
                                wheelCross - 0.11f)
                            : new Vector3(wheelCross - 0.11f, trackFloor + 0.32f,
                                alongPosition);
                        Vector3 wheelEnd = alongX
                            ? new Vector3(alongPosition, trackFloor + 0.32f,
                                wheelCross + 0.11f)
                            : new Vector3(wheelCross + 0.11f, trackFloor + 0.32f,
                                alongPosition);
                        CreateCylinderBetween("Subway Room " + room.Index + " Car " + car
                            + " Wheel " + bogie + "/" + wheelSide,
                            wheelStart, wheelEnd, 0.32f,
                            new Color(0.055f, 0.06f, 0.06f));
                    }
                }
                Vector3 roofScale = alongX
                    ? new Vector3(size.x - 0.15f, 0.18f, size.z - 0.12f)
                    : new Vector3(size.x - 0.12f, 0.18f, size.z - 0.15f);
                CreateCube("Subway Room " + room.Index + " Car Roof " + car,
                    center + Vector3.up * (size.y * 0.5f + 0.08f), roofScale,
                    new Color(0.32f, 0.35f, 0.36f), false);
                for (int side = -1; side <= 1; side += 2)
                {
                    float cross = (alongX ? center.z : center.x)
                                  + side * (alongX ? size.z : size.x) * 0.505f;
                    for (int window = -1; window <= 1; window += 2)
                    {
                        float along = (alongX ? center.x : center.z)
                                      + window * (alongX ? size.x : size.z) * 0.28f;
                        Vector3 windowPosition = alongX
                            ? new Vector3(along, center.y + 0.30f, cross)
                            : new Vector3(cross, center.y + 0.30f, along);
                        CreateCube("Subway Room " + room.Index + " Car " + car
                                   + " Window " + side + "/" + window,
                            windowPosition, alongX
                                ? new Vector3(1.35f, 0.65f, 0.035f)
                                : new Vector3(0.035f, 0.65f, 1.35f), glass, false);
                    }
                    Vector3 doorPosition = alongX
                        ? new Vector3(center.x, center.y - 0.18f, cross)
                        : new Vector3(cross, center.y - 0.18f, center.z);
                    CreateCube("Subway Room " + room.Index + " Car " + car
                               + " Door " + side, doorPosition,
                        alongX ? new Vector3(1.05f, 1.75f, 0.04f)
                            : new Vector3(0.04f, 1.75f, 1.05f),
                        new Color(0.30f, 0.36f, 0.39f), false);
                }
            }
        }

        private void BuildSubwayTunnelDetails(DungeonCorridor corridor, int index,
            bool includeTracks = true)
        {
            WorldBounds bounds = corridor.WalkableBounds;
            Vector3 center = BoundsCenter(bounds);
            Vector3 size = BoundsSize(bounds);
            bool alongX = DungeonCorridorGeometry.IsAlongX(activeLayout, corridor);
            float length = alongX ? size.x : size.z;
            float width = alongX ? size.z : size.x;
            float height = size.y;
            Color trackBed = new Color(0.065f, 0.07f, 0.065f);
            Color rail = new Color(0.57f, 0.59f, 0.57f);
            Color rib = new Color(0.20f, 0.22f, 0.21f);
            Color panel = new Color(0.12f, 0.25f, 0.31f);

            if (corridor.ChangesElevation)
            {
                BuildSubwayStairwellDetails(corridor, index, alongX);
                return;
            }
            float floorY = ToUnity(corridor.FromFloorY);

            if (includeTracks)
            {
                Vector3 bedScale = alongX
                    ? new Vector3(length, 0.035f, width * 0.58f)
                    : new Vector3(width * 0.58f, 0.035f, length);
                CreateCube("Subway Tunnel " + index + " Track Bed",
                    new Vector3(center.x, floorY + 0.012f, center.z), bedScale, trackBed, false);
                float railOffset = width * 0.17f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 position = alongX
                        ? new Vector3(center.x, floorY + 0.075f, center.z + side * railOffset)
                        : new Vector3(center.x + side * railOffset, floorY + 0.075f, center.z);
                    Vector3 scale = alongX
                        ? new Vector3(length, 0.07f, 0.07f)
                        : new Vector3(0.07f, 0.07f, length);
                    CreateCube("Subway Tunnel " + index + " Rail " + side,
                        position, scale, rail, false, assetRole: DungeonAssetRole.Rail);
                }
                int sleeperCount = Mathf.Clamp(Mathf.FloorToInt(length / 1.15f), 3, 32);
                for (int sleeper = 0; sleeper < sleeperCount; sleeper++)
                {
                    float offset = Mathf.Lerp(-length * 0.48f, length * 0.48f,
                        sleeperCount == 1 ? 0.5f : sleeper / (float)(sleeperCount - 1));
                    Vector3 position = alongX
                        ? new Vector3(center.x + offset, floorY + 0.035f, center.z)
                        : new Vector3(center.x, floorY + 0.035f, center.z + offset);
                    Vector3 scale = alongX
                        ? new Vector3(0.18f, 0.045f, width * 0.94f)
                        : new Vector3(width * 0.94f, 0.045f, 0.18f);
                    CreateCube("Subway Tunnel " + index + " Sleeper " + sleeper,
                        position, scale, rib, false);
                }
            }

            int ribCount = Mathf.Clamp(Mathf.CeilToInt(length / 2.2f), 2, 8);
            for (int i = 0; i < ribCount; i++)
            {
                float offset = Mathf.Lerp(-length * 0.43f, length * 0.43f,
                    ribCount == 1 ? 0.5f : i / (float)(ribCount - 1));
                Vector3 crossBeamPosition = alongX
                    ? new Vector3(center.x + offset, floorY + height - 0.28f, center.z)
                    : new Vector3(center.x, floorY + height - 0.28f, center.z + offset);
                Vector3 crossBeamScale = alongX
                    ? new Vector3(0.18f, 0.20f, width - 0.12f)
                    : new Vector3(width - 0.12f, 0.20f, 0.18f);
                CreateCube("Subway Tunnel " + index + " Roof Rib " + i,
                    crossBeamPosition, crossBeamScale, rib, false);
                if ((i & 1) == 0)
                    CreateCube("Subway Tunnel " + index + " Light " + i,
                        crossBeamPosition + Vector3.down * 0.16f,
                        alongX ? new Vector3(0.28f, 0.08f, 0.75f)
                            : new Vector3(0.75f, 0.08f, 0.28f),
                        new Color(0.86f, 0.92f, 0.88f), false, assetRole: DungeonAssetRole.Light);
            }

            // Colored lower-wall route panels provide a strong horizontal subway silhouette.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = alongX
                    ? new Vector3(center.x, floorY + 1.05f, center.z + side * (width * 0.5f - 0.14f))
                    : new Vector3(center.x + side * (width * 0.5f - 0.14f), floorY + 1.05f, center.z);
                Vector3 scale = alongX
                    ? new Vector3(length - 0.25f, 0.42f, 0.08f)
                    : new Vector3(0.08f, 0.42f, length - 0.25f);
                CreateCube("Subway Tunnel " + index + " Route Panel " + side,
                    position, scale, panel, false);
            }
        }

        private bool BuildAuthoredSubwayPassage(DungeonCorridor corridor, int index)
        {
            DungeonModuleCatalogEntry entry = prefabModuleProvider?.ResolvePassage(
                activeProfile, activeTheme, corridor.ChangesElevation);
            if (entry == null) return false;
            if (corridor.ChangesElevation)
            {
                BuildCorridorSideWalls(corridor, index);
                Vector3 center = BoundsCenter(corridor.WalkableBounds);
                Vector3 size = BoundsSize(corridor.WalkableBounds);
                CreateCube("Authored Stairwell " + index + " Ceiling",
                    new Vector3(center.x, ToUnity(corridor.WalkableBounds.Maximum.Y), center.z),
                    new Vector3(size.x, floorThickness, size.z), ThemeWallColor());
                BuildSubwayStairwellDetails(corridor, index,
                    DungeonCorridorGeometry.IsAlongX(activeLayout, corridor));
                BuildSubwayStairHandrails(corridor, index);
                BuildSubwayPassageEndFacades(corridor, index);
                return true;
            }

            BuildCurvedSubwayTunnelShell(corridor, index);
            bool railPassage = IsSubwayRailPassage(corridor);
            BuildSubwayTunnelDetails(corridor, index, railPassage);
            if (!railPassage) BuildSubwayPassageEndFacades(corridor, index);
            return true;
        }

        private bool IsSubwayRailPassage(DungeonCorridor corridor)
        {
            DungeonConnection connection = null;
            for (int i = 0; activeLayout != null && i < activeLayout.Connections.Count; i++)
                if (activeLayout.Connections[i].Id == corridor.ConnectionId)
                {
                    connection = activeLayout.Connections[i];
                    break;
                }
            if (connection == null) return false;
            DungeonRoom from = null;
            DungeonRoom to = null;
            for (int i = 0; i < activeLayout.Rooms.Count; i++)
            {
                if (activeLayout.Rooms[i].Id == connection.FromRoomId) from = activeLayout.Rooms[i];
                if (activeLayout.Rooms[i].Id == connection.ToRoomId) to = activeLayout.Rooms[i];
            }
            return from != null && to != null
                && (from.ModuleKind == DungeonModuleKind.TrainChamber
                        && to.ModuleKind == DungeonModuleKind.TrackTunnel
                    || to.ModuleKind == DungeonModuleKind.TrainChamber
                        && from.ModuleKind == DungeonModuleKind.TrackTunnel);
        }

        private void BuildSubwayPassageEndFacades(DungeonCorridor corridor, int index)
        {
            for (int portalIndex = 0; portalIndex < activeLayout.DoorPortals.Count;
                 portalIndex++)
            {
                DungeonDoorPortal portal = activeLayout.DoorPortals[portalIndex];
                if (portal.ConnectionId != corridor.ConnectionId) continue;
                bool xWall = portal.Facing == DungeonPortalFacing.NegativeX
                    || portal.Facing == DungeonPortalFacing.PositiveX;
                WorldBounds blocker = portal.ClosedBlockingBounds;
                WorldBounds hall = corridor.WalkableBounds;
                float floor = ToUnity(portal.Center.Y);
                float doorTop = floor + DoorVisualClearHeight;
                float ceiling = ToUnity(hall.Maximum.Y) + 0.10f;
                float fixedAxis = ToUnity(xWall ? portal.Center.X : portal.Center.Z);
                float hallStart = ToUnity(xWall ? hall.Minimum.Z : hall.Minimum.X) - 0.08f;
                float hallEnd = ToUnity(xWall ? hall.Maximum.Z : hall.Maximum.X) + 0.08f;
                float doorStart = ToUnity(xWall
                    ? blocker.Minimum.Z : blocker.Minimum.X);
                float doorEnd = ToUnity(xWall
                    ? blocker.Maximum.Z : blocker.Maximum.X);
                float headerHeight = ceiling - doorTop;
                if (headerHeight > 0.05f)
                    CreateFacadePart("Header", doorTop, headerHeight, hallStart, hallEnd);
                CreateFacadePart("Left", floor, DoorVisualClearHeight, hallStart, doorStart);
                CreateFacadePart("Right", floor, DoorVisualClearHeight, doorEnd, hallEnd);

                void CreateFacadePart(string label, float baseY, float partHeight,
                    float start, float end)
                {
                    float span = end - start;
                    if (span <= 0.02f || partHeight <= 0.02f) return;
                    Vector3 position = xWall
                        ? new Vector3(fixedAxis, baseY + partHeight * 0.5f,
                            (start + end) * 0.5f)
                        : new Vector3((start + end) * 0.5f,
                            baseY + partHeight * 0.5f, fixedAxis);
                    Vector3 scale = xWall
                        ? new Vector3(wallThickness, partHeight, span)
                        : new Vector3(span, partHeight, wallThickness);
                    CreateCube("Subway Passage " + index + " End " + portalIndex
                        + " " + label, position, scale, ThemeWallColor(), false);
                }
            }
        }

        private void BuildCurvedSubwayTunnelShell(DungeonCorridor corridor, int index)
        {
            WorldBounds bounds = corridor.WalkableBounds;
            bool alongX = DungeonCorridorGeometry.IsAlongX(activeLayout, corridor);
            Vector3 center = BoundsCenter(bounds);
            Vector3 size = BoundsSize(bounds);
            float floorY = ToUnity(corridor.FromFloorY);
            float width = alongX ? size.z : size.x;
            float length = alongX ? size.x : size.z;
            float springHeight = Mathf.Min(2.25f, size.y * 0.52f);
            float horizontalRadius = Mathf.Max(0.8f, width * 0.5f);
            float verticalRadius = Mathf.Max(0.8f, size.y - springHeight - 0.10f);

            // Lower walls are inset from the old box shell and meet a true curved roof.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 wallPosition = alongX
                    ? new Vector3(center.x, floorY + springHeight * 0.5f,
                        center.z + side * width * 0.5f)
                    : new Vector3(center.x + side * width * 0.5f,
                        floorY + springHeight * 0.5f, center.z);
                Vector3 wallScale = alongX
                    ? new Vector3(length, springHeight, wallThickness)
                    : new Vector3(wallThickness, springHeight, length);
                CreateCube("Authored Tunnel " + index + " Lower Wall " + side,
                    wallPosition, wallScale, ThemeWallColor());
            }

            const int arcSegments = 14;
            int surfaceVertexCount = (arcSegments + 1) * 2;
            var vertices = new Vector3[surfaceVertexCount * 2];
            var triangles = new int[arcSegments * 12];
            for (int arc = 0; arc <= arcSegments; arc++)
            {
                float angle = Mathf.PI - arc * Mathf.PI / arcSegments;
                float cross = Mathf.Cos(angle) * horizontalRadius;
                float y = floorY + springHeight + Mathf.Sin(angle) * verticalRadius;
                vertices[arc * 2] = alongX
                    ? new Vector3(center.x - length * 0.5f, y, center.z + cross)
                    : new Vector3(center.x + cross, y, center.z - length * 0.5f);
                vertices[arc * 2 + 1] = alongX
                    ? new Vector3(center.x + length * 0.5f, y, center.z + cross)
                    : new Vector3(center.x + cross, y, center.z + length * 0.5f);
                vertices[surfaceVertexCount + arc * 2] = vertices[arc * 2];
                vertices[surfaceVertexCount + arc * 2 + 1] = vertices[arc * 2 + 1];
                if (arc == arcSegments) continue;
                int vertex = arc * 2;
                int triangle = arc * 12;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex + 2;
                triangles[triangle + 4] = vertex + 1;
                triangles[triangle + 5] = vertex + 3;
                int back = surfaceVertexCount + vertex;
                triangles[triangle + 6] = back + 2;
                triangles[triangle + 7] = back + 1;
                triangles[triangle + 8] = back;
                triangles[triangle + 9] = back + 3;
                triangles[triangle + 10] = back + 1;
                triangles[triangle + 11] = back + 2;
            }
            var mesh = new Mesh { name = "Authored Subway Tunnel Shell " + index };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            activeAuthoredMeshes.Add(mesh);
            var shell = new GameObject("Authored Tunnel " + index + " Curved Shell");
            shell.transform.SetParent(buildParent, false);
            shell.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = shell.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = EnsureProceduralSurfaceMaterial();
            SetRendererColor(renderer, ThemeWallColor());
            if (addColliders) shell.AddComponent<MeshCollider>().sharedMesh = mesh;

            // Long fitted utilities follow the wall instead of appearing as obstacles.
            for (int pipe = 0; pipe < 3; pipe++)
            {
                float pipeY = floorY + 1.15f + pipe * 0.24f;
                float cross = -width * 0.5f + 0.16f + pipe * 0.09f;
                Vector3 start = alongX
                    ? new Vector3(center.x - length * 0.48f, pipeY, center.z + cross)
                    : new Vector3(center.x + cross, pipeY, center.z - length * 0.48f);
                Vector3 end = alongX
                    ? new Vector3(center.x + length * 0.48f, pipeY, center.z + cross)
                    : new Vector3(center.x + cross, pipeY, center.z + length * 0.48f);
                CreateCylinderBetween("Authored Tunnel " + index + " Pipe " + pipe,
                    start, end, 0.045f, pipe == 1
                        ? new Color(0.66f, 0.25f, 0.12f) : new Color(0.30f, 0.33f, 0.32f));
            }
        }

        private void BuildSubwayStairHandrails(DungeonCorridor corridor, int index)
        {
            bool alongX = DungeonCorridorGeometry.IsAlongX(activeLayout, corridor);
            Vector3 size = BoundsSize(corridor.WalkableBounds);
            Vector3 center = BoundsCenter(corridor.WalkableBounds);
            float halfLength = (alongX ? size.x : size.z) * 0.45f;
            float halfWidth = (alongX ? size.z : size.x) * 0.40f;
            bool increasing = alongX
                ? FindConnectionToRoom(corridor).Center.X > FindConnectionFromRoom(corridor).Center.X
                : FindConnectionToRoom(corridor).Center.Z > FindConnectionFromRoom(corridor).Center.Z;
            for (int side = -1; side <= 1; side += 2)
            {
                float fromY = ToUnity(corridor.FromFloorY) + 0.92f;
                float toY = ToUnity(corridor.ToFloorY) + 0.92f;
                Vector3 start = alongX
                    ? new Vector3(center.x + (increasing ? -halfLength : halfLength), fromY,
                        center.z + side * halfWidth)
                    : new Vector3(center.x + side * halfWidth, fromY,
                        center.z + (increasing ? -halfLength : halfLength));
                Vector3 end = alongX
                    ? new Vector3(center.x + (increasing ? halfLength : -halfLength), toY,
                        center.z + side * halfWidth)
                    : new Vector3(center.x + side * halfWidth, toY,
                        center.z + (increasing ? halfLength : -halfLength));
                BuildSubwayHandrail("Authored Stairwell " + index + " Handrail " + side, start, end);
            }
        }

        private DungeonRoom FindConnectionFromRoom(DungeonCorridor corridor) =>
            FindConnectionRoom(corridor, true);

        private DungeonRoom FindConnectionToRoom(DungeonCorridor corridor) =>
            FindConnectionRoom(corridor, false);

        private DungeonRoom FindConnectionRoom(DungeonCorridor corridor, bool from)
        {
            DungeonConnection connection = null;
            for (int i = 0; i < activeLayout.Connections.Count; i++)
                if (activeLayout.Connections[i].Id == corridor.ConnectionId)
                { connection = activeLayout.Connections[i]; break; }
            string roomId = from ? connection.FromRoomId : connection.ToRoomId;
            for (int i = 0; i < activeLayout.Rooms.Count; i++)
                if (activeLayout.Rooms[i].Id == roomId) return activeLayout.Rooms[i];
            throw new InvalidOperationException("Passage references a missing room.");
        }

        private void CreateCylinderBetween(string name, Vector3 start, Vector3 end,
            float radius, Color color)
        {
            Vector3 delta = end - start;
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(buildParent, false);
            cylinder.transform.localPosition = (start + end) * 0.5f;
            cylinder.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            cylinder.transform.localScale = new Vector3(radius, delta.magnitude * 0.5f, radius);
            Renderer renderer = cylinder.GetComponent<Renderer>();
            renderer.sharedMaterial = EnsureProceduralSurfaceMaterial();
            SetRendererColor(renderer, color);
            Collider collider = cylinder.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
        }

        private void BuildSubwayStairwellDetails(DungeonCorridor corridor, int index,
            bool alongX)
        {
            IReadOnlyList<WorldBounds> steps = DungeonCorridorGeometry.FloorSections(
                activeLayout, corridor);
            Color edge = new Color(0.96f, 0.70f, 0.08f);
            for (int i = 0; i < steps.Count; i++)
            {
                WorldBounds step = steps[i];
                Vector3 center = BoundsCenter(step);
                Vector3 size = BoundsSize(step);
                center.y = ToUnity(step.Minimum.Y) + 0.026f;
                Vector3 stripeScale = alongX
                    ? new Vector3(0.07f, 0.025f, Mathf.Max(0.4f, size.z - 0.35f))
                    : new Vector3(Mathf.Max(0.4f, size.x - 0.35f), 0.025f, 0.07f);
                CreateCube("Subway Stairwell " + index + " Step Edge " + i,
                    center, stripeScale, edge, false);
            }
            Vector3 boundsCenter = BoundsCenter(corridor.WalkableBounds);
            Vector3 boundsSize = BoundsSize(corridor.WalkableBounds);
            float top = ToUnity(corridor.WalkableBounds.Maximum.Y) - 0.24f;
            CreateCube("Subway Stairwell " + index + " Overhead Light",
                new Vector3(boundsCenter.x, top, boundsCenter.z),
                alongX ? new Vector3(Mathf.Max(1f, boundsSize.x - 0.5f), 0.09f, 0.20f)
                    : new Vector3(0.20f, 0.09f, Mathf.Max(1f, boundsSize.z - 0.5f)),
                new Color(0.84f, 0.91f, 0.88f), false, assetRole: DungeonAssetRole.Light);
        }

        private Color RoomFloorColor(DungeonRoom room)
        {
            if (activeTheme == DungeonVisualTheme.Subway)
                return new Color(0.12f, 0.145f, 0.16f);
            Color roleColor = room.Role switch
            {
                DungeonRoomRole.Objective => new Color(0.55f, 0.38f, 0.10f),
                DungeonRoomRole.Boss => new Color(0.46f, 0.08f, 0.09f),
                DungeonRoomRole.Treasure => new Color(0.24f, 0.42f, 0.10f),
                _ => ThemeFloorColor()
            };
            return Color.Lerp(ThemeFloorColor(), roleColor, 0.62f);
        }

        private Color RoomWallColor(DungeonRoom room) =>
            activeTheme == DungeonVisualTheme.Subway
                ? new Color(0.70f, 0.73f, 0.75f)
                : Color.Lerp(RoomFloorColor(room), ThemeWallColor(), 0.68f);

        private Color RoomAccentColor(DungeonRoom room) => room.Role switch
        {
            DungeonRoomRole.Objective => new Color(0.95f, 0.61f, 0.12f),
            DungeonRoomRole.Boss => new Color(0.9f, 0.16f, 0.12f),
            DungeonRoomRole.Treasure => new Color(0.48f, 0.78f, 0.18f),
            _ => activeTheme == DungeonVisualTheme.Subway
                ? new Color(0.06f, 0.30f, 0.55f) : ThemeAccentColor()
        };

        private Color ThemeFloorColor() => activeTheme switch
        {
            DungeonVisualTheme.Maintenance => new Color(0.25f, 0.17f, 0.12f),
            DungeonVisualTheme.Alien => new Color(0.15f, 0.20f, 0.17f),
            DungeonVisualTheme.Subway => new Color(0.12f, 0.145f, 0.16f),
            DungeonVisualTheme.Temple => new Color(0.34f, 0.27f, 0.17f),
            DungeonVisualTheme.Keep => new Color(0.22f, 0.24f, 0.27f),
            DungeonVisualTheme.Cavern => new Color(0.24f, 0.19f, 0.14f),
            _ => new Color(0.18f, 0.25f, 0.29f)
        };

        private Color ThemeWallColor() => activeTheme switch
        {
            DungeonVisualTheme.Maintenance => new Color(0.38f, 0.27f, 0.20f),
            DungeonVisualTheme.Alien => new Color(0.25f, 0.34f, 0.29f),
            DungeonVisualTheme.Subway => new Color(0.70f, 0.73f, 0.75f),
            DungeonVisualTheme.Temple => new Color(0.52f, 0.43f, 0.28f),
            DungeonVisualTheme.Keep => new Color(0.35f, 0.38f, 0.42f),
            DungeonVisualTheme.Cavern => new Color(0.32f, 0.27f, 0.21f),
            _ => new Color(0.26f, 0.31f, 0.34f)
        };

        private Color ThemeAccentColor() => activeTheme switch
        {
            DungeonVisualTheme.Maintenance => new Color(0.86f, 0.34f, 0.10f),
            DungeonVisualTheme.Alien => new Color(0.36f, 0.82f, 0.48f),
            DungeonVisualTheme.Subway => new Color(0.06f, 0.30f, 0.55f),
            DungeonVisualTheme.Temple => new Color(0.24f, 0.62f, 0.68f),
            DungeonVisualTheme.Keep => new Color(0.84f, 0.43f, 0.16f),
            DungeonVisualTheme.Cavern => new Color(0.45f, 0.72f, 0.56f),
            _ => new Color(0.24f, 0.48f, 0.62f)
        };

        private void BuildWallTrim(DungeonRoom room, DungeonRoomWallSection section,
            IReadOnlyList<DungeonDoorPortal> portals, Color color)
        {
            DungeonPortalFacing facing = section.Facing;
            bool xWall = facing == DungeonPortalFacing.NegativeX
                         || facing == DungeonPortalFacing.PositiveX;
            float fixedAxis = ToUnity(section.FixedAxis);
            float start = ToUnity(section.Start) + 0.18f;
            float end = ToUnity(section.End) - 0.18f;
            var openings = new List<Vector2>();
            for (int i = 0; section.AcceptsPortals && i < portals.Count; i++)
            {
                DungeonDoorPortal portal = portals[i];
                if (portal.RoomId != room.Id || portal.Facing != facing) continue;
                openings.Add(new Vector2(ToUnity(xWall
                    ? portal.ClosedBlockingBounds.Minimum.Z : portal.ClosedBlockingBounds.Minimum.X),
                    ToUnity(xWall
                        ? portal.ClosedBlockingBounds.Maximum.Z : portal.ClosedBlockingBounds.Maximum.X)));
            }
            openings.Sort((left, right) => left.x.CompareTo(right.x));
            float cursor = start;
            for (int i = 0; i < openings.Count; i++)
            {
                CreateTrimSpan(cursor, Mathf.Min(end, openings[i].x));
                cursor = Mathf.Max(cursor, openings[i].y);
            }
            CreateTrimSpan(cursor, end);

            void CreateTrimSpan(float spanStart, float spanEnd)
            {
                float length = spanEnd - spanStart;
                if (length <= 0.02f) return;
                Vector3 position = xWall
                    ? new Vector3(fixedAxis, ToUnity(room.Bounds.Minimum.Y) + 0.42f,
                        (spanStart + spanEnd) * 0.5f)
                    : new Vector3((spanStart + spanEnd) * 0.5f,
                        ToUnity(room.Bounds.Minimum.Y) + 0.42f, fixedAxis);
                Vector3 scale = xWall
                    ? new Vector3(0.12f, 0.48f, length)
                    : new Vector3(length, 0.48f, 0.12f);
                CreateCube("Room " + room.Index + " Trim " + facing,
                    position, scale, color, false);
            }
        }

        private void BuildSpawn(DungeonSpawnPoint spawn, int index)
        {
            Vector3 position = ToUnity(spawn.Position) + Vector3.up * 0.25f;
            Transform previousBuildParent = buildParent;
            if (roomRoots.TryGetValue(spawn.RoomId, out GameObject roomRoot))
                buildParent = roomRoot.transform;
            CreateCube("Spawn " + index + " " + spawn.Role, position,
                new Vector3(0.5f, 0.5f, 0.5f), new Color(0.3f, 0.9f, 0.35f));
            buildParent = previousBuildParent;
        }

        private void BuildSubwayWallCorners(DungeonRoom room, IReadOnlyList<DungeonRoomWallSection> walls)
        {
            if (subwayDungeonKit == null || subwayDungeonKit.Resolve("wall-corner") == null) return;
            for (int i = 0; i < walls.Count; i++)
                for (int j = i + 1; j < walls.Count; j++)
                {
                    var a = walls[i]; var b = walls[j];
                    bool ax = a.Facing == DungeonPortalFacing.NegativeX || a.Facing == DungeonPortalFacing.PositiveX;
                    bool bx = b.Facing == DungeonPortalFacing.NegativeX || b.Facing == DungeonPortalFacing.PositiveX;
                    if (ax == bx) continue;
                    var x = ax ? a : b; var z = ax ? b : a;
                    if ((z.FixedAxis != x.Start && z.FixedAxis != x.End)
                        || (x.FixedAxis != z.Start && x.FixedAxis != z.End)) continue;
                    Vector3 inward = new Vector3(x.Facing == DungeonPortalFacing.NegativeX ? 1 : -1, 0,
                        z.Facing == DungeonPortalFacing.NegativeZ ? 1 : -1);
                    float height = ToUnity(room.Bounds.Maximum.Y - room.Bounds.Minimum.Y);
                    Vector3 center = new Vector3(ToUnity(x.FixedAxis), ToUnity(room.Bounds.Minimum.Y) + height * .5f,
                        ToUnity(z.FixedAxis)) + inward * .055f;
                    subwayDungeonKit.Place("wall-corner", "Room " + room.Index + " Metal Wall Corner " + i + "/" + j,
                        buildParent, center, new Vector3(.18f, height, .18f),
                        Quaternion.Euler(0, Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg - 45, 0));
                }
        }

        private void BuildSubwayStep(string label, WorldBounds bounds, bool travelAlongX, float bottom, float rise)
        {
            Vector3 size = BoundsSize(bounds);
            Vector3 center = BoundsCenter(bounds);
            float top = ToUnity(bounds.Minimum.Y);
            // A closed solid behind every tread hides the outside world through risers and undersides.
            float supportTop = top - .005f;
            float depth = Mathf.Max(.05f, supportTop - bottom);
            CreateCube(label + " Closed Underfill", new Vector3(center.x, supportTop - depth * .5f, center.z),
                new Vector3(size.x, depth, size.z), new Color(.025f, .025f, .025f), false);
            Vector3 treadSize = travelAlongX ? new Vector3(size.z, rise, size.x) : new Vector3(size.x, rise, size.z);
            if (subwayDungeonKit == null || !subwayDungeonKit.Place("stair", label, buildParent,
                new Vector3(center.x, top - rise * .5f, center.z), treadSize,
                Quaternion.Euler(0, travelAlongX ? 90 : 0, 0)))
                CreateCube(label + " Closed Tread", new Vector3(center.x, top - rise * .5f, center.z),
                    new Vector3(size.x, rise, size.z), ThemeFloorColor(), false);
        }

        private void BuildSubwayHandrail(string label, Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            int count = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 2f));
            Quaternion rotation = Quaternion.FromToRotation(Vector3.right, delta.normalized);
            for (int i = 0; i < count; i++)
            {
                Vector3 center = Vector3.Lerp(start, end, (i + .5f) / count);
                if (subwayDungeonKit == null || !subwayDungeonKit.Place("handrail", label + " Module " + i,
                    buildParent, center, new Vector3(delta.magnitude / count, .16f, .12f), rotation))
                    CreateCylinderBetween(label + " Module " + i, Vector3.Lerp(start, end, i / (float)count),
                        Vector3.Lerp(start, end, (i + 1f) / count), .045f, new Color(.72f, .74f, .70f));
            }
            for (int i = 0; i <= count; i++)
            {
                Vector3 at = Vector3.Lerp(start, end, i / (float)count);
                subwayDungeonKit?.Place("handrail-post", label + " Post " + i, buildParent,
                    at - Vector3.up * .46f, new Vector3(.12f, .92f, .12f), Quaternion.identity);
            }
        }

        private void BuildSubwayPlatformHandrail(DungeonRoom room, WorldBounds cheek, int platform, int side)
        {
            bool alongX = room.Size.X >= room.Size.Z;
            Vector3 center = BoundsCenter(cheek);
            float track = ToUnity(DungeonRoomGeometry.SubwayTrackFloorY(room));
            float high = ToUnity(room.Bounds.Minimum.Y);
            Vector3 low = alongX
                ? new Vector3(center.x, track + .92f, ToUnity(platform == 1 ? cheek.Minimum.Z : cheek.Maximum.Z))
                : new Vector3(ToUnity(platform == 1 ? cheek.Minimum.X : cheek.Maximum.X), track + .92f, center.z);
            Vector3 top = alongX
                ? new Vector3(center.x, high + .92f, ToUnity(platform == 1 ? cheek.Maximum.Z : cheek.Minimum.Z))
                : new Vector3(ToUnity(platform == 1 ? cheek.Maximum.X : cheek.Minimum.X), high + .92f, center.z);
            BuildSubwayHandrail("Room " + room.Index + " Stair Handrail " + platform + "/" + side, low, top);
        }

        private void BuildBox(string name, WorldBounds bounds, float thickness, Color color)
        {
            Vector3 size = BoundsSize(bounds);
            size.y = thickness;
            Vector3 center = BoundsCenter(bounds);
            center.y = ToUnity(bounds.Minimum.Y) - thickness * 0.5f;
            CreateCube(name, center, size, color, assetRole: DungeonAssetRole.Floor);
        }

        private void CreateCube(string name, Vector3 position, Vector3 scale, Color color,
            bool collidable = true, DungeonAssetRole assetRole = DungeonAssetRole.Unspecified,
            DungeonPortalFacing? wallFacing = null)
        {
            if (activeTheme == DungeonVisualTheme.Subway && subwayDungeonKit != null
                && SubwaySurfaceRenderer.TryPlace(subwayDungeonKit, assetRole, name,
                    buildParent != null ? buildParent : generatedRoot, position, scale, wallFacing,
                    activeAuthoredMeshes, (label, center, size, parent) =>
                        CreateCubeObject(label, center, size, Color.black, false, parent))) return;
            CreateCubeObject(name, position, scale, color, collidable,
                buildParent != null ? buildParent : generatedRoot);
        }

        private void UpdateRoomVisibility()
        {
            if (activeLayout == null || generatedRoot == null || serverSession == null)
                return;
            Transform player = serverSession.ControlledPlayerTransform;
            if (player == null) return;

            Vector3 local = generatedRoot.InverseTransformPoint(player.position);
            var logicalPosition = new WorldVector3(
                Mathf.RoundToInt(local.x * WorldUnitsPerUnityUnit),
                Mathf.RoundToInt(local.y * WorldUnitsPerUnityUnit),
                Mathf.RoundToInt(local.z * WorldUnitsPerUnityUnit));
            DungeonLocation containing = DungeonVisibilityResolver.FindContainingLocation(
                activeLayout, logicalPosition);
            if (containing != null && (currentLocation == null
                || currentLocation.Kind != containing.Kind
                || !string.Equals(currentLocation.Id, containing.Id, StringComparison.Ordinal)))
            {
                currentLocation = containing;
                visibilityDirty = true;
                if (containing.Kind == DungeonLocationKind.Room)
                    for (int i = 0; i < activeLayout.Rooms.Count; i++)
                    {
                        DungeonRoom room = activeLayout.Rooms[i];
                        if (room.Id != containing.Id) continue;
                        AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                            $"Dungeon area: room {room.Index}, module={room.ModuleKind}, "
                            + $"elevation={ToUnity(room.Bounds.Minimum.Y):0.##}m.");
                        break;
                    }
            }
            if (!visibilityDirty || currentLocation == null) return;

            DungeonVisibilitySet visibility = DungeonVisibilityResolver.Resolve(
                activeLayout, currentLocation, serverSession.ProceduralDoorStates);
            var visibleRooms = new HashSet<string>(visibility.VisibleRoomIds, StringComparer.Ordinal);
            var visibleConnections = new HashSet<string>(
                visibility.VisibleConnectionIds, StringComparer.Ordinal);
            foreach (KeyValuePair<string, GameObject> pair in roomRoots)
                SetPresentationVisible(pair.Value, visibleRooms.Contains(pair.Key));
            foreach (KeyValuePair<string, GameObject> pair in corridorRoots)
                SetPresentationVisible(pair.Value, visibleConnections.Contains(pair.Key));
            foreach (List<DoorLeafPresentation> leaves in doorLeaves.Values)
                for (int i = 0; i < leaves.Count; i++)
                    if (leaves[i].Root != null)
                        SetPresentationVisible(leaves[i].Root,
                            visibleRooms.Contains(leaves[i].RoomId)
                            || visibleConnections.Contains(leaves[i].ConnectionId));
            visibilityDirty = false;
        }

        private static void SetPresentationVisible(GameObject root, bool visible)
        {
            if (root == null) return;
            // Collision is authoritative traversal data and must never disappear with
            // presentation culling. Disabling an entire room for one frame can put the
            // CharacterController below its floor before the next area is revealed.
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = visible;
            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++) lights[i].enabled = visible;
        }

        private GameObject CreateCubeObject(string name, Vector3 position, Vector3 scale,
            Color color, bool collidable, Transform parent)
        {
            EnsurePoolRoot();
            GameObject cube = null;
            while (cubePool.Count > 0 && cube == null) cube = cubePool.Pop();
            if (cube == null) cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = scale;
            cube.SetActive(true);
            Renderer renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
                renderer.sharedMaterial = EnsureProceduralSurfaceMaterial();
            }
            SetRendererColor(renderer, color);
            BoxCollider collider = cube.GetComponent<BoxCollider>();
            if (collider == null) collider = cube.AddComponent<BoxCollider>();
            collider.isTrigger = false;
            collider.enabled = addColliders && collidable
                && !suppressVisualCollidersForCoreBake;
            activeCubes.Add(cube);
            return cube;
        }

        private void EnsurePoolRoot()
        {
            if (poolRoot != null) return;
            poolRoot = new GameObject("Procedural Presentation Pool").transform;
            poolRoot.SetParent(transform, false);
            poolRoot.gameObject.SetActive(false);
        }

        private void ReleaseCube(GameObject cube)
        {
            if (cube == null) return;
            EnsurePoolRoot();
            Collider collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                collider.isTrigger = false;
            }
            cube.SetActive(false);
            cube.transform.SetParent(poolRoot, false);
            cubePool.Push(cube);
        }

        private static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        private void ConfigureVisualKit(DungeonLayout layout)
        {
            activeProfile = DungeonGenerationProfileCatalog.ReadProfile(layout.Manifest);
            DungeonVisualTheme nextTheme = DungeonGenerationProfileCatalog.ReadTheme(layout.Manifest);
            activeAssetSource = DungeonGenerationProfileCatalog.ReadAssetSource(layout.Manifest);
            prefabModuleProvider = new DungeonPrefabModuleProvider(authoredModuleCatalog);
            worldGenPrefabCatalog = Resources.Load<WorldGenPrefabCatalog>("WorldGen/WorldGenPrefabCatalog");
            subwayDungeonKit = nextTheme == DungeonVisualTheme.Subway
                ? Resources.Load<SubwayDungeonKit>("SubwayKit/SubwayDungeonKit") : null;
            if (nextTheme == DungeonVisualTheme.Subway)
            {
                ApplySubwayEnvironment();
                string error = "Resources/SubwayKit/SubwayDungeonKit is missing";
                if (subwayDungeonKit == null || !subwayDungeonKit.TryValidate(out error))
                {
                    subwayDungeonKit = null;
                    string message = "Subway GLB kit unavailable: " + error
                        + ". Stop Play mode and use Tools/WorldGen/Refresh Subway Kit.";
                    Debug.LogError("[WorldGen] " + message, this);
                    AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(message);
                }
                else Debug.Log("[WorldGen] Subway GLB kit loaded: "
                    + subwayDungeonKit.Entries.Count + " verified roles.", this);
            }
            templeDungeonKit = nextTheme == DungeonVisualTheme.Temple
                ? Resources.Load<TempleDungeonKit>("TempleKit/TempleDungeonKit") : null;
            if (nextTheme == DungeonVisualTheme.Temple && templeDungeonKit == null)
                Debug.LogWarning("TempleDungeonKit was not found in Resources/TempleKit. " +
                    "Use Tools/WorldGen/Refresh Temple Kit before rebuilding.", this);
            if (nextTheme != activeTheme)
            {
                activeTheme = nextTheme;
                if (proceduralSurfaceMaterial != null)
                {
                    if (Application.isPlaying) Destroy(proceduralSurfaceMaterial);
                    else DestroyImmediate(proceduralSurfaceMaterial);
                    proceduralSurfaceMaterial = null;
                }
                if (proceduralSurfaceTexture != null)
                {
                    if (Application.isPlaying) Destroy(proceduralSurfaceTexture);
                    else DestroyImmediate(proceduralSurfaceTexture);
                    proceduralSurfaceTexture = null;
                }
                if (caveGroundMaterial != null)
                {
                    if (Application.isPlaying) Destroy(caveGroundMaterial);
                    else DestroyImmediate(caveGroundMaterial);
                    caveGroundMaterial = null;
                }
                caveGroundTexture = null;
            }
            if (activeAssetSource != DungeonAssetSource.Procedural
                && ProceduralDungeonAssetProviderRegistry.RdbProvider == null)
                AO.Unity.Prototype.PrototypeUiContext.Active?.PublishStatus(
                    "Procedural RDB kit requested; no RDB dungeon provider is registered, using procedural fallback.");
        }

        private bool TryBuildCatalogRoom(DungeonRoom room, Transform parent)
        {
            DungeonModuleCatalogEntry entry = prefabModuleProvider?.ResolveRoom(
                activeProfile, activeTheme, room.ModuleKind);
            if (entry?.Prefab == null) return false;
            return DungeonPrefabModuleProvider.InstantiatePrefab(entry, parent,
                BoundsCenter(room.Bounds), BoundsSize(room.Bounds)) != null;
        }

        private bool TryBuildCatalogPassage(DungeonCorridor corridor, Transform parent)
        {
            DungeonModuleCatalogEntry entry = prefabModuleProvider?.ResolvePassage(
                activeProfile, activeTheme, corridor.ChangesElevation);
            if (entry?.Prefab == null) return false;
            return DungeonPrefabModuleProvider.InstantiatePrefab(entry, parent,
                BoundsCenter(corridor.WalkableBounds), BoundsSize(corridor.WalkableBounds)) != null;
        }

        private bool TryBuildExternalRoom(DungeonLayout layout, DungeonRoom room, Transform parent)
        {
            if (activeAssetSource == DungeonAssetSource.Procedural) return false;
            IProceduralDungeonAssetProvider provider = ProceduralDungeonAssetProviderRegistry.RdbProvider;
            if (provider == null) return false;
            try { return provider.TryBuildRoom(layout, room, parent); }
            catch (Exception exception)
            { Debug.LogWarning("[WorldGen] RDB room provider fallback: " + exception.Message); return false; }
        }

        private bool TryBuildExternalCorridor(DungeonLayout layout,
            DungeonCorridor corridor, Transform parent)
        {
            if (activeAssetSource == DungeonAssetSource.Procedural) return false;
            IProceduralDungeonAssetProvider provider = ProceduralDungeonAssetProviderRegistry.RdbProvider;
            if (provider == null) return false;
            try { return provider.TryBuildCorridor(layout, corridor, parent); }
            catch (Exception exception)
            { Debug.LogWarning("[WorldGen] RDB corridor provider fallback: " + exception.Message); return false; }
        }

        private Material EnsureProceduralSurfaceMaterial()
        {
            if (proceduralSurfaceMaterial != null) return proceduralSurfaceMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Diffuse");
            if (shader == null) return null;
            proceduralSurfaceTexture = BuildProceduralPanelTexture();
            Texture2D surfaceTexture = activeTheme == DungeonVisualTheme.Cavern
                ? Resources.Load<Texture2D>("Procedural/CaveRockAlbedo-v2")
                : null;
            if (surfaceTexture == null) surfaceTexture = proceduralSurfaceTexture;
            proceduralSurfaceMaterial = new Material(shader)
            {
                name = "Procedural Dungeon Panel Material",
                mainTexture = surfaceTexture,
                mainTextureScale = new Vector2(2f, 2f)
            };
            if (proceduralSurfaceMaterial.HasProperty("_BaseMap"))
                proceduralSurfaceMaterial.SetTexture("_BaseMap", surfaceTexture);
            if (proceduralSurfaceMaterial.HasProperty("_Smoothness"))
                proceduralSurfaceMaterial.SetFloat("_Smoothness",
                    activeTheme == DungeonVisualTheme.Keep
                    || activeTheme == DungeonVisualTheme.Cavern ? 0.08f
                    : activeTheme == DungeonVisualTheme.Subway ? 0.42f : 0.28f);
            if (proceduralSurfaceMaterial.HasProperty("_Metallic"))
                proceduralSurfaceMaterial.SetFloat("_Metallic",
                    activeTheme == DungeonVisualTheme.Keep
                    || activeTheme == DungeonVisualTheme.Cavern ? 0f
                    : activeTheme == DungeonVisualTheme.Subway ? 0.08f : 0.18f);
            if (activeTheme == DungeonVisualTheme.Cavern
                && proceduralSurfaceMaterial.HasProperty("_Cull"))
                proceduralSurfaceMaterial.SetFloat("_Cull", 0f);
            return proceduralSurfaceMaterial;
        }

        private Material EnsureCaveGroundMaterial()
        {
            if (caveGroundMaterial != null) return caveGroundMaterial;
            Material baseMaterial = EnsureProceduralSurfaceMaterial();
            if (baseMaterial == null) return null;
            caveGroundTexture = Resources.Load<Texture2D>(
                "Procedural/CaveGroundAlbedo-v1");
            if (caveGroundTexture == null) return baseMaterial;
            caveGroundMaterial = new Material(baseMaterial)
            {
                name = "Procedural Cave Ground Material",
                mainTexture = caveGroundTexture,
                mainTextureScale = new Vector2(1.45f, 1.45f)
            };
            if (caveGroundMaterial.HasProperty("_BaseMap"))
                caveGroundMaterial.SetTexture("_BaseMap", caveGroundTexture);
            if (caveGroundMaterial.HasProperty("_Smoothness"))
                caveGroundMaterial.SetFloat("_Smoothness", 0.04f);
            return caveGroundMaterial;
        }

        private Texture2D BuildProceduralPanelTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "Procedural Dungeon Panel Texture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool keepStone = activeTheme == DungeonVisualTheme.Keep;
                bool caveStone = activeTheme == DungeonVisualTheme.Cavern;
                bool modernSubway = activeTheme == DungeonVisualTheme.Subway;
                bool seam = keepStone
                    ? y % 8 == 0 || (x + (y / 8 % 2) * 8) % 16 == 0
                    : modernSubway ? x == 0 || y == 0 || x == size - 1
                        || y == size - 1 || x == 16
                    : x == 0 || y == 0 || x == size - 1 || y == size - 1;
                bool inset = !keepStone
                    && (x == 2 || y == 2 || x == size - 3 || y == size - 3);
                byte value = caveStone
                    ? (byte)(104 + ((x * 31 + y * 17 + (x * y) * 7) & 39))
                    : keepStone
                    ? seam ? (byte)65 : (byte)(132 + ((x * 11 + y * 17) & 15))
                    : modernSubway ? seam ? (byte)92 : inset ? (byte)155
                        : (byte)(202 + ((x * 13 + y * 7) & 5))
                    : seam ? (byte)72 : inset ? (byte)112
                        : (byte)(142 + ((x * 13 + y * 7) & 7));
                pixels[y * size + x] = new Color32(value, value, value, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static float ToUnity(int value) => value / WorldUnitsPerUnityUnit;
        private static Vector3 ToUnity(WorldVector3 value) =>
            new Vector3(ToUnity(value.X), ToUnity(value.Y), ToUnity(value.Z));
        private static Vector3 BoundsCenter(WorldBounds bounds) =>
            (ToUnity(bounds.Minimum) + ToUnity(bounds.Maximum)) * 0.5f;
        private static Vector3 BoundsSize(WorldBounds bounds) => ToUnity(bounds.Maximum) - ToUnity(bounds.Minimum);
    }
}
