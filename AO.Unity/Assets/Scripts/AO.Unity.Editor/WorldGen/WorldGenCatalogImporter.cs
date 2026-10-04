using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using WorldGen.Content;
using WorldGen.UnityIntegration;

namespace WorldGen.UnityIntegration.Editor
{
    public static class WorldGenCatalogImporter
    {
        private const string Models = "Assets/WorldGen/Models";
        private const string Prefabs = "Assets/WorldGen/Prefabs";
        public const string CatalogPath = "Assets/Resources/WorldGen/WorldGenPrefabCatalog.asset";

        [MenuItem("Tools/WorldGen/Import GLB Catalog")]
        private static void ImportMenu()
        {
            string catalogPath = EditorUtility.OpenFilePanel("Choose WorldGen catalog.json", "", "json");
            if (string.IsNullOrEmpty(catalogPath)) return;
            string assetRoot = Path.Combine(Path.GetDirectoryName(catalogPath), "Assets");
            if (!Directory.Exists(assetRoot))
                assetRoot = EditorUtility.OpenFolderPanel("Choose the catalog's Assets folder", Path.GetDirectoryName(catalogPath), "");
            if (string.IsNullOrEmpty(assetRoot)) return;
            try
            {
                WorldGenPrefabCatalog catalog = Import(catalogPath, assetRoot);
                Selection.activeObject = catalog;
                Debug.Log($"Imported WorldGen content {catalog.ContentVersion}: {catalog.Entries.Count} assets.", catalog);
            }
            catch (Exception error) { Debug.LogException(error); }
        }

        public static WorldGenPrefabCatalog Import(string catalogPath, string assetRoot)
        {
            AssetCatalog source = JsonUtility.FromJson<AssetCatalog>(File.ReadAllText(catalogPath));
            AssetCatalogValidator.VerifyFiles(source, assetRoot);
            EnsureFolder(Models); EnsureFolder(Prefabs); EnsureFolder("Assets/Resources/WorldGen");
            var entries = new List<WorldGenPrefabCatalog.Entry>();
            foreach (CatalogAsset asset in source.assets)
            {
                // Full ID hash prevents collisions between identically named game.glb files.
                // Content hash folders keep old prefab references intact while an update imports.
                string key = Key(asset.id);
                string modelFolder = Models + "/" + key;
                string prefabFolder = Prefabs + "/" + key;
                EnsureFolder(modelFolder); EnsureFolder(prefabFolder);
                string modelPath = modelFolder + "/" + asset.sha256 + ".glb";
                string prefabPath = prefabFolder + "/" + Key(JsonUtility.ToJson(asset)) + ".prefab";
                string originalPath = AssetCatalogValidator.ResolvePath(assetRoot, asset.path);
                string absolute = Path.GetFullPath(modelPath);
                if (!File.Exists(absolute)) File.Copy(originalPath, absolute);
                // Always verify an existing cached file before trusting it.
                VerifyHash(absolute, asset.sha256);
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null) throw new InvalidDataException("GLB import failed. Enable glTFast's editor importer: " + modelPath);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) prefab = CreatePrefab(model, asset, prefabPath);
                entries.Add(new WorldGenPrefabCatalog.Entry { Asset = asset, Prefab = prefab });
            }
            // Publish the new mapping only after every GLB and prefab succeeds.
            WorldGenPrefabCatalog result = AssetDatabase.LoadAssetAtPath<WorldGenPrefabCatalog>(CatalogPath);
            if (result == null)
            {
                result = ScriptableObject.CreateInstance<WorldGenPrefabCatalog>();
                AssetDatabase.CreateAsset(result, CatalogPath);
            }
            result.Replace(source.contentVersion, entries);
            EditorUtility.SetDirty(result);
            AssetDatabase.SaveAssets();
            return result;
        }

        private static GameObject CreatePrefab(GameObject model, CatalogAsset asset, string path)
        {
            var root = new GameObject(asset.id);
            try
            {
                var normalization = new GameObject("Visual").transform;
                normalization.SetParent(root.transform, false);
                var visual = UnityEngine.Object.Instantiate(model, normalization, false);
                if (visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                    throw new InvalidDataException("Only static meshes are supported.");
                // Imported render bounds can be conservative after node rotations;
                // use actual vertices so normalization matches the portable validator.
                Bounds bounds = MeshBounds(visual, root.transform);
                Vector3 size = V(asset.dimensionsMeters);
                if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                    throw new InvalidDataException("GLB has empty mesh bounds.");
                normalization.localScale = new Vector3(size.x/bounds.size.x, size.y/bounds.size.y, size.z/bounds.size.z);
                Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                normalization.localPosition = -Vector3.Scale(pivot, normalization.localScale);
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                CheckOpening(visual, root.transform, asset.clearance);
                var collisionRoot = new GameObject("Collision").transform;
                collisionRoot.SetParent(root.transform, false);
                foreach (CatalogBox proxy in asset.collision.boxes)
                {
                    var box = new GameObject("Box").AddComponent<BoxCollider>();
                    box.transform.SetParent(collisionRoot, false);
                    box.center = V(proxy.centerMeters); box.size = V(proxy.sizeMeters);
                }
                foreach (CatalogSocket socket in asset.sockets)
                {
                    var marker = new GameObject("Socket " + socket.id).transform;
                    marker.SetParent(root.transform, false);
                    marker.localPosition = V(socket.positionMeters);
                    marker.localRotation = Quaternion.LookRotation(V(socket.forward), Vector3.up);
                }
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                if (saved == null) throw new InvalidDataException("Could not save prefab: " + path);
                return saved;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Bounds MeshBounds(GameObject model, Transform root)
        {
            bool first = true;
            Bounds bounds = new Bounds();
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null || !mesh.isReadable) throw new InvalidDataException("Imported meshes must be readable for clearance verification.");
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 point = root.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            if (first) throw new InvalidDataException("GLB has no static mesh vertices.");
            return bounds;
        }

        private static void CheckOpening(GameObject model, Transform root, CatalogBox clearance)
        {
            Vector3 center = V(clearance.centerMeters), half = V(clearance.sizeMeters)/2 - Vector3.one * .00001f;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] indices = mesh.triangles;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = root.InverseTransformPoint(filter.transform.TransformPoint(vertices[i])) - center;
                for (int i = 0; i < indices.Length; i += 3)
                    if (TriangleIntersectsBox(vertices[indices[i]], vertices[indices[i+1]], vertices[indices[i+2]], half))
                        throw new InvalidDataException("Imported mesh obstructs declared doorway clearance.");
            }
        }

        private static bool TriangleIntersectsBox(Vector3 a, Vector3 b, Vector3 c, Vector3 half)
        {
            Vector3[] edges = { b-a, c-b, a-c };
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            foreach (Vector3 axis in axes) if (Separated(a,b,c,half,axis)) return false;
            if (Separated(a,b,c,half,Vector3.Cross(edges[0],edges[1]))) return false;
            foreach (Vector3 edge in edges)
                foreach (Vector3 axis in axes)
                    if (Separated(a,b,c,half,Vector3.Cross(edge,axis))) return false;
            return true;
        }
        private static bool Separated(Vector3 a, Vector3 b, Vector3 c, Vector3 half, Vector3 axis)
        {
            float radius = half.x*Mathf.Abs(axis.x)+half.y*Mathf.Abs(axis.y)+half.z*Mathf.Abs(axis.z);
            float x = Vector3.Dot(a,axis), y = Vector3.Dot(b,axis), z = Vector3.Dot(c,axis);
            return Mathf.Min(x,y,z) > radius+1e-7f || Mathf.Max(x,y,z) < -radius-1e-7f;
        }
        private static Vector3 V(CatalogVector value) => new Vector3(value.x, value.y, value.z);
        private static string Key(string text)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        private static void VerifyHash(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                if (BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != expected)
                    throw new InvalidDataException("Cached GLB hash mismatch: " + path);
        }
        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
