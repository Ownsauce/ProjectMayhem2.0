using AO.Core.Characters;
using AO.Core.Stats;
using AO.Data.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.World
{
    public partial class PrototypeWorldBootstrap : MonoBehaviour
    {
        private string ResolvePlayfieldPackageFolderPath(int pf)
        {
            if (TryResolveAutomaticPlayfieldFolder(pf, out string autoFolder, out bool isGlbOverride) && !isGlbOverride)
                return autoFolder;

            return Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                $"playfield_{pf}");
        }

        private string ResolvePlayfieldGlbOverrideFolderPath(int pf)
        {
            if (TryResolveAutomaticPlayfieldFolder(pf, out string autoFolder, out bool isGlbOverride) && isGlbOverride)
                return autoFolder;

            string folderName = string.IsNullOrWhiteSpace(playfieldGlbOverrideFolderNameFormat)
                ? $"Playfield_{pf}_test"
                : string.Format(playfieldGlbOverrideFolderNameFormat, pf);
            return Path.Combine(
                Application.streamingAssetsPath,
                playfieldsSubfolder,
                folderName);
        }

        private bool TryResolveAutomaticPlayfieldFolder(int pf, out string folderPath, out bool isMonolith)
        {
            if (!TryResolveAutomaticPlayfieldFolderMode(pf, out folderPath, out var kind))
            {
                isMonolith = false;
                return false;
            }

            isMonolith = kind == AutoPlayfieldOverrideKind.MonolithGlb
                || kind == AutoPlayfieldOverrideKind.IndoorPfGlb;
            return true;
        }

        private bool TryResolveTeleportDefaultForPlayfield(int pf, out Vector3 aoPosition, out float? heading, out string label)
        {
            aoPosition = Vector3.zero;
            heading = null;
            label = null;
            if (pf <= 0)
                return false;

            string defaultsPath = Path.Combine(
                Application.streamingAssetsPath,
                "AOData",
                "Playfields",
                "teleport_defaults.json");
            if (!File.Exists(defaultsPath))
            {
                // Editor/dev fallback: prefer authoritative server playfield defaults
                // from the repo root when available.
                string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
                string serverDefaultsPath = Path.Combine(
                    repoRoot,
                    "AO.Server",
                    "Data",
                    "Playfields",
                    "teleport_defaults.json");
                if (File.Exists(serverDefaultsPath))
                    defaultsPath = serverDefaultsPath;
            }
            if (!File.Exists(defaultsPath))
                return false;

            try
            {
                string json = File.ReadAllText(defaultsPath);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                List<TeleportDefaultEntry> defaults = null;
                var token = JToken.Parse(json);
                if (token.Type == JTokenType.Array)
                    defaults = token.ToObject<List<TeleportDefaultEntry>>();
                else
                    defaults = token.ToObject<TeleportDefaultsFile>()?.Defaults;

                if (defaults == null || defaults.Count == 0)
                    return false;

                TeleportDefaultEntry match = defaults.Find(d => d != null && d.PlayfieldId == pf);
                if (match == null)
                    return false;

                float? x = match.AoX ?? match.X;
                float? y = match.AoY ?? match.Y;
                float? z = match.AoZ ?? match.Z;
                if (!x.HasValue || !y.HasValue || !z.HasValue)
                    return false;
                if (!float.IsFinite(x.Value) || !float.IsFinite(y.Value) || !float.IsFinite(z.Value))
                    return false;

                aoPosition = new Vector3(x.Value, y.Value, z.Value);
                if (match.Heading.HasValue && float.IsFinite(match.Heading.Value))
                    heading = match.Heading.Value;
                label = string.IsNullOrWhiteSpace(match.Label) ? null : match.Label.Trim();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed parsing teleport defaults {defaultsPath}: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private bool TryResolveAutomaticPlayfieldFolderMode(int pf, out string folderPath, out AutoPlayfieldOverrideKind kind)
        {
            folderPath = null;
            kind = AutoPlayfieldOverrideKind.None;
            if (!autoSelectPlayfieldOverrideByFolder || pf <= 0)
                return false;

            string root = Path.Combine(Application.streamingAssetsPath, playfieldsSubfolder);
            if (!Directory.Exists(root))
                return false;

            string pfPrefix = $"playfield_{pf}";

            var dirs = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
            string bestIndoorPf = null;
            string bestMonolith = null;
            string bestPackage = null;

            for (int i = 0; i < dirs.Length; i++)
            {
                string dir = dirs[i];
                string name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string lower = name.ToLowerInvariant();
                if (!TryParseAutomaticPlayfieldFolderSuffix(lower, pfPrefix, out string suffix))
                    continue;

                if (IsIndoorPfFolderSuffix(suffix))
                {
                    if (string.Equals(suffix, "indoorpf", StringComparison.Ordinal))
                    {
                        bestIndoorPf = dir;
                        continue;
                    }

                    if (bestIndoorPf == null)
                        bestIndoorPf = dir;
                    continue;
                }

                if (IsMonolithFolderSuffix(suffix))
                {
                    if (string.Equals(suffix, "monolith", StringComparison.Ordinal)
                        || string.Equals(suffix, "monlith", StringComparison.Ordinal))
                    {
                        bestMonolith = dir;
                        break;
                    }

                    if (bestMonolith == null)
                        bestMonolith = dir;
                }
                else if (string.IsNullOrEmpty(suffix))
                {
                    if (bestPackage == null)
                        bestPackage = dir;
                }
            }

            if (!string.IsNullOrWhiteSpace(bestIndoorPf))
            {
                folderPath = bestIndoorPf;
                kind = AutoPlayfieldOverrideKind.IndoorPfGlb;
                Debug.Log($"Auto playfield folder mode PF {pf}: kind={kind} path='{folderPath}'.");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(bestMonolith))
            {
                folderPath = bestMonolith;
                kind = AutoPlayfieldOverrideKind.MonolithGlb;
                Debug.Log($"Auto playfield folder mode PF {pf}: kind={kind} path='{folderPath}'.");
                return true;
            }

            if (!string.IsNullOrWhiteSpace(bestPackage))
            {
                folderPath = bestPackage;
                kind = AutoPlayfieldOverrideKind.Package;
                Debug.Log($"Auto playfield folder mode PF {pf}: kind={kind} path='{folderPath}'.");
                return true;
            }

            return false;
        }

        private static bool TryParseAutomaticPlayfieldFolderSuffix(string lowerName, string pfPrefix, out string suffix)
        {
            suffix = null;
            if (string.IsNullOrWhiteSpace(lowerName) || string.IsNullOrWhiteSpace(pfPrefix))
                return false;
            if (!lowerName.StartsWith(pfPrefix, StringComparison.Ordinal))
                return false;

            int prefixLength = pfPrefix.Length;
            if (lowerName.Length == prefixLength)
            {
                suffix = string.Empty;
                return true;
            }

            char separator = lowerName[prefixLength];
            if (separator != '_' && separator != '-')
                return false;

            string rawSuffix = lowerName.Substring(prefixLength + 1).Trim();
            suffix = string.IsNullOrWhiteSpace(rawSuffix) ? string.Empty : rawSuffix;
            return true;
        }

        private static bool IsIndoorPfFolderSuffix(string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix))
                return false;

            return suffix.IndexOf("indoorpf", StringComparison.Ordinal) >= 0
                || string.Equals(suffix, "indoor_pf", StringComparison.Ordinal);
        }

        private static bool IsMonolithFolderSuffix(string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix))
                return false;

            return suffix.IndexOf("monolith", StringComparison.Ordinal) >= 0
                || suffix.IndexOf("monlith", StringComparison.Ordinal) >= 0;
        }

        private bool IsPlayfieldPackageOverrideEnabled(int pf)
        {
            if (TryResolveAutomaticPlayfieldFolderMode(pf, out _, out var kind))
                return kind == AutoPlayfieldOverrideKind.Package;

            bool manual = enablePlayfieldPackageOverride
                && preferPlayfieldPackageFolder
                && playfieldPackageOverridePlayfieldId > 0
                && pf == playfieldPackageOverridePlayfieldId;
            if (manual)
            {
                Debug.Log(
                    $"Using manual package override for PF {pf}: " +
                    $"autoSelectOverrideByFolder={autoSelectPlayfieldOverrideByFolder}, " +
                    $"preferPlayfieldPackageFolder={preferPlayfieldPackageFolder}.");
            }
            return manual;
        }

        private bool IsPlayfieldPackageStrictMode(int pf)
        {
            return IsPlayfieldPackageOverrideEnabled(pf) && playfieldPackageOverrideStrict;
        }

        private bool IsPlayfieldGlbOverrideEnabled(int pf)
        {
            if (TryResolveAutomaticPlayfieldFolderMode(pf, out _, out var kind))
            {
                return kind == AutoPlayfieldOverrideKind.MonolithGlb
                    || kind == AutoPlayfieldOverrideKind.IndoorPfGlb;
            }

            bool manual = enablePlayfieldGlbOverride
                && playfieldGlbOverridePlayfieldId > 0
                && pf == playfieldGlbOverridePlayfieldId;
            if (manual)
            {
                Debug.Log(
                    $"Using manual GLB override for PF {pf}: " +
                    $"autoSelectOverrideByFolder={autoSelectPlayfieldOverrideByFolder}.");
            }
            return manual;
        }

        private bool IsPlayfieldIndoorPfOverrideEnabled(int pf)
        {
            if (TryResolveAutomaticPlayfieldFolderMode(pf, out _, out var kind))
                return kind == AutoPlayfieldOverrideKind.IndoorPfGlb;

            return false;
        }

        private bool IsPlayfieldGlbStrictMode(int pf)
        {
            return IsPlayfieldGlbOverrideEnabled(pf) && playfieldGlbOverrideStrict;
        }

        private bool TryStartPlayfieldGlbOverrideLoad(int pf, out Vector3 centerWorld)
        {
            centerWorld = Vector3.zero;
            if (!IsPlayfieldGlbOverrideEnabled(pf))
                return false;

            string folder = ResolvePlayfieldGlbOverrideFolderPath(pf);
            if (!Directory.Exists(folder))
            {
                if (IsPlayfieldGlbStrictMode(pf))
                    Debug.LogError($"Playfield GLB strict mode is enabled for PF {pf}, but folder does not exist: {folder}");
                return false;
            }

            bool indoorPfOverride = IsPlayfieldIndoorPfOverrideEnabled(pf);
            if (indoorPfOverride)
            {
                var indoorGlbPaths = ResolvePlayfieldIndoorPfGlbFiles(folder, pf);
                if (IsPlayfieldGlbStrictMode(pf) && indoorGlbPaths.Count == 0)
                {
                    Debug.LogError($"Playfield IndoorPF strict mode is enabled for PF {pf}, but no GLB files were found in '{folder}'.");
                    return false;
                }

                if (indoorGlbPaths.Count == 0)
                {
                    Debug.LogWarning($"Playfield IndoorPF override enabled for PF {pf}, but no GLB files were found in: {folder}");
                    return false;
                }

                var indoorPfRoot = new GameObject($"PF_{pf}_IndoorPFOverride");
                indoorPfRoot.transform.SetParent(_worldRoot, false);
                InitializePlayfieldGlbOverrideRuntimeState(pf, indoorPfRoot);

                _activePlayfieldGlbLoadInProgress = true;
                int loadTicket = ++_activePlayfieldGlbLoadTicket;
                StartCoroutine(LoadPlayfieldIndoorPfGlbOverrideCoroutine(pf, indoorPfRoot.transform, indoorGlbPaths, loadTicket));
                centerWorld = Vector3.zero;
                Debug.Log(
                    $"Playfield IndoorPF override load for PF {pf}: glbCount={indoorGlbPaths.Count}, " +
                    $"placeWorldInAoCoordinates={placeWorldInAoCoordinates} " +
                    $"(glbUsesAoUnitsSetting={playfieldGlbUsesAoUnits}) centered={_activeUseCenteredCoordinates} scale={_activeCoordinateScale:0.###}");
                return true;
            }

            bool isAutoMonolithMode =
                TryResolveAutomaticPlayfieldFolderMode(pf, out _, out var autoModeKind)
                && autoModeKind == AutoPlayfieldOverrideKind.MonolithGlb;

            bool loadStatelsGlb = loadPlayfieldGlbStatels || isAutoMonolithMode;
            bool loadTerrainGlb = loadPlayfieldGlbTerrain || isAutoMonolithMode;
            bool loadWaterGlb = loadPlayfieldGlbWater || isAutoMonolithMode;

            string statelsPath = loadStatelsGlb
                ? ResolvePlayfieldGlbFile(folder, pf, "statels")
                : null;
            string terrainPath = loadTerrainGlb
                ? ResolvePlayfieldGlbFile(folder, pf, "terrain")
                : null;
            string waterPath = loadWaterGlb
                ? ResolvePlayfieldGlbFile(folder, pf, "water")
                : null;
            Debug.Log(
                $"Playfield GLB candidate files PF {pf}: folder='{folder}', " +
                $"statels='{statelsPath ?? "<none>"}', terrain='{terrainPath ?? "<none>"}', water='{waterPath ?? "<none>"}'.");

            if (IsPlayfieldGlbStrictMode(pf))
            {
                if (loadPlayfieldGlbStatels && string.IsNullOrWhiteSpace(statelsPath))
                {
                    Debug.LogError($"Playfield GLB strict mode missing statels file in '{folder}'.");
                    return false;
                }

                if (loadPlayfieldGlbTerrain && string.IsNullOrWhiteSpace(terrainPath))
                {
                    Debug.LogError($"Playfield GLB strict mode missing terrain file in '{folder}'.");
                    return false;
                }

                if (loadPlayfieldGlbWater && string.IsNullOrWhiteSpace(waterPath))
                {
                    Debug.LogError($"Playfield GLB strict mode missing water file in '{folder}'.");
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(statelsPath)
                && string.IsNullOrWhiteSpace(terrainPath)
                && string.IsNullOrWhiteSpace(waterPath))
            {
                Debug.LogWarning($"Playfield GLB override enabled for PF {pf}, but no matching GLB files were found in: {folder}");
                return false;
            }

            var pfRoot = new GameObject($"PF_{pf}_GLBOverride");
            pfRoot.transform.SetParent(_worldRoot, false);
            InitializePlayfieldGlbOverrideRuntimeState(pf, pfRoot);

            _activePlayfieldGlbLoadInProgress = true;
            int glbLoadTicket = ++_activePlayfieldGlbLoadTicket;
            StartCoroutine(LoadPlayfieldGlbOverrideCoroutine(pf, pfRoot.transform, statelsPath, terrainPath, waterPath, glbLoadTicket));
            centerWorld = Vector3.zero;
            Debug.Log(
                $"Playfield GLB override coordinate mapping PF {pf}: " +
                $"placeWorldInAoCoordinates={placeWorldInAoCoordinates} " +
                $"(glbUsesAoUnitsSetting={playfieldGlbUsesAoUnits}) centered={_activeUseCenteredCoordinates} scale={_activeCoordinateScale:0.###}");
            return true;
        }

        private void InitializePlayfieldGlbOverrideRuntimeState(int pf, GameObject root)
        {
            _activePlayfieldRoot = root.transform;
            _activePlayfieldId = pf;
            _activeHorizontalCenter = Vector3.zero;
            _activePlayfieldUsesIndoorRoomSurfaces = false;
            _activeRuntimePrefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            _activeMeshRotationOverrides = new MeshRotationOverrideSet();
            _activePlayfieldLoadedFromPackage = false;
            if (playfieldGlbUsesAoUnits)
            {
                if (placeWorldInAoCoordinates)
                {
                    // AO-space world mode: keep direct AO units.
                    _activeCoordinateScale = 1f;
                    _activeUseCenteredCoordinates = false;
                }
                else
                {
                    // Unity-space world mode: monolith source is AO units, so conversion uses
                    // configured scale/centering and we scale the GLB root in finalize.
                    _activeCoordinateScale = Mathf.Max(0.0001f, coordinateScale);
                    _activeUseCenteredCoordinates = centerPlayfieldAroundOrigin;
                }
            }
            else if (placeWorldInAoCoordinates)
            {
                _activeCoordinateScale = 1f;
                _activeUseCenteredCoordinates = false;
            }
            else
            {
                _activeCoordinateScale = Mathf.Max(0.0001f, coordinateScale);
                _activeUseCenteredCoordinates = centerPlayfieldAroundOrigin;
            }
            _packageStatelMeshMapByPlayfield.Remove(pf);
        }

        private static string ResolvePlayfieldGlbFile(string folder, int pf, string kind)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return null;

            string[] candidateNames =
            {
                $"Playfield_{kind}_{pf}.glb",
                $"playfield_{kind}_{pf}.glb"
            };

            foreach (string candidate in candidateNames)
            {
                string full = Path.Combine(folder, candidate);
                if (File.Exists(full))
                    return full;
            }

            var allGlbs = Directory.EnumerateFiles(folder, "*.glb", SearchOption.TopDirectoryOnly);
            foreach (string file in allGlbs)
            {
                string fileName = Path.GetFileName(file);
                if (candidateNames.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
                    return file;
            }

            // Tolerant fallback for naming variants like:
            // Playfield-Statels-735.glb, PF_735_Terrain.glb, etc.
            string pfToken = pf.ToString();
            string kindToken = (kind ?? string.Empty).Trim().ToLowerInvariant();
            foreach (string file in Directory.EnumerateFiles(folder, "*.glb", SearchOption.TopDirectoryOnly))
            {
                string stem = Path.GetFileNameWithoutExtension(file)?.ToLowerInvariant() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(stem))
                    continue;

                if (stem.Contains(kindToken, StringComparison.Ordinal)
                    && stem.Contains(pfToken, StringComparison.Ordinal))
                {
                    return file;
                }
            }

            return null;
        }

        private static List<string> ResolvePlayfieldIndoorPfGlbFiles(string folder, int pf)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return results;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddIfFileExists(string fileName)
            {
                string full = Path.Combine(folder, fileName);
                if (File.Exists(full) && seen.Add(full))
                    results.Add(full);
            }

            AddIfFileExists($"Playfield_IndoorPF_{pf}.glb");
            AddIfFileExists($"playfield_indoorpf_{pf}.glb");
            AddIfFileExists($"Playfield_{pf}_IndoorPF.glb");
            AddIfFileExists($"playfield_{pf}_indoorpf.glb");
            AddIfFileExists($"Playfield_Indoor_{pf}.glb");
            AddIfFileExists($"playfield_indoor_{pf}.glb");

            foreach (string file in Directory
                .EnumerateFiles(folder, "*.glb", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                if (seen.Add(file))
                    results.Add(file);
            }

            return results;
        }

        private System.Collections.IEnumerator LoadPlayfieldGlbOverrideCoroutine(
            int pf,
            Transform parent,
            string statelsPath,
            string terrainPath,
            string waterPath,
            int loadTicket)
        {
            if (parent == null)
                yield break;

            yield return LoadSinglePlayfieldGlbCoroutine(parent, statelsPath, $"PF_{pf}_Statels_GLB", loadTicket);
            yield return LoadSinglePlayfieldGlbCoroutine(parent, terrainPath, $"PF_{pf}_Terrain_GLB", loadTicket);
            yield return LoadSinglePlayfieldGlbCoroutine(parent, waterPath, $"PF_{pf}_Water_GLB", loadTicket);

            if (playfieldGlbOverrideLoadTerrainFromJson && loadTerrainFromJson)
            {
                bool terrainJsonLoaded = TryLoadTerrainJson(
                    pf,
                    parent,
                    Vector3.zero,
                    centerAroundOrigin: false,
                    Mathf.Max(0.0001f, _activeCoordinateScale));
                Debug.Log($"Playfield GLB override terrain JSON fallback for PF {pf}: loaded={terrainJsonLoaded}.");
            }

            if (playfieldGlbOverrideLoadWaterFromJson && loadWaterFromJson)
            {
                bool waterJsonLoaded = TryLoadWaterJson(
                    pf,
                    parent,
                    Vector3.zero,
                    centerAroundOrigin: false,
                    Mathf.Max(0.0001f, _activeCoordinateScale));
                Debug.Log($"Playfield GLB override water JSON fallback for PF {pf}: loaded={waterJsonLoaded}.");
            }

            FinalizePlayfieldGlbOverride(parent);
            CompletePlayfieldGlbOverrideLoad(pf, parent, loadTicket);
        }

        private System.Collections.IEnumerator LoadPlayfieldIndoorPfGlbOverrideCoroutine(
            int pf,
            Transform parent,
            List<string> glbPaths,
            int loadTicket)
        {
            if (parent == null || glbPaths == null || glbPaths.Count == 0)
                yield break;

            int loaded = 0;
            for (int i = 0; i < glbPaths.Count; i++)
            {
                string path = glbPaths[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                yield return LoadSinglePlayfieldGlbCoroutine(parent, path, $"PF_{pf}_IndoorPF_GLB_{i:D2}", loadTicket);
                loaded++;
            }

            FinalizePlayfieldGlbOverride(parent);
            CompletePlayfieldGlbOverrideLoad(pf, parent, loadTicket);
            Debug.Log($"Loaded Playfield IndoorPF GLB override for PF {pf}: loaded {loaded}/{glbPaths.Count} GLB file(s).");
        }

        private void CompletePlayfieldGlbOverrideLoad(int pf, Transform parent, int loadTicket)
        {
            if (loadTicket != _activePlayfieldGlbLoadTicket)
                return;

            _activePlayfieldGlbLoadInProgress = false;

            if (_activePlayfieldId != pf || _activePlayfieldRoot != parent)
                return;

            ApplyPendingTransitionSpawnIfAny(pf, loadTicket);
            CompleteEnterWorldLoadingIfReady(ResolvePlayerTransform());

            var zoneManager = FindFirstObjectByType<ZoneTransitionManager>();
            if (zoneManager != null)
                zoneManager.RebuildPortalsForCurrentPlayfield();

            Debug.Log($"Playfield GLB content ready for PF {pf}. Rebuilt zone portals after GLB load.");
        }

        private System.Collections.IEnumerator HideLaunchLogoAfterDelay()
        {
            float seconds = Mathf.Max(0f, launchLogoDurationSeconds);
            if (seconds > 0f)
                yield return new WaitForSecondsRealtime(seconds);

            HidePlayfieldLoadingOverlay(immediate: true);
            if (pauseGameplayDuringLaunchLogo)
                Time.timeScale = _timeScaleBeforeLaunchLogo;
            MarkCharacterFlowReady();

            _launchLogoRoutine = null;
        }

        private void ShowPlayfieldLoadingOverlay()
        {
            EnsurePlayfieldLoadingOverlay();
            if (_playfieldLoadingOverlayRoot == null)
                return;

            if (_playfieldLoadingOverlayHideCoroutine != null)
            {
                StopCoroutine(_playfieldLoadingOverlayHideCoroutine);
                _playfieldLoadingOverlayHideCoroutine = null;
            }

            _playfieldLoadingOverlayShownAt = Time.realtimeSinceStartup;
            _playfieldLoadingOverlayRoot.SetActive(true);
            StartLoadingLogoSweepAnimation();
        }

        private void HidePlayfieldLoadingOverlay(bool immediate = false)
        {
            if (_playfieldLoadingOverlayRoot == null)
                return;

            if (_playfieldLoadingOverlayHideCoroutine != null)
            {
                StopCoroutine(_playfieldLoadingOverlayHideCoroutine);
                _playfieldLoadingOverlayHideCoroutine = null;
            }

            if (immediate || _playfieldLoadingOverlayShownAt < 0f)
            {
                StopLoadingLogoSweepAnimation();
                _playfieldLoadingOverlayRoot.SetActive(false);
                _playfieldLoadingOverlayShownAt = -1f;
                return;
            }

            StopLoadingLogoSweepAnimation();
            _playfieldLoadingOverlayRoot?.SetActive(false);
            _playfieldLoadingOverlayShownAt = -1f;
        }

        private void StartLoadingLogoSweepAnimation()
        {
            if (_playfieldLoadingOverlayLogoPHighlightMaskRect == null
                || _playfieldLoadingOverlayLogoMHighlightMaskRect == null
                || _playfieldLoadingOverlayLogoPHighlightImage == null
                || _playfieldLoadingOverlayLogoMHighlightImage == null)
                return;

            if (_playfieldLoadingOverlayLogoSweepCoroutine != null)
                StopCoroutine(_playfieldLoadingOverlayLogoSweepCoroutine);

            _playfieldLoadingOverlayLogoPHighlightImage.enabled = true;
            _playfieldLoadingOverlayLogoMHighlightImage.enabled = true;
            _playfieldLoadingOverlayLogoSweepCoroutine = StartCoroutine(AnimateLoadingLogoSweep());
        }

        private void StopLoadingLogoSweepAnimation()
        {
            if (_playfieldLoadingOverlayLogoSweepCoroutine != null)
            {
                StopCoroutine(_playfieldLoadingOverlayLogoSweepCoroutine);
                _playfieldLoadingOverlayLogoSweepCoroutine = null;
            }

            if (_playfieldLoadingOverlayLogoPHighlightImage != null)
                _playfieldLoadingOverlayLogoPHighlightImage.enabled = false;

            if (_playfieldLoadingOverlayLogoMHighlightImage != null)
                _playfieldLoadingOverlayLogoMHighlightImage.enabled = false;

            if (_playfieldLoadingOverlayLogoPHighlightMaskRect != null)
                _playfieldLoadingOverlayLogoPHighlightMaskRect.anchoredPosition = Vector2.zero;

            if (_playfieldLoadingOverlayLogoMHighlightMaskRect != null)
                _playfieldLoadingOverlayLogoMHighlightMaskRect.anchoredPosition = Vector2.zero;

            if (_playfieldLoadingOverlayLogoPHighlightRect != null)
                _playfieldLoadingOverlayLogoPHighlightRect.anchoredPosition = Vector2.zero;

            if (_playfieldLoadingOverlayLogoMHighlightRect != null)
                _playfieldLoadingOverlayLogoMHighlightRect.anchoredPosition = Vector2.zero;

            if (_playfieldLoadingOverlayLogo != null)
                _playfieldLoadingOverlayLogo.color = playfieldLoadingLogoTint;
        }

        private System.Collections.IEnumerator AnimateLoadingLogoSweep()
        {
            float highlightDuration = Mathf.Max(0.15f, playfieldLoadingLogoLetterHighlightDurationSeconds);
            float pause = Mathf.Max(0f, playfieldLoadingLogoSweepPauseSeconds);
            float pulseDuration = Mathf.Max(0.25f, playfieldLoadingLogoPulseDurationSeconds);
            float pulseAmount = Mathf.Clamp(playfieldLoadingLogoPulseAmount, 0f, 0.35f);
            Color pSweepColor = playfieldLoadingLogoPSweepTint;
            pSweepColor.a = Mathf.Clamp01(pSweepColor.a);
            Color mSweepColor = playfieldLoadingLogoMSweepTint;
            mSweepColor.a = Mathf.Clamp01(mSweepColor.a);
            Color baseColor = playfieldLoadingLogoTint;
            float baseAlpha = Mathf.Clamp01(baseColor.a);

            while (_playfieldLoadingOverlayRoot != null && _playfieldLoadingOverlayRoot.activeInHierarchy)
            {
                if (_playfieldLoadingOverlayLogoRect == null
                    || _playfieldLoadingOverlayLogoPHighlightMaskRect == null
                    || _playfieldLoadingOverlayLogoMHighlightMaskRect == null
                    || _playfieldLoadingOverlayLogoPHighlightImage == null
                    || _playfieldLoadingOverlayLogoMHighlightImage == null)
                {
                    yield return null;
                    continue;
                }

                Rect logoRect = _playfieldLoadingOverlayLogoRect.rect;
                float logoWidth = Mathf.Max(1f, logoRect.width);
                float logoHeight = Mathf.Max(1f, logoRect.height);
                float highlightWidthNorm = Mathf.Clamp(playfieldLoadingLogoLetterHighlightWidthNormalized, 0.05f, 0.95f);
                float highlightHeightNorm = Mathf.Clamp(playfieldLoadingLogoLetterHighlightHeightNormalized, 0.05f, 0.95f);
                float highlightWidth = Mathf.Max(24f, logoWidth * highlightWidthNorm);
                float highlightHeight = Mathf.Max(24f, logoHeight * highlightHeightNorm);
                float pNorm = Mathf.Clamp01(playfieldLoadingLogoPPositionNormalized);
                float mNorm = Mathf.Clamp01(playfieldLoadingLogoMPositionNormalized);
                float yNorm = Mathf.Clamp01(playfieldLoadingLogoLetterYPositionNormalized);
                float pX = Mathf.Lerp(-0.5f * logoWidth, 0.5f * logoWidth, pNorm);
                float mX = Mathf.Lerp(-0.5f * logoWidth, 0.5f * logoWidth, mNorm);
                float y = Mathf.Lerp(-0.5f * logoHeight, 0.5f * logoHeight, yNorm);

                _playfieldLoadingOverlayLogoPHighlightMaskRect.sizeDelta = new Vector2(highlightWidth, highlightHeight);
                _playfieldLoadingOverlayLogoPHighlightMaskRect.anchoredPosition = new Vector2(pX, y);
                _playfieldLoadingOverlayLogoMHighlightMaskRect.sizeDelta = new Vector2(highlightWidth, highlightHeight);
                _playfieldLoadingOverlayLogoMHighlightMaskRect.anchoredPosition = new Vector2(mX, y);

                if (_playfieldLoadingOverlayLogoPHighlightRect != null)
                {
                    _playfieldLoadingOverlayLogoPHighlightRect.sizeDelta = new Vector2(logoWidth, logoHeight);
                    _playfieldLoadingOverlayLogoPHighlightRect.anchoredPosition = new Vector2(-pX, -y);
                }

                if (_playfieldLoadingOverlayLogoMHighlightRect != null)
                {
                    _playfieldLoadingOverlayLogoMHighlightRect.sizeDelta = new Vector2(logoWidth, logoHeight);
                    _playfieldLoadingOverlayLogoMHighlightRect.anchoredPosition = new Vector2(-mX, -y);
                }

                for (int letterIndex = 0; letterIndex < 2; letterIndex++)
                {
                    bool pActive = letterIndex == 0;
                    float elapsed = 0f;
                    while (elapsed < highlightDuration && _playfieldLoadingOverlayRoot != null && _playfieldLoadingOverlayRoot.activeInHierarchy)
                    {
                        elapsed += Time.unscaledDeltaTime;
                        float t = Mathf.Clamp01(elapsed / highlightDuration);
                        float fade = Mathf.Sin(t * Mathf.PI);
                        Color pColor = pSweepColor;
                        pColor.a *= pActive ? fade : 0.05f;
                        Color mColor = mSweepColor;
                        mColor.a *= pActive ? 0.05f : fade;
                        _playfieldLoadingOverlayLogoPHighlightImage.color = pColor;
                        _playfieldLoadingOverlayLogoMHighlightImage.color = mColor;

                        if (_playfieldLoadingOverlayLogo != null)
                        {
                            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / pulseDuration));
                            float alpha = Mathf.Clamp01(baseAlpha - (pulseAmount * 0.5f) + (pulse * pulseAmount));
                            _playfieldLoadingOverlayLogo.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
                        }
                        yield return null;
                    }

                    if (pause > 0f)
                    {
                        float pauseElapsed = 0f;
                        while (pauseElapsed < pause && _playfieldLoadingOverlayRoot != null && _playfieldLoadingOverlayRoot.activeInHierarchy)
                        {
                            pauseElapsed += Time.unscaledDeltaTime;
                            if (_playfieldLoadingOverlayLogo != null)
                            {
                                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI / pulseDuration));
                                float alpha = Mathf.Clamp01(baseAlpha - (pulseAmount * 0.5f) + (pulse * pulseAmount));
                                _playfieldLoadingOverlayLogo.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
                            }
                            yield return null;
                        }
                    }
                }
            }

            if (_playfieldLoadingOverlayLogo != null)
                _playfieldLoadingOverlayLogo.color = playfieldLoadingLogoTint;
            _playfieldLoadingOverlayLogoSweepCoroutine = null;
        }

        private void ConfigureLoadingLogoRect(RectTransform target)
        {
            if (target == null)
                return;

            if (playfieldLoadingLogoFillScreen)
            {
                target.anchorMin = Vector2.zero;
                target.anchorMax = Vector2.one;
                target.offsetMin = Vector2.zero;
                target.offsetMax = Vector2.zero;
            }
            else
            {
                target.anchorMin = new Vector2(0.5f, 0.5f);
                target.anchorMax = new Vector2(0.5f, 0.5f);
                target.pivot = new Vector2(0.5f, 0.5f);
                target.sizeDelta = playfieldLoadingLogoSize;
                target.anchoredPosition = Vector2.zero;
            }
        }

        private void EnsurePlayfieldLoadingOverlay()
        {
            if (_playfieldLoadingOverlayRoot != null)
                return;

            var root = new GameObject("PlayfieldLoadingOverlay");
            root.transform.SetParent(transform, false);

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(root.transform, false);
            var bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bg = bgGo.AddComponent<Image>();
            bg.color = playfieldLoadingOverlayColor;
            bg.raycastTarget = false;

            var logoGo = new GameObject("Logo");
            logoGo.transform.SetParent(root.transform, false);
            var logoRect = logoGo.AddComponent<RectTransform>();
            ConfigureLoadingLogoRect(logoRect);
            _playfieldLoadingOverlayLogoRect = logoRect;

            _playfieldLoadingOverlayLogo = logoGo.AddComponent<Image>();
            _playfieldLoadingOverlayLogo.color = playfieldLoadingLogoTint;
            _playfieldLoadingOverlayLogo.raycastTarget = false;
            _playfieldLoadingOverlayLogo.preserveAspect = !playfieldLoadingLogoFillScreen;

            Sprite logo = TryLoadLaunchLogoSprite(playfieldLoadingLogoResourcePath);

            if (logo != null)
            {
                _playfieldLoadingOverlayLogo.sprite = logo;
                CreateLetterHighlight(
                    root.transform,
                    "PHighlightMask",
                    "PHighlight",
                    logo,
                    playfieldLoadingLogoPSweepTint,
                    out _playfieldLoadingOverlayLogoPHighlightMaskRect,
                    out _playfieldLoadingOverlayLogoPHighlightRect,
                    out _playfieldLoadingOverlayLogoPHighlightImage);

                CreateLetterHighlight(
                    root.transform,
                    "MHighlightMask",
                    "MHighlight",
                    logo,
                    playfieldLoadingLogoMSweepTint,
                    out _playfieldLoadingOverlayLogoMHighlightMaskRect,
                    out _playfieldLoadingOverlayLogoMHighlightRect,
                    out _playfieldLoadingOverlayLogoMHighlightImage);
            }
            else
            {
                Debug.LogWarning(
                    $"Playfield loading overlay could not load logo sprite at Resources path '{playfieldLoadingLogoResourcePath}'. " +
                    $"If this is an SVG, confirm its importer generates a Sprite. " +
                    $"{BuildLaunchLogoResourceDebug(playfieldLoadingLogoResourcePath)}");
            }

            _playfieldLoadingOverlayRoot = root;
            _playfieldLoadingOverlayRoot.SetActive(false);
        }

        private void CreateLetterHighlight(
            Transform parent,
            string maskName,
            string imageName,
            Sprite logo,
            Color tint,
            out RectTransform maskRect,
            out RectTransform highlightRect,
            out Image highlightImage)
        {
            var maskGo = new GameObject(maskName);
            maskGo.transform.SetParent(parent, false);
            maskRect = maskGo.AddComponent<RectTransform>();
            maskRect.anchorMin = new Vector2(0.5f, 0.5f);
            maskRect.anchorMax = new Vector2(0.5f, 0.5f);
            maskRect.pivot = new Vector2(0.5f, 0.5f);
            maskRect.anchoredPosition = Vector2.zero;
            maskGo.AddComponent<RectMask2D>();

            var highlightGo = new GameObject(imageName);
            highlightGo.transform.SetParent(maskGo.transform, false);
            highlightRect = highlightGo.AddComponent<RectTransform>();
            highlightRect.anchorMin = new Vector2(0.5f, 0.5f);
            highlightRect.anchorMax = new Vector2(0.5f, 0.5f);
            highlightRect.pivot = new Vector2(0.5f, 0.5f);
            highlightRect.anchoredPosition = Vector2.zero;

            highlightImage = highlightGo.AddComponent<Image>();
            highlightImage.sprite = logo;
            highlightImage.color = tint;
            highlightImage.raycastTarget = false;
            highlightImage.preserveAspect = !playfieldLoadingLogoFillScreen;
            highlightImage.enabled = false;
        }

        private static Sprite TryLoadLaunchLogoSprite(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return null;

            string normalized = resourcePath.Replace('\\', '/');
            string ext = Path.GetExtension(normalized);
            string basePath = string.IsNullOrEmpty(ext)
                ? normalized
                : normalized.Substring(0, normalized.Length - ext.Length);

            var candidatePaths = new List<string>();
            void AddCandidate(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                if (!candidatePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    candidatePaths.Add(path);
            }

            AddCandidate(normalized);
            AddCandidate(basePath);
            AddCandidate(basePath + ".svg");

            for (int i = 0; i < candidatePaths.Count; i++)
            {
                string path = candidatePaths[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var directSprite = Resources.Load<Sprite>(path);
                if (directSprite != null)
                    return directSprite;

                var sprites = Resources.LoadAll<Sprite>(path);
                if (sprites != null && sprites.Length > 0)
                    return sprites[0];

                var all = Resources.LoadAll<UnityEngine.Object>(path);
                if (all == null || all.Length == 0)
                    continue;

                for (int a = 0; a < all.Length; a++)
                {
                    if (all[a] is Sprite sprite)
                        return sprite;
                }

                for (int a = 0; a < all.Length; a++)
                {
                    if (all[a] is Texture2D tex)
                    {
                        return Sprite.Create(
                            tex,
                            new Rect(0f, 0f, tex.width, tex.height),
                            new Vector2(0.5f, 0.5f),
                            100f);
                    }
                }
            }

            return null;
        }

        private static string BuildLaunchLogoResourceDebug(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
                return "Empty resource path.";

            string normalized = resourcePath.Replace('\\', '/');
            string ext = Path.GetExtension(normalized);
            string basePath = string.IsNullOrEmpty(ext)
                ? normalized
                : normalized.Substring(0, normalized.Length - ext.Length);

            var candidatePaths = new List<string>();
            void AddCandidate(string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                if (!candidatePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    candidatePaths.Add(path);
            }

            AddCandidate(normalized);
            AddCandidate(basePath);
            AddCandidate(basePath + ".svg");

            var lines = new List<string>();
            for (int i = 0; i < candidatePaths.Count; i++)
            {
                string path = candidatePaths[i];
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                var all = Resources.LoadAll<UnityEngine.Object>(path);
                if (all == null || all.Length == 0)
                {
                    lines.Add($"path='{path}' assets=0");
                    continue;
                }

                string types = string.Join(", ", all.Select(o => o == null ? "null" : o.GetType().Name).Distinct());
                lines.Add($"path='{path}' assets={all.Length} types=[{types}]");
            }

            return lines.Count > 0 ? string.Join(" | ", lines) : "No candidate paths resolved.";
        }

    }
}
