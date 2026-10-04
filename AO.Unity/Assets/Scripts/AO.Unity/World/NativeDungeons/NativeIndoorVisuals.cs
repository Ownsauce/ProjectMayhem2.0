using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AO.Assets.Conversion;
using AO.Assets.Decoders;
using AO.Assets.Navigation;
using AO.Assets.ResourceDatabase;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Dungeons;

namespace AO.Unity.World
{
    /// <summary>Reads local indoor presentation resources on demand; existing physics remains authoritative.</summary>
    public sealed class NativeIndoorVisuals : MonoBehaviour
    {
        private sealed class Group
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();
        }
        private sealed class Room { public int Index; public Dictionary<uint, Group> Groups = new Dictionary<uint, Group>(); }
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private CancellationTokenSource _cancellation;
        private ResourceDatabase _statelDatabase;
        private AbiffMaterialFactory _statelMaterials;
        private StatelParser _statelParser;
        public bool IsReady { get; private set; }
        public string Failure { get; private set; }
        private void OnDestroy()
        {
            _cancellation?.Cancel(); _cancellation?.Dispose();
            _statelParser?.ClearMeshes(); _statelMaterials?.Clear(); _statelDatabase?.Dispose();
            foreach (var asset in _owned) if (asset != null) Release(asset);
        }
        private static void Release(UnityEngine.Object asset) { if (Application.isPlaying) Destroy(asset); else DestroyImmediate(asset); }
        private T Own<T>(T asset) where T : UnityEngine.Object { _owned.Add(asset); return asset; }

        public static void BeginLoad(int pf, AOInstallValidation install, AOPlayfieldDefinition definition,
            Transform parent, Vector3 horizontalCenter, bool centered, float scale, Transform diagnosticSurfaces, NativeRoomRecipe recipe = null)
        {
            try
            {
                var owner = PrepareLoad(pf, install, definition, parent, horizontalCenter, centered, scale, diagnosticSurfaces, recipe, out var process);
                if (owner != null) owner.StartCoroutine(process);
            }
            catch (Exception error) { Debug.LogWarning("Native visual loading could not start: " + error.Message); }
        }

        // The editor drives the same loader through EditorApplication.update; no second renderer.
        public static NativeIndoorVisuals PrepareLoad(int pf, AOInstallValidation install, AOPlayfieldDefinition definition,
            Transform parent, Vector3 horizontalCenter, bool centered, float scale, Transform diagnosticSurfaces,
            NativeRoomRecipe recipe, out IEnumerator process)
        {
            process = null;
            if (install == null || !install.IsValid || definition == null) throw new ArgumentException("Select a valid AO installation first.");
            string helper = Path.Combine(Application.streamingAssetsPath, "Tools", "AOIndoorVisualExtractor.exe");
            if (!File.Exists(helper)) throw new FileNotFoundException("Indoor runtime helper is missing. Run tools/build-indoor-runtime-helpers.sh.", helper);
            var root = new GameObject("PF_" + pf + "_NativeTexturedVisuals"); root.transform.SetParent(parent, false);
            var owner = root.AddComponent<NativeIndoorVisuals>();
            owner._cancellation = new CancellationTokenSource();
            var token = owner._cancellation.Token;
            var selectedRooms = recipe?.Rooms.Select(r => r.SourceIndex).Distinct().OrderBy(i => i).ToArray();
            var read = Task.Run(() => AOIndoorVisualCache.Load(install, pf, helper, token, selectedRooms), token);
            read.ContinueWith(task => { var observed = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            process = owner.Load(read, install.RootPath, pf, definition, horizontalCenter, centered, scale, diagnosticSurfaces, recipe);
            return owner;
        }

        private IEnumerator Load(Task<AOIndoorVisualSource> read, string installRoot, int pf, AOPlayfieldDefinition definition,
            Vector3 horizontalCenter, bool centered, float scale, Transform diagnosticSurfaces, NativeRoomRecipe recipe = null)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log($"Reading indoor PF {pf} meshes and textures from the selected local AO database.");
            while (!read.IsCompleted) yield return null;
            if (read.IsCanceled) { Failure = "Loading was cancelled."; Release(gameObject); yield break; }
            if (read.IsFaulted)
            {
                Failure = read.Exception.GetBaseException().Message;
                Debug.LogWarning($"Local indoor database read failed for PF {pf}: {read.Exception.GetBaseException().Message}. Keeping diagnostic surfaces.");
                Release(gameObject); yield break;
            }
            var stack = new Stack<IEnumerator>();
            stack.Push(Build(read.Result, installRoot, pf, definition, horizontalCenter, centered, scale, diagnosticSurfaces, recipe));
            while (stack.Count > 0)
            {
                var build = stack.Peek();
                bool more;
                try { more = build.MoveNext(); }
                catch (Exception error)
                {
                    Failure = error.Message;
                    Debug.LogWarning($"Local indoor presentation failed for PF {pf}: {error.Message}. Keeping diagnostic surfaces.");
                    Release(gameObject); yield break;
                }
                if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (build.Current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return build.Current;
            }
            IsReady = true;
            Debug.Log($"Native presentation load: {timer.Elapsed.TotalSeconds:F2}s, cachedRooms={AOIndoorVisualCache.CachedRooms}, cacheBytes={AOIndoorVisualCache.MemoryBytes}, cacheHits={AOIndoorVisualCache.CacheHits}.");
        }

        private IEnumerator Build(AOIndoorVisualSource source, string installRoot, int pf, AOPlayfieldDefinition definition,
            Vector3 horizontalCenter, bool centered, float scale, Transform diagnosticSurfaces, NativeRoomRecipe recipe = null)
        {
            if (source.Definition.Id != definition.Id || source.Definition.TilemapId != definition.TilemapId
                || source.Definition.RoomCount != definition.RoomCount)
                throw new InvalidDataException("Local indoor definition changed during loading");
            var rooms = ReadRooms(source.Geometry, definition.TilemapId, recipe?.Rooms.Select(r => r.SourceIndex).Distinct().OrderBy(i => i).ToArray() ?? Enumerable.Range(0, definition.RoomCount).ToArray());
            var imageBytes = source.Images;
            var usedMaterials = rooms.SelectMany(r => r.Groups.Keys).Distinct().ToArray();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No supported native surface shader");
            var visuals = new GameObject("Architecture"); visuals.SetActive(false); visuals.transform.SetParent(transform, false);
            var owner = this;
            var materials = new Dictionary<uint, Material>();
            int decodedTextures = 0;
            foreach (uint id in usedMaterials)
            {
                var image = owner.Own(new Texture2D(2, 2, TextureFormat.RGBA32, true));
                if (!image.LoadImage(imageBytes[id], true)) throw new InvalidDataException("Cannot decode native texture " + id);
                image.name = "AO_" + id; image.wrapMode = TextureWrapMode.Repeat; image.filterMode = FilterMode.Trilinear; image.anisoLevel = 2;
                var material = owner.Own(new Material(shader)); material.name = image.name; material.mainTexture = image;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", image);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0);
                materials.Add(id, material);
                if (++decodedTextures % 8 == 0) yield return null;
            }
            int triangles = 0, floorTriangles = 0;
            foreach (var room in rooms)
            {
                var roomRoot = new GameObject("Room_" + room.Index + "_" + definition.Rooms[room.Index].Name); roomRoot.transform.SetParent(visuals.transform, false);
                roomRoot.AddComponent<NativeRoomSourcePart>().Configure(room.Index, NativeRoomPartKind.Architecture);
                foreach (var pair in room.Groups)
                {
                    var group = pair.Value;
                    for (int i = 0; i < group.Positions.Count; i++) group.Positions[i] = (group.Positions[i] - (centered ? horizontalCenter : Vector3.zero)) * scale;
                    var mesh = owner.Own(new Mesh { name = "AO_Room_" + room.Index + "_" + pair.Key,
                        indexFormat = group.Positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 });
                    mesh.SetVertices(group.Positions); mesh.SetNormals(group.Normals); mesh.SetUVs(0, group.Uvs);
                    mesh.SetTriangles(group.Indices, 0); mesh.RecalculateBounds();
                    var part = new GameObject("Material_" + pair.Key); part.transform.SetParent(roomRoot.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = materials[pair.Key];
                    renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                    triangles += group.Indices.Count / 3;
                }
                var floor = AOIndoorVisualFloorBuilder.Build(source.Geometry.Rooms.Single(r => r.Index == room.Index));
                if (floor != null)
                {
                    var positions = new List<Vector3>(floor.VertexCount);
                    for (int i = 0; i < floor.Vertices.Length; i += 3)
                    {
                        var position = new Vector3(floor.Vertices[i], floor.Vertices[i + 1], floor.Vertices[i + 2]);
                        positions.Add((position - (centered ? horizontalCenter : Vector3.zero)) * scale);
                    }
                    var supportMesh = owner.Own(new Mesh { name = "AO_Room_" + room.Index + "_FloorSupport",
                        indexFormat = floor.VertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 });
                    supportMesh.SetVertices(positions); supportMesh.SetTriangles(floor.Triangles, 0);
                    supportMesh.RecalculateBounds();
                    var support = new GameObject("FloorSupport"); support.transform.SetParent(roomRoot.transform, false);
                    support.AddComponent<MeshCollider>().sharedMesh = supportMesh;
                    floorTriangles += floor.TriangleCount;
                }
                yield return null; // Build one room per frame while preserving the collision baseline.
            }
            // Indoor statels are room-local model placements, not outdoor world-space sets.
            _statelDatabase = new ResourceDatabase(); _statelDatabase.Initialize(installRoot);
            _statelMaterials = new AbiffMaterialFactory(_statelDatabase);
            _statelParser = new StatelParser(_statelDatabase, new RenderConfig(), _statelMaterials);
            var props = new GameObject("StaticModels"); props.SetActive(false); props.transform.SetParent(visuals.transform, false);
            props.transform.localPosition = centered ? -horizontalCenter * scale : Vector3.zero;
            props.transform.localScale = Vector3.one * scale;
            yield return _statelParser.BuildIndoorCoroutine(definition, props.transform, new HashSet<int>(rooms.Select(r => r.Index)));
            props.SetActive(true);
            // Collision/terrain helper meshes have no source UVs and are not presentation.
            // Hiding their renderers preserves the MeshColliders and removes the white cages.
            int hidden = 0;
            if (diagnosticSurfaces != null)
                foreach (var renderer in diagnosticSurfaces.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.enabled) { renderer.enabled = false; hidden++; }
            if (recipe == null) visuals.SetActive(true);
            else NativeRoomDungeonRuntime.BuildPresentation(visuals.transform, transform, recipe);
            Physics.SyncTransforms();
            Debug.Log($"Read original AO presentation from local database for PF {pf}: rooms={rooms.Count}, tileTriangles={triangles}, floorSupportTriangles={floorTriangles}, tileTextures={materials.Count}, staticModels={_statelParser.CreatedPlacements}, hiddenCollisionRenderers={hidden}. Existing native colliders retained.");
        }
        private static List<Room> ReadRooms(AOIndoorVisualSet source, int tilemap, int[] indices)
        {
            if (source.TilemapId != tilemap || source.Rooms.Count != indices.Length
                || !source.Rooms.Select(r => r.Index).SequenceEqual(indices))
                throw new InvalidDataException("Incomplete native visual room set");
            var rooms = new List<Room>();
            foreach (var sourceRoom in source.Rooms)
            {
                var room = new Room { Index = sourceRoom.Index };
                foreach (var pair in sourceRoom.Meshes)
                {
                    var mesh = pair.Value; var group = new Group();
                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        group.Positions.Add(new Vector3(mesh.Positions[i*3], mesh.Positions[i*3+1], mesh.Positions[i*3+2]));
                        group.Normals.Add(new Vector3(mesh.Normals[i*3], mesh.Normals[i*3+1], mesh.Normals[i*3+2]));
                        group.Uvs.Add(new Vector2(mesh.Uvs[i*2], -mesh.Uvs[i*2+1]));
                    }
                    group.Indices.AddRange(mesh.Triangles); room.Groups.Add(pair.Key, group);
                }
                rooms.Add(room);
            }
            return rooms;
        }
    }
}
