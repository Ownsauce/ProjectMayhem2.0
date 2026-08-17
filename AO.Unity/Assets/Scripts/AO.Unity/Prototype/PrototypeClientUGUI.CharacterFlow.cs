using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AO.Core.Characters;
using AO.Core.Stats;
using AO.Unity.AOStyle;
using AO.Unity.World;
using UnityEngine;

namespace AO.Unity.Prototype
{
    public partial class PrototypeClientUGUI
    {
        private sealed class CharacterPreviewSlot
        {
            public CharacterRuntimeBridge Bridge;
            public CharacterAppearanceController Appearance;
            public Transform Transform;
            public TextMesh NameLabel;
            public Vector3 BaseScale = Vector3.one;
        }

        private sealed class ProfessionPreviewActor
        {
            public int ProfessionId;
            public string ProfessionName;
            public string Description;
            public GameObject Root;
            public bool Loaded;
        }

        private readonly CharacterPreviewSlot[] _characterCreatePreviewSlots = new CharacterPreviewSlot[3];
        private readonly Dictionary<int, ProfessionPreviewActor> _professionPreviewActors = new();
        private readonly List<int> _professionPreviewOrder = new();
        private bool _professionStepActive;
        private bool _createFlowUiActive;
        private int _selectedProfessionPreviewId = -1;

        private void BuildCharacterSelectionFlow(RectTransform body, RectTransform root, Font font)
        {
            if (font == null)
                return;

            var overlayParent = root != null ? root : body;
            if (overlayParent == null)
                return;

            var overlay = AOStyleUiFactory.CreatePanel("CharacterSelectionOverlay", overlayParent, new Color(0f, 0f, 0f, 1f));
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = Vector2.zero;
            overlay.offsetMax = Vector2.zero;
            overlay.SetAsLastSibling();

            _characterSelectionRoot = overlay;
            _characterSelectionView = overlay.gameObject.AddComponent<CharacterSelectionWindowView>();
            _characterSelectionView.Initialize(
                font,
                _context?.GetProfessionLookup(),
                HandleCharacterPlayRequested,
                HandleCharacterBackRequested,
                ProvideHeadLookupForDraft,
                PreviewDraftBreedSex,
                PreviewDraftBody,
                PreviewDraftHead,
                HandleCreateFlowStateChanged,
                HandleCharacterCreatePreviewDragged,
                HandleHeadEditOffsetDelta,
                HandleHeadEditEulerDelta,
                HandleHeadEditSaveRequested,
                HandleCharacterSelectionChanged,
                HandleProfessionPreviewSelected,
                HandleProfessionStepVisibilityChanged,
                HandleCharacterProfilesChanged);
            var profiles = LoadPersistedCharacterProfiles();
            if (profiles.Count == 0)
                profiles = CreateSeedProfiles();
            _characterSelectionView.SetProfiles(profiles);
            if (profiles.Count > 0 && !string.IsNullOrWhiteSpace(profiles[0].Name))
                _activeProfileName = profiles[0].Name.Trim();
            SetupCharacterCreatePreview();
            SetCharacterFlowActive(true);
            var bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            bootstrap?.MarkCharacterFlowReady();
        }

        private List<CharacterSelectionWindowView.CharacterProfile> CreateSeedProfiles()
        {
            var profiles = new List<CharacterSelectionWindowView.CharacterProfile>();
            string professionName = "Prototype";
            int professionId = _context?.CharacterProfessionId ?? 1;
            var professionLookup = _context?.GetProfessionLookup();
            if (professionLookup != null && professionLookup.TryGetValue(professionId, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
                professionName = resolved;

            int breedId = _context?.CharacterBreedId ?? 1;
            var sex = _context != null ? _context.CharacterSex : CharacterRuntimeBridge.CharacterSex.Male;
            string breedLabel = breedId switch
            {
                1 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Solitus Female" : "Solitus Male",
                2 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Opifex Female" : "Opifex Male",
                3 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Nanomage Female" : "Nanomage Male",
                4 => "Uni Atrox",
                _ => "Solitus Male"
            };

            profiles.Add(new CharacterSelectionWindowView.CharacterProfile
            {
                Name = _context != null ? _context.GetSelfDisplayName() : "PrototypeCharacter",
                Level = Mathf.Max(1, _context?.CharacterLevel ?? 1),
                BreedId = breedId,
                Sex = sex,
                ProfessionId = professionId,
                ProfessionName = professionName,
                BreedLabel = breedLabel,
                Height = CharacterSelectionWindowView.BodyHeightPreset.Medium,
                Weight = CharacterSelectionWindowView.BodyWeightPreset.Medium,
                HeadMeshKey = _selfBridge != null ? _selfBridge.DebugHeadMeshKey : string.Empty,
                StartPlayfieldId = 0
            });

            return profiles;
        }

        private List<CharacterSelectionWindowView.CharacterProfile> LoadPersistedCharacterProfiles()
        {
            var loaded = new List<CharacterSelectionWindowView.CharacterProfile>();
            if (_clientPrefs?.Characters == null)
                return loaded;

            foreach (var entry in _clientPrefs.Characters)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                    continue;

                loaded.Add(new CharacterSelectionWindowView.CharacterProfile
                {
                    Name = entry.Name.Trim(),
                    Level = Mathf.Max(1, entry.Level),
                    BreedId = entry.BreedId <= 0 ? 1 : entry.BreedId,
                    Sex = Enum.IsDefined(typeof(CharacterRuntimeBridge.CharacterSex), entry.Sex)
                        ? (CharacterRuntimeBridge.CharacterSex)entry.Sex
                        : CharacterRuntimeBridge.CharacterSex.Male,
                    ProfessionId = entry.ProfessionId <= 0 ? 1 : entry.ProfessionId,
                    ProfessionName = string.IsNullOrWhiteSpace(entry.ProfessionName) ? "Prototype" : entry.ProfessionName,
                    BreedLabel = string.IsNullOrWhiteSpace(entry.BreedLabel)
                        ? ResolveBreedLabel(entry.BreedId, entry.Sex)
                        : entry.BreedLabel,
                    Height = Enum.IsDefined(typeof(CharacterSelectionWindowView.BodyHeightPreset), entry.Height)
                        ? (CharacterSelectionWindowView.BodyHeightPreset)entry.Height
                        : CharacterSelectionWindowView.BodyHeightPreset.Medium,
                    Weight = Enum.IsDefined(typeof(CharacterSelectionWindowView.BodyWeightPreset), entry.Weight)
                        ? (CharacterSelectionWindowView.BodyWeightPreset)entry.Weight
                        : CharacterSelectionWindowView.BodyWeightPreset.Medium,
                    HeadMeshKey = entry.HeadMeshKey ?? string.Empty,
                    StartPlayfieldId = entry.StartPlayfieldId <= 0 ? 4604 : entry.StartPlayfieldId
                });
            }

            Debug.Log($"[Prefs] Loaded {loaded.Count} persisted character profile(s) from {GetLocalClientPrefsFilePath()}.");
            return loaded;
        }

        private static string ResolveBreedLabel(int breedId, int sexRaw)
        {
            var sex = Enum.IsDefined(typeof(CharacterRuntimeBridge.CharacterSex), sexRaw)
                ? (CharacterRuntimeBridge.CharacterSex)sexRaw
                : CharacterRuntimeBridge.CharacterSex.Male;

            return breedId switch
            {
                1 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Solitus Female" : "Solitus Male",
                2 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Opifex Female" : "Opifex Male",
                3 => sex == CharacterRuntimeBridge.CharacterSex.Female ? "Nanomage Female" : "Nanomage Male",
                4 => "Uni Atrox",
                _ => "Solitus Male"
            };
        }

        private void HandleCharacterPlayRequested(CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (profile == null)
                return;

            ApplyCharacterProfile(profile);
            _activeProfileName = string.IsNullOrWhiteSpace(profile.Name) ? "PrototypeCharacter" : profile.Name.Trim();
            Debug.Log($"[Prefs] Play selected '{_activeProfileName}'. Deferring layout apply until UI is stable.");
            TryTransitionToProfilePlayfield(profile);
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();
            SetCharacterFlowActive(false);
            StartCoroutine(ApplyWindowLayoutWhenStable(_activeProfileName));
            if (_characterSelectionRoot != null)
                _characterSelectionRoot.gameObject.SetActive(false);
        }

        private void HandleCharacterBackRequested()
        {
            CaptureWindowLayoutForCharacter(_activeProfileName);
            UpdateActiveCharacterLastLocation();
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleCharacterProfilesChanged()
        {
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();
        }

        private void ApplyCharacterProfile(CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (profile == null)
                return;

            _context?.RequestCharacterSettings(
                Mathf.Max(1, profile.Level),
                profile.BreedId <= 0 ? 1 : profile.BreedId,
                profile.ProfessionId <= 0 ? 1 : profile.ProfessionId,
                profile.Sex,
                0);

            if (_selfBridge == null)
                return;

            _selfBridge.Sex = profile.Sex;
            _selfBridge.DisplayNameOverride = string.IsNullOrWhiteSpace(profile.Name) ? "PrototypeCharacter" : profile.Name.Trim();
            _selfBridge.DebugHeadMeshKey = profile.HeadMeshKey ?? string.Empty;

            var character = _selfBridge.Character;
            if (character != null)
            {
                character.SetIdentity(
                    profile.BreedId <= 0 ? 1 : profile.BreedId,
                    profile.ProfessionId <= 0 ? 1 : profile.ProfessionId,
                    new Profession { Name = string.IsNullOrWhiteSpace(profile.ProfessionName) ? "Prototype" : profile.ProfessionName });
                character.SetLevelAndRebuildIp(Mathf.Max(1, profile.Level), preserveSpent: false);
            }

            _selfBridge.transform.localScale = ComputeCharacterScale(profile.Height, profile.Weight);
            var selfAppearance = _selfBridge.GetComponent<CharacterAppearanceController>();
            if (selfAppearance != null)
                selfAppearance.ReloadHeadPreviewOffsets();
        }

        private void TryTransitionToProfilePlayfield(CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (profile == null || profile.StartPlayfieldId <= 0 || _selfBridge == null)
                return;

            if (_authoritativeClient != null && _authoritativeClient.IsServerAvailable)
            {
                // Server-authoritative spawn resolution:
                // use teleport_defaults/highest-point policy on AO.Server and apply only zone_transition.
                _authoritativeClient.RequestAdminTeleport(
                    profile.StartPlayfieldId,
                    Vector3.zero,
                    targetYaw: 0f,
                    useDefaultSpawn: true);
                return;
            }

            var bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (bootstrap == null)
                return;

            var persisted = FindPersistedProfileByName(profile.Name);
            Vector3? worldTarget = null;
            float? yawTarget = null;
            if (persisted != null
                && persisted.HasLastLocation
                && persisted.StartPlayfieldId > 0
                && persisted.StartPlayfieldId == profile.StartPlayfieldId)
            {
                worldTarget = new Vector3(persisted.LastWorldX, persisted.LastWorldY, persisted.LastWorldZ);
                yawTarget = persisted.LastYaw;
            }

            if (bootstrap.ActivePlayfieldId == profile.StartPlayfieldId && worldTarget.HasValue)
            {
                _selfBridge.transform.position = ResolveSafeGroundedWorldPosition(worldTarget.Value, _selfBridge.transform.position);
                var e = _selfBridge.transform.eulerAngles;
                e.y = yawTarget ?? e.y;
                _selfBridge.transform.eulerAngles = e;
                var walker = _selfBridge.GetComponent<AO.Unity.World.PrototypeWalkerController>();
                walker?.RecenterCameraBehindCharacter();
                Debug.Log($"[Prefs] Restored same-PF world position for '{profile.Name}' to {_selfBridge.transform.position:F3}.");
                return;
            }

            if (bootstrap.ActivePlayfieldId == profile.StartPlayfieldId)
                return;

            bool transitioned = bootstrap.TransitionToPlayfield(profile.StartPlayfieldId, _selfBridge.transform, null, yawTarget);
            if (!transitioned)
                Debug.LogWarning($"Failed to transition newly selected character to PF {profile.StartPlayfieldId}.");
            else if (worldTarget.HasValue)
                StartCoroutine(RestoreWorldPositionAfterTransition(profile.StartPlayfieldId, worldTarget.Value, yawTarget ?? 0f));
        }

        private IEnumerator RestoreWorldPositionAfterTransition(int playfieldId, Vector3 worldPosition, float yaw)
        {
            var bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            float timeoutAt = Time.realtimeSinceStartup + 10f;
            while (bootstrap != null
                && (bootstrap.ActivePlayfieldId != playfieldId || bootstrap.ActivePlayfieldGlbLoadInProgress)
                && Time.realtimeSinceStartup < timeoutAt)
            {
                yield return null;
            }

            if (_selfBridge == null)
                yield break;

            _selfBridge.transform.position = ResolveSafeGroundedWorldPosition(worldPosition, _selfBridge.transform.position);
            var e = _selfBridge.transform.eulerAngles;
            e.y = yaw;
            _selfBridge.transform.eulerAngles = e;
            var walker = _selfBridge.GetComponent<AO.Unity.World.PrototypeWalkerController>();
            walker?.RecenterCameraBehindCharacter();

            var rb = _selfBridge.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Debug.Log($"[Prefs] Restored post-transition world position to {_selfBridge.transform.position:F3} for PF {playfieldId}.");
        }

        private static Vector3 ResolveSafeGroundedWorldPosition(Vector3 candidate, Vector3 fallback)
        {
            bool hasCandidate = TryFindWalkableGround(candidate, out var candidateGrounded);
            bool hasFallback = TryFindWalkableGround(fallback, out var fallbackGrounded);

            if (hasCandidate && hasFallback)
            {
                // Reject implausibly high saved points relative to active fallback spawn.
                if (candidateGrounded.y > fallbackGrounded.y + 12f)
                    return fallbackGrounded + Vector3.up * 0.35f;
                return candidateGrounded + Vector3.up * 0.35f;
            }

            if (hasCandidate)
                return candidateGrounded + Vector3.up * 0.35f;
            if (hasFallback)
                return fallbackGrounded + Vector3.up * 0.35f;
            return fallback;
        }

        private static bool IsLikelyPersistableWorldPosition(Vector3 current)
        {
            if (current.y < -50f || current.y > 10000f)
                return false;
            if (!TryFindWalkableGround(current, out var grounded))
                return false;
            return Mathf.Abs(grounded.y - current.y) <= 5f;
        }

        private static bool TryFindWalkableGround(Vector3 source, out Vector3 grounded)
        {
            grounded = source;
            Vector3 origin = new Vector3(source.x, source.y + 80f, source.z);
            var hits = Physics.RaycastAll(origin, Vector3.down, 500f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null)
                    continue;
                if (hit.normal.y < 0.55f)
                    continue;
                grounded = hit.point;
                return true;
            }

            return false;
        }

        private IEnumerator ApplyWindowLayoutWhenStable(string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
                yield break;

            yield return null;
            ApplyWindowLayoutForCharacter(characterName);
            yield return null;
            ApplyWindowLayoutForCharacter(characterName);
            yield return new WaitForSecondsRealtime(0.2f);
            ApplyWindowLayoutForCharacter(characterName);
            yield return new WaitForSecondsRealtime(0.5f);
            ApplyWindowLayoutForCharacter(characterName);
        }

        private IReadOnlyDictionary<string, string> ProvideHeadLookupForDraft(int breedId, CharacterRuntimeBridge.CharacterSex sex)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { string.Empty, "None" }
            };

            string root = Path.Combine(Application.streamingAssetsPath, "AOData", "ItemMeshes");
            if (!Directory.Exists(root))
                return result;

            string breedToken = breedId switch
            {
                1 => "solitus",
                2 => "opifex",
                3 => "nanomage",
                4 => "athrox",
                _ => string.Empty
            };

            string sexPrefixToken = sex switch
            {
                CharacterRuntimeBridge.CharacterSex.Female => "female",
                CharacterRuntimeBridge.CharacterSex.Uni => string.Empty,
                _ => "male"
            };

            string expectedPrefix = string.IsNullOrWhiteSpace(breedToken)
                ? "head_"
                : $"head_{breedToken}{sexPrefixToken}";

            foreach (var file in Directory.EnumerateFiles(root, "*.glb"))
            {
                string meshKey = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(meshKey))
                    continue;
                if (!meshKey.StartsWith("head_", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Use strict prefix matching so "male" does not match inside "female".
                if (!meshKey.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!result.ContainsKey(meshKey))
                    result[meshKey] = meshKey;
            }

            return result;
        }

        private void PreviewDraftBreedSex(int breedId, CharacterRuntimeBridge.CharacterSex sex)
        {
            if (_characterCreatePreviewBridge == null)
                return;

            var previewCharacter = _characterCreatePreviewBridge.Character;
            if (previewCharacter != null)
            {
                int professionId = _context != null ? Mathf.Max(1, _context.CharacterProfessionId) : 1;
                string professionName = "Prototype";
                var lookup = _context?.GetProfessionLookup();
                if (lookup != null && lookup.TryGetValue(professionId, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
                    professionName = resolved;

                previewCharacter.SetIdentity(
                    breedId <= 0 ? 1 : breedId,
                    professionId,
                    new Profession { Name = professionName });
                previewCharacter.SetLevelAndRebuildIp(1, preserveSpent: false);
            }

            _characterCreatePreviewBridge.Sex = sex;
            // Always switch to the first valid head for this breed/sex so preview never
            // keeps a mismatched head from a previous selection.
            string defaultHeadMeshKey = string.Empty;
            var headLookup = ProvideHeadLookupForDraft(breedId, sex);
            if (headLookup != null)
            {
                foreach (var pair in headLookup)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                        continue;
                    defaultHeadMeshKey = pair.Key;
                    break;
                }
            }
            _characterCreatePreviewBridge.DebugHeadMeshKey = defaultHeadMeshKey;
            _characterCreatePreviewBridge.DisplayNameOverride = "Preview";
            _characterCreatePreviewAppearance?.PrewarmCurrentVisual();
            if (_characterCreatePreviewSlots[1]?.NameLabel != null)
                _characterCreatePreviewSlots[1].NameLabel.text = "Preview";
        }

        private void PreviewDraftBody(
            CharacterSelectionWindowView.BodyHeightPreset height,
            CharacterSelectionWindowView.BodyWeightPreset weight)
        {
            if (_characterCreatePreviewBridge == null)
                return;
            _characterCreatePreviewBridge.transform.localScale = ComputeCharacterScale(height, weight);
            _characterCreatePreviewAppearance?.PrewarmCurrentVisual();
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private void PreviewDraftHead(string headMeshKey)
        {
            if (_characterCreatePreviewBridge == null)
                return;
            _characterCreatePreviewBridge.DebugHeadMeshKey = headMeshKey ?? string.Empty;
            _characterCreatePreviewAppearance?.PrewarmCurrentVisual();
        }

        private void SetupCharacterCreatePreview()
        {
            CleanupCharacterCreatePreview();
            _characterCreatePreviewOrbitYaw = 165f;

            var previewRoot = new GameObject("CharacterCreatePreviewRoot");
            previewRoot.transform.position = new Vector3(5000f, -500f, 5000f);
            _characterCreatePreviewRoot = previewRoot;

            string professionName = "Prototype";
            int professionId = _context != null ? Mathf.Max(1, _context.CharacterProfessionId) : 1;
            var lookup = _context?.GetProfessionLookup();
            if (lookup != null && lookup.TryGetValue(professionId, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
                professionName = resolved;

            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var previewActor = new GameObject($"CharacterCreatePreviewActor_{i}");
                previewActor.transform.SetParent(previewRoot.transform, false);
                float x = (i - 1) * 1.8f;
                float z = i == 1 ? 0f : 0.55f;
                previewActor.transform.localPosition = new Vector3(x, 0f, z);
                previewActor.transform.localRotation = Quaternion.identity;
                previewActor.transform.localScale = i == 1 ? Vector3.one : new Vector3(0.9f, 0.9f, 0.9f);

                var bridge = previewActor.AddComponent<CharacterRuntimeBridge>();
                var appearance = previewActor.AddComponent<CharacterAppearanceController>();
                appearance.ConfigureCreateVisualIfMissing(true);
                appearance.ConfigureDisableCharacterMeshAnimations(true);

                var character = new Character($"CharacterPreview_{i}", new Profession { Name = professionName }, 0, breedId: 1, professionId: professionId);
                character.SetLevelAndRebuildIp(1, preserveSpent: false);
                bridge.SetCharacter(character);
                bridge.Sex = CharacterRuntimeBridge.CharacterSex.Male;
                bridge.DebugHeadMeshKey = string.Empty;
                bridge.DisplayNameOverride = string.Empty;
                appearance.PrewarmCurrentVisual();

                var labelGo = new GameObject($"PreviewName_{i}");
                labelGo.transform.SetParent(previewActor.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                var textMesh = labelGo.AddComponent<TextMesh>();
                textMesh.text = string.Empty;
                textMesh.fontSize = 48;
                textMesh.characterSize = 0.03f;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
                textMesh.color = new Color(0.92f, 0.97f, 1f, 0.95f);
                textMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var labelRenderer = textMesh.GetComponent<MeshRenderer>();
                if (labelRenderer != null)
                    labelRenderer.material = textMesh.font.material;

                _characterCreatePreviewSlots[i] = new CharacterPreviewSlot
                {
                    Bridge = bridge,
                    Appearance = appearance,
                    Transform = previewActor.transform,
                    NameLabel = textMesh
                };
            }

            _characterCreatePreviewBridge = _characterCreatePreviewSlots[1]?.Bridge;
            _characterCreatePreviewAppearance = _characterCreatePreviewSlots[1]?.Appearance;

            _characterCreatePreviewTexture = new RenderTexture(1536, 1536, 24, RenderTextureFormat.ARGB32);
            _characterCreatePreviewTexture.name = "CharacterCreatePreviewRT";

            var camGo = new GameObject("CharacterCreatePreviewCamera");
            camGo.transform.SetParent(previewRoot.transform, false);
            _characterCreatePreviewCamera = camGo.AddComponent<Camera>();
            _characterCreatePreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            _characterCreatePreviewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _characterCreatePreviewCamera.fieldOfView = 28f;
            _characterCreatePreviewCamera.nearClipPlane = 0.03f;
            _characterCreatePreviewCamera.farClipPlane = 100f;
            _characterCreatePreviewCamera.targetTexture = _characterCreatePreviewTexture;
            _characterCreatePreviewCamera.enabled = true;
            UpdateCharacterCreatePreviewCameraFraming();

            _characterSelectionView?.SetPreviewTexture(_characterCreatePreviewTexture);
            PreviewDraftBreedSex(1, CharacterRuntimeBridge.CharacterSex.Male);
            PreviewDraftBody(
                CharacterSelectionWindowView.BodyHeightPreset.Medium,
                CharacterSelectionWindowView.BodyWeightPreset.Medium);
            PreviewDraftHead(string.Empty);
            HandleCharacterSelectionChanged(0);
            _ = EnsureProfessionPreviewActorsLoadedAsync();
        }

        private void HandleCharacterSelectionChanged(int selectedIndex)
        {
            if (_characterSelectionView == null)
                return;

            var profiles = _characterSelectionView.GetProfiles();
            if (profiles == null || profiles.Count == 0)
            {
                for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                {
                    var slot = _characterCreatePreviewSlots[i];
                    if (slot?.Transform != null)
                        slot.Transform.gameObject.SetActive(false);
                }
                return;
            }

            int center = Mathf.Clamp(selectedIndex, 0, profiles.Count - 1);
            int[] indices = { center - 1, center, center + 1 };
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var slot = _characterCreatePreviewSlots[i];
                if (slot?.Transform == null)
                    continue;

                int profileIndex = indices[i];
                bool hasProfile = profileIndex >= 0 && profileIndex < profiles.Count;
                slot.Transform.gameObject.SetActive(hasProfile);
                if (!hasProfile)
                    continue;

                var profile = profiles[profileIndex];
                ApplyProfileToPreviewSlot(slot, profile);
            }

            UpdateCharacterSelectionCarouselLayout();
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private static void ApplyProfileToPreviewSlot(CharacterPreviewSlot slot, CharacterSelectionWindowView.CharacterProfile profile)
        {
            if (slot == null || slot.Bridge == null || profile == null)
                return;

            var previewCharacter = slot.Bridge.Character;
            if (previewCharacter != null)
            {
                previewCharacter.SetIdentity(
                    profile.BreedId <= 0 ? 1 : profile.BreedId,
                    profile.ProfessionId <= 0 ? 1 : profile.ProfessionId,
                    new Profession { Name = string.IsNullOrWhiteSpace(profile.ProfessionName) ? "Prototype" : profile.ProfessionName });
                previewCharacter.SetLevelAndRebuildIp(Mathf.Max(1, profile.Level), preserveSpent: false);
            }

            slot.Bridge.Sex = profile.Sex;
            slot.Bridge.DebugHeadMeshKey = profile.HeadMeshKey ?? string.Empty;
            slot.Bridge.DisplayNameOverride = profile.Name ?? string.Empty;
            slot.BaseScale = ComputeCharacterScale(profile.Height, profile.Weight);
            slot.Transform.localScale = slot.BaseScale;
            if (slot.NameLabel != null)
                slot.NameLabel.text = profile.Name ?? string.Empty;
            slot.Appearance?.PrewarmCurrentVisual();
        }

        private void UpdateCharacterSelectionCarouselLayout()
        {
            if (_characterCreatePreviewSlots == null || _characterCreatePreviewSlots.Length == 0)
                return;

            // Match the profession carousel style: selected in front, neighbors curved back.
            // Slots are ordered: 0=left neighbor, 1=selected, 2=right neighbor.
            const float radius = 2.2f;
            const float zCenter = 1.45f;
            const float stepDeg = 42f;
            int[] relative = { -1, 0, 1 };

            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var slot = _characterCreatePreviewSlots[i];
                if (slot?.Transform == null || !slot.Transform.gameObject.activeSelf || slot.Bridge == null)
                    continue;

                int rel = relative[i];
                float angleDeg = rel * stepDeg;
                float rad = angleDeg * Mathf.Deg2Rad;
                float x = Mathf.Sin(rad) * radius;
                float z = zCenter + Mathf.Cos(rad) * radius;
                slot.Transform.localPosition = new Vector3(x, 0f, z);

                // Face camera on yaw only so characters are always readable.
                if (_characterCreatePreviewCamera != null)
                {
                    Vector3 toCamera = _characterCreatePreviewCamera.transform.position - slot.Transform.position;
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 0.0001f)
                        slot.Transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }

                // Keep selected character emphasized.
                float emphasis = rel == 0 ? 1.08f : 0.82f;
                slot.Transform.localScale = slot.BaseScale * emphasis;
                if (slot.NameLabel != null)
                {
                    slot.NameLabel.gameObject.SetActive(rel == 0);
                    slot.NameLabel.transform.localPosition = rel == 0 ? new Vector3(0f, 1.20f, 0f) : new Vector3(0f, 2.2f, 0f);
                }
            }
        }

        private void HandleCreateFlowStateChanged(bool inCreateFlow)
        {
            _createFlowUiActive = inCreateFlow;
            if (!inCreateFlow)
            {
                SetProfessionActorsVisible(false);
                HandleCharacterSelectionChanged(0);
                return;
            }

            if (_characterCreatePreviewAppearance != null)
                _characterCreatePreviewAppearance.PrewarmCurrentVisual();
            if (_characterSelectionView != null && _characterCreatePreviewTexture != null)
                _characterSelectionView.SetPreviewTexture(_characterCreatePreviewTexture);
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var slot = _characterCreatePreviewSlots[i];
                if (slot?.Transform != null)
                    slot.Transform.gameObject.SetActive(i == 1);
            }
            SetProfessionActorsVisible(_professionStepActive);
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private void HandleProfessionStepVisibilityChanged(bool visible)
        {
            _professionStepActive = visible;
            if (visible)
                _ = EnsureProfessionPreviewActorsLoadedAsync();
            SetProfessionActorsVisible(visible);
            if (visible)
            {
                for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                {
                    var slot = _characterCreatePreviewSlots[i];
                    if (slot?.Transform != null)
                        slot.Transform.gameObject.SetActive(false);
                }
            }
            else
            {
                // If we're still inside create flow, keep only the draft preview model active.
                // Do not restore character-selection previews here, or we overwrite visuals/draft continuity.
                if (_createFlowUiActive)
                {
                    for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                    {
                        var slot = _characterCreatePreviewSlots[i];
                        if (slot?.Transform != null)
                            slot.Transform.gameObject.SetActive(i == 1);
                    }
                }
                else
                {
                    HandleCharacterSelectionChanged(0);
                }
            }

            UpdateCharacterCreatePreviewCameraFraming();
            if (visible)
            {
                // Apply layout/facing again after camera framing is set so initial entry
                // doesn't show sideways models until next selection.
                UpdateProfessionPreviewCarouselLayout();
                RefreshPreviewNameLabelFacing();
            }
        }

        private void HandleProfessionPreviewSelected(int professionId, string professionName, string description)
        {
            _selectedProfessionPreviewId = professionId;
            if (_professionPreviewActors.TryGetValue(professionId, out var actor))
            {
                actor.ProfessionName = professionName ?? actor.ProfessionName;
                actor.Description = description ?? actor.Description;
            }

            if (_professionStepActive)
            {
                UpdateProfessionPreviewCarouselLayout();
                FocusSelectedProfessionPreview();
            }
        }

        private void HandleCharacterCreatePreviewDragged(Vector2 delta)
        {
            if (!_characterFlowActive || _characterCreatePreviewCamera == null)
                return;

            _characterCreatePreviewOrbitYaw -= delta.x * 0.18f;
            if (_characterCreatePreviewOrbitYaw < -180f)
                _characterCreatePreviewOrbitYaw += 360f;
            else if (_characterCreatePreviewOrbitYaw > 180f)
                _characterCreatePreviewOrbitYaw -= 360f;

            UpdateCharacterCreatePreviewCameraFraming();
        }

        private void HandleHeadEditOffsetDelta(Vector3 delta)
        {
            if (_characterCreatePreviewAppearance == null)
                return;
            _characterCreatePreviewAppearance.AdjustCurrentDebugHeadMeshOffset(delta);
        }

        private void HandleHeadEditEulerDelta(Vector3 delta)
        {
            if (_characterCreatePreviewAppearance == null)
                return;
            _characterCreatePreviewAppearance.AdjustCurrentDebugHeadMeshEulerOffset(delta);
        }

        private bool HandleHeadEditSaveRequested()
        {
            if (_characterCreatePreviewAppearance == null)
                return false;
            bool ok = _characterCreatePreviewAppearance.SaveHeadPreviewOffsets();
            if (!ok)
                return false;

            var selfAppearance = _selfBridge != null ? _selfBridge.GetComponent<CharacterAppearanceController>() : null;
            if (selfAppearance != null)
                selfAppearance.ReloadHeadPreviewOffsets();
            _characterCreatePreviewAppearance.ReloadHeadPreviewOffsets();
            return true;
        }

        private void UpdateCharacterCreatePreviewCameraFraming()
        {
            if (_characterCreatePreviewCamera == null)
                return;

            Vector3 rootPos;
            Vector3 lookTarget;
            if (_professionStepActive && TryGetSelectedProfessionFocusPoint(out var focusPoint))
            {
                rootPos = focusPoint;
                lookTarget = rootPos + Vector3.up * 1.10f;
            }
            else
            {
                if (_characterCreatePreviewBridge == null)
                    return;
                rootPos = _characterCreatePreviewBridge.transform.position;
                lookTarget = rootPos + Vector3.up * 0.90f;
            }

            Quaternion orbit = Quaternion.Euler(8f, _characterCreatePreviewOrbitYaw, 0f);
            Vector3 offset = orbit * new Vector3(0f, 0.00f, -6.25f);
            _characterCreatePreviewCamera.transform.position = lookTarget + offset;
            _characterCreatePreviewCamera.transform.LookAt(lookTarget);
            RefreshPreviewNameLabelFacing();
        }

        private void FocusSelectedProfessionPreview()
        {
            if (!_professionStepActive)
                return;
            _characterCreatePreviewOrbitYaw = 180f;
            UpdateCharacterCreatePreviewCameraFraming();
        }

        private bool TryGetSelectedProfessionFocusPoint(out Vector3 focus)
        {
            focus = Vector3.zero;
            if (_selectedProfessionPreviewId <= 0)
                return false;
            if (!_professionPreviewActors.TryGetValue(_selectedProfessionPreviewId, out var actor))
                return false;
            if (actor?.Root == null || !actor.Root.activeInHierarchy)
                return false;

            focus = actor.Root.transform.position;
            return true;
        }

        private void SetProfessionActorsVisible(bool visible)
        {
            foreach (var actor in _professionPreviewActors.Values)
            {
                if (actor?.Root != null)
                    actor.Root.SetActive(visible);
            }
        }

        private async Task EnsureProfessionPreviewActorsLoadedAsync()
        {
            if (_characterCreatePreviewRoot == null)
                return;
            if (_professionPreviewActors.Count > 0)
                return;

            var professionLookup = _context?.GetProfessionLookup();
            if (professionLookup == null || professionLookup.Count == 0)
                return;

            var ids = professionLookup.Keys.Where(id => id > 0 && id != 13).OrderBy(id => id).Take(14).ToList();
            for (int i = 0; i < ids.Count; i++)
            {
                int professionId = ids[i];
                string name = professionLookup.TryGetValue(professionId, out var n) ? n : $"Profession {professionId}";

                var actorRoot = new GameObject($"ProfessionPreview_{professionId}_{name}");
                actorRoot.transform.SetParent(_characterCreatePreviewRoot.transform, false);
                bool leftColumn = i < 7;
                int row = leftColumn ? i : (i - 7);
                float x = leftColumn ? -4.8f : 4.8f;
                float z = 3.8f - (row * 1.25f);
                actorRoot.transform.localPosition = new Vector3(x, 0f, z);
                actorRoot.transform.localRotation = Quaternion.Euler(0f, leftColumn ? 35f : -35f, 0f);
                actorRoot.SetActive(false);

                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(actorRoot.transform, false);
                labelGo.transform.localPosition = new Vector3(0f, 2.0f, 0f);
                var tm = labelGo.AddComponent<TextMesh>();
                tm.text = name;
                tm.fontSize = 42;
                tm.characterSize = 0.03f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = new Color(0.82f, 0.93f, 1f, 0.95f);
                tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                string meshPath = ResolveProfessionPreviewMeshPath(name);
                bool loaded = false;
                if (!string.IsNullOrWhiteSpace(meshPath))
                    loaded = await TryInstantiateGlbViaCharacterAppearanceHelper(meshPath, actorRoot.transform);

                if (!loaded)
                {
                    // Fallback placeholder so layout still works even if a GLB is missing.
                    var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    capsule.name = "ProfessionPreviewFallback";
                    capsule.transform.SetParent(actorRoot.transform, false);
                    capsule.transform.localPosition = new Vector3(0f, 1f, 0f);
                    capsule.transform.localScale = new Vector3(0.7f, 1.0f, 0.7f);
                    var renderer = capsule.GetComponent<Renderer>();
                    if (renderer != null)
                        renderer.material.color = leftColumn ? new Color(0.18f, 0.46f, 0.66f, 1f) : new Color(0.16f, 0.36f, 0.56f, 1f);
                }

                _professionPreviewActors[professionId] = new ProfessionPreviewActor
                {
                    ProfessionId = professionId,
                    ProfessionName = name,
                    Root = actorRoot,
                    Loaded = loaded
                };
                _professionPreviewOrder.Add(professionId);
            }

            SetProfessionActorsVisible(_professionStepActive);
            if (_professionStepActive)
            {
                UpdateCharacterCreatePreviewCameraFraming();
                UpdateProfessionPreviewCarouselLayout();
                FocusSelectedProfessionPreview();
            }
        }

        private static string ResolveProfessionPreviewMeshPath(string professionName)
        {
            if (string.IsNullOrWhiteSpace(professionName))
                return null;

            string token = professionName.Trim().ToLowerInvariant()
                .Replace(" ", "_")
                .Replace("-", "_");
            var candidates = new[]
            {
                Path.Combine(Application.streamingAssetsPath, "AOData", "ProfessionMeshes", $"{token}_3d.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", "CharacterMeshes", $"{token}_3d.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", "UI", $"{token}_3d.glb"),
                Path.Combine(Application.streamingAssetsPath, "AOData", $"{token}_3d.glb"),
            };

            if (string.Equals(token, "bureaucrat", StringComparison.OrdinalIgnoreCase))
            {
                string typo = Path.Combine(Application.streamingAssetsPath, "AOData", "ProfessionMeshes", "bureaucraft_3d.glb");
                if (File.Exists(typo))
                    return typo;
            }
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private void UpdateProfessionPreviewCarouselLayout()
        {
            if (_professionPreviewOrder.Count == 0)
                return;

            int selectedIndex = _professionPreviewOrder.IndexOf(_selectedProfessionPreviewId);
            if (selectedIndex < 0)
                selectedIndex = 0;

            const float radius = 5.4f;
            const float zCenter = 1.4f;
            float stepDeg = 360f / _professionPreviewOrder.Count;

            for (int i = 0; i < _professionPreviewOrder.Count; i++)
            {
                int professionId = _professionPreviewOrder[i];
                if (!_professionPreviewActors.TryGetValue(professionId, out var actor) || actor?.Root == null)
                    continue;
                bool isSelected = i == selectedIndex;

                float angleDeg = (i - selectedIndex) * stepDeg;
                float rad = angleDeg * Mathf.Deg2Rad;
                float x = Mathf.Sin(rad) * radius;
                float z = zCenter + Mathf.Cos(rad) * radius;
                actor.Root.transform.localPosition = new Vector3(x, 0f, z);
                actor.Root.transform.localScale = isSelected ? new Vector3(2f, 2f, 2f) : Vector3.one;
                var label = actor.Root.transform.Find("Label");
                if (label != null)
                {
                    label.gameObject.SetActive(isSelected);
                    // Keep selected label near head even when mesh is scaled up.
                    label.localPosition = isSelected ? new Vector3(0f, 1.15f, 0f) : new Vector3(0f, 2.0f, 0f);
                }
                // Always face the active preview camera so we never see backs.
                if (_characterCreatePreviewCamera != null)
                {
                    Vector3 toCamera = _characterCreatePreviewCamera.transform.position - actor.Root.transform.position;
                    // Keep models upright by rotating on yaw only (ignore vertical pitch component).
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 0.0001f)
                        actor.Root.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }
            }
        }

        private static async Task<bool> TryInstantiateGlbViaCharacterAppearanceHelper(string glbPath, Transform parent)
        {
            try
            {
                var helperType = typeof(CharacterAppearanceController);
                var method = helperType.GetMethod(
                    "TryInstantiateGlbWithReflection",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                    return false;

                object taskObj = method.Invoke(null, new object[] { glbPath, parent, null, false });
                if (taskObj is Task<bool> boolTask)
                    return await boolTask;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Profession preview GLB load failed for '{glbPath}': {ex.Message}");
            }

            return false;
        }

        private void RefreshPreviewNameLabelFacing()
        {
            if (_characterCreatePreviewCamera == null)
                return;

            Vector3 camPos = _characterCreatePreviewCamera.transform.position;
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
            {
                var label = _characterCreatePreviewSlots[i]?.NameLabel;
                if (label == null)
                    continue;

                var labelTransform = label.transform;
                Vector3 toCamera = camPos - labelTransform.position;
                if (toCamera.sqrMagnitude > 0.0001f)
                    // TextMesh front-face orientation is opposite of our model labels, so invert to avoid mirrored text.
                    labelTransform.rotation = Quaternion.LookRotation((-toCamera).normalized, Vector3.up);
            }

            foreach (var actor in _professionPreviewActors.Values)
            {
                if (actor?.Root == null)
                    continue;
                var labelTransform = actor.Root.transform.Find("Label");
                if (labelTransform == null)
                    continue;
                Vector3 toCamera = camPos - labelTransform.position;
                if (toCamera.sqrMagnitude > 0.0001f)
                    labelTransform.rotation = Quaternion.LookRotation((-toCamera).normalized, Vector3.up);
            }
        }

        private static Vector3 ComputeCharacterScale(
            CharacterSelectionWindowView.BodyHeightPreset height,
            CharacterSelectionWindowView.BodyWeightPreset weight)
        {
            float heightScale = height switch
            {
                CharacterSelectionWindowView.BodyHeightPreset.Short => 0.92f,
                CharacterSelectionWindowView.BodyHeightPreset.Tall => 1.08f,
                _ => 1f
            };

            float weightScale = weight switch
            {
                CharacterSelectionWindowView.BodyWeightPreset.Skinny => 0.92f,
                CharacterSelectionWindowView.BodyWeightPreset.Fat => 1.08f,
                _ => 1f
            };

            float xzScale = heightScale * weightScale;
            return new Vector3(xzScale, heightScale, xzScale);
        }

        private void SetCharacterFlowActive(bool active)
        {
            _characterFlowActive = active;
            if (active)
                UpdateCharacterCreatePreviewCameraFraming();
            else
            {
                ApplyWindowLayoutForCharacter(_activeProfileName);
                StartCoroutine(ApplyWindowLayoutDeferred(_activeProfileName));
                StartCoroutine(ApplyWindowLayoutWhenStable(_activeProfileName));
            }

            if (_selfBridge == null)
                return;

            var walker = _selfBridge.GetComponent<PrototypeWalkerController>();
            if (walker == null)
                return;

            if (active)
            {
                _savedWalkerEnabled = walker.enabled;
                walker.enabled = false;
                return;
            }

            walker.enabled = _savedWalkerEnabled;
        }

        private void OnChatCommandIssued(string commandText)
        {
            string cmd = (commandText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(cmd))
                return;

            if (string.Equals(cmd, "camp", StringComparison.OrdinalIgnoreCase))
            {
                EnterCharacterSelectFromCamp();
                return;
            }

            _context?.PublishStatus($"Unknown command: {cmd}");
        }

        private void EnterCharacterSelectFromCamp()
        {
            CaptureWindowLayoutForCharacter(_activeProfileName);
            UpdateActiveCharacterLastLocation();
            SavePersistedCharacterProfilesFromSelection();
            SaveLocalClientPreferences();

            SetCharacterFlowActive(true);
            if (_characterSelectionRoot != null)
                _characterSelectionRoot.gameObject.SetActive(true);
            _characterSelectionView?.ReturnToSelectionScreen();
            _context?.PublishStatus("Camped to character selection.");
        }

        private void CleanupCharacterCreatePreview()
        {
            if (_characterCreatePreviewCamera != null)
            {
                _characterCreatePreviewCamera.targetTexture = null;
                Destroy(_characterCreatePreviewCamera.gameObject);
            }

            if (_characterCreatePreviewRoot != null)
                Destroy(_characterCreatePreviewRoot);

            if (_characterCreatePreviewTexture != null)
            {
                _characterCreatePreviewTexture.Release();
                Destroy(_characterCreatePreviewTexture);
            }

            _characterCreatePreviewRoot = null;
            _characterCreatePreviewBridge = null;
            _characterCreatePreviewAppearance = null;
            _characterCreatePreviewCamera = null;
            _characterCreatePreviewTexture = null;
            for (int i = 0; i < _characterCreatePreviewSlots.Length; i++)
                _characterCreatePreviewSlots[i] = null;
        }
    }
}
