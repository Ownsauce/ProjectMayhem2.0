using System;
using System.IO;
using System.Text;
using AO.Unity.World.Procedural;
using UnityEditor;
using UnityEngine;

namespace AO.Unity.Editor.WorldGen
{
    // Project the imported GLB face and its UVs into a repeatable albedo tile.
    // Small tiles then cost two triangles per surface instead of thousands per tile.
    public static class SubwaySurfaceBaker
    {
        private const string Output = "Assets/Resources/SubwayKit";
        private const int Resolution = 512;
        [Serializable] private sealed class Document { public View[] bufferViews; public Image[] images; public Texture[] textures; public SourceMaterial[] materials; }
        [Serializable] private sealed class View { public int byteOffset; public int byteLength; }
        [Serializable] private sealed class Image { public int bufferView; }
        [Serializable] private sealed class Texture { public int source; }
        [Serializable] private sealed class SourceMaterial { public Pbr pbrMetallicRoughness; }
        [Serializable] private sealed class Pbr { public TextureInfo baseColorTexture; }
        [Serializable] private sealed class TextureInfo { public int index; }

        [InitializeOnLoadMethod]
        private static void ScheduleMissingSurfaces()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) ScheduleBake();
            };
            ScheduleBake();
        }

        private static void ScheduleBake()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var kit = AssetDatabase.LoadAssetAtPath<SubwayDungeonKit>(Output + "/SubwayDungeonKit.asset");
                if (kit != null)
                {
                    if (kit.Entries.Count < 16 || string.IsNullOrEmpty(kit.Entries[0].Asset?.id))
                        SubwayDungeonKitImporter.RefreshKit();
                    else if (!kit.HasTiledSurfaces) BakeKit(kit);
                    VerifySurfacePlacement(kit);
                }
            };
        }

        private static void VerifySurfacePlacement(SubwayDungeonKit kit)
        {
            var root = new GameObject("Subway surface verification") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                foreach (Vector3 inward in new[] { Vector3.up, Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    bool floor = inward == Vector3.up;
                    Quaternion rotation = floor ? Quaternion.Euler(-90, 0, 0) : Quaternion.LookRotation(inward, Vector3.up);
                    Mesh mesh = kit.PlaceSurface(floor ? "floor" : "wall", "verification", root.transform,
                        Vector3.zero, 8, 4, rotation);
                    if (mesh == null) throw new InvalidDataException("Missing baked Subway surface material.");
                    try
                    {
                        float spacing = floor ? .5f : .375f;
                        if (Mathf.Abs(mesh.uv[2].x - 8 / spacing) > .0001f
                            || Mathf.Abs(mesh.uv[2].y - 4 / .5f) > .0001f
                            || Vector3.Dot(rotation * mesh.normals[0], inward) < .9999f)
                            throw new InvalidDataException("Incorrect Subway surface repeat or facing.");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(mesh); }
                }
                Debug.Log("[WorldGen] Subway surface verification passed: 0.5m floor repeats, 0.375m x 0.25m staggered wall tiles, all four walls face inward.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        public static void BakeKit(SubwayDungeonKit kit)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before baking Subway surfaces.");
            Directory.CreateDirectory(Output + "/Surfaces");
            AssetDatabase.Refresh();
            Material floor = Bake("floor", kit.ResolveEntry("floor", "floor"));
            Material wall = Bake("wall", kit.ResolveEntry("wall", "wall"));
            string emitterPath = Output + "/Surfaces/light-emission.mat";
            Material emitter = AssetDatabase.LoadAssetAtPath<Material>(emitterPath);
            if (emitter == null)
            {
                emitter = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(emitter, emitterPath);
            }
            emitter.SetColor("_BaseColor", new Color(.86f, .94f, 1));
            emitter.SetColor("_EmissionColor", new Color(.86f, .94f, 1) * 4);
            emitter.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(emitter);
            kit.SetEmitterMaterial(emitter);
            kit.SetSurfaceMaterials(floor, wall);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            Debug.Log("[WorldGen] Subway tiled surfaces baked: floor=0.5m, wall=0.375m x 0.25m staggered; lighter wall face selected.");
        }

        private static Material Bake(string role, SubwayDungeonKit.Entry entry)
        {
            GameObject prefab = entry?.Prefab;
            if (prefab == null) throw new InvalidDataException("Missing " + role + " GLB prefab.");
            Texture2D source = ReadAlbedo(Output + "/" + (entry.Asset?.path ?? "Models/" + role + ".glb"));
            try
            {
                Color[] pixels = Project(prefab, source, role == "floor", 1);
                if (role == "wall")
                {
                    Color[] opposite = Project(prefab, source, false, -1);
                    float front = Brightness(pixels), back = Brightness(opposite);
                    if (back > front) pixels = opposite;
                    Debug.Log("[WorldGen] Wall face albedo luminance: +Z=" + front.ToString("F3")
                        + ", -Z=" + back.ToString("F3") + "; selected " + (back > front ? "-Z" : "+Z"));
                }
                int outputSize = role == "wall" ? Resolution * 2 : Resolution;
                if (role == "wall")
                {
                    // Two rows in the repeat: the second starts half a tile farther across.
                    var staggered = new Color[outputSize * outputSize];
                    for (int y = 0; y < outputSize; y++)
                        for (int x = 0; x < outputSize; x++)
                        {
                            int shiftedX = (x + (y >= Resolution ? Resolution : 0)) % outputSize;
                            staggered[y * outputSize + x] = pixels[(y % Resolution) * Resolution + shiftedX / 2];
                        }
                    pixels = staggered;
                }
                var tile = new Texture2D(outputSize, outputSize, TextureFormat.RGBA32, false);
                try
                {
                    tile.SetPixels(pixels); tile.Apply();
                    string texturePath = Output + "/Surfaces/" + role + ".png";
                    File.WriteAllBytes(texturePath, tile.EncodeToPNG());
                    AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.mipmapEnabled = true;
                    importer.anisoLevel = 4;
                    importer.SaveAndReimport();
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    if (shader == null) throw new InvalidDataException("No Lit shader available.");
                    string materialPath = Output + "/Surfaces/" + role + ".mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, materialPath); }
                    material.shader = shader;
                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                    if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                    if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .2f);
                    EditorUtility.SetDirty(material);
                    return material;
                }
                finally { UnityEngine.Object.DestroyImmediate(tile); }
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private static Texture2D ReadAlbedo(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            int jsonLength = BitConverter.ToInt32(bytes, 12);
            var gltf = JsonUtility.FromJson<Document>(Encoding.UTF8.GetString(bytes, 20, jsonLength));
            if (gltf.materials == null || gltf.materials.Length != 1)
                throw new InvalidDataException("Surface baking expects the reviewed single-material Subway GLB: " + path);
            int texture = gltf.materials[0].pbrMetallicRoughness.baseColorTexture.index;
            View view = gltf.bufferViews[gltf.images[gltf.textures[texture].source].bufferView];
            int binaryOffset = 20 + jsonLength + 8;
            var image = new byte[view.byteLength];
            Array.Copy(bytes, binaryOffset + view.byteOffset, image, 0, image.Length);
            var result = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!result.LoadImage(image)) { UnityEngine.Object.DestroyImmediate(result); throw new InvalidDataException("Invalid embedded albedo."); }
            result.wrapMode = TextureWrapMode.Repeat;
            return result;
        }

        private static Color[] Project(GameObject prefab, Texture2D texture, bool floor, int side)
        {
            var colors = new Color[Resolution * Resolution];
            var depths = new float[colors.Length];
            for (int i = 0; i < depths.Length; i++) depths[i] = float.NegativeInfinity;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Vector3[] positions = mesh.vertices;
                Vector2[] uv = mesh.uv;
                int[] triangles = mesh.triangles;
                for (int i = 0; i < positions.Length; i++)
                    positions[i] = prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(positions[i]));
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
                    Vector3 a = positions[ia], b = positions[ib], c = positions[ic];
                    Vector2 A = Plane(a), B = Plane(b), C = Plane(c);
                    float denominator = Cross(B - A, C - A);
                    if (Mathf.Abs(denominator) < .0000001f) continue;
                    int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(A.x, Mathf.Min(B.x, C.x)) * Resolution), 0, Resolution - 1);
                    int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(A.x, Mathf.Max(B.x, C.x)) * Resolution), 0, Resolution - 1);
                    int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(A.y, Mathf.Min(B.y, C.y)) * Resolution), 0, Resolution - 1);
                    int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(A.y, Mathf.Max(B.y, C.y)) * Resolution), 0, Resolution - 1);
                    for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 point = new Vector2((x + .5f) / Resolution, (y + .5f) / Resolution);
                        float beta = Cross(point - A, C - A) / denominator;
                        float gamma = Cross(B - A, point - A) / denominator;
                        float alpha = 1 - beta - gamma;
                        if (alpha < -.00001f || beta < -.00001f || gamma < -.00001f) continue;
                        Vector3 position = alpha * a + beta * b + gamma * c;
                        float depth = floor ? position.y : side * position.z;
                        int index = y * Resolution + x;
                        if (depth < depths[index]) continue;
                        depths[index] = depth;
                        Vector2 sample = alpha * uv[ia] + beta * uv[ib] + gamma * uv[ic];
                        colors[index] = texture.GetPixelBilinear(sample.x, sample.y);
                    }
                }
                Vector2 Plane(Vector3 p) => floor ? new Vector2(p.x + .5f, .5f - p.z)
                    : new Vector2(side * p.x + .5f, p.y + .5f);
            }
            // Fill only uncovered silhouette pixels; preserve the authored front's appearance.
            Color average = Color.black;
            int covered = 0;
            for (int i = 0; i < colors.Length; i++) if (!float.IsNegativeInfinity(depths[i])) { average += colors[i]; covered++; }
            if (covered < colors.Length / 2) throw new InvalidDataException("Surface projection has insufficient coverage.");
            average /= covered;
            for (int i = 0; i < colors.Length; i++) if (float.IsNegativeInfinity(depths[i])) colors[i] = average;
            return colors;
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static float Brightness(Color[] colors)
        {
            float sum = 0;
            foreach (Color color in colors) sum += .2126f * color.r + .7152f * color.g + .0722f * color.b;
            return sum / colors.Length;
        }
    }
}
