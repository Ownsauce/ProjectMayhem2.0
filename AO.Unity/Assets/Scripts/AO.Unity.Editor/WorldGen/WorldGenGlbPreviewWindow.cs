using System;
using System.IO;
using System.Linq;
using GLTFast;
using UnityEditor;
using UnityEngine;

namespace AO.Unity.Editor.WorldGen
{
    /// <summary>Reviews GLB meshes and baked textures from the shared asset library.</summary>
    public sealed class WorldGenGlbPreviewWindow : EditorWindow
    {
        private enum View { Front, Side, Top, ThreeQuarter }
        [SerializeField] private string _glbPath;
        private PreviewRenderUtility _preview;
        private GltfImport _import;
        private GameObject _root;
        private Bounds _bounds;
        private View _view = View.ThreeQuarter;
        private string _status = "Choose a GLB asset.";
        private bool _loading;

        [MenuItem("Tools/WorldGen/GLB Preview")]
        private static void Open()
        {
            var window = CreateInstance<WorldGenGlbPreviewWindow>();
            window.titleContent = new GUIContent("GLB Preview");
            window.minSize = new Vector2(520, 520);
            window._glbPath = DefaultPath();
            window.Show();
        }

        private static string DefaultPath()
        {
            string coding = Directory.GetParent(Application.dataPath)?.Parent?.Parent?.FullName;
            if (string.IsNullOrWhiteSpace(coding)) return string.Empty;
            string path = Path.Combine(coding, "WorldGen", "Assets");
            if (!Directory.Exists(path)) return string.Empty;
            return Directory.GetFiles(path, "*.glb", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? string.Empty;
        }

        private void OnDisable() => DisposePreview();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("GLB Asset", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Load a GLB to inspect its geometry and materials, then capture a fixed view.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                _glbPath = EditorGUILayout.TextField("GLB", _glbPath);
                if (GUILayout.Button("Browse", GUILayout.Width(72)))
                {
                    string selected = EditorUtility.OpenFilePanel("Choose GLB",
                        Directory.Exists(Path.GetDirectoryName(_glbPath)) ? Path.GetDirectoryName(_glbPath) : Application.dataPath, "glb");
                    if (!string.IsNullOrEmpty(selected)) _glbPath = selected;
                }
            }
            using (new EditorGUI.DisabledScope(_loading))
                if (GUILayout.Button("Load / Rebuild")) _ = LoadGlb();
            EditorGUILayout.LabelField(_status, EditorStyles.wordWrappedLabel);
            _view = (View)GUILayout.Toolbar((int)_view, new[] { "Front", "Side", "Top", "Three-quarter" });
            using (new EditorGUI.DisabledScope(_root == null))
                if (GUILayout.Button("Capture PNG")) Capture();
            Rect rect = GUILayoutUtility.GetRect(256, 4096, 256, 4096,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint && _root != null)
            {
                FrameCamera();
                _preview.BeginPreview(rect, GUIStyle.none);
                _preview.camera.Render();
                GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
            }
        }

        private async System.Threading.Tasks.Task LoadGlb()
        {
            _loading = true;
            try
            {
                if (string.IsNullOrWhiteSpace(_glbPath) || !File.Exists(_glbPath))
                    throw new FileNotFoundException("GLB was not found.", _glbPath);
                DisposePreview();
                _preview = new PreviewRenderUtility();
                _preview.cameraFieldOfView = 35;
                _preview.ambientColor = new Color(.35f, .35f, .35f);
                _preview.lights[0].intensity = 1.25f;
                _preview.lights[1].intensity = .65f;
                _import = new GltfImport();
                if (!await _import.LoadFile(_glbPath)) throw new InvalidDataException("glTFast could not load the GLB.");
                _root = new GameObject("WorldGen GLB") { hideFlags = HideFlags.HideAndDontSave };
                if (!await _import.InstantiateMainSceneAsync(_root.transform))
                    throw new InvalidDataException("glTFast could not instantiate the GLB scene.");
                Renderer[] renderers = _root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidDataException("GLB has no renderers.");
                _bounds = renderers[0].bounds;
                foreach (Renderer renderer in renderers.Skip(1)) _bounds.Encapsulate(renderer.bounds);
                _preview.AddSingleGO(_root);
                _status = $"Loaded {renderers.Length} renderers from {Path.GetFileName(_glbPath)}.";
                Repaint();
            }
            catch (Exception error)
            {
                _status = error.Message;
                DisposePreview();
            }
            finally { _loading = false; Repaint(); }
        }

        private void FrameCamera()
        {
            Vector3 center = _bounds.center;
            float distance = Mathf.Max(1f, _bounds.size.magnitude * 1.8f);
            Vector3 direction = _view switch
            {
                View.Front => Vector3.forward,
                View.Side => Vector3.right,
                View.Top => Vector3.up,
                _ => new Vector3(.8f, .35f, 1f).normalized
            };
            _preview.camera.transform.position = center + direction * distance;
            _preview.camera.transform.LookAt(center, _view == View.Top ? Vector3.forward : Vector3.up);
            _preview.camera.nearClipPlane = .01f;
            _preview.camera.farClipPlane = distance * 4f;
        }

        private void Capture()
        {
            FrameCamera();
            _preview.BeginStaticPreview(new Rect(0, 0, 1024, 1024));
            _preview.camera.Render();
            Texture2D image = _preview.EndStaticPreview();
            if (image == null) { _status = "Unity did not return a capture."; return; }
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(_glbPath), "unity-glb-captures");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, _view.ToString().ToLowerInvariant() + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                _status = "Captured " + path;
            }
            finally { DestroyImmediate(image); }
        }

        private void DisposePreview()
        {
            if (_preview != null) { _preview.Cleanup(); _preview = null; }
            else if (_root != null) DestroyImmediate(_root);
            _root = null;
            _import?.Dispose();
            _import = null;
        }
    }
}
