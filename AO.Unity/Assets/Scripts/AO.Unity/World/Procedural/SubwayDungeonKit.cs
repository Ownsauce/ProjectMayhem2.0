using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Content;
using WorldGen.Dungeons;

namespace AO.Unity.World.Procedural
{
    // Visual-only unit-bounds models. Traversal remains owned by DungeonCollisionBaker.
    public sealed class SubwayDungeonKit : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public string Role;
            public GameObject Prefab;
            public CatalogAsset Asset;
        }
        [SerializeField] private List<Entry> entries = new List<Entry>();
        [SerializeField] private Material floorSurface;
        [SerializeField] private Material wallSurface;
        [SerializeField] private Material lampEmitterMaterial;
        public void SetEmitterMaterial(Material material) => lampEmitterMaterial = material;
        [SerializeField] private int surfaceVersion;
        public bool HasTiledSurfaces => floorSurface != null && wallSurface != null && surfaceVersion == 2;
        public IReadOnlyList<Entry> Entries => entries;
        public void SetSurfaceMaterials(Material floor, Material wall)
        { floorSurface = floor; wallSurface = wall; surfaceVersion = 2; }

        public Mesh PlaceSurface(string role, string label, Transform parent, Vector3 center,
            float width, float height, Quaternion rotation)
        {
            Material material = role == "floor" ? floorSurface : role == "wall" ? wallSurface : null;
            if (material == null) return null;
            float tileWidth = role == "floor" ? .5f : .375f;
            float repeatHeight = .5f;
            var mesh = new Mesh { name = label + " tiled surface" };
            mesh.vertices = new[] { new Vector3(-width / 2, -height / 2, 0), new Vector3(width / 2, -height / 2, 0),
                new Vector3(width / 2, height / 2, 0), new Vector3(-width / 2, height / 2, 0) };
            mesh.uv = new[] { Vector2.zero, new Vector2(width / tileWidth, 0),
                new Vector2(width / tileWidth, height / repeatHeight), new Vector2(0, height / repeatHeight) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var surface = new GameObject(label + " [Subway GLB]");
            surface.transform.SetParent(parent, false);
            surface.transform.localPosition = center;
            surface.transform.localRotation = rotation;
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            surface.AddComponent<MeshRenderer>().sharedMaterial = material;
            return mesh;
        }
        public void Replace(IEnumerable<Entry> replacement) => entries = new List<Entry>(replacement);
        public bool TryValidate(out string error)
        {
            foreach (string role in new[] { "floor", "wall", "ceiling", "pillar", "light", "safety", "rail", "train" })
            {
                GameObject prefab = Resolve(role);
                if (prefab == null)
                { error = "missing prefab for " + role; return false; }
                MeshFilter[] meshes = prefab.GetComponentsInChildren<MeshFilter>(true);
                if (meshes.Length == 0)
                { error = "no mesh for " + role; return false; }
                foreach (MeshFilter mesh in meshes)
                    if (mesh.sharedMesh == null)
                    { error = "broken mesh reference for " + role; return false; }
            }
            error = null;
            return true;
        }

        public GameObject Resolve(string role) => ResolveEntry(role, role)?.Prefab;

        public Entry ResolveEntry(string role, string placementId, DungeonVisualTheme theme = DungeonVisualTheme.Subway)
        {
            var compatible = new List<Entry>();
            string purpose = role.Replace("-", "");
            foreach (Entry entry in entries)
            {
                if (entry == null || entry.Prefab == null) continue;
                if (entry.Asset == null)
                { if (entry.Role == role) compatible.Add(entry); continue; }
                if (string.Equals(entry.Asset.purpose, purpose, StringComparison.OrdinalIgnoreCase)
                    && entry.Asset.SupportsTheme(theme.ToString())) compatible.Add(entry);
            }
            compatible.Sort((a, b) => StringComparer.Ordinal.Compare(a.Asset?.id ?? a.Role, b.Asset?.id ?? b.Role));
            if (compatible.Count == 0) return null;
            uint hash = 2166136261;
            foreach (char c in placementId ?? string.Empty) hash = unchecked((hash ^ c) * 16777619);
            return compatible[(int)(hash % (uint)compatible.Count)];
        }

        public bool Place(DungeonAssetRole role, string label, Transform parent, Vector3 center, Vector3 size, Quaternion rotation)
            => Place(role.ToString(), label, parent, center, size, rotation);

        public bool PlaceMetalDoorFrame(string label, Transform parent, Vector3 floorCenter,
            float width, float height, Quaternion rotation)
        {
            GameObject frame = ResolveEntry("doorframe", label)?.Prefab;
            if (frame == null) return false;
            float innerHalfWidth = .5f, headerBottom = .5f;
            foreach (MeshFilter filter in frame.GetComponentsInChildren<MeshFilter>(true))
            {
                Vector3[] vertices = filter.sharedMesh.vertices;
                int[] triangles = filter.sharedMesh.triangles;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = frame.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                for (int i = 0; i < triangles.Length; i += 3)
                    for (int edge = 0; edge < 3; edge++)
                    {
                        Vector3 a = vertices[triangles[i + edge]], b = vertices[triangles[i + (edge + 1) % 3]];
                        if (Mathf.Abs(b.y - a.y) > .00001f && a.y * b.y <= 0)
                            innerHalfWidth = Mathf.Min(innerHalfWidth, Mathf.Abs(Vector3.Lerp(a, b, -a.y / (b.y - a.y)).x));
                        if (Mathf.Abs(b.x - a.x) > .00001f && a.x * b.x <= 0)
                        {
                            float y = Vector3.Lerp(a, b, -a.x / (b.x - a.x)).y;
                            if (y > 0) headerBottom = Mathf.Min(headerBottom, y);
                        }
                    }
            }
            float outerWidth = width / Mathf.Clamp(innerHalfWidth * 2, .2f, 1);
            float outerHeight = height / Mathf.Clamp(headerBottom + .5f, .5f, 1);
            return Place("doorframe", label, parent, floorCenter + Vector3.up * (outerHeight * .5f),
                new Vector3(outerWidth, outerHeight, .22f), rotation);
        }

        [NonSerialized] private Material emitterMaterial;
        private Material GetEmitterMaterial()
        {
            if (lampEmitterMaterial != null) return lampEmitterMaterial;
            if (emitterMaterial != null) return emitterMaterial;
            emitterMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { name = "Subway lamp emission", hideFlags = HideFlags.HideAndDontSave };
            emitterMaterial.SetColor("_BaseColor", new Color(.86f, .94f, 1));
            emitterMaterial.SetColor("_EmissionColor", new Color(.86f, .94f, 1) * 4);
            emitterMaterial.EnableKeyword("_EMISSION");
            return emitterMaterial;
        }
        private void OnDisable()
        {
            if (emitterMaterial == null) return;
            if (Application.isPlaying) Destroy(emitterMaterial); else DestroyImmediate(emitterMaterial);
            emitterMaterial = null;
        }

        public bool Place(string role, string label, Transform parent, Vector3 center,
            Vector3 size, Quaternion rotation)
        {
            Entry entry = ResolveEntry(role, label);
            GameObject prefab = entry?.Prefab;
            if (prefab == null) return false;
            GameObject instance = Instantiate(prefab, parent, false);
            instance.name = label + " [Subway GLB]";
            instance.transform.localPosition = center;
            instance.transform.localRotation = rotation;
            instance.transform.localScale = size;
            if (string.Equals(role, "light", StringComparison.OrdinalIgnoreCase)
                && (entry.Asset?.lighting == null || entry.Asset.lighting.enabled))
            {
                SubwayFixtureLighting.Attach(instance.transform, size, entry.Asset?.lighting, GetEmitterMaterial());
            }
            return true;
        }
    }
}
