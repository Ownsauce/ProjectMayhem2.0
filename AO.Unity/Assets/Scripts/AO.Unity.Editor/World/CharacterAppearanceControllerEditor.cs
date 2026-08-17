using System.Collections.Generic;
using System.Linq;
using AO.Unity.World;
using UnityEditor;
using UnityEngine;

namespace AO.Unity.Editor.World
{
    [CustomEditor(typeof(CharacterAppearanceController))]
    public sealed class CharacterAppearanceControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(
                serializedObject,
                "idleClipOverride",
                "moveStartClipOverride",
                "moveClipOverride",
                "attackClipOverride",
                "availableAnimationClipNames");

            var controller = (CharacterAppearanceController)target;
            bool changed = false;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Animation Clip Overrides", EditorStyles.boldLabel);

            if (GUILayout.Button("Refresh Available Clips"))
            {
                foreach (var t in targets.OfType<CharacterAppearanceController>())
                {
                    t.RefreshAnimationClipCatalogForInspector();
                    EditorUtility.SetDirty(t);
                }
            }

            var options = controller.GetAvailableAnimationClipNames()?.ToList() ?? new List<string>();
            changed |= DrawClipPopup("Idle Clip Override", "idleClipOverride", options);
            changed |= DrawClipPopup("Move Start Clip Override", "moveStartClipOverride", options);
            changed |= DrawClipPopup("Move Clip Override", "moveClipOverride", options);
            changed |= DrawClipPopup("Attack Clip Override", "attackClipOverride", options);

            if (Application.isPlaying && GUILayout.Button("Apply Overrides Now"))
            {
                foreach (var t in targets.OfType<CharacterAppearanceController>())
                    t.ApplyAnimationOverridesNow();
            }

            if (options.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No clips discovered yet. Enter Play Mode and click 'Refresh Available Clips' after the character mesh is loaded.",
                    MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();

            if (changed && Application.isPlaying)
            {
                foreach (var t in targets.OfType<CharacterAppearanceController>())
                    t.ApplyAnimationOverridesNow();
            }
        }

        private bool DrawClipPopup(string label, string propertyName, List<string> options)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
                return false;

            var popup = new List<string> { "<Auto>" };
            popup.AddRange(options);

            int index = 0;
            if (!string.IsNullOrWhiteSpace(property.stringValue))
            {
                int found = options.FindIndex(v => string.Equals(v, property.stringValue, System.StringComparison.OrdinalIgnoreCase));
                if (found >= 0)
                    index = found + 1;
            }

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(label, index, popup.ToArray());
            bool changed = EditorGUI.EndChangeCheck();
            property.stringValue = next <= 0 ? string.Empty : popup[next];
            return changed;
        }
    }
}
