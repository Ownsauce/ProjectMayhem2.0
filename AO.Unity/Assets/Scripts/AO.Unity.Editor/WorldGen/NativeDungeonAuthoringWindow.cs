using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AO.Assets.Decoders;
using AO.Assets.Navigation;
using AO.Assets.ResourceDatabase;
using AO.Unity.World;
using AO.Unity.Assets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WorldGen.Dungeons;
using WorldGen.Authoring;
using WorldGen.Spatial;

namespace AO.Unity.Editor.WorldGen
{
    /// <summary>Uses shared planning/validation and the actual runtime reader for native previews.</summary>
    public sealed class NativeDungeonAuthoringWindow : EditorWindow
    {
        [Serializable] private sealed class ExportedRecipe
        {
            public int Version = 2;
            public string SourceId;
            public string GeneratorVersion, CatalogHash, Seed, Pool;
            public NativeRoomOverrides Overrides;
            public int RoomCount;
            public NativeRoomRecipeData Recipe;
        }
        [SerializeField] private string sourceId, roomSearch = "", serverCatalogRoot = "", dotnetPath = "";
        [SerializeField] private bool onlyUnreviewed;
        private NativeRoomSourceRegistration[] sources = Array.Empty<NativeRoomSourceRegistration>();
        private string savedSettings, preparedDirectory;
        private System.Diagnostics.Process publicationProcess;
        private System.Threading.Tasks.Task<string> processOutput, processErrors;
        private bool preparingPublication, publicationReloadLocked;
        [SerializeField] private string seed = "90602", pool = "mixed";
        [SerializeField] private int count = 24, selected;
        [SerializeField] private bool showNavigation = true, showCollision;
        [SerializeField] private bool showRoomLabels = true;
        private bool pickEncounterCenter;
        private NativeRoomCatalog catalog;
        private NativeRoomOverrides overrides;
        private INativeRoomAuthoringDao authoringDao;
        private NativeRoomRecipe previewRecipe;
        private GameObject preview;
        private IEnumerator loading;
        private NativeIndoorVisuals loadingOwner;
        private readonly List<Mesh> meshes = new List<Mesh>();
        private long publishedSettingsRevision;
        private string catalogHash, status = "Load the prepared native catalog to begin.";
        private Vector2 scroll;
        private Vector2 windowScroll;
        private double nextLighting;
        private string OverridePath => AuthoringPath(catalog.SourcePlayfield);
        private static string AuthoringPath(int source) => Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/WorldGen.NativeRoomCatalog", "pf" + source + ".authoring.json"));
        [MenuItem("Tools/WorldGen/Native Dungeon Authoring")]
        public static void Open() => GetWindow<NativeDungeonAuthoringWindow>("Native Dungeons");
        private void OnEnable() { EditorApplication.update -= FinishPublicationAfterClose; Reload(); EditorApplication.update += Tick; SceneView.duringSceneGui += Draw; }
        private void OnDisable() { EditorApplication.update -= Tick; SceneView.duringSceneGui -= Draw; ClearPreview();
            if (publicationProcess != null) { EditorApplication.update += FinishPublicationAfterClose; } }
        private void Reload()
        {
            try
            {
                sources = NativeRoomCatalogs.Store.Registry().Sources;
                if (!sources.Any(v => v.Id == sourceId)) sourceId = NativeRoomCatalogs.Store.Registry().DefaultSource;
                catalog = NativeRoomCatalogs.Read(sourceId, null, out catalogHash);
                publishedSettingsRevision = NativeRoomCatalogs.Store.Load(sourceId, catalogHash).Publication.AuthoringRevision;
                authoringDao = new NativeRoomAuthoringFileDao(AuthoringPath,
                    value => JsonUtility.FromJson<NativeRoomOverrides>(value), value => JsonUtility.ToJson(value, true));
                overrides = authoringDao.Load(catalog.SourcePlayfield)
                    ?? new NativeRoomOverrides { SourcePlayfield = catalog.SourcePlayfield, SourceSha256 = catalog.SourceSha256 };
                PreviewCatalog();
                savedSettings = JsonUtility.ToJson(overrides);
                status = $"Loaded {catalog.Rooms.Length} rooms. Select a source, inspect rooms, then validate and publish saved settings to both local hosts.";
            }
            catch (Exception error) { catalog = null; status = error.Message; }
        }
        private NativeRoomOverride Edit(int source)
        {
            bool wasClean = !HasUnsavedEdits;
            var entry = overrides.Rooms.FirstOrDefault(r => r.SourceIndex == source);
            if (entry == null)
            {
                var room = catalog.Rooms.Single(r => r.SourceIndex == source);
                var light = room.Lights.FirstOrDefault();
                entry = new NativeRoomOverride {
                    SourceIndex = source, Enabled = room.Enabled,
                    Annotation = room.Annotation == null ? new NativeRoomAnnotation()
                        : JsonUtility.FromJson<NativeRoomAnnotation>(JsonUtility.ToJson(room.Annotation)),
                    BlockedSockets = room.Sockets.Select((s, i) => new { s, i }).Where(v => v.s.Excluded).Select(v => v.i).ToArray(),
                    ClosureStyle = room.Sockets.FirstOrDefault()?.ClosureStyle ?? NativeClosureStyle.MetalPanel,
                    LightMultiplier = light != null && light.BaseIntensity > 0 ? light.Intensity / light.BaseIntensity : 1,
                    LightRangeMultiplier = light != null && light.BaseRange > 0 ? light.Range / light.BaseRange : 1,
                    Emission = light?.Emission ?? 8
                };
                overrides.Rooms = overrides.Rooms.Concat(new[] { entry }).ToArray();
            }
            if (entry.Annotation == null)
                entry.Annotation = JsonUtility.FromJson<NativeRoomAnnotation>(JsonUtility.ToJson(catalog.Rooms.Single(r => r.SourceIndex == source).Annotation ?? new NativeRoomAnnotation()));
            if (wasClean) savedSettings = JsonUtility.ToJson(overrides);
            return entry;
        }
        private NativeRoomCatalog PreviewCatalog()
        {
            var copy = JsonUtility.FromJson<NativeRoomCatalog>(JsonUtility.ToJson(catalog)); overrides.Apply(copy); NativeRoomDungeon.Validate(copy); return copy;
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Inspect original rooms, mark their allowed uses and landmarks, and choose supported encounter centers. Tags describe template capabilities; normal generation ignores roles.", MessageType.Info);
            windowScroll = EditorGUILayout.BeginScrollView(windowScroll);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || publicationProcess != null))
            {
                if (sources.Length > 0)
                {
                    int current = Math.Max(0, Array.FindIndex(sources, v => v.Id == sourceId));
                    int chosen = EditorGUILayout.Popup("AO room source", current, sources.Select(v => v.Name + " [" + v.Id + "]").ToArray());
                    if (chosen != current && CanDiscardEdits()) { ClearPreview(); selected = 0; pool = "mixed"; sourceId = sources[chosen].Id; Reload(); }
                }
                if (GUILayout.Button("Reload catalog / overrides") && CanDiscardEdits()) { ClearPreview(); Reload(); }
                if (catalog != null)
                {
                    EditorGUILayout.LabelField("Published catalog", catalogHash.Substring(0, 12) + " / settings revision " + publishedSettingsRevision);
                    EditorGUILayout.LabelField("Authoring", "saved revision " + overrides.Revision + (HasUnsavedEdits ? " / unsaved changes" : " / saved"));
                    seed = EditorGUILayout.TextField("Seed", seed);
                    count = EditorGUILayout.IntSlider("Room count", count, 4, 24);
                    var pools = catalog.Rooms.Select(r => r.Pool).Where(p => !string.IsNullOrEmpty(p)).Distinct().Prepend("mixed").ToArray();
                    pool = pools[EditorGUILayout.Popup("Room pool", Math.Max(0, Array.IndexOf(pools, pool)), pools)];
                    int previousSelection = selected;
                    roomSearch = EditorGUILayout.TextField("Find room", roomSearch);
                    onlyUnreviewed = EditorGUILayout.Toggle("Unreviewed rooms only", onlyUnreviewed);
                    var visible = catalog.Rooms.Select((value, index) => new { value, index }).Where(v =>
                        (string.IsNullOrWhiteSpace(roomSearch) || (v.value.SourceIndex + " " + v.value.Name).IndexOf(roomSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                        && (!onlyUnreviewed || !((overrides.Rooms.FirstOrDefault(e => e.SourceIndex == v.value.SourceIndex)?.Annotation ?? v.value.Annotation)?.Reviewed ?? false))).ToArray();
                    if (visible.Length == 0) EditorGUILayout.LabelField("No rooms match this filter.");
                    else
                    {
                        int at = Math.Max(0, Array.FindIndex(visible, v => v.index == selected));
                        selected = visible[EditorGUILayout.Popup("Source room", at, visible.Select(v => v.value.SourceIndex + ": " + v.value.Name).ToArray())].index;
                    }
                    if (selected != previousSelection) pickEncounterCenter = false;
                    var room = catalog.Rooms[selected]; var edit = Edit(room.SourceIndex);
                    using (new EditorGUI.DisabledScope(room.SourceIndex == catalog.EntranceSourceRoom)) edit.Enabled = EditorGUILayout.Toggle("Use in generation", edit.Enabled);
                    DrawAnnotations(room, edit);
                    edit.ClosureStyle = (NativeClosureStyle)EditorGUILayout.EnumPopup("Closed doorway appearance", edit.ClosureStyle);
                    edit.LightMultiplier = EditorGUILayout.Slider("Light intensity multiplier", edit.LightMultiplier, 0, 10);
                    edit.LightRangeMultiplier = EditorGUILayout.Slider("Light range multiplier", edit.LightRangeMultiplier, .1f, 3);
                    edit.Emission = EditorGUILayout.Slider("Visible diffuser emission", edit.Emission, 0, 50);
                    scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(210));
                    for (int i = 0; i < room.Sockets.Length; i++)
                    {
                        var socket = room.Sockets[i]; bool excluded = edit.BlockedSockets.Contains(i);
                        bool value = EditorGUILayout.Toggle($"Socket {i}: floor {socket.Y * .001f:F2} m, clearance {socket.Clearance * .001f:F2} m, region {socket.Region}" + (socket.Blocked ? " (unsafe)" : socket.Exterior ? " (exterior)" : ""), excluded);
                        if (value != excluded) edit.BlockedSockets = value ? edit.BlockedSockets.Concat(new[] { i }).OrderBy(v => v).ToArray() : edit.BlockedSockets.Where(v => v != i).ToArray();
                    }
                    EditorGUILayout.EndScrollView();
                    showNavigation = EditorGUILayout.Toggle("Show walking regions / sockets", showNavigation);
                    showCollision = EditorGUILayout.Toggle("Show collision bounds", showCollision);
                    showRoomLabels = EditorGUILayout.Toggle("Show room roles / encounter centers", showRoomLabels);
                    if (GUILayout.Button("Preview selected source room")) Run(PreviewSourceRoom);
                    if (GUILayout.Button("Generate textured native preview")) Run(GeneratePreview);
                    using (new EditorGUI.DisabledScope(previewRecipe == null || previewRecipe.Rooms.Count < 4))
                        if (GUILayout.Button("Pin current preview rooms")) { overrides.PinnedRecipe = NativeRoomRecipeData.Capture(previewRecipe); status = $"Pinned {previewRecipe.Rooms.Count} rooms. Increase the room count and regenerate to grow this layout."; }
                    if (overrides.PinnedRecipe != null)
                    {
                        EditorGUILayout.LabelField("Pinned rooms", overrides.PinnedRecipe.Rooms.Length.ToString());
                        if (GUILayout.Button("Clear pinned rooms")) overrides.PinnedRecipe = null;
                    }
                    if (GUILayout.Button("Validate 100 seeds (layout / traversal)")) Run(Sweep);
                    if (GUILayout.Button("Save room tags / settings")) Run(SaveSettings);
                    EditorGUILayout.Space();
                    serverCatalogRoot = EditorGUILayout.TextField("Server NativeCopies folder", ServerCatalogRoot);
                    dotnetPath = EditorGUILayout.TextField("dotnet executable", DotnetPath);
                    if (GUILayout.Button("Validate and publish catalog to client / server")) Run(BeginPublication);
                    using (new EditorGUI.DisabledScope(previewRecipe == null || previewRecipe.Rooms.Count < 4))
                        if (GUILayout.Button("Export resolved preview recipe")) Run(() => {
                            string path = EditorUtility.SaveFilePanel("Save native dungeon recipe", "", "native-dungeon", "json");
                            if (string.IsNullOrEmpty(path)) return;
                            File.WriteAllText(path, JsonUtility.ToJson(new ExportedRecipe { SourceId = sourceId, GeneratorVersion = NativeRoomDungeon.GeneratorVersion,
                                CatalogHash = catalogHash, Overrides = overrides,
                                Seed = seed, RoomCount = previewRecipe.Rooms.Count, Pool = pool, Recipe = NativeRoomRecipeData.Capture(previewRecipe) }, true));
                            status = "Saved resolved preview placements and joins to " + path;
                        });
                    if (GUILayout.Button("Import resolved preview recipe")) Run(ImportPreview);
                    if (GUILayout.Button("Remove preview")) ClearPreview();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox(status, MessageType.None);
        }
        private bool HasUnsavedEdits => overrides != null && JsonUtility.ToJson(overrides) != savedSettings;
        private bool CanDiscardEdits() => !HasUnsavedEdits || EditorUtility.DisplayDialog("Unsaved room settings", "Discard unsaved edits before changing source or revision?", "Discard", "Keep editing");
        private string ServerCatalogRoot => string.IsNullOrEmpty(serverCatalogRoot)
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../AORebirth/AORebirth/Server/ZoneEngine_New/NativeCopies")) : serverCatalogRoot;
        private string DotnetPath
        {
            get
            {
                if (!string.IsNullOrEmpty(dotnetPath)) return dotnetPath;
                string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", Application.platform == RuntimePlatform.WindowsEditor ? "dotnet.exe" : "dotnet");
                return File.Exists(local) ? local : "dotnet";
            }
        }
        private void SaveSettings()
        {
            PreviewCatalog(); overrides.SourceSha256 = catalog.SourceSha256; authoringDao.Save(overrides);
            savedSettings = JsonUtility.ToJson(overrides); status = "Saved settings revision " + overrides.Revision + ". Publish to update gameplay.";
        }
        private string SurfacePath()
        {
            var install = AOInstallConfiguration.GetConfiguredInstall();
            string fingerprint = new string(install.DatabaseFingerprint.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
            return Path.Combine(AOInstallConfiguration.CacheRoot, "IndoorSurfaces", fingerprint, catalog.SourcePlayfield + "_v2.aois");
        }
        private void BeginPublication()
        {
            if (HasUnsavedEdits || !File.Exists(OverridePath)) SaveSettings();
            if (!Directory.Exists(ServerCatalogRoot)) throw new DirectoryNotFoundException("Choose the server NativeCopies folder.");
            if (!File.Exists(SurfacePath())) throw new FileNotFoundException("Preview this source once to prepare local surfaces before publishing.");
            ClearPreview(); preparedDirectory = Path.Combine(Application.temporaryCachePath, "NativePrepared-" + Guid.NewGuid().ToString("N"));
            preparingPublication = true;
            EditorApplication.LockReloadAssemblies(); publicationReloadLocked = true;
            try { StartPublicationProcess("WorldGen.NativeRoomCatalog", AOInstallConfiguration.GetConfiguredInstall().RootPath,
                SurfacePath(), preparedDirectory, OverridePath, SourceDefinitionPath()); }
            catch { EditorApplication.UnlockReloadAssemblies(); publicationReloadLocked = false; throw; }
            status = "Preparing and validating source geometry, spawn support and generated joins…";
        }
        private string SourceDefinitionPath() => Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools/WorldGen.NativeRoomCatalog", "pf" + catalog.SourcePlayfield + ".source.json"));
        // Process arguments must preserve paths containing spaces on both supported desktop editors.
        private static string QuoteArgument(string value)
        {
            var result = new System.Text.StringBuilder("\""); int slashes = 0;
            foreach (char character in value)
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes); slashes = 0;
                result.Append(character);
            }
            result.Append('\\', slashes * 2); result.Append('"'); return result.ToString();
        }
        private void StartPublicationProcess(string project, params string[] arguments)
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../tools", project, project + ".csproj"));
            var start = new System.Diagnostics.ProcessStartInfo { FileName = DotnetPath,
                Arguments = "run --project " + QuoteArgument(projectPath) + " -- " + string.Join(" ", arguments.Select(QuoteArgument)),
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            publicationProcess = System.Diagnostics.Process.Start(start);
            processOutput = publicationProcess.StandardOutput.ReadToEndAsync(); processErrors = publicationProcess.StandardError.ReadToEndAsync();
        }
        private void FinishPublicationAfterClose()
        {
            PollPublication();
            if (publicationProcess == null) EditorApplication.update -= FinishPublicationAfterClose;
        }
        private void PollPublication()
        {
            if (publicationProcess == null || !publicationProcess.HasExited || !processOutput.IsCompleted || !processErrors.IsCompleted) return;
            int exit = publicationProcess.ExitCode;
            string output = processOutput.Result + processErrors.Result;
            publicationProcess.Dispose(); publicationProcess = null;
            string log = Path.Combine(Application.temporaryCachePath, "native-catalog-publication.log"); File.AppendAllText(log, output);
            try
            {
                if (exit != 0) throw new InvalidDataException("Catalog publication failed. Review the log before generating with new settings: " + log);
                if (preparingPublication)
                {
                    preparingPublication = false;
                    StartPublicationProcess("WorldGen.PublishRoomCatalog", SourceDefinitionPath(), preparedDirectory,
                        NativeRoomCatalogs.Root, ServerCatalogRoot, overrides.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    status = "Staging matching client/server catalog and collision revisions…";
                }
                else { Reload(); status = "Published " + sourceId + " revision " + catalogHash.Substring(0, 12) + " to both local repositories. Rebuild/install the server for gameplay. " + log; }
            }
            catch (Exception error) { status = error.Message; Debug.LogError(status); }
            finally
            {
                if (publicationProcess == null && preparedDirectory != null && Directory.Exists(preparedDirectory)) Directory.Delete(preparedDirectory, true);
                if (publicationProcess == null)
                { preparedDirectory = null; if (publicationReloadLocked) { EditorApplication.UnlockReloadAssemblies(); publicationReloadLocked = false; } }
            }
            Repaint();
        }
        private void Run(Action action) { try { action(); } catch (Exception error) { status = error.Message; Debug.LogException(error); } }
        private void DrawAnnotations(NativeRoomTemplate source, NativeRoomOverride edit)
        {
            var annotation = edit.Annotation;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Room capabilities", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            annotation.AllowedUses = (NativeRoomUse)EditorGUILayout.EnumFlagsField("Allowed uses", annotation.AllowedUses);
            // Unity's Everything entry uses -1; persist only the declared role bits.
            annotation.AllowedUses &= NativeRoomAnnotations.AllUses;
            annotation.Landmark = EditorGUILayout.Toggle("Landmark", annotation.Landmark);
            annotation.Reviewed = EditorGUILayout.Toggle("Tags reviewed", annotation.Reviewed);
            string themes = string.Join(", ", annotation.ThemeTags ?? Array.Empty<string>());
            string updated = EditorGUILayout.TextField("Theme tags (comma separated)", themes);
            if (updated != themes) annotation.ThemeTags = updated.Split(',').Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            EditorGUILayout.LabelField("Notes");
            annotation.Notes = EditorGUILayout.TextArea(annotation.Notes ?? "", GUILayout.MinHeight(36));
            var effective = new NativeRoomTemplate { SourceIndex = source.SourceIndex, Sockets = source.Sockets.Select((socket, i) => new NativeRoomSocket {
                Exterior = socket.Exterior, Blocked = socket.Blocked, Excluded = edit.BlockedSockets.Contains(i), Region = socket.Region
            }).ToArray() };
            EditorGUILayout.LabelField("Usable doors in one region", NativeRoomAnnotations.MostUsableDoorsInOneRegion(effective).ToString());
            foreach (string warning in NativeRoomAnnotations.Warnings(effective, annotation)) EditorGUILayout.HelpBox(warning, MessageType.Warning);
            if (annotation.HasEncounterCenter)
                EditorGUILayout.LabelField("Encounter center", $"({annotation.EncounterX * .001f:F2}, {annotation.EncounterY * .001f:F2}, {annotation.EncounterZ * .001f:F2}) m; region {annotation.EncounterRegion}");
            using (new EditorGUI.DisabledScope(previewRecipe == null || !previewRecipe.Rooms.Any(r => r.SourceIndex == source.SourceIndex)))
            {
                bool wasPicking = pickEncounterCenter;
                pickEncounterCenter = GUILayout.Toggle(pickEncounterCenter, "Pick encounter center in Scene view", "Button");
                if (pickEncounterCenter && !wasPicking) { status = "Click a floor inside the selected preview room. The marker snaps to a validated walking sample. Escape cancels."; SceneView.RepaintAll(); }
            }
            if (annotation.HasEncounterCenter && GUILayout.Button("Clear encounter center")) annotation.HasEncounterCenter = false;
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            EditorGUILayout.HelpBox("Arena: major fight. Encounter: regular groups. Connector: passage. Hub: route choices. Landmark is an independent visual/navigation tag. Arena size still needs visual review.", MessageType.None);
        }
        private void Sweep()
        {
            var value = PreviewCatalog(); int failures = 0; var report = new List<string>();
            for (ulong s = 1; s <= 100; s++)
                try { var recipe = NativeRoomDungeon.Generate(value, s, count, pool); NativeRoomTraversal.Validate(recipe); }
                catch (Exception error) { failures++; report.Add($"seed={s}, pool={pool}, count={count}: {error.Message}"); }
            string path = Path.Combine(Application.temporaryCachePath, "native-dungeon-seed-report.txt");
            File.WriteAllLines(path, report.Prepend($"Checked 100 seeds; failures={failures}; generator={NativeRoomDungeon.GeneratorVersion}"));
            status = $"Checked 100 seeds; failures={failures}. Report: {path}";
        }
        private void PreviewSourceRoom()
        {
            ClearPreview();
            var value = PreviewCatalog(); var room = value.Rooms[selected];
            previewRecipe = new NativeRoomRecipe { Catalog = value };
            previewRecipe.Rooms.Add(new NativeRoomPlacement { Index = 0, SourceIndex = room.SourceIndex, X = room.OriginX, Y = room.OriginY, Z = room.OriginZ });
            BuildPreview();
        }
        private void GeneratePreview()
        {
            ClearPreview();
            var install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid) throw new InvalidOperationException("Select a valid AO installation at client launch first.");
            previewRecipe = NativeRoomDungeon.Generate(PreviewCatalog(), ulong.Parse(seed), count, pool);
            BuildPreview();
        }
        private void ImportPreview()
        {
            string path = EditorUtility.OpenFilePanel("Open native dungeon recipe", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            var saved = JsonUtility.FromJson<ExportedRecipe>(File.ReadAllText(path));
            if ((saved.Version != 1 && saved.Version != 2) || saved.GeneratorVersion != NativeRoomDungeon.GeneratorVersion)
                throw new InvalidDataException("Preview recipe uses another generator version.");
            string importSource = saved.Version == 1 && saved.Overrides != null
                ? NativeRoomCatalogs.Store.LoadPlayfield(saved.Overrides.SourcePlayfield, saved.CatalogHash).Source.Id
                : saved.Version == 1 ? sourceId : saved.SourceId;
            var imported = NativeRoomCatalogs.Read(importSource, saved.CatalogHash, out string importedHash);
            if (!CanDiscardEdits()) return;
            sourceId = importSource; selected = 0; catalog = imported; catalogHash = importedHash;
            publishedSettingsRevision = NativeRoomCatalogs.Store.Load(sourceId, catalogHash).Publication.AuthoringRevision;
            authoringDao = new NativeRoomAuthoringFileDao(AuthoringPath,
                value => JsonUtility.FromJson<NativeRoomOverrides>(value), value => JsonUtility.ToJson(value, true));
            overrides = authoringDao.Load(catalog.SourcePlayfield) ?? new NativeRoomOverrides { SourcePlayfield = catalog.SourcePlayfield, SourceSha256 = catalog.SourceSha256 };
            ClearPreview();
            long currentRevision = overrides.Revision;
            overrides = saved.Overrides ?? new NativeRoomOverrides { SourcePlayfield = catalog.SourcePlayfield };
            // Import changes authored values; optimistic saving still compares with our last loaded store revision.
            overrides.Revision = currentRevision;
            previewRecipe = saved.Recipe.Restore(PreviewCatalog()); seed = saved.Seed; count = saved.RoomCount; pool = saved.Pool;
            BuildPreview();
        }
        private void BuildPreview()
        {
            var install = AOInstallConfiguration.GetConfiguredInstall();
            using var db = new AOResourceDatabase(install.RootPath);
            if (!db.TryReadRaw(AOResourceTypes.Playfield, catalog.SourcePlayfield, out var raw)) throw new InvalidDataException("Source playfield missing.");
            if (NativeRoomDungeon.Sha(raw) != catalog.SourceSha256) throw new InvalidDataException("Local source definition differs from the prepared catalog.");
            var definition = AOPlayfieldDefinitionDecoder.Decode(raw, catalog.SourcePlayfield);
            string fingerprint = new string(install.DatabaseFingerprint.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
            string surfacePath = Path.Combine(AOInstallConfiguration.CacheRoot, "IndoorSurfaces", fingerprint, catalog.SourcePlayfield + "_v2.aois");
            if (NativeRoomDungeon.Sha(File.ReadAllBytes(surfacePath)) != catalog.SurfaceSha256) throw new InvalidDataException("Local collision differs from the prepared catalog.");
            var surfaces = AOIndoorSurfaceStreamDecoder.Decode(surfacePath, catalog.SourcePlayfield);
            preview = new GameObject("Native Dungeon Preview") { hideFlags = HideFlags.DontSave };
            var source = new GameObject("SourceCollision").transform; source.SetParent(preview.transform, false);
            foreach (int index in previewRecipe.Rooms.Select(r => r.SourceIndex).Distinct())
            {
                var surfaceRoom = surfaces.Rooms.Single(r => r.Instance == index);
                var sourceMeshes = surfaceRoom.Meshes.Select(part => catalog.CollisionGeometryVersion == 1
                    ? AOIndoorRoomCollisionClipper.Clip(part, definition.Rooms[index], surfaces.Tilemap.TileSize) : part)
                    .Where(part => part.TriangleCount > 0).ToList();
                var terrain = AOIndoorDungeonTerrainBuilder.Build(definition.Rooms[index], surfaces.Tilemap, true);
                if (terrain != null) sourceMeshes.Add(terrain);
                var positions = new List<Vector3>(); var triangles = new List<int>();
                foreach (var part in sourceMeshes)
                {
                    int start = positions.Count;
                    for (int i = 0; i < part.Vertices.Length; i += 3) positions.Add(new Vector3(part.Vertices[i], part.Vertices[i + 1], part.Vertices[i + 2]));
                    triangles.AddRange(part.Triangles.Select(v => v + start));
                }
                var mesh = new Mesh { indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(positions); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds(); meshes.Add(mesh);
                var go = new GameObject("Source_" + index); go.transform.SetParent(source, false);
                go.AddComponent<NativeRoomSourcePart>().Configure(index, NativeRoomPartKind.Collision);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>(); go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            NativeRoomDungeonRuntime.BuildCollision(source, preview.transform, previewRecipe);
            loadingOwner = NativeIndoorVisuals.PrepareLoad(catalog.SourcePlayfield, install, definition, preview.transform, Vector3.zero, false, 1, source, previewRecipe, out loading);
            Selection.activeGameObject = preview;
            var bounds = new Bounds(); bool first = true;
            foreach (var room in previewRecipe.Rooms)
            {
                var value = NativeRoomDungeon.Bounds(previewRecipe.Template(room), room);
                var b = new Bounds(new Vector3(value.Minimum.X + value.Maximum.X, value.Minimum.Y + value.Maximum.Y, value.Minimum.Z + value.Maximum.Z) * .0005f,
                    new Vector3(value.Maximum.X - value.Minimum.X, value.Maximum.Y - value.Minimum.Y, value.Maximum.Z - value.Minimum.Z) * .001f);
                if (first) { bounds = b; first = false; } else bounds.Encapsulate(b);
            }
            SceneView.lastActiveSceneView?.Frame(bounds, true);
            status = "Loading selected room meshes and textures from the local AO installation…";
        }
        private void Tick()
        {
            PollPublication();
            if (loading != null)
                try { if (!loading.MoveNext()) { (loading as IDisposable)?.Dispose(); loading = null; status = loadingOwner != null && loadingOwner.IsReady ? "Native preview ready. Inspect sockets, walking regions, lighting and collision bounds." : "Native preview loading failed: " + loadingOwner?.Failure; } }
                catch (Exception error) { loading = null; status = error.Message; }
            if (preview != null && SceneView.lastActiveSceneView != null && EditorApplication.timeSinceStartup >= nextLighting)
            { nextLighting = EditorApplication.timeSinceStartup + .25; preview.GetComponentInChildren<NativeRoomVisibility>()?.Refresh(SceneView.lastActiveSceneView.camera.transform.position, false); }
        }
        private void ClearPreview()
        {
            pickEncounterCenter = false;
            (loading as IDisposable)?.Dispose(); loading = null;
            if (preview != null) DestroyImmediate(preview); preview = null; previewRecipe = null;
            foreach (var mesh in meshes) if (mesh != null) DestroyImmediate(mesh); meshes.Clear();
        }
        private void Draw(SceneView view)
        {
            if (preview == null || previewRecipe == null) return;
            PickEncounterCenter(view);
            if (showRoomLabels)
                foreach (var room in previewRecipe.Rooms)
                {
                    var template = previewRecipe.Template(room);
                    var annotation = overrides.Rooms.FirstOrDefault(r => r.SourceIndex == room.SourceIndex)?.Annotation ?? template.Annotation;
                    var labelPosition = NativeRoomDungeon.Transform(room, new WorldVector3(0, template.MinY, 0));
                    string roles = annotation == null ? "Unreviewed" : annotation.AllowedUses + (annotation.Landmark ? " / Landmark" : "") + (annotation.Reviewed ? "" : " / Unreviewed");
                    Handles.Label(new Vector3(labelPosition.X, labelPosition.Y, labelPosition.Z) * .001f + Vector3.up * 2, template.Name + "\n" + roles);
                    if (annotation?.HasEncounterCenter == true)
                    {
                        var center = NativeRoomDungeon.Transform(room, new WorldVector3(annotation.EncounterX, annotation.EncounterY, annotation.EncounterZ));
                        var position = new Vector3(center.X, center.Y, center.Z) * .001f + Vector3.up * .08f;
                        Handles.color = Color.yellow; Handles.DrawWireDisc(position, Vector3.up, .75f);
                        Handles.Label(position + Vector3.up, "Encounter center / region " + annotation.EncounterRegion);
                    }
                }
            if (showCollision)
            { Handles.color = Color.cyan; foreach (var collider in preview.GetComponentsInChildren<Collider>()) Handles.DrawWireCube(collider.bounds.center, collider.bounds.size); }
            if (showNavigation)
                foreach (var room in previewRecipe.Rooms.Where(r => r.SourceIndex == catalog.Rooms[selected].SourceIndex))
                {
                    var template = previewRecipe.Template(room);
                    foreach (var point in template.WalkPoints.Where((p, i) => i % 4 == 0))
                    {
                        var p = NativeRoomDungeon.Transform(room, new WorldVector3(point.X, point.Y, point.Z));
                        Handles.color = Color.HSVToRGB((point.Region * .17f) % 1, .8f, 1);
                        Handles.DrawWireDisc(new Vector3(p.X, p.Y, p.Z) * .001f, Vector3.up, .15f);
                    }
                    foreach (var socket in template.Sockets)
                    {
                        var p = NativeRoomDungeon.Transform(room, new WorldVector3(socket.X, socket.Y, socket.Z));
                        var position = new Vector3(p.X, p.Y, p.Z) * .001f;
                        Handles.Label(position + Vector3.up, $"Region {socket.Region} / {socket.Clearance * .001f:F1} m" + (socket.Blocked ? " BLOCKED" : ""));
                    }
                }
        }

        private void PickEncounterCenter(SceneView view)
        {
            if (!pickEncounterCenter) return;
            var current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            { pickEncounterCenter = false; current.Use(); Repaint(); return; }
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (current.type != EventType.MouseDown || current.button != 0 || current.alt) return;
            Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
            float nearest = float.PositiveInfinity; Vector3 hitPoint = default; bool hit = false;
            foreach (var collider in preview.GetComponentsInChildren<Collider>())
                if (collider.Raycast(ray, out var result, 10000) && result.distance < nearest)
                { nearest = result.distance; hitPoint = result.point; hit = true; }
            current.Use();
            if (!hit) { status = "No preview floor hit. Click inside the selected room."; Repaint(); return; }
            int source = catalog.Rooms[selected].SourceIndex;
            NativeWalkPoint best = null; NativeRoomTemplate selectedTemplate = null; float distance = .75f * .75f;
            foreach (var placement in previewRecipe.Rooms.Where(r => r.SourceIndex == source))
            {
                var template = previewRecipe.Template(placement);
                var edit = Edit(source);
                var accessible = new System.Collections.Generic.HashSet<int>(template.Sockets.Select((s, i) => new { s, i })
                    .Where(v => NativeRoomAnnotations.IsUsable(v.s) && !edit.BlockedSockets.Contains(v.i)).Select(v => v.s.Region));
                if (template.SpawnRegion >= 0) accessible.Add(template.SpawnRegion);
                foreach (var point in template.WalkPoints)
                {
                    if (!accessible.Contains(point.Region)) continue;
                    var p = NativeRoomDungeon.Transform(placement, new WorldVector3(point.X, point.Y, point.Z));
                    float squared = (new Vector3(p.X, p.Y, p.Z) * .001f - hitPoint).sqrMagnitude;
                    if (squared < distance) { distance = squared; best = point; selectedTemplate = template; }
                }
            }
            if (best == null) status = "That point is not near a supported, accessible walking sample in the selected room.";
            else
            {
                var edit = Edit(source);
                NativeRoomAnnotations.SetEncounterCenter(edit.Annotation, best);
                NativeRoomAnnotations.Validate(selectedTemplate, edit.Annotation);
                pickEncounterCenter = false; status = "Encounter center placed on validated floor. Save room tags/settings to keep it.";
            }
            Repaint(); view.Repaint();
        }
    }
}
