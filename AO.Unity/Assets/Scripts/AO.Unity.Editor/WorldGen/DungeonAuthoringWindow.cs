using System;
using System.Linq;
using AO.Unity.World.Procedural;
using UnityEditor;
using UnityEngine;
using WorldGen.Content;

namespace AO.Unity.Editor.WorldGen
{
    public sealed class DungeonAuthoringWindow : EditorWindow
    {
        private SubwayDungeonKit kit;
        private SerializedObject kitObject;
        [SerializeField] private int selected;
        [SerializeField] private string filter = "";
        private Vector2 scroll;
        [SerializeField] private int seed = 90602, rooms = 30;
        [SerializeField] private bool showCollision;
        private double nextLightingRefresh;
        [SerializeField] private ProceduralDungeonDebugView preview;
        private UnityEditor.Editor assetPreview;
        private string newAssetId = "custom-prop-01";
        private DungeonAssetRole newAssetRole = DungeonAssetRole.Pillar;
        private string status = "Select an asset to edit its metadata.";

        [MenuItem("Tools/WorldGen/Dungeon Authoring")]
        public static void Open() => GetWindow<DungeonAuthoringWindow>("Dungeon Authoring");
        private void OnEnable()
        {
            minSize = new Vector2(850, 700);
            LoadKit();
            SceneView.duringSceneGui += DrawCollision;
        }
        private void OnDisable()
        {
            SceneView.duringSceneGui -= DrawCollision;
            if (assetPreview != null) DestroyImmediate(assetPreview);
        }
        private void LoadKit()
        {
            kit = AssetDatabase.LoadAssetAtPath<SubwayDungeonKit>("Assets/Resources/SubwayKit/SubwayDungeonKit.asset");
            kitObject = kit != null ? new SerializedObject(kit) : null;
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Edit reusable asset metadata, preview a seeded Subway dungeon, and inspect its collision. Changes are local until saved.", MessageType.Info);
            if (GUILayout.Button("Open native PF room authoring")) NativeDungeonAuthoringWindow.Open();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Refresh source models"))
                {
                    try { SubwayDungeonKitImporter.RefreshKit(); LoadKit(); status = "Models refreshed; authored metadata preserved."; }
                    catch (Exception e) { status = e.Message; }
                }
                if (kitObject == null) { EditorGUILayout.HelpBox("Import the Subway kit first.", MessageType.Warning); return; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    newAssetId = EditorGUILayout.TextField("New asset ID", newAssetId);
                    newAssetRole = (DungeonAssetRole)EditorGUILayout.EnumPopup(newAssetRole);
                    if (GUILayout.Button("Add GLB asset"))
                    {
                        string source = EditorUtility.OpenFilePanel("Import a Y-up GLB", "", "glb");
                        if (!string.IsNullOrEmpty(source))
                        {
                            try { SubwayDungeonKitImporter.AddAsset(kit, source, newAssetId, newAssetRole); LoadKit(); selected = kit.Entries.Count - 1; status = "Asset registered. Review its orientation, role, and eligible themes."; }
                            catch (Exception e) { status = e.Message; }
                        }
                    }
                }
                kitObject.Update();
                filter = EditorGUILayout.TextField("Find asset", filter);
                var entries = kitObject.FindProperty("entries");
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(220)))
                        for (int i = 0; i < kit.Entries.Count; i++)
                        {
                            var entry = kit.Entries[i];
                            string title = entry.Asset?.id ?? entry.Role;
                            if (filter.Length > 0 && title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                                && (entry.Asset?.purpose ?? entry.Role).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (GUILayout.Toggle(i == selected, title, "Button") && selected != i)
                            {
                                selected = i;
                                if (assetPreview != null) DestroyImmediate(assetPreview);
                                assetPreview = null;
                            }
                        }
                    using (new EditorGUILayout.VerticalScope())
                    {
                        selected = Mathf.Clamp(selected, 0, entries.arraySize - 1);
                        scroll = EditorGUILayout.BeginScrollView(scroll);
                        SerializedProperty entry = entries.GetArrayElementAtIndex(selected);
                        EditorGUILayout.PropertyField(entry.FindPropertyRelative("Prefab"), new GUIContent("Model binding"));
                        var metadata = entry.FindPropertyRelative("Asset");
                        if (kit.Entries[selected].Asset == null)
                            EditorGUILayout.HelpBox("Refresh source models to create portable metadata.", MessageType.Warning);
                        else
                        {
                            EditorGUILayout.PropertyField(metadata.FindPropertyRelative("id"), new GUIContent("Stable asset ID"));
                            var purpose = metadata.FindPropertyRelative("purpose");
                            if (Enum.TryParse(purpose.stringValue, true, out DungeonAssetRole role))
                                purpose.stringValue = ((DungeonAssetRole)EditorGUILayout.EnumPopup("Placement role", role)).ToString();
                            else EditorGUILayout.PropertyField(purpose);
                            foreach (string field in new[] { "allowedThemes", "excludedThemes", "tags", "lighting", "approvalStatus", "notes" })
                                EditorGUILayout.PropertyField(metadata.FindPropertyRelative(field), true);
                            using (new EditorGUI.DisabledScope(true))
                                foreach (string field in new[] { "dimensionsMeters", "pivot", "fitMode", "allowedRotations", "clearance", "collision", "sockets" })
                                    EditorGUILayout.PropertyField(metadata.FindPropertyRelative(field), true);
                            EditorGUILayout.HelpBox("Structural sizes and collision are generated by shared Core. This first editor changes visual bindings, eligibility, and light settings; collision is shown for inspection.", MessageType.Info);
                        }
                        EditorGUILayout.EndScrollView();
                    }
                }
                kitObject.ApplyModifiedProperties();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Validate and save catalog"))
                    {
                        try
                        {
                            if (!kit.TryValidate(out string error)) throw new InvalidOperationException(error);
                            SubwayDungeonKitImporter.ExportCatalog(kit);
                            EditorUtility.SetDirty(kit); AssetDatabase.SaveAssets();
                            status = "Catalog validated, model hashes checked, and local changes saved.";
                        }
                        catch (Exception e) { status = "Validation failed: " + e.Message; }
                    }
                    if (GUILayout.Button("Select kit asset")) Selection.activeObject = kit;
                }
                var prefab = kit.Entries[selected].Prefab;
                if (prefab != null)
                {
                    if (assetPreview == null) assetPreview = UnityEditor.Editor.CreateEditor(prefab);
                    assetPreview.OnInteractivePreviewGUI(GUILayoutUtility.GetRect(220, 140), GUIStyle.none);
                }
                EditorGUILayout.Space();
                seed = EditorGUILayout.IntField("Dungeon seed", seed);
                rooms = EditorGUILayout.IntSlider("Room count", rooms, 3, 256);
                showCollision = EditorGUILayout.Toggle("Show collision in Scene view", showCollision);
                if (GUILayout.Button("Generate / rebuild Subway preview"))
                {
                    try
                    {
                        if (!kit.TryValidate(out string error)) throw new InvalidOperationException(error);
                        if (preview == null)
                        {
                            var root = new GameObject("Authored Subway Preview");
                            Undo.RegisterCreatedObjectUndo(root, "Create Subway preview");
                            preview = root.AddComponent<ProceduralDungeonDebugView>();
                            preview.ConfigureAsStandaloneSubwayPreview();
                        }
                        var settings = new SerializedObject(preview);
                        settings.FindProperty("seed").longValue = seed;
                        settings.FindProperty("roomCount").intValue = rooms;
                        settings.ApplyModifiedProperties();
                        preview.RebuildSubway();
                        string verification = SubwayPresentationVerifier.Verify(preview);
                        Selection.activeGameObject = preview.gameObject;
                        SceneView.RepaintAll();
                        status = verification;
                    }
                    catch (Exception e) { status = e.Message; }
                }
                if (preview != null && GUILayout.Button("Remove preview"))
                { preview.Clear(); Undo.DestroyObjectImmediate(preview.gameObject); preview = null; }
            }
            EditorGUILayout.HelpBox(status, MessageType.None);
        }
        private void DrawCollision(SceneView scene)
        {
            if (preview == null) return;
            if (EditorApplication.timeSinceStartup >= nextLightingRefresh)
            {
                preview.RefreshFixtureShadows(scene.camera.transform.position);
                nextLightingRefresh = EditorApplication.timeSinceStartup + .25;
            }
            if (!showCollision) return;
            Handles.color = new Color(.1f, .8f, 1, .7f);
            foreach (BoxCollider collider in preview.GetComponentsInChildren<BoxCollider>(true))
                if (collider.enabled && !collider.isTrigger)
                    using (new Handles.DrawingScope(collider.transform.localToWorldMatrix))
                        Handles.DrawWireCube(collider.center, collider.size);
        }
    }
}
