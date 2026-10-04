using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AO.Unity.World.Procedural;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Security.Cryptography;
using WorldGen.Content;

namespace AO.Unity.Editor.WorldGen
{
    public static class SubwayDungeonKitImporter
    {
        private const string Output = "Assets/Resources/SubwayKit";
        private static readonly string[] Roles = { "floor", "wall", "ceiling", "pillar", "light", "safety", "rail", "train", "stair", "handrail", "handrail-post", "wall-corner", "toilet", "sink", "doorframe", "door" };
        private static readonly string[] Sources = { "architecture/floors/1", "architecture/walls/1",
            "architecture/ceiling/1", "architecture/pillars/1", "architecture/light-fixture/1",
            "architecture/saftey-strip", "architecture/train-rail/straights", "train", "architecture/stairs/stationary",
            "architecture/stairs/straight-hand-rail", "architecture/stairs/hand-rail-post", "architecture/wall-edge/2", "props/toilet", "props/sink", "architecture/doorways/2", "props/door" };

        [MenuItem("Tools/WorldGen/Refresh Subway Kit")]
        public static void RefreshKit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before refreshing Subway assets.");
            string source = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../../WorldGen/Assets/subway"));
            Directory.CreateDirectory(Output + "/Models");
            Directory.CreateDirectory(Output + "/Prefabs");
            AssetDatabase.Refresh();
            var previousEntries = AssetDatabase.LoadAssetAtPath<SubwayDungeonKit>(Output + "/SubwayDungeonKit.asset")?.Entries.ToArray()
                ?? Array.Empty<SubwayDungeonKit.Entry>();
            var entries = new List<SubwayDungeonKit.Entry>();
            for (int i = 0; i < Roles.Length; i++)
            {
                string from = Path.Combine(source, Sources[i], "game.glb");
                if (!File.Exists(from)) throw new FileNotFoundException("Missing Subway asset", from);
                string modelPath = Output + "/Models/" + Roles[i] + ".glb";
                File.Copy(from, modelPath, true);
                AssetDatabase.ImportAsset(modelPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null) throw new InvalidDataException("glTFast could not import " + modelPath);
                var root = new GameObject(Roles[i]);
                try
                {
                    var visual = UnityEngine.Object.Instantiate(model, root.transform, false);
                    foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(collider);
                    Bounds bounds = BoundsOf(root);
                    // Generated sources use different axes. Orient the major/minor axes before normalization.
                    int[] axes = { 0, 1, 2 };
                    Array.Sort(axes, (a, b) => bounds.size[a].CompareTo(bounds.size[b]));
                    Vector3[] directions = { Vector3.right, Vector3.up, Vector3.forward };
                    Vector3 up, forward;
                    if (Roles[i] == "pillar" || Roles[i] == "train" || Roles[i] == "handrail-post" || Roles[i] == "wall-corner" || Roles[i] == "door" || Roles[i] == "doorframe")
                    {
                        up = directions[Roles[i] != "train" ? axes[2] : axes[1]];
                        forward = directions[Roles[i] == "train" ? axes[2] : axes[0]];
                    }
                    else if (Roles[i] == "toilet" || Roles[i] == "sink")
                    { up = Vector3.up; forward = Vector3.forward; }
                    else if (Roles[i] == "wall" || Roles[i] == "handrail")
                    { up = directions[axes[1]]; forward = directions[axes[0]]; }
                    else
                    { up = directions[axes[0]]; forward = directions[axes[1]]; }
                    visual.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up))
                        * visual.transform.localRotation;
                    bounds = BoundsOf(root);
                    if (bounds.size.x < .00001f || bounds.size.y < .00001f || bounds.size.z < .00001f)
                        throw new InvalidDataException("Empty model bounds: " + modelPath);
                    // A separate wrapper makes authored node transforms and mesh data stay intact.
                    var normalized = new GameObject("Normalized visual").transform;
                    normalized.SetParent(root.transform, false);
                    visual.transform.SetParent(normalized, false);
                    normalized.localScale = new Vector3(1 / bounds.size.x, 1 / bounds.size.y, 1 / bounds.size.z);
                    normalized.localPosition = -Vector3.Scale(bounds.center, normalized.localScale);
                    Bounds unit = BoundsOf(root);
                    if ((unit.center).sqrMagnitude > .000001f || (unit.size - Vector3.one).sqrMagnitude > .000001f)
                        throw new InvalidDataException("Subway normalization failed for " + Roles[i]);
                    var prefab = PrefabUtility.SaveAsPrefabAsset(root, Output + "/Prefabs/" + Roles[i] + ".prefab");
                    if (prefab == null) throw new InvalidDataException("Could not save " + Roles[i]);
                    var existing = AssetDatabase.LoadAssetAtPath<SubwayDungeonKit>(Output + "/SubwayDungeonKit.asset")?.Entries
                        .FirstOrDefault(x => x.Role == Roles[i])?.Asset;
                    if (string.IsNullOrEmpty(existing?.id)) existing = null;
                    using var hash = SHA256.Create();
                    string digest = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(modelPath))).Replace("-", "").ToLowerInvariant();
                    var asset = existing ?? new CatalogAsset
                    {
                        id = "subway-" + Roles[i] + "-01", theme = "Subway", category = "architecture",
                        purpose = Enum.Parse<DungeonAssetRole>(Roles[i].Replace("-", ""), true).ToString(),
                        pivot = "center", fitMode = "stretch", approvalStatus = "prototype",
                        allowedThemes = new[] { "Subway" }, excludedThemes = Array.Empty<string>(),
                        tags = new[] { "industrial" }, allowedRotations = new[] { 0, 90, 180, 270 },
                        collision = new CatalogCollision { mode = "none" },
                        lighting = Roles[i] == "light" ? new CatalogLighting { enabled = true,
                            socketMeters = new CatalogVector { y = -.2f }, color = new CatalogVector { x = .86f, y = .94f, z = 1 } } : null
                    };
                    asset.path = "Models/" + Roles[i] + ".glb";
                    asset.sha256 = digest;
                    asset.dimensionsMeters = new CatalogVector { x = bounds.size.x, y = bounds.size.y, z = bounds.size.z };
                    asset.sourceBounds = new CatalogBounds { minimum = new CatalogVector { x = bounds.min.x, y = bounds.min.y, z = bounds.min.z },
                        maximum = new CatalogVector { x = bounds.max.x, y = bounds.max.y, z = bounds.max.z } };
                    asset.triangles = root.GetComponentsInChildren<MeshFilter>(true).Sum(x => x.sharedMesh.triangles.Length / 3);
                    entries.Add(new SubwayDungeonKit.Entry { Role = Roles[i], Prefab = prefab, Asset = asset });
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            string kitPath = Output + "/SubwayDungeonKit.asset";
            var kit = AssetDatabase.LoadAssetAtPath<SubwayDungeonKit>(kitPath);
            if (kit == null) { kit = ScriptableObject.CreateInstance<SubwayDungeonKit>(); AssetDatabase.CreateAsset(kit, kitPath); }
            foreach (var custom in previousEntries)
                if (custom?.Asset != null && custom.Prefab != null && !entries.Any(x => x.Asset.id == custom.Asset.id))
                    entries.Add(custom);
            kit.Replace(entries);
            SubwaySurfaceBaker.BakeKit(kit);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(kitPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            kit = AssetDatabase.LoadAssetAtPath<SubwayDungeonKit>(kitPath);
            if (kit == null || !kit.TryValidate(out string error))
                throw new InvalidDataException("Subway kit import has unresolved references. Reopen Unity and refresh the kit.");
            ExportCatalog(kit);
            Debug.Log("Imported Subway kit: " + entries.Count + " visual roles.");
        }

        public static void AddAsset(SubwayDungeonKit kit, string sourcePath, string id, DungeonAssetRole role)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before importing.");
            if (role == DungeonAssetRole.Unspecified || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9][a-z0-9_-]*$"))
                throw new InvalidOperationException("Choose a role and an asset ID using lowercase letters, numbers, underscores, and hyphens.");
            if (kit.Entries.Any(x => x.Asset?.id == id)) throw new InvalidOperationException("Asset ID already exists.");
            string path = Output + "/Models/" + id + ".glb";
            if (File.Exists(path)) throw new InvalidOperationException("Model path already exists.");
            File.Copy(sourcePath, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidDataException("Could not import GLB.");
            var root = new GameObject(id);
            try
            {
                var visual = UnityEngine.Object.Instantiate(model, root.transform, false);
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                Bounds bounds = BoundsOf(root);
                if (bounds.size.x < .00001f || bounds.size.y < .00001f || bounds.size.z < .00001f)
                    throw new InvalidDataException("Model has empty bounds.");
                // Custom assets retain authored Y-up orientation; inspect it in the asset preview.
                var normalized = new GameObject("Normalized visual").transform;
                normalized.SetParent(root.transform, false);
                visual.transform.SetParent(normalized, false);
                normalized.localScale = new Vector3(1 / bounds.size.x, 1 / bounds.size.y, 1 / bounds.size.z);
                normalized.localPosition = -Vector3.Scale(bounds.center, normalized.localScale);
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, Output + "/Prefabs/" + id + ".prefab");
                using var hash = SHA256.Create();
                var asset = new CatalogAsset { id = id, purpose = role.ToString(), theme = "Subway", category = "custom",
                    path = "Models/" + id + ".glb", sha256 = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(),
                    dimensionsMeters = new CatalogVector { x = bounds.size.x, y = bounds.size.y, z = bounds.size.z },
                    pivot = "center", fitMode = "stretch", approvalStatus = "prototype", allowedThemes = new[] { "Subway" },
                    allowedRotations = new[] { 0, 90, 180, 270 }, collision = new CatalogCollision { mode = "none" },
                    triangles = root.GetComponentsInChildren<MeshFilter>(true).Sum(x => x.sharedMesh.triangles.Length / 3),
                    lighting = role == DungeonAssetRole.Light ? new CatalogLighting { enabled = true,
                        socketMeters = new CatalogVector { y = -.2f }, color = new CatalogVector { x = .86f, y = .94f, z = 1 } } : null };
                var catalog = new AssetCatalog { schemaVersion = 2, contentVersion = "local", coordinateSystem = "right-handed-y-up",
                    assets = kit.Entries.Select(x => x.Asset).Append(asset).ToArray() };
                AssetCatalogValidator.Validate(catalog);
                Undo.RecordObject(kit, "Register dungeon asset");
                kit.Replace(kit.Entries.Concat(new[] { new SubwayDungeonKit.Entry { Role = role.ToString().ToLowerInvariant(), Prefab = prefab, Asset = asset } }).ToArray());
                EditorUtility.SetDirty(kit); AssetDatabase.SaveAssets(); ExportCatalog(kit);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static void ExportCatalog(SubwayDungeonKit kit)
        {
            var catalog = new AssetCatalog { schemaVersion = 2, contentVersion = "subway-2026-10-02",
                coordinateSystem = "right-handed-y-up", assets = kit.Entries.Select(x => x.Asset).ToArray() };
            AssetCatalogValidator.VerifyFiles(catalog, Path.GetFullPath(Output));
            File.WriteAllText(Output + "/asset-catalog.json", JsonUtility.ToJson(catalog, true));
            AssetDatabase.ImportAsset(Output + "/asset-catalog.json");
        }

        private static Bounds BoundsOf(GameObject root)
        {
            bool first = true;
            Bounds bounds = default;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                    throw new InvalidDataException("Subway imports require readable static meshes.");
                foreach (Vector3 vertex in filter.sharedMesh.vertices)
                {
                    Vector3 point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            if (first) throw new InvalidDataException("Subway model contains no vertices.");
            return bounds;
        }

        [MenuItem("Tools/WorldGen/Build Subway Prototype Dungeon")]
        public static void BuildDungeon()
        {
            RefreshKit();
            var view = UnityEngine.Object.FindObjectsByType<ProceduralDungeonDebugView>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(x => x.name == "Subway Dungeon Prototype");
            if (view == null) view = new GameObject("Subway Dungeon Prototype").AddComponent<ProceduralDungeonDebugView>();
            view.ConfigureAsStandaloneSubwayPreview();
            view.RebuildSubway();
            Selection.activeGameObject = view.gameObject;
            EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log("Built Subway prototype with authored GLB kit.", view);
        }

        // Batch verification creates an isolated scene and never overwrites the user's TestScene.
        public static void VerifyAndSavePreview()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildDungeon();
            var view = UnityEngine.Object.FindFirstObjectByType<ProceduralDungeonDebugView>();
            var kit = AssetDatabase.LoadAssetAtPath<SubwayDungeonKit>(Output + "/SubwayDungeonKit.asset");
            foreach (var entry in kit.Entries)
            {
                if (entry.Prefab.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Visual kit contains a collider: " + entry.Role);
                Bounds bounds = BoundsOf(entry.Prefab);
                if (bounds.center.sqrMagnitude > .000001f || (bounds.size - Vector3.one).sqrMagnitude > .000001f)
                    throw new InvalidOperationException("Invalid saved prefab bounds: " + entry.Role);
            }
            int pieces = view.GetComponentsInChildren<Transform>(true).Count(x => x.name.EndsWith("[Subway GLB]"));
            if (pieces == 0) throw new InvalidOperationException("No authored Subway pieces were placed.");
            foreach (string role in Roles)
            {
                int count = view.GetComponentsInChildren<MeshFilter>(true).Count(x =>
                    (role == "floor" || role == "wall")
                        ? AssetDatabase.GetAssetPath(x.GetComponent<MeshRenderer>().sharedMaterial) == Output + "/Surfaces/" + role + ".mat"
                        : AssetDatabase.GetAssetPath(x.sharedMesh) == Output + "/Models/" + role + ".glb");
                Debug.Log("SUBWAY_ROLE " + role + " meshes=" + count);
                if (count == 0 && role != "pillar" && role != "handrail-post" && role != "wall-corner" && role != "toilet" && role != "sink" && role != "doorframe" && role != "door")
                    throw new InvalidOperationException("Generated preview did not place role: " + role);
            }
            if (!view.GetComponentsInChildren<Transform>(true).Any(x => x.name.StartsWith("Core Collision ")))
                throw new InvalidOperationException("Preview is missing authoritative collision primitives.");
            Directory.CreateDirectory("Assets/Generated/Subway");
            EditorSceneManager.SaveScene(view.gameObject.scene, "Assets/Generated/Subway/SubwayPrototype.unity");
            Debug.Log("SUBWAY_VERIFIED pieces=" + pieces);
        }
    }
}
