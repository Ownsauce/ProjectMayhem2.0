using System;
using System.Collections.Generic;
using System.Text;
using AODB.Common.RDBObjects;
using AO.Client.World;
using AO.Unity.Assets;
using UnityEngine;

namespace AO.Unity.World
{
    /// <summary>Renders server appearance independently of gameplay equipment slots.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class ServerAppearanceVisualController : MonoBehaviour
    {
        private NearbyEntity _entity;
        private Transform _visual;
        private Transform _appliedVisual;
        private string _appliedSignature;
        private string _diagnosedSignature;
        private float _retryAt;
        private ResourceDatabase _database;
        private AoImageTextureCache _images;
        private AbiffMaterialFactory _materials;
        private AbiffLoader _loader;
        private Dictionary<string, int> _skinIds;
        private readonly List<GameObject> _attachments = new();
        private readonly List<Mesh> _ownedMeshes = new();
        private readonly List<Material> _ownedMaterials = new();
        private readonly Dictionary<(int skin, int armor), Texture2D> _bakes = new();
        private readonly Dictionary<Renderer, Material[]> _originalMaterials = new();
        private static readonly string[] Parts = { "hands", "body", "feet", "arms", "legs" };
        private static readonly string[] Places = { "head", "righthand", "lefthand", "rightshoulder",
            "leftshoulder", "back", "hip", "rightthigh", "leftthigh", "rightcrus", "leftcrus",
            "rightarm", "leftarm", "rightforearm", "leftforearm" };

        public bool IsApplied => _appliedVisual != null && _appliedSignature != null;
        public bool HasAppearance => _entity?.EquipmentAppearance != null;
        public bool HasAuthoritativeBodyTextures
        {
            get
            {
                var textures = _entity?.EquipmentAppearance?.Textures;
                if (textures == null) return false;
                for (int index = 0; index < textures.Count; index++)
                    if (textures[index].TextureId > 0) return true;
                return false;
            }
        }

        public void SetAppearance(NearbyEntity entity)
        {
            if (entity?.EquipmentAppearance == null) return;
            if (_entity == null || entity.Appearance != _entity.Appearance
                || !ReferenceEquals(entity.EquipmentAppearance, _entity.EquipmentAppearance))
                _retryAt = 0f;
            _entity = entity;
        }

        public void SetVisual(Transform visual) => _visual = visual;

        public void ClearAppearance()
        {
            _entity = null;
            ReleaseVisuals();
            _appliedSignature = null;
            _appliedVisual = null;
            _retryAt = 0f;
        }

        private void LateUpdate()
        {
            if (!HasAppearance) return;
            var local = GetComponent<CharacterAppearanceController>();
            Transform visual = local != null ? local.BodyVisualRoot : _visual;
            if (visual == null || Time.unscaledTime < _retryAt) return;
            string signature = Signature(_entity);
            if (visual == _appliedVisual && signature == _appliedSignature) return;
            try
            {
                if (!EnsureResources()) { _retryAt = Time.unscaledTime + 1f; return; }
                ReleaseVisuals();
                bool complete = ApplyBody(visual);
                int attached = 0;
                int missingSockets = 0;
                int failedMeshes = 0;
                foreach (var mesh in _entity.EquipmentAppearance.VisibleMeshes())
                {
                    Transform socket = FindSocket(visual, mesh.Position);
                    if (socket == null)
                    {
                        missingSockets++;
                        Debug.LogWarning($"[AO Appearance] {_entity.IdentityInstance}: no socket for "
                            + $"position {mesh.Position} ({PlaceName(mesh.Position)}), mesh {mesh.MeshId}.");
                        continue; // Non-humanoid bodies need not have every socket.
                    }
                    if (mesh.MeshId > int.MaxValue || !_loader.TryCreateVisual((int)mesh.MeshId,
                        socket, mesh.OverrideTextureId, out GameObject root))
                    {
                        failedMeshes++;
                        complete = false;
                        Debug.LogWarning($"[AO Appearance] {_entity.IdentityInstance}: failed mesh "
                            + $"{mesh.MeshId} at {PlaceName(mesh.Position)}, override {mesh.OverrideTextureId}.");
                        continue;
                    }
                    root.name = $"ServerEquip_{mesh.Position}_{mesh.MeshId}_{mesh.Layer}";
                    _attachments.Add(root);
                    foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh != null) _ownedMeshes.Add(filter.sharedMesh);
                    if (mesh.OverrideTextureId > 0)
                        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                            _ownedMaterials.AddRange(renderer.sharedMaterials);
                    attached++;
                }
                _appliedVisual = visual;
                _appliedSignature = complete ? signature : null;
                _retryAt = complete ? 0f : Time.unscaledTime + 5f;
                if (_diagnosedSignature != signature)
                {
                    _diagnosedSignature = signature;
                    Debug.Log($"[AO Appearance] {_entity.IdentityInstance}: applied "
                        + $"{_entity.EquipmentAppearance.Textures.Count} body textures and {attached} meshes "
                        + $"(missing sockets={missingSockets}, failed meshes={failedMeshes}, complete={complete}).");
                }
            }
            catch (Exception exception)
            {
                _retryAt = Time.unscaledTime + 5f;
                Debug.LogWarning($"[AO Appearance] {_entity.IdentityInstance}: {exception.Message}");
            }
        }

        private bool EnsureResources()
        {
            if (_database != null) return true;
            var install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid) return false;
            var database = new ResourceDatabase();
            try { database.Initialize(install.RootPath); }
            catch { database.Dispose(); throw; }
            _database = database;
            _images = new AoImageTextureCache(database);
            _materials = new AbiffMaterialFactory(database);
            _loader = new AbiffLoader(database, _materials, _images);
            _skinIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var info = database.Get<InfoObject>(1);
            if (info?.Types != null && info.Types.TryGetValue(ResourceTypeId.SkinTexture, out var names))
                foreach (var pair in names)
                    if (!string.IsNullOrWhiteSpace(pair.Value)) _skinIds[pair.Value.Trim().Trim('\0')] = pair.Key;
            return true;
        }

        private bool ApplyBody(Transform visual)
        {
            var textures = new Dictionary<int, int>();
            foreach (var texture in _entity.EquipmentAppearance.Textures)
                if (texture.TextureId > 0)
                    textures[texture.Position] = texture.TextureId;

            // Some servers send the five body positions with zero IDs. Those entries
            // mean that no authoritative armor overlay was supplied; leave the body
            // materials alone so CharacterAppearanceController can resolve overlays
            // from the worn item definitions instead of baking a naked skin over them.
            if (textures.Count == 0)
                return true;
            bool complete = true;
            var appliedParts = new HashSet<int>();
            var materialNames = new List<string>();
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] originals = renderer.sharedMaterials;
                Material[] replacements = (Material[])originals.Clone();
                bool changed = false;
                for (int i = 0; i < originals.Length; i++)
                {
                    Material source = originals[i];
                    if (source == null) continue;
                    if (materialNames.Count < 20) materialNames.Add(renderer.name + "/" + source.name);
                    int part = ResolveBodyPart(renderer, source);
                    if (part < 0) continue;
                    string skinName = NakedSkinName(part, _entity.Appearance);
                    if (skinName == null || !_skinIds.TryGetValue(skinName, out int skinId))
                    {
                        complete = false;
                        Debug.LogWarning($"[AO Appearance] {_entity.IdentityInstance}: naked skin "
                            + $"'{skinName ?? "<unsupported appearance>"}' was not found for {Parts[part]}.");
                        continue;
                    }
                    textures.TryGetValue(part, out int armorId);
                    var key = (skinId, armorId);
                    if (!_bakes.TryGetValue(key, out Texture2D baked))
                    {
                        Texture2D skin = _images.GetSkinTexture(skinId);
                        Texture2D armor = armorId > 0 ? _images.GetAoTexture(armorId) : null;
                        if (skin == null || (armorId > 0 && armor == null)) { complete = false; continue; }
                        baked = TextureCompositor.BakeGreenKey(skin, armor, $"SkinArmor_{skinId}_{armorId}");
                        _bakes[key] = baked;
                    }
                    var material = new Material(source);
                    if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", baked);
                    if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", baked);
                    if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", baked);
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                    replacements[i] = material;
                    _ownedMaterials.Add(material);
                    changed = true;
                    appliedParts.Add(part);
                }
                if (changed)
                {
                    _originalMaterials[renderer] = originals;
                    renderer.sharedMaterials = replacements;
                }
            }
            if (appliedParts.Count == 0)
            {
                // Creature/NPC ABIFF meshes commonly have one monolithic material and do not
                // use humanoid skin slots. That is a valid final state, not a retry condition.
                if (_entity.Kind == NearbyEntityKind.Player)
                {
                    complete = false;
                    Debug.LogWarning($"[AO Appearance] {_entity.IdentityInstance}: no body slots matched. "
                        + "Renderer/materials: " + string.Join(", ", materialNames));
                }
            }
            return complete;
        }

        private static int ResolveBodyPart(Renderer renderer, Material material)
        {
            string[] candidates = { material?.name, renderer?.name, renderer is SkinnedMeshRenderer skin
                ? skin.sharedMesh?.name : null };
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                string normalized = candidate.Replace(" (Instance)", "").Replace('\\', '/').ToLowerInvariant();
                int slash = normalized.LastIndexOf('/');
                if (slash >= 0) normalized = normalized.Substring(slash + 1);
                for (int part = 0; part < Parts.Length; part++)
                {
                    string token = Parts[part];
                    if (normalized == token || normalized.StartsWith(token + "_")
                        || normalized.EndsWith("_" + token) || normalized.Contains("_" + token + "_"))
                        return part;
                }
            }
            return -1;
        }

        private static string PlaceName(int position) =>
            position >= 0 && position < Places.Length ? Places[position] : "unknown";

        public static string NakedSkinName(int part, uint appearance)
        {
            if (part < 0 || part >= Parts.Length) return null;
            int breed = (int)((appearance >> 5) & 7);
            int sex = (int)((appearance >> 8) & 3);
            int race = (int)(appearance >> 10);
            if (breed == 4) return Parts[part] + "_athroxmale_naked.png";
            string breedName = breed == 1 ? "solitus" : breed == 2 ? "opifex" : breed == 3 ? "nanomage" : null;
            if (breedName == null) return null;
            string gender = sex == 3 ? "female" : "male";
            string raceName = race == 1 ? "caucation" : race == 2 ? "african" : race == 3 ? "asian" : null;
            if (breed == 1 && raceName == null) return null;
            return Parts[part] + "_" + breedName + gender + (breed == 1 ? "_" + raceName : "") + "_naked.png";
        }

        public static string BodyMeshName(uint appearance)
        {
            int fatness = (int)((appearance >> 3) & 3);
            int breed = (int)((appearance >> 5) & 7);
            string name = breed == 1 ? "solitus" : breed == 2 ? "opifex" : breed == 3 ? "nanomage" : breed == 4 ? "athrox" : null;
            string sex = ((appearance >> 8) & 3) == 3 && breed != 4 ? "_female" : "_male";
            string build = fatness == 0 ? "_thin" : fatness == 2 ? "_fat" : string.Empty;
            return name == null ? null : name + sex + build;
        }

        public static Transform FindSocket(Transform visual, int position)
        {
            if (visual == null || position < 0 || position >= Places.Length) return null;
            foreach (var child in visual.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                if (name.StartsWith("AOAttractor_", StringComparison.OrdinalIgnoreCase)) name = name.Substring(12);
                if (!name.StartsWith("Attractor", StringComparison.OrdinalIgnoreCase)) continue;
                int end = 9;
                while (end < name.Length && char.IsDigit(name[end])) end++;
                if (end > 9 && int.TryParse(name.Substring(9, end - 9), out int index) && index == position + 1)
                    return child;
                if (name.EndsWith("_" + Places[position], StringComparison.OrdinalIgnoreCase)) return child;
            }
            return null;
        }

        private static string Signature(NearbyEntity entity)
        {
            var result = new StringBuilder().Append(entity.Appearance).Append(':')
                .Append(entity.EquipmentAppearance.VisualFlags).Append(':').Append(entity.EquipmentAppearance.HeadMeshId);
            foreach (var t in entity.EquipmentAppearance.Textures)
                result.Append('|').Append(t.Position).Append(':').Append(t.TextureId);
            foreach (var m in entity.EquipmentAppearance.Meshes)
                result.Append('|').Append(m.Position).Append(':').Append(m.MeshId).Append(':').Append(m.OverrideTextureId).Append(':').Append(m.Layer);
            return result.ToString();
        }

        private void ReleaseVisuals()
        {
            foreach (var pair in _originalMaterials) if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            _originalMaterials.Clear();
            foreach (var root in _attachments) if (root != null) { root.SetActive(false); Destroy(root); }
            _attachments.Clear();
            foreach (var mesh in _ownedMeshes) if (mesh != null) Destroy(mesh);
            _ownedMeshes.Clear();
            foreach (var material in _ownedMaterials) if (material != null) Destroy(material);
            _ownedMaterials.Clear();
            foreach (var texture in _bakes.Values) if (texture != null) Destroy(texture);
            _bakes.Clear();
            // Caches live only as long as this actor's current outfit; no unbounded crowd cache.
            _images?.Clear();
            _materials?.Clear();
        }

        private void OnDestroy() { ReleaseVisuals(); _database?.Dispose(); }
    }
}
