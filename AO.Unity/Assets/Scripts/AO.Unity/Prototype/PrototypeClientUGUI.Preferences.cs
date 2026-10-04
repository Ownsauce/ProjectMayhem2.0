using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AO.Client.World;
using AO.Unity.AOStyle;
using AO.Unity.World;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public partial class PrototypeClientUGUI
    {
        private void SavePersistedCharacterProfilesFromSelection()
        {
            if (_characterSelectionView == null)
                return;

            _clientPrefs ??= new LocalClientPreferencesFile();
            var previousByName = new Dictionary<string, PersistedCharacterProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in _clientPrefs.Characters)
            {
                if (existing == null || string.IsNullOrWhiteSpace(existing.Name))
                    continue;
                previousByName[existing.Name.Trim()] = existing;
            }

            var profiles = _characterSelectionView.GetProfiles();
            _clientPrefs.Characters.Clear();
            if (profiles == null)
                return;

            foreach (var profile in profiles)
            {
                if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
                    continue;

                NearbyEntity liveAppearance = _gameServerSession?.CurrentControlledAppearance;
                if (liveAppearance != null
                    && int.TryParse(profile.ServerCharacterId, out int serverCharacterId)
                    && liveAppearance.IdentityInstance == serverCharacterId)
                    CacheSelectedServerAppearance(profile, new[] { liveAppearance });

                PersistedCharacterProfile previous = null;
                previousByName.TryGetValue(profile.Name.Trim(), out previous);

                bool reusePreviousSpawnState = previous != null
                    && previous.BreedId == profile.BreedId
                    && previous.Sex == (int)profile.Sex
                    && previous.ProfessionId == profile.ProfessionId
                    && string.Equals(previous.HeadMeshKey ?? string.Empty, profile.HeadMeshKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                _clientPrefs.Characters.Add(new PersistedCharacterProfile
                {
                    Name = profile.Name.Trim(),
                    Level = Mathf.Max(1, profile.Level),
                    BreedId = profile.BreedId,
                    Sex = (int)profile.Sex,
                    ProfessionId = profile.ProfessionId,
                    ProfessionName = profile.ProfessionName ?? string.Empty,
                    BreedLabel = profile.BreedLabel ?? string.Empty,
                    Height = (int)profile.Height,
                    Weight = (int)profile.Weight,
                    HeadMeshKey = profile.HeadMeshKey ?? string.Empty,
                    CachedAppearanceValue = profile.CachedAppearanceValue,
                    CachedVisualFlags = profile.CachedVisualFlags,
                    CachedHeadMeshId = profile.CachedHeadMeshId,
                    CachedAppearanceTextures = profile.CachedAppearanceTextures?.ToList() ?? new List<AppearanceTexture>(),
                    CachedAppearanceMeshes = profile.CachedAppearanceMeshes?.ToList() ?? new List<AppearanceMesh>(),
                    // Keep runtime-updated playfield as source of truth; selection profile can be stale.
                    StartPlayfieldId = reusePreviousSpawnState && previous != null
                        ? (previous.StartPlayfieldId <= 0 ? 4604 : previous.StartPlayfieldId)
                        : (profile.StartPlayfieldId <= 0 ? 4604 : profile.StartPlayfieldId),
                    HasLastLocation = reusePreviousSpawnState && previous.HasLastLocation,
                    LastAoX = reusePreviousSpawnState ? previous.LastAoX : 0f,
                    LastAoY = reusePreviousSpawnState ? previous.LastAoY : 0f,
                    LastAoZ = reusePreviousSpawnState ? previous.LastAoZ : 0f,
                    LastYaw = reusePreviousSpawnState ? previous.LastYaw : 0f,
                    LastWorldX = reusePreviousSpawnState ? previous.LastWorldX : 0f,
                    LastWorldY = reusePreviousSpawnState ? previous.LastWorldY : 0f,
                    LastWorldZ = reusePreviousSpawnState ? previous.LastWorldZ : 0f
                });
            }
        }

        private void CaptureWindowLayoutForCharacter(string characterName)
        {
            if (_windowByPersistId.Count == 0 || string.IsNullOrWhiteSpace(characterName))
                return;

            _clientPrefs ??= new LocalClientPreferencesFile();
            string keyName = characterName.Trim();

            var capturedEntries = new List<PersistedWindowLayoutEntry>();
            foreach (var pair in _windowByPersistId)
            {
                var root = pair.Value?.Root;
                if (root == null)
                    continue;
                capturedEntries.Add(BuildWindowLayoutEntry(pair.Key, root));
            }

            // Never overwrite an existing layout with an empty capture (can happen during teardown).
            if (capturedEntries.Count == 0)
            {
                Debug.LogWarning($"[Prefs] Skipped empty window layout capture for '{keyName}'.");
                return;
            }

            var layout = _clientPrefs.UiLayouts.FirstOrDefault(l =>
                l != null && string.Equals(l.CharacterName, keyName, StringComparison.OrdinalIgnoreCase));
            if (layout == null)
            {
                layout = new PersistedCharacterUiLayout { CharacterName = keyName };
                _clientPrefs.UiLayouts.Add(layout);
            }

            EnsureLayoutSchema(layout);
            layout.Windows.Clear();
            layout.WindowsById.Clear();
            layout.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            layout.Windows.AddRange(capturedEntries);
            foreach (var entry in capturedEntries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.Id))
                    layout.WindowsById[entry.Id] = entry;
            }
            RebuildDockOrder(layout);

            Debug.Log($"[Prefs] Captured {layout.Windows.Count} window layout entries for '{keyName}'.");
        }

        private void PersistWindowLayoutForRoot(RectTransform root)
        {
            if (root == null || string.IsNullOrWhiteSpace(_activeProfileName))
                return;
            if (!_persistIdByRoot.TryGetValue(root, out var id) || string.IsNullOrWhiteSpace(id))
                return;

            _clientPrefs ??= new LocalClientPreferencesFile();
            var layout = GetOrCreateCharacterLayout(_activeProfileName.Trim());
            EnsureLayoutSchema(layout);

            var entry = BuildWindowLayoutEntry(id, root);
            layout.WindowsById[id] = entry;
            UpsertLegacyLayoutList(layout, entry);
            layout.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            RebuildDockOrder(layout);
        }

        private PersistedWindowLayoutEntry BuildWindowLayoutEntry(string id, RectTransform root)
        {
            bool docked = false;
            int dockSiblingIndex = -1;
            var dockState = root.GetComponent<WindowDockState>();
            if (dockState != null)
                docked = dockState.IsDocked;
            if (docked && _dockPane?.DockContent != null && root.parent == _dockPane.DockContent)
                dockSiblingIndex = root.GetSiblingIndex();

            string parentScope = "body";
            if (_uiRoot != null && root.parent == _uiRoot)
                parentScope = "root";
            else if (_uiBodyRoot != null && root.parent == _uiBodyRoot)
                parentScope = "body";

            return new PersistedWindowLayoutEntry
            {
                Id = id,
                Active = root.gameObject.activeSelf,
                Docked = docked,
                DockSiblingIndex = dockSiblingIndex,
                ParentScope = parentScope,
                AnchoredX = root.anchoredPosition.x,
                AnchoredY = root.anchoredPosition.y,
                SizeX = root.sizeDelta.x,
                SizeY = root.sizeDelta.y
            };
        }

        private PersistedCharacterUiLayout GetOrCreateCharacterLayout(string keyName)
        {
            var layout = _clientPrefs.UiLayouts.FirstOrDefault(l =>
                l != null && string.Equals(l.CharacterName, keyName, StringComparison.OrdinalIgnoreCase));
            if (layout != null)
                return layout;

            layout = new PersistedCharacterUiLayout { CharacterName = keyName };
            _clientPrefs.UiLayouts.Add(layout);
            return layout;
        }

        private static void UpsertLegacyLayoutList(PersistedCharacterUiLayout layout, PersistedWindowLayoutEntry entry)
        {
            if (layout?.Windows == null || entry == null || string.IsNullOrWhiteSpace(entry.Id))
                return;

            int index = layout.Windows.FindIndex(w =>
                w != null && string.Equals(w.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                layout.Windows.Add(entry);
            else
                layout.Windows[index] = entry;
        }

        private void RebuildDockOrder(PersistedCharacterUiLayout layout)
        {
            if (layout == null)
                return;

            EnsureLayoutSchema(layout);
            layout.Dock.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            layout.Dock.OrderedWindowIds.Clear();

            if (_dockPane?.DockContent != null)
            {
                for (int i = 0; i < _dockPane.DockContent.childCount; i++)
                {
                    var child = _dockPane.DockContent.GetChild(i) as RectTransform;
                    if (child == null)
                        continue;
                    if (!_persistIdByRoot.TryGetValue(child, out var id) || string.IsNullOrWhiteSpace(id))
                        continue;
                    if (!layout.WindowsById.TryGetValue(id, out var entry) || entry == null)
                        continue;

                    entry.Docked = true;
                    entry.DockSiblingIndex = i;
                    layout.Dock.OrderedWindowIds.Add(id);
                    UpsertLegacyLayoutList(layout, entry);
                }

                // Any tracked windows not in the current dock should not be marked as docked.
                foreach (var pair in layout.WindowsById)
                {
                    if (pair.Value == null)
                        continue;
                    if (layout.Dock.OrderedWindowIds.Contains(pair.Key))
                        continue;
                    pair.Value.Docked = false;
                    pair.Value.DockSiblingIndex = -1;
                    UpsertLegacyLayoutList(layout, pair.Value);
                }
                return;
            }

            foreach (var pair in layout.WindowsById
                         .Where(p => p.Value != null && p.Value.Docked)
                         .OrderBy(p => p.Value.DockSiblingIndex < 0 ? int.MaxValue : p.Value.DockSiblingIndex))
            {
                layout.Dock.OrderedWindowIds.Add(pair.Key);
            }
        }

        private static void EnsureLayoutSchema(PersistedCharacterUiLayout layout)
        {
            if (layout == null)
                return;

            layout.Windows ??= new List<PersistedWindowLayoutEntry>();
            layout.WindowsById ??= new Dictionary<string, PersistedWindowLayoutEntry>(StringComparer.OrdinalIgnoreCase);
            layout.Dock ??= new PersistedDockLayout();
            layout.Dock.OrderedWindowIds ??= new List<string>();

            if (layout.WindowsById.Count == 0 && layout.Windows.Count > 0)
            {
                foreach (var entry in layout.Windows)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                        continue;
                    layout.WindowsById[entry.Id] = entry;
                }
            }
            else if (layout.Windows.Count == 0 && layout.WindowsById.Count > 0)
            {
                layout.Windows.AddRange(layout.WindowsById.Values.Where(v => v != null));
            }
        }

        private void ApplyWindowLayoutForCharacter(string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
                return;

            // Always refresh from disk before applying to avoid stale in-memory state.
            _clientPrefs = LoadLocalClientPreferences();
            if (_clientPrefs?.UiLayouts == null)
                return;

            var layout = _clientPrefs.UiLayouts.FirstOrDefault(l =>
                l != null && string.Equals(l.CharacterName, characterName.Trim(), StringComparison.OrdinalIgnoreCase));
            EnsureLayoutSchema(layout);
            if (layout?.WindowsById == null || layout.WindowsById.Count == 0)
            {
                Debug.Log($"[Prefs] No window layout found for '{characterName.Trim()}'.");
                return;
            }

            // Reset stale dock state before reapplying saved layout/order.
            ResetDockStateBeforeLayoutApply();

            foreach (var entry in layout.WindowsById.Values.Where(e => e != null && !e.Docked))
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                    continue;
                if (!_windowByPersistId.TryGetValue(entry.Id, out var window) || window?.Root == null)
                    continue;

                ApplyWindowLayoutEntry(window.Root, entry);
            }

            var orderedDockIds = layout.Dock?.OrderedWindowIds != null && layout.Dock.OrderedWindowIds.Count > 0
                ? layout.Dock.OrderedWindowIds
                : layout.WindowsById.Values
                    .Where(e => e != null && e.Docked && !string.IsNullOrWhiteSpace(e.Id))
                    .OrderBy(e => e.DockSiblingIndex < 0 ? int.MaxValue : e.DockSiblingIndex)
                    .Select(e => e.Id)
                    .ToList();

            foreach (var dockId in orderedDockIds)
            {
                if (string.IsNullOrWhiteSpace(dockId))
                    continue;
                if (!layout.WindowsById.TryGetValue(dockId, out var entry) || entry == null || !entry.Docked)
                    continue;
                if (!_windowByPersistId.TryGetValue(dockId, out var window) || window?.Root == null)
                    continue;

                ApplyWindowLayoutEntry(window.Root, entry);
            }

            NormalizeDockedWindowOrder(layout);
            StartCoroutine(RebuildDockLayoutDeferred());

            if (_uiBodyRoot != null && _uiRoot != null)
                ClampAllWindowsToViewport(_uiBodyRoot, _uiRoot);

            Debug.Log($"[Prefs] Applied {layout.WindowsById.Count} window layout entries for '{characterName.Trim()}'.");
        }

        private void ResetDockStateBeforeLayoutApply()
        {
            if (_windowByPersistId.Count == 0 || _uiBodyRoot == null)
                return;

            foreach (var pair in _windowByPersistId)
            {
                var root = pair.Value?.Root;
                if (root == null)
                    continue;

                var dockState = root.GetComponent<WindowDockState>();
                if (dockState != null && dockState.IsDocked)
                    dockState.SetDocked(false);

                if (root.parent == _dockPane?.DockContent)
                    root.SetParent(_uiBodyRoot, false);
            }
        }

        private void NormalizeDockedWindowOrder(PersistedCharacterUiLayout layout)
        {
            EnsureLayoutSchema(layout);
            if (layout?.WindowsById == null || _dockPane?.DockContent == null)
                return;

            var orderedIds = layout.Dock?.OrderedWindowIds != null && layout.Dock.OrderedWindowIds.Count > 0
                ? layout.Dock.OrderedWindowIds
                : layout.WindowsById.Values
                    .Where(w => w != null && w.Docked && !string.IsNullOrWhiteSpace(w.Id))
                    .OrderBy(w => w.DockSiblingIndex < 0 ? int.MaxValue : w.DockSiblingIndex)
                    .Select(w => w.Id)
                    .ToList();

            int nextIndex = 0;
            for (int i = 0; i < orderedIds.Count; i++)
            {
                string id = orderedIds[i];
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                if (!_windowByPersistId.TryGetValue(id, out var window) || window?.Root == null)
                    continue;
                if (window.Root.parent != _dockPane.DockContent)
                    continue;

                window.Root.SetSiblingIndex(Mathf.Clamp(nextIndex, 0, _dockPane.DockContent.childCount - 1));
                if (layout.WindowsById.TryGetValue(id, out var entry) && entry != null)
                    entry.DockSiblingIndex = nextIndex;
                nextIndex++;
            }

            RebuildDockOrder(layout);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_dockPane.DockContent);
            if (_dockPane.DockContent.parent is RectTransform viewport)
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
            Canvas.ForceUpdateCanvases();
        }

        private IEnumerator RebuildDockLayoutDeferred()
        {
            if (_dockPane?.DockContent == null)
                yield break;

            // Give Unity layout/content-fit passes time to settle after parent/sibling changes.
            yield return null;
            yield return new WaitForEndOfFrame();

            var dockContent = _dockPane.DockContent;
            var vlg = dockContent.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
            {
                vlg.enabled = false;
                vlg.enabled = true;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(dockContent);
            if (dockContent.parent is RectTransform viewport)
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
            if (dockContent.parent?.parent is RectTransform host)
                LayoutRebuilder.ForceRebuildLayoutImmediate(host);
            Canvas.ForceUpdateCanvases();
        }

        private IEnumerator ApplyWindowLayoutDeferred(string characterName)
        {
            yield return null;
            yield return null;
            ApplyWindowLayoutForCharacter(characterName);
        }

        private void ApplyWindowLayoutEntry(RectTransform root, PersistedWindowLayoutEntry entry)
        {
            if (root == null || entry == null)
                return;

            var dockState = root.GetComponent<WindowDockState>();
            if (entry.Docked && _dockPane?.DockContent != null)
            {
                if (dockState != null)
                    dockState.CaptureFloatingState(_uiBodyRoot);
                root.SetParent(_dockPane.DockContent, false);
                if (dockState != null)
                    dockState.SetDocked(true);
                int maxIndex = Mathf.Max(0, _dockPane.DockContent.childCount - 1);
                int index = entry.DockSiblingIndex < 0 ? maxIndex : Mathf.Clamp(entry.DockSiblingIndex, 0, maxIndex);
                root.SetSiblingIndex(index);
                root.localScale = Vector3.one;
                root.localRotation = Quaternion.identity;
                root.anchoredPosition = Vector2.zero;
            }
            else
            {
                RectTransform targetParent = ResolveParentForScope(entry.ParentScope);
                if (targetParent != null)
                    root.SetParent(targetParent, false);
                if (dockState != null)
                    dockState.SetDocked(false);
                root.sizeDelta = new Vector2(
                    Mathf.Max(120f, entry.SizeX <= 0f ? root.sizeDelta.x : entry.SizeX),
                    Mathf.Max(80f, entry.SizeY <= 0f ? root.sizeDelta.y : entry.SizeY));
                root.anchoredPosition = new Vector2(entry.AnchoredX, entry.AnchoredY);
            }

            root.gameObject.SetActive(entry.Active);
        }

        private RectTransform ResolveParentForScope(string scope)
        {
            if (string.Equals(scope, "root", StringComparison.OrdinalIgnoreCase))
                return _uiRoot;
            return _uiBodyRoot != null ? _uiBodyRoot : _uiRoot;
        }

        private LocalClientPreferencesFile LoadLocalClientPreferences()
        {
            try
            {
                string file = GetLocalClientPrefsFilePath();
                if (!File.Exists(file))
                {
                    Debug.Log($"[Prefs] No preferences file found yet at: {file}");
                    return new LocalClientPreferencesFile();
                }

                var parsed = JsonConvert.DeserializeObject<LocalClientPreferencesFile>(File.ReadAllText(file));
                parsed ??= new LocalClientPreferencesFile();
                parsed.Characters ??= new List<PersistedCharacterProfile>();
                parsed.UiLayouts ??= new List<PersistedCharacterUiLayout>();
                foreach (var layout in parsed.UiLayouts)
                    EnsureLayoutSchema(layout);
                Debug.Log($"[Prefs] Loaded preferences from: {file} (chars={parsed.Characters.Count}, layouts={parsed.UiLayouts.Count})");
                return parsed;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading client preferences: {ex.Message}");
                return new LocalClientPreferencesFile();
            }
        }

        private void SaveLocalClientPreferences()
        {
            try
            {
                _clientPrefs ??= new LocalClientPreferencesFile();
                string file = GetLocalClientPrefsFilePath();
                string dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonConvert.SerializeObject(_clientPrefs, Formatting.Indented);
                File.WriteAllText(file, json);
                Debug.Log($"[Prefs] Saved preferences to: {file}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed saving client preferences: {ex.Message}");
            }
        }

        private static string GetLocalClientPrefsFilePath()
        {
            string installRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string root = Path.Combine(installRoot, LocalPrefsFolderName, LocalAccountName);
            return Path.Combine(root, LocalClientPrefsFileName);
        }

        private PersistedCharacterProfile FindPersistedProfileByName(string name)
        {
            if (_clientPrefs?.Characters == null || string.IsNullOrWhiteSpace(name))
                return null;
            string n = name.Trim();
            return _clientPrefs.Characters.FirstOrDefault(c =>
                c != null && !string.IsNullOrWhiteSpace(c.Name) && string.Equals(c.Name.Trim(), n, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateActiveCharacterLastLocation()
        {
            if (_clientPrefs?.Characters == null
                || _selfBridge == null
                || string.IsNullOrWhiteSpace(_activeProfileName))
            {
                return;
            }

            var bootstrap = FindFirstObjectByType<PrototypeWorldBootstrap>();
            if (bootstrap == null || bootstrap.ActivePlayfieldId <= 0)
                return;

            string name = _activeProfileName.Trim();
            var persisted = _clientPrefs.Characters.FirstOrDefault(c =>
                c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (persisted == null)
                return;

            persisted.StartPlayfieldId = bootstrap.ActivePlayfieldId;
            Vector3 world = _selfBridge.transform.position;
            if (!IsLikelyPersistableWorldPosition(world))
                return;
            Vector3 ao = bootstrap.ConvertWorldToAo(world);
            persisted.HasLastLocation = true;
            persisted.LastAoX = ao.x;
            persisted.LastAoY = ao.y;
            persisted.LastAoZ = ao.z;
            persisted.LastYaw = _selfBridge.transform.eulerAngles.y;
            persisted.LastWorldX = world.x;
            persisted.LastWorldY = world.y;
            persisted.LastWorldZ = world.z;

            // Keep character-selection data in sync so profile exports don't drift stale.
            var profiles = _characterSelectionView?.GetProfiles();
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    var profile = profiles[i];
                    if (profile == null || string.IsNullOrWhiteSpace(profile.Name))
                        continue;
                    if (!string.Equals(profile.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    profile.StartPlayfieldId = persisted.StartPlayfieldId;
                    break;
                }
            }
        }
    }
}
