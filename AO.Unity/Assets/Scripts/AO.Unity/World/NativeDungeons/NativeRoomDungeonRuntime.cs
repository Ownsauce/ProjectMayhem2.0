using System;
using System.IO;
using System.Linq;
using UnityEngine;
using WorldGen.Dungeons;
using WorldGen.Spatial;

namespace AO.Unity.World
{
    /// <summary>Places borrowed source meshes using the same room recipe as server physics.</summary>
    public sealed class NativeRoomDungeonRuntime : MonoBehaviour
    {
        private Material _sealMaterial;
        private static NativeRoomCatalog cachedCatalog;
        private static string cachedHash, cachedPath;
        private static long cachedLength;
        private static DateTime cachedWrite;
        private void OnDestroy() { if (_sealMaterial != null) { if (Application.isPlaying) Destroy(_sealMaterial); else DestroyImmediate(_sealMaterial); } }

        public static NativeRoomCatalog ReadCatalog(out string hash)
        {
            if (File.Exists(Path.Combine(NativeRoomCatalogs.Root, "sources.json")))
                return NativeRoomCatalogs.Read(null, null, out hash);
            return ReadLegacyCatalog(127, out hash);
        }
        internal static NativeRoomCatalog ReadLegacyCatalog(int playfield, out string hash)
        {
            string path = Path.Combine(Application.streamingAssetsPath, "NativeCopies", "pf" + playfield + ".rooms.json");
            var info = new FileInfo(path);
            if (cachedCatalog == null || cachedPath != path || cachedLength != info.Length || cachedWrite != info.LastWriteTimeUtc)
            {
                byte[] bytes = File.ReadAllBytes(path);
                var catalog = JsonUtility.FromJson<NativeRoomCatalog>(System.Text.Encoding.UTF8.GetString(bytes));
                NativeRoomDungeon.Validate(catalog);
                cachedCatalog = catalog; cachedHash = NativeRoomDungeon.Sha(bytes);
                cachedPath = path; cachedLength = info.Length; cachedWrite = info.LastWriteTimeUtc;
            }
            hash = cachedHash;
            return cachedCatalog;
        }

        private static Vector3 Meters(WorldVector3 value) => new Vector3(value.X, value.Y, value.Z) * .001f;
        private static Transform Placement(Transform parent, NativeRoomRecipe recipe, NativeRoomPlacement room)
        {
            var template = recipe.Template(room);
            var root = new GameObject($"NativeRoom_{room.Index}_{template.Name}").transform;
            root.SetParent(parent, false);
            root.localRotation = Quaternion.Euler(0, room.QuarterTurns * 90, 0);
            NativeRoomTransform.SourcePoint(template, room, 0, 0, 0, out float x, out float y, out float z);
            root.localPosition = new Vector3(x, y, z);
            return root;
        }

        public static void BuildCollision(Transform source, Transform parent, NativeRoomRecipe recipe)
        {
            if (source == null) throw new InvalidOperationException("Native room collision source is missing.");
            var root = new GameObject("GeneratedNativeCollision"); root.SetActive(false); root.transform.SetParent(parent, false);
            var owner = root.AddComponent<NativeRoomDungeonRuntime>();
            if (Application.isPlaying) root.AddComponent<NativeDungeonEnvironment>();
            var parts = source.GetComponentsInChildren<NativeRoomSourcePart>(true)
                .Where(p => p.Kind == NativeRoomPartKind.Collision).ToLookup(p => p.SourceIndex);
            foreach (var room in recipe.Rooms)
            {
                var destination = Placement(root.transform, recipe, room);
                if (!parts.Contains(room.SourceIndex)) throw new InvalidDataException("Missing native collision room " + room.SourceIndex);
                foreach (var part in parts[room.SourceIndex])
                {
                    var clone = Instantiate(part.gameObject, destination, false);
                    foreach (var renderer in clone.GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = false;
                }
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            owner._sealMaterial = new Material(shader) { name = "NativeRoomClosedDoorway", color = new Color(.12f, .14f, .15f) };
            if (owner._sealMaterial.HasProperty("_Metallic")) owner._sealMaterial.SetFloat("_Metallic", .7f);
            foreach (var seal in NativeRoomDungeon.Seals(recipe))
            {
                var panel = GameObject.CreatePrimitive(PrimitiveType.Cube); panel.name = "ClosedDoorway";
                panel.transform.SetParent(root.transform, false);
                panel.transform.localPosition = Meters(seal.Center);
                panel.transform.localRotation = Quaternion.Euler(0, seal.Facing * 90, 0);
                panel.transform.localScale = Meters(seal.Size);
                panel.GetComponent<MeshRenderer>().sharedMaterial = owner._sealMaterial;
                if (seal.Style == NativeClosureStyle.MetalPanel) NativeDoorwayClosure.AddFrame(panel.transform, owner._sealMaterial);
                else { var properties = new MaterialPropertyBlock(); properties.SetColor("_BaseColor", Color.black); properties.SetColor("_Color", Color.black); panel.GetComponent<MeshRenderer>().SetPropertyBlock(properties); }
            }
            source.gameObject.SetActive(false);
            root.SetActive(true); Physics.SyncTransforms();
        }

        public static void BuildPresentation(Transform source, Transform parent, NativeRoomRecipe recipe)
        {
            var root = new GameObject("GeneratedNativePresentation"); root.SetActive(false); root.transform.SetParent(parent, false);
            var parts = source.GetComponentsInChildren<NativeRoomSourcePart>(true).ToLookup(p => p.SourceIndex);
            var visibility = root.AddComponent<NativeRoomVisibility>();
            visibility.Initialize(recipe);
            foreach (var room in recipe.Rooms)
            {
                var destination = Placement(root.transform, recipe, room);
                var roomParts = parts[room.SourceIndex].ToArray();
                if (!roomParts.Any(p => p.Kind == NativeRoomPartKind.Architecture))
                    throw new InvalidDataException("Missing native presentation room " + room.SourceIndex);
                foreach (var part in roomParts) Instantiate(part.gameObject, destination, false);
                NativeRoomLighting.Build(destination, recipe.Template(room));
                visibility.Register(room.Index, destination.gameObject);
            }
            // Originals stay inactive because their meshes/materials are shared by the placed copies.
            source.gameObject.SetActive(false);
            root.SetActive(true); Physics.SyncTransforms();
            Debug.Log($"[WorldGen] Native room presentation ready: rooms={recipe.Rooms.Count}, joins={recipe.Joins.Count}.");
        }
    }
}
