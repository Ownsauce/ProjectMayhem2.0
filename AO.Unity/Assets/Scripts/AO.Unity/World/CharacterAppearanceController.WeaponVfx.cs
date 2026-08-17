using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace AO.Unity.World
{
    public sealed partial class CharacterAppearanceController : MonoBehaviour
    {
        private void UpdateWeaponShotVfxLifetime()
        {
            float now = Time.time;
            if (_activeWeaponShotVfx.Count > 0)
            {
                for (int i = _activeWeaponShotVfx.Count - 1; i >= 0; i--)
                {
                    var active = _activeWeaponShotVfx[i];
                    if (active == null || active.Root == null)
                    {
                        _activeWeaponShotVfx.RemoveAt(i);
                        continue;
                    }
                    if (active.ExpireAt > now)
                        continue;

                    Destroy(active.Root);
                    _activeWeaponShotVfx.RemoveAt(i);
                }
            }

            if (_activeProjectiles.Count > 0)
            {
                for (int i = _activeProjectiles.Count - 1; i >= 0; i--)
                {
                    var projectile = _activeProjectiles[i];
                    if (projectile == null || projectile.Root == null)
                    {
                        _activeProjectiles.RemoveAt(i);
                        continue;
                    }

                    float elapsed = now - projectile.StartAt;
                    if (elapsed < 0f)
                        continue;

                    float duration = Mathf.Max(0.01f, projectile.Duration);
                    float t = Mathf.Clamp01(elapsed / duration);
                    Vector3 pos = Vector3.Lerp(projectile.Start, projectile.End, t);
                    if (projectile.ArcHeight > 0.0001f)
                    {
                        float arc = 4f * projectile.ArcHeight * t * (1f - t);
                        pos.y += arc;
                    }
                    projectile.Root.transform.position = pos;
                    if (t >= 1f)
                    {
                        if (projectile.ExplodeOnImpact)
                            SpawnGrenadeImpactVfx(projectile.End);
                        Destroy(projectile.Root);
                        _activeProjectiles.RemoveAt(i);
                    }
                }
            }
        }

        private void ClearWeaponShotVfxState()
        {
            for (int i = 0; i < _activeWeaponShotVfx.Count; i++)
            {
                var active = _activeWeaponShotVfx[i];
                if (active?.Root != null)
                    Destroy(active.Root);
            }
            _activeWeaponShotVfx.Clear();

            for (int i = 0; i < _activeProjectiles.Count; i++)
            {
                var projectile = _activeProjectiles[i];
                if (projectile?.Root != null)
                    Destroy(projectile.Root);
            }
            _activeProjectiles.Clear();

            foreach (var pair in _weaponShotVfxTemplateByKey)
            {
                if (pair.Value != null)
                    Destroy(pair.Value);
            }
            _weaponShotVfxTemplateByKey.Clear();

            foreach (var importer in _weaponShotVfxImporterByKey.Values)
                DisposeImporter(importer);
            _weaponShotVfxImporterByKey.Clear();

            _weaponShotVfxLoadInProgress.Clear();
            _weaponShotVfxFailedKeys.Clear();
            _spellFormatById.Clear();
            _abiffNamesById.Clear();
            _itemMeshKeyByStatelId.Clear();
            _weaponVfxKeyByAoid.Clear();
            _weaponVfxKeyByAttackSkill.Clear();
            _spellFormatsLoaded = false;
            _abiffNamesLoaded = false;
            _itemMeshManifestLoaded = false;
            _weaponVfxMapLoaded = false;

            if (_proceduralSmokeMaterial != null)
                Destroy(_proceduralSmokeMaterial);
            if (_proceduralTracerMaterial != null)
                Destroy(_proceduralTracerMaterial);
            if (_proceduralBulletMaterial != null)
                Destroy(_proceduralBulletMaterial);
            if (_proceduralGrenadeMaterial != null)
                Destroy(_proceduralGrenadeMaterial);
            if (_proceduralHitMaterial != null)
                Destroy(_proceduralHitMaterial);
            _proceduralSmokeMaterial = null;
            _proceduralTracerMaterial = null;
            _proceduralBulletMaterial = null;
            _proceduralGrenadeMaterial = null;
            _proceduralHitMaterial = null;
        }

        private void TriggerWeaponShotVfx(int side, string actionKey)
        {
            if (!enableWeaponShotVfx || _isDestroying || !Application.isPlaying)
                return;
            if (!_activeAttackIsRanged)
                return;
            if (Time.time < (_lastWeaponShotVfxSpawnTime + Mathf.Max(0f, weaponShotVfxMinSpawnIntervalSeconds)))
                return;
            if (string.IsNullOrWhiteSpace(actionKey))
                return;

            int actionSide = ResolveActionSide(actionKey);
            if (actionSide != 0)
            {
                if (ShouldInvertActionSideForWeaponVfx(actionKey))
                    actionSide = -actionSide;
                side = actionSide;
            }

            if (!TryResolveWeaponShotVfxMeshKey(side, out string meshKey))
                return;

            if (!SpawnWeaponShotVfxInstance(meshKey, side, actionKey))
            {
                if (!string.Equals(meshKey, "fx_muzzleflash", StringComparison.OrdinalIgnoreCase))
                {
                    if (!SpawnWeaponShotVfxInstance("fx_muzzleflash", side, actionKey))
                        return;
                }
                else
                {
                    return;
                }
            }

            _lastWeaponShotVfxSpawnTime = Time.time;
        }

        private void TriggerMeleeHitVfx()
        {
            if (!enableMeleeHitVfx || _isDestroying || !Application.isPlaying)
                return;
            if (Time.time < (_lastMeleeHitVfxSpawnTime + Mathf.Max(0f, meleeHitVfxMinSpawnIntervalSeconds)))
                return;

            if (!TryResolveSelectedTargetMeleeImpactPoint(out Vector3 impactPoint))
                return;

            SpawnMeleeImpactVfx(impactPoint);
            _lastMeleeHitVfxSpawnTime = Time.time;
        }

        private bool TryResolveWeaponShotVfxMeshKey(int side, out string meshKey)
        {
            meshKey = string.Empty;
            bool hasMappedDecision = TryResolveMappedWeaponVfxKey(side, out string mappedVfxKey);
            if (hasMappedDecision)
            {
                meshKey = mappedVfxKey;
                return !string.IsNullOrWhiteSpace(meshKey)
                    && !string.Equals(meshKey, "off", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(meshKey, "none", StringComparison.OrdinalIgnoreCase);
            }
            if (useWeaponVfxMapOnly)
                return false;

            if (!TryGetWeaponRawItemForSide(side, out var raw))
            {
                meshKey = "fx_muzzleflash";
                return true;
            }

            if (TryResolveWeaponShotVfxMeshKeyFromSpellFormats(raw, out meshKey))
                return true;

            if (raw?.StatValues != null)
            {
                for (int i = 0; i < raw.StatValues.Count; i++)
                {
                    var statValue = raw.StatValues[i];
                    int rawValue = statValue?.RawValue ?? 0;
                    if (rawValue <= 0)
                        continue;

                    if (!TryGetItemMeshKeyFromRawValue(rawValue, out string candidate))
                        continue;
                    if (!IsLikelyWeaponShotVfxMesh(candidate))
                        continue;

                    meshKey = candidate;
                    return true;
                }
            }

            meshKey = "fx_muzzleflash";
            return true;
        }

        private bool TryResolveMappedWeaponVfxKey(int side, out string vfxKey)
        {
            vfxKey = string.Empty;
            EnsureWeaponVfxMapLoaded();

            int aoid = GetEquippedWeaponAoidForSide(side);
            if (aoid > 0 && _weaponVfxKeyByAoid.TryGetValue(aoid, out string byAoid))
            {
                vfxKey = byAoid?.Trim() ?? string.Empty;
                return true;
            }

            int attackSkillId = _activeAttackSkillId;
            if (attackSkillId > 0 && _weaponVfxKeyByAttackSkill.TryGetValue(attackSkillId, out string bySkill))
            {
                vfxKey = bySkill?.Trim() ?? string.Empty;
                return true;
            }

            if (_activeAttackIsRanged)
            {
                vfxKey = "proc:smoke_tracer";
                return true;
            }

            return false;
        }

        private bool TryResolveWeaponShotVfxMeshKeyFromSpellFormats(AO.Data.Core.Item raw, out string meshKey)
        {
            meshKey = string.Empty;
            if (raw?.SpellData == null || raw.SpellData.Count == 0)
                return false;

            EnsureSpellFormatsLoaded();
            EnsureAbiffNamesLoaded();

            AO.Data.Core.ItemSpellData bestSpell = null;
            int bestPriority = int.MinValue;
            int bestScore = int.MinValue;

            for (int g = 0; g < raw.SpellData.Count; g++)
            {
                var group = raw.SpellData[g];
                if (group?.Items == null)
                    continue;

                int eventValue = group.Event;
                for (int i = 0; i < group.Items.Count; i++)
                {
                    var spell = group.Items[i];
                    if (spell == null)
                        continue;

                    string formatText = ResolveSpellFormatText(spell);
                    bool isAttractor = IsAttractorSpellFormat(spell.SpellID, formatText);
                    if (!isAttractor)
                        continue;

                    int priority = 0;
                    if (eventValue == 10)
                        priority += 30;
                    if (spell.Target == 1)
                        priority += 20;
                    if (spell.Target == 2)
                        priority += 10;
                    if (spell.SpellID == 53065 || spell.SpellID == 53075 || spell.SpellID == 53076 || spell.SpellID == 53204)
                        priority += 10;

                    int score = 0;
                    if (TryResolveSpellEffectMeshKey(spell, out var candidateMesh, out score)
                        && !string.IsNullOrWhiteSpace(candidateMesh))
                    {
                        priority += score;
                        if (bestSpell == null || priority > bestPriority || (priority == bestPriority && score > bestScore))
                        {
                            bestSpell = spell;
                            bestPriority = priority;
                            bestScore = score;
                            meshKey = candidateMesh;
                        }
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(meshKey))
            {
                if (logWeaponShotVfx)
                {
                    Debug.Log($"[WeaponVFX] SpellFormat resolved mesh='{meshKey}' spellId={bestSpell?.SpellID ?? 0} " +
                              $"A={bestSpell?.A ?? 0} B={bestSpell?.B ?? 0} C={bestSpell?.C ?? 0} D={bestSpell?.D ?? 0}");
                }
                return true;
            }

            return false;
        }

        private bool TryGetWeaponRawItemForSide(int side, out AO.Data.Core.Item rawItem)
        {
            rawItem = null;
            long instanceId = GetEquippedInstanceIdForAttackSide(side);
            if (instanceId == 0)
                return false;
            if (AO.Core.Characters.CharacterEquipment.GetItemInstance == null)
                return false;

            var coreInst = AO.Core.Characters.CharacterEquipment.GetItemInstance(instanceId);
            int aoid = coreInst?.Definition?.AOID ?? 0;
            if (aoid <= 0)
                return false;

            AO.Data.Unity.AODataManager.EnsureInstance();
            var dataManager = AO.Data.Unity.AODataManager.Instance;
            rawItem = dataManager?.GetRawItemByAoid(aoid);
            return rawItem != null;
        }

        private int GetEquippedWeaponAoidForSide(int side)
        {
            long instanceId = GetEquippedInstanceIdForAttackSide(side);
            if (instanceId == 0 || AO.Core.Characters.CharacterEquipment.GetItemInstance == null)
                return 0;

            var coreInst = AO.Core.Characters.CharacterEquipment.GetItemInstance(instanceId);
            return coreInst?.Definition?.AOID ?? 0;
        }

        private void EnsureWeaponVfxMapLoaded()
        {
            if (_weaponVfxMapLoaded)
                return;

            _weaponVfxMapLoaded = true;
            _weaponVfxKeyByAoid.Clear();
            _weaponVfxKeyByAttackSkill.Clear();

            string fileName = string.IsNullOrWhiteSpace(weaponVfxMapFileName) ? "weapon_vfx_map.json" : weaponVfxMapFileName.Trim();
            string path = Path.Combine(Application.streamingAssetsPath, "AOData", fileName);
            if (!File.Exists(path))
                return;

            try
            {
                var parsed = JsonConvert.DeserializeObject<WeaponVfxMapFile>(File.ReadAllText(path));
                if (parsed?.Entries == null)
                    return;

                for (int i = 0; i < parsed.Entries.Count; i++)
                {
                    var entry = parsed.Entries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.VfxKey))
                        continue;

                    string key = entry.VfxKey.Trim();
                    if (entry.AOID > 0)
                        _weaponVfxKeyByAoid[entry.AOID] = key;
                    if (entry.AttackSkillId > 0)
                        _weaponVfxKeyByAttackSkill[entry.AttackSkillId] = key;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading {fileName} for weapon VFX map: {ex.Message}");
            }
        }

        private void EnsureSpellFormatsLoaded()
        {
            if (_spellFormatsLoaded)
                return;

            _spellFormatsLoaded = true;
            _spellFormatById.Clear();

            string path = Path.Combine(Application.streamingAssetsPath, "AOData", "spell_formats.json");
            if (!File.Exists(path))
                return;

            try
            {
                var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (raw == null)
                    return;

                foreach (var pair in raw)
                {
                    if (!int.TryParse(pair.Key, out int spellId))
                        continue;
                    string text = pair.Value?.Trim() ?? string.Empty;
                    _spellFormatById[spellId] = text;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading spell_formats.json for weapon VFX: {ex.Message}");
            }
        }

        private void EnsureAbiffNamesLoaded()
        {
            if (_abiffNamesLoaded)
                return;

            _abiffNamesLoaded = true;
            _abiffNamesById.Clear();

            string path = Path.Combine(Application.dataPath, "Resources", itemMeshResourcesFolder, "AbiffNames.json");
            if (!File.Exists(path))
                return;

            try
            {
                var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (map == null)
                    return;

                foreach (var pair in map)
                {
                    if (!int.TryParse(pair.Key, out int id))
                        continue;
                    string mesh = pair.Value?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(mesh))
                        continue;
                    _abiffNamesById[id] = mesh;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading AbiffNames.json for weapon VFX: {ex.Message}");
            }
        }

        private string ResolveSpellFormatText(AO.Data.Core.ItemSpellData spell)
        {
            if (spell == null)
                return string.Empty;

            if (spell.SpellID > 0 && _spellFormatById.TryGetValue(spell.SpellID, out string mapped))
                return mapped ?? string.Empty;
            return spell.SpellFormat ?? string.Empty;
        }

        private static bool IsAttractorSpellFormat(int spellId, string formatText)
        {
            if (spellId == 53055 || spellId == 53065 || spellId == 53075 || spellId == 53076 || spellId == 53204)
                return true;

            if (string.IsNullOrWhiteSpace(formatText))
                return false;

            return formatText.IndexOf("Attractor", StringComparison.OrdinalIgnoreCase) >= 0
                || formatText.IndexOf("Gfx effect", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryResolveSpellEffectMeshKey(AO.Data.Core.ItemSpellData spell, out string meshKey, out int score)
        {
            meshKey = string.Empty;
            score = 0;
            if (spell == null)
                return false;

            var candidates = new int[10];
            int count = 0;
            AddSpellCandidateValue(spell.A, candidates, ref count);
            AddSpellCandidateValue(spell.B, candidates, ref count);
            AddSpellCandidateValue(spell.C, candidates, ref count);
            AddSpellCandidateValue(spell.D, candidates, ref count);
            AddSpellCandidateValue(spell.E, candidates, ref count);
            AddSpellCandidateValue(spell.Texture, candidates, ref count);
            AddSpellCandidateValue(spell.Location, candidates, ref count);
            AddSpellCandidateValue(spell.Stat, candidates, ref count);
            AddSpellCandidateValue(spell.NanoID, candidates, ref count);
            AddSpellCandidateValue(spell.ModifierStat, candidates, ref count);

            for (int i = 0; i < count; i++)
            {
                int value = candidates[i];
                if (TryResolveEffectMeshByNumericCode(value, out string resolved, out int resolvedScore))
                {
                    meshKey = resolved;
                    score = resolvedScore;
                    return true;
                }
            }

            return false;
        }

        private static void AddSpellCandidateValue(object value, int[] candidates, ref int count)
        {
            if (candidates == null || count >= candidates.Length)
                return;
            if (!TryParseFlexibleInt(value, out int parsed))
                return;
            if (parsed <= 0)
                return;

            for (int i = 0; i < count; i++)
            {
                if (candidates[i] == parsed)
                    return;
            }

            candidates[count++] = parsed;
        }

        private bool TryResolveEffectMeshByNumericCode(int code, out string meshKey, out int score)
        {
            meshKey = string.Empty;
            score = 0;
            if (code <= 0)
                return false;

            if (TryGetItemMeshKeyFromRawValue(code, out string fromItemManifest) && IsLikelyWeaponShotVfxMesh(fromItemManifest))
            {
                meshKey = fromItemManifest;
                score = 50;
                return true;
            }

            EnsureAbiffNamesLoaded();
            if (_abiffNamesById.TryGetValue(code, out string fromAbiff) && IsLikelyWeaponShotVfxMesh(fromAbiff))
            {
                meshKey = fromAbiff;
                score = 45;
                return true;
            }

            if (KnownAttractorFxByCode.TryGetValue(code, out string known) && !string.IsNullOrWhiteSpace(known))
            {
                meshKey = known;
                score = 35;
                return true;
            }

            return false;
        }

        private static bool TryParseFlexibleInt(object value, out int result)
        {
            result = 0;
            if (value == null)
                return false;
            if (value is int i)
            {
                result = i;
                return true;
            }
            if (value is long l && l >= int.MinValue && l <= int.MaxValue)
            {
                result = (int)l;
                return true;
            }
            if (value is float f)
            {
                result = Mathf.RoundToInt(f);
                return true;
            }
            if (value is double d)
            {
                result = (int)Math.Round(d);
                return true;
            }

            string text = value.ToString()?.Trim() ?? string.Empty;
            return int.TryParse(text, out result);
        }

        private void EnsureItemMeshManifestLoaded()
        {
            if (_itemMeshManifestLoaded)
                return;

            _itemMeshManifestLoaded = true;
            _itemMeshKeyByStatelId.Clear();

            string path = Path.Combine(Application.streamingAssetsPath, "AOData", "item_mesh_manifest.json");
            if (!File.Exists(path))
                return;

            try
            {
                var entries = JsonConvert.DeserializeObject<List<ItemMeshManifestEntry>>(File.ReadAllText(path));
                if (entries == null)
                    return;

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null || entry.StatelId <= 0 || string.IsNullOrWhiteSpace(entry.MeshName))
                        continue;

                    string key = Path.GetFileNameWithoutExtension(entry.MeshName)?.Trim();
                    if (string.IsNullOrWhiteSpace(key))
                        continue;
                    _itemMeshKeyByStatelId[entry.StatelId] = key;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading item_mesh_manifest.json for weapon VFX: {ex.Message}");
            }
        }

        private bool TryGetItemMeshKeyFromRawValue(int rawValue, out string meshKey)
        {
            meshKey = string.Empty;
            EnsureItemMeshManifestLoaded();
            if (rawValue <= 0)
                return false;
            return _itemMeshKeyByStatelId.TryGetValue(rawValue, out meshKey) && !string.IsNullOrWhiteSpace(meshKey);
        }

        private static bool IsLikelyWeaponShotVfxMesh(string meshKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
                return false;
            string key = meshKey.Trim();
            if (key.IndexOf("muzzle", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (key.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (key.StartsWith("fx_", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private bool SpawnWeaponShotVfxInstance(string meshKey, int side, string actionKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey))
                return false;
            if (meshKey.StartsWith("proc:", StringComparison.OrdinalIgnoreCase))
                return SpawnProceduralWeaponShotVfx(meshKey, side, actionKey);

            Transform anchor = side < 0 ? FindLeftHandAnchor() : FindRightHandAnchor();
            if (anchor == null)
                anchor = transform;

            if (!allowMeshBasedWeaponShotVfx)
                return false;

            if (!_weaponShotVfxTemplateByKey.TryGetValue(meshKey, out var template) || template == null)
            {
                var prefab = Resources.Load<GameObject>($"{itemMeshResourcesFolder}/{meshKey}");
                if (prefab != null)
                {
                    template = new GameObject($"WeaponShotVfxTemplate_{meshKey}");
                    template.SetActive(false);
                    var instance = Instantiate(prefab, template.transform, false);
                    instance.name = meshKey;
                    _weaponShotVfxTemplateByKey[meshKey] = template;
                }
            }

            if ((!_weaponShotVfxTemplateByKey.TryGetValue(meshKey, out template) || template == null)
                && !_weaponShotVfxFailedKeys.Contains(meshKey)
                && !_weaponShotVfxLoadInProgress.Contains(meshKey))
            {
                _ = LoadWeaponShotVfxTemplateFromGlbAsync(meshKey);
            }

            if (template == null)
                return false;

            var shot = Instantiate(template, anchor, false);
            shot.name = $"WeaponShotVfx_{meshKey}";
            shot.SetActive(true);
            ApplyWeaponShotVfxLocalTransform(shot.transform, side);
            _activeWeaponShotVfx.Add(new TimedVfxInstance
            {
                Root = shot,
                ExpireAt = Time.time + Mathf.Max(0.05f, weaponShotVfxLifetimeSeconds)
            });

            if (logWeaponShotVfx)
                Debug.Log($"[WeaponVFX] Spawned '{meshKey}' on {(side < 0 ? "left" : "right")} side");

            return true;
        }

        private bool SpawnProceduralWeaponShotVfx(string vfxKey, int side, string actionKey)
        {
            Transform anchor = side < 0 ? FindLeftHandAnchor() : FindRightHandAnchor();
            if (anchor == null)
                anchor = transform;

            Vector3 localPos = side < 0 ? leftHandShotVfxLocalPosition : rightHandShotVfxLocalPosition;
            Vector3 muzzleWorldPosition = anchor.TransformPoint(localPos);

            var shot = new GameObject($"WeaponShotVfx_{vfxKey}");
            shot.transform.SetParent(anchor, false);
            ApplyWeaponShotVfxLocalTransform(shot.transform, side);

            var smoke = shot.AddComponent<ParticleSystem>();
            smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = smoke.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = Mathf.Max(0.05f, proceduralSmokeLifetime);
            main.startLifetime = new ParticleSystem.MinMaxCurve(proceduralSmokeLifetime * 0.55f, proceduralSmokeLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.62f, 0.62f, 0.62f, 0.55f), new Color(0.75f, 0.75f, 0.75f, 0.35f));
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = smoke.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(proceduralSmokeBurstCount, 1, 64)) });

            var shape = smoke.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.radius = 0.01f;
            shape.angle = 14f;

            var colorOverLifetime = smoke.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var colorGradient = new Gradient();
            colorGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.84f, 0.84f, 0.84f), 0f),
                    new GradientColorKey(new Color(0.56f, 0.56f, 0.56f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.6f, 0f),
                    new GradientAlphaKey(0.35f, 0.45f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(colorGradient);

            var sizeOverLifetime = smoke.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.45f),
                new Keyframe(0.4f, 1f),
                new Keyframe(1f, 1.35f));
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var smokeRenderer = smoke.GetComponent<ParticleSystemRenderer>();
            if (smokeRenderer != null)
            {
                if (_proceduralSmokeMaterial == null)
                    _proceduralSmokeMaterial = new Material(Shader.Find("Particles/Standard Unlit"));
                smokeRenderer.material = _proceduralSmokeMaterial;
                smokeRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            Vector3 tracerDirection = ResolveProceduralTracerDirection(shot.transform, anchor);
            Vector3 tracerEnd = muzzleWorldPosition + (tracerDirection * Mathf.Max(2f, proceduralTracerDistance));
            if (TryResolveSelectedTargetShotPoint(out Vector3 targetPoint))
            {
                tracerEnd = targetPoint;
                if ((tracerEnd - muzzleWorldPosition).sqrMagnitude < 0.0001f)
                    tracerEnd = muzzleWorldPosition + (tracerDirection * Mathf.Max(2f, proceduralTracerDistance));
            }

            bool isPistolProjectileMode = string.Equals(vfxKey, "proc:pistol_bullets", StringComparison.OrdinalIgnoreCase);
            bool isGrenadeProjectileMode = string.Equals(vfxKey, "proc:grenade_projectile", StringComparison.OrdinalIgnoreCase);
            if (!isPistolProjectileMode && !isGrenadeProjectileMode)
            {
                // Simple bullet tracer from muzzle forward.
                var tracer = shot.AddComponent<LineRenderer>();
                tracer.useWorldSpace = true;
                tracer.positionCount = 2;
                tracer.SetPosition(0, muzzleWorldPosition);
                tracer.SetPosition(1, tracerEnd);
                tracer.alignment = LineAlignment.View;
                tracer.textureMode = LineTextureMode.Stretch;
                tracer.widthMultiplier = Mathf.Max(0.001f, proceduralTracerWidth);
                if (_proceduralTracerMaterial == null)
                    _proceduralTracerMaterial = new Material(Shader.Find("Sprites/Default"));
                tracer.material = _proceduralTracerMaterial;
                var tracerGradient = new Gradient();
                tracerGradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(new Color(1f, 0.92f, 0.5f), 0f),
                        new GradientColorKey(new Color(1f, 0.72f, 0.2f), 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.95f, 0f),
                        new GradientAlphaKey(0.75f, 0.35f),
                        new GradientAlphaKey(0f, 1f)
                    });
                tracer.colorGradient = tracerGradient;
            }

            smoke.Play(true);

            if (isPistolProjectileMode)
            {
                int projectileCount = ResolveProceduralProjectileCount(actionKey);
                SpawnProceduralProjectileBursts(shot.transform, muzzleWorldPosition, tracerEnd, projectileCount);
            }
            else if (isGrenadeProjectileMode)
            {
                SpawnProceduralGrenadeProjectile(shot.transform, muzzleWorldPosition, tracerEnd);
            }

            float vfxLife = Mathf.Max(weaponShotVfxLifetimeSeconds, proceduralSmokeLifetime, proceduralTracerLifetime, 0.08f);
            _activeWeaponShotVfx.Add(new TimedVfxInstance
            {
                Root = shot,
                ExpireAt = Time.time + vfxLife
            });

            if (logWeaponShotVfx)
                Debug.Log($"[WeaponVFX] Spawned procedural effect '{vfxKey}' on {(side < 0 ? "left" : "right")} side");

            return true;
        }

        private Vector3 ResolveProceduralTracerDirection(Transform shotRoot, Transform handAnchor)
        {
            Vector3 actorForward = transform.forward.sqrMagnitude > 0.000001f ? transform.forward.normalized : Vector3.forward;
            Vector3 candidate = Vector3.zero;

            if (shotRoot != null && shotRoot.forward.sqrMagnitude > 0.000001f)
                candidate = shotRoot.forward.normalized;

            if (handAnchor != null)
            {
                if (handAnchor.forward.sqrMagnitude > 0.000001f)
                    candidate = handAnchor.forward.normalized;
            }

            if (candidate.sqrMagnitude <= 0.000001f)
                return actorForward;

            // Keep shot travel generally in front of actor even when hand-bone local axes are mirrored.
            if (Vector3.Dot(candidate, actorForward) < 0f)
                candidate = -candidate;

            // Avoid steep downward/upward muzzle vectors from problematic rig axes.
            float vertical = Mathf.Abs(Vector3.Dot(candidate, Vector3.up));
            float blend = vertical > 0.75f ? 0.2f : 0.45f;
            return Vector3.Slerp(actorForward, candidate, blend).normalized;
        }

        private static bool ShouldInvertActionSideForWeaponVfx(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey))
                return false;

            string key = actionKey.Trim();
            return key.StartsWith("1h ranged shot", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("1h ranged burst", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("1h ranged full auto", StringComparison.OrdinalIgnoreCase);
        }

        private static int ResolveProceduralProjectileCount(string actionKey)
        {
            if (string.IsNullOrWhiteSpace(actionKey))
                return 1;

            string key = actionKey.Trim();
            if (key.IndexOf("full auto", StringComparison.OrdinalIgnoreCase) >= 0)
                return 6;
            if (key.IndexOf("burst", StringComparison.OrdinalIgnoreCase) >= 0)
                return 3;
            return 1;
        }

        private void SpawnProceduralProjectileBursts(Transform parent, Vector3 start, Vector3 end, int count)
        {
            if (count <= 0)
                return;

            Vector3 dir = end - start;
            float distance = dir.magnitude;
            if (distance <= 0.01f)
                return;
            if (_proceduralBulletMaterial == null)
            {
                var shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                _proceduralBulletMaterial = shader != null ? new Material(shader) : null;
                if (_proceduralBulletMaterial != null)
                    _proceduralBulletMaterial.color = new Color(0.05f, 0.05f, 0.05f, 1f);
            }

            float speed = 85f;
            float duration = Mathf.Clamp(distance / speed, 0.03f, 0.3f);
            int clampedCount = Mathf.Clamp(count, 1, 12);
            float startTime = Time.time;
            float stagger = clampedCount > 1 ? 0.02f : 0f;

            for (int i = 0; i < clampedCount; i++)
            {
                var bullet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (bullet == null)
                    continue;
                bullet.name = "ProjectileBullet";
                bullet.transform.position = start;
                bullet.transform.localScale = Vector3.one * 0.07f;

                var collider = bullet.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);

                var renderer = bullet.GetComponent<Renderer>();
                if (renderer != null && _proceduralBulletMaterial != null)
                    renderer.material = _proceduralBulletMaterial;

                Vector3 spread = Vector3.zero;
                if (clampedCount > 1)
                {
                    float lateral = (i - (clampedCount - 1) * 0.5f) * 0.03f;
                    spread = transform.right * lateral;
                }

                _activeProjectiles.Add(new ActiveProjectile
                {
                    Root = bullet,
                    Start = start + spread,
                    End = end + spread * 0.2f,
                    StartAt = startTime + (stagger * i),
                    Duration = duration,
                    ArcHeight = 0f,
                    ExplodeOnImpact = false
                });
            }
        }

        private void SpawnProceduralGrenadeProjectile(Transform parent, Vector3 start, Vector3 end)
        {
            var grenade = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (grenade == null)
                return;

            grenade.name = "ProjectileGrenade";
            grenade.transform.position = start;
            grenade.transform.localScale = Vector3.one * 0.16f;
            if (parent != null)
                grenade.transform.SetParent(parent, true);

            var collider = grenade.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            if (_proceduralGrenadeMaterial == null)
            {
                var shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                _proceduralGrenadeMaterial = shader != null ? new Material(shader) : null;
                if (_proceduralGrenadeMaterial != null)
                    _proceduralGrenadeMaterial.color = new Color(0.24f, 0.32f, 0.15f, 1f);
            }

            var renderer = grenade.GetComponent<Renderer>();
            if (renderer != null && _proceduralGrenadeMaterial != null)
                renderer.material = _proceduralGrenadeMaterial;

            float distance = Vector3.Distance(start, end);
            float speed = 34f;
            float duration = Mathf.Clamp(distance / speed, 0.18f, 0.85f);
            float arcHeight = Mathf.Clamp(distance * 0.15f, 0.6f, 2.4f);

            _activeProjectiles.Add(new ActiveProjectile
            {
                Root = grenade,
                Start = start,
                End = end,
                StartAt = Time.time,
                Duration = duration,
                ArcHeight = arcHeight,
                ExplodeOnImpact = true
            });
        }

        private void SpawnGrenadeImpactVfx(Vector3 at)
        {
            var impact = new GameObject("GrenadeImpact");
            impact.transform.position = at;

            var ps = impact.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.18f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.62f, 0.18f, 0.95f),
                new Color(0.42f, 0.42f, 0.42f, 0.7f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                if (_proceduralSmokeMaterial == null)
                    _proceduralSmokeMaterial = new Material(Shader.Find("Particles/Standard Unlit"));
                renderer.material = _proceduralSmokeMaterial;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            ps.Play(true);
            Destroy(impact, 0.5f);
        }

        private void SpawnMeleeImpactVfx(Vector3 at)
        {
            var impact = new GameObject("MeleeImpact");
            impact.transform.position = at;

            if (_proceduralHitMaterial == null)
            {
                var shader = Shader.Find("Particles/Standard Unlit");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");
                _proceduralHitMaterial = shader != null ? new Material(shader) : null;
            }

            // 1) Hit Flash: compact strobe at contact point.
            var flashGo = new GameObject("HitFlash");
            flashGo.transform.SetParent(impact.transform, false);
            var flashPs = flashGo.AddComponent<ParticleSystem>();
            flashPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var flashMain = flashPs.main;
            flashMain.loop = false;
            flashMain.playOnAwake = false;
            flashMain.duration = 0.05f;
            flashMain.startLifetime = new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            flashMain.startSpeed = 0f;
            flashMain.startSize = new ParticleSystem.MinMaxCurve(Mathf.Clamp(meleeHitFlashSize, 0.06f, 0.45f));
            flashMain.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.98f, 0.9f, 0.9f),
                new Color(1f, 0.85f, 0.62f, 0.7f));
            flashMain.simulationSpace = ParticleSystemSimulationSpace.World;
            flashMain.maxParticles = 2;
            var flashEmission = flashPs.emission;
            flashEmission.enabled = true;
            flashEmission.rateOverTime = 0f;
            flashEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var flashShape = flashPs.shape;
            flashShape.enabled = true;
            flashShape.shapeType = ParticleSystemShapeType.Sphere;
            flashShape.radius = 0.004f;
            var flashSize = flashPs.sizeOverLifetime;
            flashSize.enabled = true;
            flashSize.size = new ParticleSystem.MinMaxCurve(
                1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.15f),
                    new Keyframe(0.4f, 1f),
                    new Keyframe(1f, 0.1f)));
            var flashColor = flashPs.colorOverLifetime;
            flashColor.enabled = true;
            var flashGradient = new Gradient();
            flashGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.98f, 0.9f), 0f),
                    new GradientColorKey(new Color(1f, 0.75f, 0.4f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.35f, 0.3f),
                    new GradientAlphaKey(0f, 1f)
                });
            flashColor.color = new ParticleSystem.MinMaxGradient(flashGradient);
            var flashRenderer = flashPs.GetComponent<ParticleSystemRenderer>();
            if (flashRenderer != null)
            {
                flashRenderer.material = _proceduralHitMaterial;
                flashRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            }

            // 2) Muted-yellow spiky sparks: short-lived, sharp, and local.
            var sparkGo = new GameObject("HitSparks");
            sparkGo.transform.SetParent(impact.transform, false);
            var sparks = sparkGo.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sparksMain = sparks.main;
            sparksMain.loop = false;
            sparksMain.playOnAwake = false;
            sparksMain.duration = 0.07f;
            sparksMain.startLifetime = new ParticleSystem.MinMaxCurve(0.035f, 0.085f);
            sparksMain.startSpeed = new ParticleSystem.MinMaxCurve(1.15f, 2.6f);
            sparksMain.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.024f);
            sparksMain.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.88f, 0.82f, 0.48f, 0.86f),
                new Color(0.73f, 0.66f, 0.28f, 0.56f));
            sparksMain.simulationSpace = ParticleSystemSimulationSpace.World;
            sparksMain.maxParticles = Mathf.Clamp(meleeHitSparkCount + 1, 3, 8);
            var sparksEmission = sparks.emission;
            sparksEmission.enabled = true;
            sparksEmission.rateOverTime = 0f;
            sparksEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(meleeHitSparkCount, 2, 6)) });
            var sparksShape = sparks.shape;
            sparksShape.enabled = true;
            sparksShape.shapeType = ParticleSystemShapeType.Sphere;
            sparksShape.radius = 0.006f;
            var sparksVel = sparks.velocityOverLifetime;
            sparksVel.enabled = true;
            sparksVel.radial = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            var sparksColor = sparks.colorOverLifetime;
            sparksColor.enabled = true;
            var sparksGradient = new Gradient();
            sparksGradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.9f, 0.84f, 0.5f), 0f),
                    new GradientColorKey(new Color(0.72f, 0.62f, 0.24f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0.92f, 0f),
                    new GradientAlphaKey(0.72f, 0.25f),
                    new GradientAlphaKey(0f, 1f)
                });
            sparksColor.color = new ParticleSystem.MinMaxGradient(sparksGradient);
            var sparksRenderer = sparks.GetComponent<ParticleSystemRenderer>();
            if (sparksRenderer != null)
            {
                sparksRenderer.material = _proceduralHitMaterial;
                sparksRenderer.renderMode = ParticleSystemRenderMode.Stretch;
                sparksRenderer.lengthScale = 1.85f;
                sparksRenderer.velocityScale = 0.28f;
            }

            flashPs.Play(true);
            sparks.Play(true);
            Destroy(impact, Mathf.Max(0.12f, meleeHitVfxLifetimeSeconds));
        }

        private bool TryResolveSelectedTargetShotPoint(out Vector3 point)
        {
            point = Vector3.zero;

            var uiContext = AO.Unity.Prototype.PrototypeUiContext.Active;
            var selectedTarget = uiContext?.SelectedTarget;
            if (selectedTarget == null)
                return false;

            // Ignore self-target for weapon tracers.
            if (selectedTarget == bridge || selectedTarget.transform == transform)
                return false;

            var targetAppearance = selectedTarget.GetComponent<CharacterAppearanceController>();
            if (targetAppearance != null && targetAppearance.TryResolveMeshAimPoint(out point))
                return true;

            var targetRenderer = selectedTarget.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (targetRenderer != null)
            {
                point = targetRenderer.bounds.center;
                return true;
            }

            var controller = selectedTarget.GetComponent<CharacterController>();
            if (controller != null)
            {
                point = selectedTarget.transform.TransformPoint(controller.center);
                return true;
            }

            point = selectedTarget.transform.position + new Vector3(0f, 1.2f, 0f);
            return true;
        }

        private bool TryResolveSelectedTargetMeleeImpactPoint(out Vector3 point)
        {
            point = Vector3.zero;

            var uiContext = AO.Unity.Prototype.PrototypeUiContext.Active;
            var selectedTarget = uiContext?.SelectedTarget;
            if (selectedTarget == null)
                return false;

            if (selectedTarget == bridge || selectedTarget.transform == transform)
                return false;

            if (TryResolveSurfaceImpactPointFromTarget(selectedTarget, out point))
                return true;

            return TryResolveSelectedTargetShotPoint(out point);
        }

        private bool TryResolveSurfaceImpactPointFromTarget(CharacterRuntimeBridge selectedTarget, out Vector3 point)
        {
            point = Vector3.zero;
            if (selectedTarget == null)
                return false;

            Bounds bounds;
            bool hasBounds = false;

            var targetAppearance = selectedTarget.GetComponent<CharacterAppearanceController>();
            if (targetAppearance != null && targetAppearance.TryResolvePrimaryVisibleBounds(out bounds))
            {
                hasBounds = true;
            }
            else
            {
                var targetRenderer = selectedTarget.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (targetRenderer != null)
                {
                    bounds = targetRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds = default;
                }
            }

            if (!hasBounds)
                return false;

            Vector3 attackerOrigin = transform.position + new Vector3(0f, 1.05f, 0f);
            Vector3 jitteredTarget = bounds.center;
            Vector3 e = bounds.extents;
            jitteredTarget += new Vector3(
                UnityEngine.Random.Range(-0.35f, 0.35f) * e.x,
                UnityEngine.Random.Range(-0.45f, 0.45f) * e.y,
                UnityEngine.Random.Range(-0.35f, 0.35f) * e.z);

            Vector3 toJittered = jitteredTarget - attackerOrigin;
            if (toJittered.sqrMagnitude < 0.0001f)
                toJittered = transform.forward.sqrMagnitude > 0.0001f ? transform.forward : Vector3.forward;
            Vector3 dir = toJittered.normalized;

            var ray = new Ray(attackerOrigin, dir);
            if (bounds.IntersectRay(ray, out float enterDistance))
            {
                // Slightly pull the point toward attacker so effect renders above the surface.
                point = ray.origin + (ray.direction * Mathf.Max(0f, enterDistance));
                point -= ray.direction * 0.02f;
                return true;
            }

            point = bounds.ClosestPoint(attackerOrigin);
            if ((point - bounds.center).sqrMagnitude < 0.0001f)
                point = bounds.center + dir * Mathf.Max(0.04f, bounds.extents.magnitude * 0.25f);
            return true;
        }

        private bool TryResolveMeshAimPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (TryResolvePrimaryVisibleBounds(out var bounds))
            {
                point = bounds.center;
                return true;
            }
            return false;
        }

        private bool TryResolvePrimaryVisibleBounds(out Bounds bounds)
        {
            bounds = default;

            var renderers = _spawnedPrefabVisual != null
                ? _spawnedPrefabVisual.GetComponentsInChildren<Renderer>(true)
                : GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            Renderer best = null;
            float bestVolume = -1f;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled)
                    continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                    continue;

                string n = r.name ?? string.Empty;
                if (n.IndexOf("weapon", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("fx", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                var b = r.bounds;
                float volume = b.size.x * b.size.y * b.size.z;
                if (volume <= bestVolume)
                    continue;

                best = r;
                bestVolume = volume;
            }

            if (best == null)
                return false;

            bounds = best.bounds;
            return true;
        }

        private async Task LoadWeaponShotVfxTemplateFromGlbAsync(string meshKey)
        {
            if (string.IsNullOrWhiteSpace(meshKey) || _isDestroying)
                return;
            if (_weaponShotVfxTemplateByKey.ContainsKey(meshKey))
                return;
            if (_weaponShotVfxLoadInProgress.Contains(meshKey))
                return;

            _weaponShotVfxLoadInProgress.Add(meshKey);
            object importer = null;
            GameObject template = null;
            try
            {
                string meshPath = ResolveItemMeshPath(meshKey);
                meshPath = GlbDataUriLoadPathResolver.Resolve(meshPath, true);
                if (string.IsNullOrWhiteSpace(meshPath) || !File.Exists(meshPath))
                {
                    _weaponShotVfxFailedKeys.Add(meshKey);
                    return;
                }

                template = new GameObject($"WeaponShotVfxTemplate_{meshKey}");
                template.SetActive(false);

                bool instantiated = await TryInstantiateGlbWithReflection(
                    meshPath,
                    template.transform,
                    loadedImporter => importer = loadedImporter,
                    disableAnimations: true);
                if (!instantiated)
                {
                    _weaponShotVfxFailedKeys.Add(meshKey);
                    return;
                }
                if (_isDestroying)
                    return;

                _weaponShotVfxTemplateByKey[meshKey] = template;
                _weaponShotVfxImporterByKey[meshKey] = importer;
                importer = null;

                if (logWeaponShotVfx)
                    Debug.Log($"[WeaponVFX] Loaded template '{meshKey}' from GLB");
            }
            catch (Exception ex)
            {
                _weaponShotVfxFailedKeys.Add(meshKey);
                Debug.LogWarning($"Weapon VFX template load failed for '{meshKey}': {ex.Message}");
            }
            finally
            {
                _weaponShotVfxLoadInProgress.Remove(meshKey);
                DisposeImporter(importer);
                if (template != null
                    && (!_weaponShotVfxTemplateByKey.TryGetValue(meshKey, out var kept) || kept != template))
                {
                    Destroy(template);
                }
            }
        }

        private void ApplyWeaponShotVfxLocalTransform(Transform root, int side)
        {
            if (root == null)
                return;

            bool left = side < 0;
            root.localPosition = left ? leftHandShotVfxLocalPosition : rightHandShotVfxLocalPosition;
            root.localRotation = Quaternion.Euler(left ? leftHandShotVfxLocalEuler : rightHandShotVfxLocalEuler);
            root.localScale = left ? leftHandShotVfxLocalScale : rightHandShotVfxLocalScale;
        }

        private long GetEquippedInstanceIdForAttackSide(int side)
        {
            var equipped = bridge?.Character?.Equipment?.GetAllEquipped();
            if (equipped == null)
                return 0;

            if (side < 0 && equipped.TryGetValue(leftHandEquipSlotId, out var leftInst) && leftInst != 0)
                return leftInst;
            if (side > 0 && equipped.TryGetValue(rightHandEquipSlotId, out var rightInst) && rightInst != 0)
                return rightInst;

            // Fallback to any occupied hand.
            if (equipped.TryGetValue(rightHandEquipSlotId, out var rightAny) && rightAny != 0)
                return rightAny;
            if (equipped.TryGetValue(leftHandEquipSlotId, out var leftAny) && leftAny != 0)
                return leftAny;
            return 0;
        }
    }
}
