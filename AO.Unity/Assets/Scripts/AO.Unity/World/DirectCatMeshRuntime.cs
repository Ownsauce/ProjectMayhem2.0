using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AODB.Common.RDBObjects;
using AO.Assets.ResourceDatabase;
using AO.Unity.Assets;
using UnityEngine;
using UnityEngine.Rendering;
using AoQuaternion = AODB.Common.Structs.Quaternion;
using AoVector2 = AODB.Common.Structs.Vector2;
using AoVector3 = AODB.Common.Structs.Vector3;

namespace AO.Unity.World
{
    internal static class CatMeshMaterialFactory
    {
        public static Dictionary<string, int> BuildTextureLookup(IReadOnlyList<RDBCatMesh.Texture> source)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;
            for (int i = 0; i < source.Count; i++)
            {
                RDBCatMesh.Texture texture = source[i];
                if (texture == null || string.IsNullOrWhiteSpace(texture.Name) || texture.Texture1 <= 0) continue;
                result[texture.Name] = texture.Texture1;
            }
            return result;
        }

        public static AbiffMaterialDesc CreateDesc(RDBCatMesh.Material source,
            IReadOnlyDictionary<string, int> textures, string fallbackName)
        {
            AbiffMaterialDesc result = AbiffMaterialDesc.CreateDefault();
            result.Name = string.IsNullOrWhiteSpace(source?.Name) ? fallbackName : source.Name;
            if (source == null) return result;
            float alpha = Mathf.Clamp01(source.SheenOpacity);
            result.Diffuse = new Color(source.Diffuse.R, source.Diffuse.G, source.Diffuse.B, alpha);
            result.Emissive = new Color(source.Emission.R, source.Emission.G, source.Emission.B, 1f);
            result.Shininess = source.Sheen;
            result.SpecularEnabled = source.Sheen > 0f;
            result.ApplyAlpha = alpha < .99f;
            if (TryTexture(textures, source.Name, out int textureId) || TryTexture(textures, source.TextureName, out textureId))
            {
                result.DiffuseTextureId = textureId;
                result.Diffuse = new Color(1f, 1f, 1f, alpha);
            }
            return result;
        }

        private static bool TryTexture(IReadOnlyDictionary<string, int> textures, string name, out int id)
        {
            id = 0;
            if (textures == null || string.IsNullOrWhiteSpace(name)) return false;
            if (textures.TryGetValue(name, out id)) return id > 0;
            string file = Path.GetFileName(name);
            return textures.TryGetValue(file, out id) && id > 0;
        }
    }

    /// <summary>
    /// Direct AO CatMesh reader adapted to Project
    /// Mayhem: RDB data is snapshotted on the main thread, CPU mesh preparation is
    /// performed off-thread, and Unity objects are created back on the main thread.
    /// AOGLTF remains the caller's fallback when a record cannot be decoded.
    /// </summary>
    public static class DirectCatMeshRuntime
    {
        private sealed class SubmeshData
        {
            public string Name;
            public Vector3[] Positions;
            public Vector3[] Normals;
            public Vector2[] Uvs;
            public int[] Triangles;
            public BoneWeight[] Weights;
            public AbiffMaterialDesc Material;
        }

        private sealed class BuildData
        {
            public int MeshId;
            public int MonsterDataId;
            public string AnimationProfile;
            public string[] BoneNames;
            public int[] Parents;
            public float[] Scales;
            public Vector3[] LocalPositions;
            public Quaternion[] LocalRotations;
            public Matrix4x4[] BindPoses;
            public SubmeshData[] Submeshes;
            public AttractorData[] Attractors;
        }

        private sealed class AttractorData
        {
            public string Name;
            public int BoneIndex;
            public Vector3 Position;
            public Quaternion Rotation;
            public float Scale;
        }

        private sealed class CachedPrototype
        {
            public GameObject Root;
            public Mesh[] Meshes;
            public int MonsterDataId;
            public string AnimationProfile;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int> KnownIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "solitus_male", 5907 }, { "solitus_female", 5927 },
            { "opifex_male", 5914 }, { "opifex_female", 5934 },
            { "nanomage_male", 5921 }, { "nanomage_female", 5941 },
            { "athrox_male", 5900 }, { "atrox_male", 5900 }
        };
        private static readonly Dictionary<string, int> NameIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, CachedPrototype> Prototypes = new Dictionary<int, CachedPrototype>();
        private static readonly Dictionary<int, Task<BuildData>> InflightBuilds = new Dictionary<int, Task<BuildData>>();
        private static ResourceDatabase _database;
        private static AbiffMaterialFactory _materials;
        private static string _installRoot;
        private static Transform _cacheRoot;

        public static async Task<GameObject> InstantiateAsync(string resourceName, Transform parent)
        {
            if (string.IsNullOrWhiteSpace(resourceName) || parent == null)
                return null;

            AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid)
                return null;

            EnsureDatabase(install.RootPath);
            if (!TryResolveId(resourceName, out int meshId))
                return null;

            return await InstantiateByIdAsync(meshId, 0, resourceName, parent);
        }

        public static async Task<GameObject> InstantiateMonsterAsync(int monsterDataId, Transform parent)
        {
            if (monsterDataId <= 0 || parent == null) return null;
            AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid) return null;
            EnsureDatabase(install.RootPath);
            try
            {
                if (!MonsterDataResolver.TryResolveBodyCatMeshId(_database, monsterDataId, out int meshId))
                    return null;
                return await InstantiateByIdAsync(meshId, monsterDataId, $"MonsterData_{monsterDataId}", parent);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Direct MonsterData {monsterDataId} load failed: {ex.Message}");
                return null;
            }
        }

        private static async Task<GameObject> InstantiateByIdAsync(int meshId, int monsterDataHint, string resourceName, Transform parent)
        {

            if (TryClone(meshId, parent, resourceName, out GameObject clone))
                return clone;

            RDBCatMesh source;
            try { source = _database.Get<RDBCatMesh>(ResourceTypeId.CatMesh, meshId); }
            catch (Exception ex)
            {
                Debug.LogWarning($"Direct CatMesh {meshId} read failed: {ex.Message}");
                return null;
            }
            if (source == null)
                return null;

            int monsterDataId = monsterDataHint;
            string animationProfile = ResolvePlayerAnimationProfile(resourceName);
            CATAnim restAnim = null;
            try
            {
                if (monsterDataId > 0)
                {
                    var resolver = new CatAnimResolver(_database, animationProfile);
                    if (resolver.TryResolve(monsterDataId, 0, "idle", out int restAnimId, out _))
                        restAnim = _database.Get<CATAnim>(ResourceTypeId.Anim, restAnimId);
                }
                else if (!string.IsNullOrEmpty(animationProfile))
                {
                    var resolver = new CatAnimResolver(_database, animationProfile);
                    if (resolver.TryResolve(0, 0, "idle", out int restAnimId, out _))
                        restAnim = _database.Get<CATAnim>(ResourceTypeId.Anim, restAnimId);
                }
                else if (MonsterDataResolver.TryFindMonsterDataForCatMesh(
                    _database, meshId, out monsterDataId))
                {
                    var resolver = new CatAnimResolver(_database);
                    if (resolver.TryResolve(monsterDataId, 0, "idle", out int restAnimId, out _))
                        restAnim = _database.Get<CATAnim>(ResourceTypeId.Anim, restAnimId);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"CatMesh rest animation lookup failed for {meshId}: {ex.Message}"); }

            Task<BuildData> buildTask;
            lock (Gate)
            {
                if (!InflightBuilds.TryGetValue(meshId, out buildTask))
                {
                    int capturedMonsterDataId = monsterDataId;
                    CATAnim capturedRestAnim = restAnim;
                    string capturedProfile = animationProfile;
                    buildTask = Task.Run(() => Prepare(source, meshId, capturedMonsterDataId,
                        capturedRestAnim, capturedProfile));
                    InflightBuilds[meshId] = buildTask;
                }
            }
            BuildData build = await buildTask;
            lock (Gate) InflightBuilds.Remove(meshId);
            if (build == null || build.Submeshes == null || build.Submeshes.Length == 0 || parent == null)
                return null;

            if (TryClone(meshId, parent, resourceName, out clone))
                return clone;

            GameObject visual = CreateVisual(build, parent, resourceName);
            if (visual != null)
                CachePrototype(meshId, visual);
            return visual;
        }

        private static void EnsureDatabase(string root)
        {
            string normalized = Path.GetFullPath(root ?? string.Empty);
            lock (Gate)
            {
                if (_database != null && string.Equals(_installRoot, normalized, StringComparison.OrdinalIgnoreCase))
                    return;

                _database?.Dispose();
                _database = new ResourceDatabase();
                _database.Initialize(normalized);
                _materials = new AbiffMaterialFactory(_database);
                _installRoot = normalized;
                NameIds.Clear();
                InflightBuilds.Clear();
                ClearPrototypes();
            }
        }

        private static bool TryResolveId(string resourceName, out int id)
        {
            string key = NormalizeName(resourceName);
            if (KnownIds.TryGetValue(key, out id) || NameIds.TryGetValue(key, out id))
                return id > 0;

            try
            {
                InfoObject info = _database.Get<InfoObject>(1);
                if (info?.Types != null && info.Types.TryGetValue(ResourceTypeId.CatMesh, out Dictionary<int, string> names))
                {
                    foreach (KeyValuePair<int, string> pair in names)
                    {
                        string name = NormalizeName(pair.Value);
                        if (!NameIds.ContainsKey(name))
                            NameIds[name] = pair.Key;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"Direct CatMesh name catalog failed: {ex.Message}"); }

            return NameIds.TryGetValue(key, out id) && id > 0;
        }

        private static string NormalizeName(string value)
        {
            string name = (value ?? string.Empty).Trim().Trim('\0').Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            if (name.EndsWith(".cir", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name.ToLowerInvariant();
        }

        private static BuildData Prepare(RDBCatMesh source, int meshId, int monsterDataId,
            CATAnim restAnim, string animationProfile)
        {
            int count = source.Joints?.Count ?? 0;
            var result = new BuildData
            {
                MeshId = meshId,
                MonsterDataId = monsterDataId,
                AnimationProfile = animationProfile,
                BoneNames = new string[count], Parents = new int[count], Scales = new float[count],
                LocalPositions = new Vector3[count], LocalRotations = new Quaternion[count]
            };
            for (int i = 0; i < count; i++)
            {
                result.BoneNames[i] = string.IsNullOrWhiteSpace(source.Joints[i]?.Name) ? $"Joint_{i}" : source.Joints[i].Name;
                result.Parents[i] = -1;
                result.Scales[i] = source.Joints[i]?.Scale > 0f ? source.Joints[i].Scale : 1f;
                result.LocalRotations[i] = Quaternion.identity;
            }
            for (int i = 0; i < count; i++)
            {
                int[] children = source.Joints[i]?.ChildJoints;
                if (children == null) continue;
                for (int c = 0; c < children.Length; c++)
                    if (children[c] >= 0 && children[c] < count) result.Parents[children[c]] = i;
            }

            if (!ApplyRestAnimation(restAnim, result.LocalPositions, result.LocalRotations))
                InferBindPose(source, result.LocalPositions);
            Matrix4x4[] worlds = ComputeWorlds(result);
            result.BindPoses = new Matrix4x4[count];
            for (int i = 0; i < count; i++) result.BindPoses[i] = worlds[i].inverse;

            Dictionary<string, int> textures = CatMeshMaterialFactory.BuildTextureLookup(source.Textures);
            var meshes = new List<SubmeshData>();
            if (source.MeshGroups != null)
            {
                for (int g = 0; g < source.MeshGroups.Count; g++)
                {
                    RDBCatMesh.MeshGroup group = source.MeshGroups[g];
                    if (group?.Meshes == null) continue;
                    for (int m = 0; m < group.Meshes.Count; m++)
                    {
                        RDBCatMesh.Mesh mesh = group.Meshes[m];
                        if (mesh?.Vertices == null || mesh.Vertices.Count == 0) continue;
                        meshes.Add(Snapshot(mesh, source.Materials, textures, worlds, g, m));
                    }
                }
            }
            result.Submeshes = meshes.ToArray();
            var attractors = new List<AttractorData>();
            if (source.Attractors != null)
            {
                for (int i = 0; i < source.Attractors.Count; i++)
                {
                    RDBCatMesh.Attractor sourceAttractor = source.Attractors[i];
                    if (sourceAttractor == null) continue;
                    AoQuaternion q = sourceAttractor.Rotation;
                    attractors.Add(new AttractorData
                    {
                        Name = string.IsNullOrWhiteSpace(sourceAttractor.Name) ? $"Attractor_{i}" : sourceAttractor.Name.Trim(),
                        // This field is named Unknown in our AODB snapshot, but AO's
                        // The CatMesh format identifies this field as BoneIdx.
                        BoneIndex = sourceAttractor.Unknown,
                        Position = ToUnity(sourceAttractor.Position),
                        Rotation = new Quaternion(q.X, q.Y, q.Z, q.W),
                        Scale = sourceAttractor.Scale > 0f ? sourceAttractor.Scale : 1f
                    });
                }
            }
            result.Attractors = attractors.ToArray();
            return result;
        }

        private static bool ApplyRestAnimation(CATAnim animation, Vector3[] positions, Quaternion[] rotations)
        {
            if (animation?.Animation.BoneData == null || animation.Animation.BoneData.Count == 0)
                return false;
            foreach (BoneData bone in animation.Animation.BoneData)
            {
                int index = bone.BoneId;
                if (index < 0 || index >= positions.Length) continue;
                if (bone.TranslationKeys != null && bone.TranslationKeys.Count > 0)
                    positions[index] = ToUnity(bone.TranslationKeys[0].Position);
                if (bone.RotationKeys != null && bone.RotationKeys.Count > 0)
                {
                    AoQuaternion q = bone.RotationKeys[0].Rotation;
                    rotations[index] = new Quaternion(q.X, q.Y, q.Z, q.W);
                }
            }
            return true;
        }

        private static void InferBindPose(RDBCatMesh source, Vector3[] joints)
        {
            int[] samples = new int[joints.Length];
            if (source.MeshGroups == null) return;
            foreach (RDBCatMesh.MeshGroup group in source.MeshGroups)
            foreach (RDBCatMesh.Mesh mesh in group.Meshes)
            foreach (RDBCatMesh.Vertex vertex in mesh.Vertices)
            {
                Vector3 position = ToUnity(vertex.Position);
                if (vertex.Joint1Weight >= .99f) Accumulate(vertex.Joint1, position - ToUnity(vertex.RelToJoint1), joints, samples);
                if (vertex.Joint1Weight <= .01f) Accumulate(vertex.Joint2, position - ToUnity(vertex.RelToJoint2), joints, samples);
            }
            for (int i = 0; i < joints.Length; i++) if (samples[i] > 0) joints[i] /= samples[i];
        }

        private static void Accumulate(int index, Vector3 value, Vector3[] positions, int[] samples)
        {
            if (index < 0 || index >= positions.Length) return;
            positions[index] += value; samples[index]++;
        }

        private static Matrix4x4[] ComputeWorlds(BuildData build)
        {
            var worlds = new Matrix4x4[build.BoneNames.Length];
            var locals = new Matrix4x4[worlds.Length];
            for (int i = 0; i < worlds.Length; i++)
                locals[i] = Matrix4x4.TRS(build.LocalPositions[i], build.LocalRotations[i], Vector3.one * build.Scales[i]);
            var done = new bool[worlds.Length];
            for (int i = 0; i < worlds.Length; i++) ComputeWorld(i, build.Parents, locals, worlds, done);
            return worlds;
        }

        private static void ComputeWorld(int index, int[] parents, Matrix4x4[] locals, Matrix4x4[] worlds, bool[] done)
        {
            if (done[index]) return;
            int parent = index < parents.Length ? parents[index] : -1;
            if (parent >= 0 && parent < locals.Length)
            {
                ComputeWorld(parent, parents, locals, worlds, done);
                worlds[index] = worlds[parent] * locals[index];
            }
            else worlds[index] = locals[index];
            done[index] = true;
        }

        private static SubmeshData Snapshot(RDBCatMesh.Mesh mesh, List<RDBCatMesh.Material> materials,
            Dictionary<string, int> textures, Matrix4x4[] worlds, int group, int index)
        {
            int count = mesh.Vertices.Count;
            var data = new SubmeshData
            {
                Name = $"Mesh_{group}_{index}", Positions = new Vector3[count], Normals = new Vector3[count],
                Uvs = new Vector2[count], Weights = new BoneWeight[count],
                Triangles = mesh.Triangles != null ? (int[])mesh.Triangles.Clone() : Array.Empty<int>()
            };
            RDBCatMesh.Material material = materials != null && mesh.MaterialId >= 0 && mesh.MaterialId < materials.Count ? materials[mesh.MaterialId] : null;
            data.Material = CatMeshMaterialFactory.CreateDesc(material, textures, data.Name);
            for (int i = 0; i < count; i++)
            {
                RDBCatMesh.Vertex vertex = mesh.Vertices[i];
                Vector3 p1 = TransformPoint(worlds, vertex.Joint1, ToUnity(vertex.RelToJoint1));
                Vector3 p2 = TransformPoint(worlds, vertex.Joint2, ToUnity(vertex.RelToJoint2));
                data.Positions[i] = Vector3.Lerp(p2, p1, Mathf.Clamp01(vertex.Joint1Weight));
                data.Normals[i] = ToUnity(vertex.Normal);
                AoVector2 uv = vertex.Uvs; data.Uvs[i] = new Vector2(uv.X, uv.Y);
                float weight = Mathf.Clamp01(vertex.Joint1Weight);
                data.Weights[i] = new BoneWeight { boneIndex0 = vertex.Joint1, boneIndex1 = vertex.Joint2, weight0 = weight, weight1 = 1f - weight };
            }
            return data;
        }

        private static Vector3 TransformPoint(Matrix4x4[] matrices, int index, Vector3 point) =>
            index >= 0 && index < matrices.Length ? matrices[index].MultiplyPoint3x4(point) : point;

        private static GameObject CreateVisual(BuildData build, Transform parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var bones = new Transform[build.BoneNames.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = new GameObject(build.BoneNames[i]).transform;
                bones[i].SetParent(root.transform, false);
                bones[i].localPosition = build.LocalPositions[i];
                bones[i].localRotation = build.LocalRotations[i];
                bones[i].localScale = Vector3.one * build.Scales[i];
            }
            for (int i = 0; i < bones.Length; i++)
                if (build.Parents[i] >= 0 && build.Parents[i] < bones.Length)
                    bones[i].SetParent(bones[build.Parents[i]], false);
            if (build.Attractors != null)
            {
                for (int i = 0; i < build.Attractors.Length; i++)
                {
                    AttractorData source = build.Attractors[i];
                    var attractor = new GameObject("AOAttractor_" + source.Name).transform;
                    Transform attractorParent = source.BoneIndex >= 0
                        && source.BoneIndex < bones.Length
                        && bones[source.BoneIndex] != null
                        ? bones[source.BoneIndex]
                        : root.transform;
                    attractor.SetParent(attractorParent, false);
                    attractor.localPosition = source.Position;
                    attractor.localRotation = source.Rotation;
                    attractor.localScale = Vector3.one * source.Scale;
                }
            }
            var owned = root.AddComponent<DirectCatMeshVisual>();
            var created = new List<Mesh>();
            foreach (SubmeshData source in build.Submeshes)
            {
                var mesh = new Mesh { name = $"CatMesh_{build.MeshId}_{source.Name}", indexFormat = source.Positions.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.vertices = source.Positions; mesh.normals = source.Normals; mesh.uv = source.Uvs;
                mesh.triangles = source.Triangles; mesh.boneWeights = source.Weights; mesh.bindposes = build.BindPoses;
                mesh.RecalculateTangents(); mesh.RecalculateBounds(); mesh.UploadMeshData(true);
                var child = new GameObject(source.Name); child.transform.SetParent(root.transform, false);
                var renderer = child.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh; renderer.bones = bones; renderer.rootBone = bones.Length > 0 ? bones[0] : root.transform;
                renderer.quality = SkinQuality.Bone2; renderer.sharedMaterial = _materials.Get(source.Material);
                created.Add(mesh);
            }
            owned.SetMeshes(created.ToArray(), true);
            if (created.Count > 0
                && (build.MonsterDataId > 0 || !string.IsNullOrEmpty(build.AnimationProfile)))
            {
                var player = root.AddComponent<CatAnimPlayer>();
                player.Initialize(_database, bones, build.MonsterDataId, 0, build.AnimationProfile);
                player.PlayDeferred("idle", 0f);
            }
            return created.Count > 0 ? root : null;
        }

        private static bool TryClone(int id, Transform parent, string name, out GameObject clone)
        {
            clone = null;
            lock (Gate)
            {
                if (!Prototypes.TryGetValue(id, out CachedPrototype cached) || cached.Root == null) return false;
                clone = UnityEngine.Object.Instantiate(cached.Root, parent, false); clone.name = name; clone.SetActive(true);
                if (clone.TryGetComponent(out DirectCatMeshVisual holder)) holder.SetMeshes(cached.Meshes, false);
                if (clone.TryGetComponent(out CatAnimPlayer player))
                {
                    Transform[] bones = clone.GetComponentInChildren<SkinnedMeshRenderer>(true)?.bones;
                    player.Initialize(_database, bones, cached.MonsterDataId, 0,
                        cached.AnimationProfile);
                    player.PlayDeferred("idle", 0f);
                }
                return true;
            }
        }

        private static void CachePrototype(int id, GameObject visual)
        {
            lock (Gate)
            {
                if (Prototypes.ContainsKey(id)) return;
                if (_cacheRoot == null)
                {
                    var cache = new GameObject("DirectCatMeshCache"); UnityEngine.Object.DontDestroyOnLoad(cache); _cacheRoot = cache.transform;
                }
                GameObject prototype = UnityEngine.Object.Instantiate(visual, _cacheRoot, false); prototype.SetActive(false);
                Mesh[] meshes = Array.ConvertAll(prototype.GetComponentsInChildren<SkinnedMeshRenderer>(true), r => r.sharedMesh);
                if (prototype.TryGetComponent(out DirectCatMeshVisual holder)) holder.SetMeshes(meshes, false);
                if (visual.TryGetComponent(out DirectCatMeshVisual live)) live.SetMeshes(meshes, false);
                int monsterDataId = visual.TryGetComponent(out CatAnimPlayer player) ? player.MonsterDataId : 0;
                string profile = ResolvePlayerAnimationProfile(visual.name);
                Prototypes[id] = new CachedPrototype { Root = prototype, Meshes = meshes,
                    MonsterDataId = monsterDataId, AnimationProfile = profile };
            }
        }

        private static void ClearPrototypes()
        {
            foreach (CachedPrototype entry in Prototypes.Values)
            {
                if (entry.Root != null) UnityEngine.Object.Destroy(entry.Root);
                if (entry.Meshes != null)
                    for (int i = 0; i < entry.Meshes.Length; i++)
                        if (entry.Meshes[i] != null) UnityEngine.Object.Destroy(entry.Meshes[i]);
            }
            Prototypes.Clear();
            if (_cacheRoot != null) UnityEngine.Object.Destroy(_cacheRoot.gameObject);
            _cacheRoot = null;
        }

        private static Vector3 ToUnity(AoVector3 value) => new Vector3(value.X, value.Y, value.Z);

        private static string ResolvePlayerAnimationProfile(string resourceName)
        {
            string normalized = NormalizeName(resourceName);
            if (normalized.StartsWith("athrox", StringComparison.Ordinal)
                || normalized.StartsWith("atrox", StringComparison.Ordinal))
                return "athrox";
            if (normalized.Contains("female"))
                return "female";
            if (normalized.Contains("male"))
                return "male";
            return string.Empty;
        }
    }

    public sealed class DirectCatMeshVisual : MonoBehaviour
    {
        private Mesh[] _meshes;
        private bool _owns;
        public void SetMeshes(Mesh[] meshes, bool owns) { _meshes = meshes; _owns = owns; }
        private void OnDestroy()
        {
            if (!_owns || _meshes == null) return;
            foreach (Mesh mesh in _meshes) if (mesh != null) Destroy(mesh);
        }
    }
}
