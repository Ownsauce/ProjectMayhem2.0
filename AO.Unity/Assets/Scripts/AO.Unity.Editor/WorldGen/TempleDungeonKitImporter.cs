using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AO.Unity.World.Procedural;
using UnityEditor;
using UnityEngine;

namespace AO.Unity.Editor.WorldGen
{
    public static class TempleDungeonKitImporter
    {
        private const string Output = "Assets/Resources/TempleKit";
        [MenuItem("Tools/WorldGen/Build Temple Prototype Dungeon")]
        private static void BuildDungeon()
        {
            try
            {
                RefreshKit();
                TempleDungeonKit kit = AssetDatabase.LoadAssetAtPath<TempleDungeonKit>(
                    Output + "/TempleDungeonKit.asset");
                if (kit == null)
                    throw new InvalidDataException("Temple dungeon kit could not be loaded.");
                GameObject tilePrefab = kit.Resolve("temple-flat-plain-wall-tile-2");
                MeshFilter tileFilter = tilePrefab != null
                    ? tilePrefab.GetComponentInChildren<MeshFilter>(true) : null;
                MeshRenderer tileRenderer = tilePrefab != null
                    ? tilePrefab.GetComponentInChildren<MeshRenderer>(true) : null;
                if (tileFilter?.sharedMesh == null || tileRenderer?.sharedMaterial == null)
                    throw new InvalidDataException("Imported temple tile prefab has a missing mesh or material.");
                ProceduralDungeonDebugView view = UnityEngine.Object
                    .FindObjectsByType<ProceduralDungeonDebugView>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(candidate => candidate.gameObject.name == "Temple Dungeon Prototype");
                if (view == null)
                    view = new GameObject("Temple Dungeon Prototype").AddComponent<ProceduralDungeonDebugView>();
                view.ConfigureAsStandaloneTemplePreview();
                view.RebuildTemple();
                MeshRenderer visibleTile = view.GetComponentsInChildren<MeshRenderer>(true)
                    .FirstOrDefault(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy
                        && renderer.sharedMaterial != null
                        && renderer.GetComponent<MeshFilter>()?.sharedMesh != null
                        && renderer.transform.parent != null
                        && renderer.transform.parent.name.StartsWith("temple-flat-", StringComparison.Ordinal));
                if (!Application.isPlaying && visibleTile == null)
                    throw new InvalidOperationException("The dungeon placed pieces, but no active wall tile renderer was found.");
                Selection.activeGameObject = visibleTile != null
                    ? visibleTile.transform.parent.gameObject : view.gameObject;
                if (!Application.isPlaying && visibleTile != null)
                {
                    SceneVisibilityManager.instance.Isolate(view.gameObject, true);
                    SceneView sceneView = SceneView.lastActiveSceneView
                        ?? EditorWindow.GetWindow<SceneView>();
                    sceneView.Focus();
                    sceneView.LookAt(visibleTile.bounds.center,
                        Quaternion.LookRotation(-visibleTile.transform.forward, Vector3.up), 5f);
                    sceneView.Repaint();
                }
                EditorUtility.SetDirty(view);
                string message = Application.isPlaying
                    ? "Temple dungeon build started. The piece count appears in the Console when construction finishes."
                    : "Temple dungeon built with " + view.TemplePiecesPlaced +
                      " authored pieces. Scene view is focused on an active temple wall tile.";
                Debug.Log(message, view);
                EditorUtility.DisplayDialog("Temple Dungeon Prototype", message, "OK");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Temple Dungeon Build Failed", exception.Message, "OK");
            }
        }

        [MenuItem("Tools/WorldGen/Refresh Temple Kit")]
        public static void ImportFromMenu()
        {
            try { Debug.Log("Refreshed " + RefreshKit() + " temple kit pieces."); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        public static int RefreshKit()
        {
            string assets = Path.GetFullPath(Path.Combine(Application.dataPath,
                "..", "..", "..", "WorldGen", "Assets", "temple", "architecture"));
            EnsureFolder(Output);
            EnsureFolder(Output + "/Prefabs");
            EnsureFolder(Output + "/Meshes");
            EnsureFolder(Output + "/Materials");
            EnsureFolder(Output + "/Textures");
            string kitPath = Output + "/TempleDungeonKit.asset";
            TempleDungeonKit kit = AssetDatabase.LoadAssetAtPath<TempleDungeonKit>(kitPath);
            if (kit == null)
                throw new InvalidDataException("The existing TempleDungeonKit.asset is required. Add reviewed client prefabs to the kit before building.");
            var entries = new List<TempleDungeonKit.Entry>(kit.Entries);
            if (entries.Count == 0)
                throw new InvalidDataException("No existing Temple kit prefabs were found.");
            ImportSurfaceTiles(assets, entries);
            kit.ReplaceEntries(entries);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            return entries.Count;
        }

        private static void ImportSurfaceTiles(string assets, List<TempleDungeonKit.Entry> entries)
        {
            for (int ceiling = 0; ceiling <= 1; ceiling++)
            {
                int count = ceiling == 1 ? 2 : 6;
                string kind = ceiling == 1 ? "ceiling" : "floor";
                for (int number = 1; number <= count; number++)
                {
                    string id = "temple-" + kind + "-tile-" + number;
                    string source = Path.Combine(assets, ceiling == 1 ? "ceilings" : "floors", id + ".jpeg");
                    if (!File.Exists(source)) continue;
                    var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    try
                    {
                        if (!decoded.LoadImage(File.ReadAllBytes(source)))
                            throw new InvalidDataException("Could not decode temple tile: " + source);
                        // The reference photos have a single square tile centered in the frame.
                        int side = Mathf.RoundToInt(Mathf.Min(decoded.width, decoded.height) * .82f);
                        int x = (decoded.width - side) / 2;
                        int y = (decoded.height - side) / 2;
                        var cropped = new Texture2D(side, side, TextureFormat.RGBA32, false);
                        try
                        {
                            cropped.SetPixels(decoded.GetPixels(x, y, side, side));
                            cropped.Apply();
                            string texturePath = Output + "/Textures/" + id + ".png";
                            File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,
                                "..", texturePath)), cropped.EncodeToPNG());
                            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Standard") ?? throw new InvalidOperationException("No Lit shader found.");
                            var material = new Material(shader);
                            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .15f);
                            material = SaveAsset(Output + "/Materials/" + id + ".mat", material);
                            var mesh = new Mesh();
                            mesh.vertices = new[] { new Vector3(-.75f, -.75f, 0),
                                new Vector3(.75f, -.75f, 0), new Vector3(.75f, .75f, 0),
                                new Vector3(-.75f, .75f, 0) };
                            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                            mesh.RecalculateNormals();
                            mesh = SaveAsset(Output + "/Meshes/" + id + ".asset", mesh);
                            var root = new GameObject(id);
                            try
                            {
                                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                                root.AddComponent<MeshRenderer>().sharedMaterial = material;
                                string prefabPath = Output + "/Prefabs/" + id + ".prefab";
                                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                                entries.RemoveAll(entry => entry != null && entry.Id == id);
                                entries.Add(new TempleDungeonKit.Entry { Id = id, Prefab = prefab,
                                    SizeMetres = new Vector3(1.5f, 1.5f, .01f) });
                            }
                            finally { UnityEngine.Object.DestroyImmediate(root); }
                        }
                        finally { UnityEngine.Object.DestroyImmediate(cropped); }
                    }
                    finally { UnityEngine.Object.DestroyImmediate(decoded); }
                }
            }
        }

        private static T SaveAsset<T>(string path, T value) where T : UnityEngine.Object
        {
            string assetName = Path.GetFileNameWithoutExtension(path);
            value.name = assetName;
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(value, path);
                return value;
            }
            EditorUtility.CopySerialized(value, existing);
            existing.name = assetName;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(value);
            return existing;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
                throw new InvalidDataException("Invalid Unity asset folder: " + assetPath);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }

    }
}
