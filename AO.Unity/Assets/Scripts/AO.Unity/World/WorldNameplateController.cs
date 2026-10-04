using UnityEngine;

namespace AO.Unity.World
{
    /// <summary>Distance-, view-, and occlusion-aware camera-facing world nameplate.</summary>
    public sealed class WorldNameplateController : MonoBehaviour
    {
        private TextMesh _label;
        private Transform _owner;
        private Transform _viewer;
        private int _level;
        private int _viewerLevel = 1;
        private float _nextVisibilityCheck;
        private const float MaxDistance = 20f;

        public void Configure(TextMesh label, Transform owner, Transform viewer,
            string displayName, int level, int viewerLevel, int health, int healthDamage)
        {
            _label = label;
            _owner = owner;
            _viewer = viewer;
            _level = Mathf.Max(0, level);
            _viewerLevel = Mathf.Max(1, viewerLevel);
            if (_label != null)
            {
                int currentHealth = Mathf.Max(0, health - healthDamage);
                string hp = health > 0 ? $"\nHP {currentHealth}/{health}" : string.Empty;
                _label.richText = true;
                _label.text = $"{displayName} <color=#{LevelColor(_level - _viewerLevel)}>Lv {_level}</color>{hp}";
            }
            _nextVisibilityCheck = Time.unscaledTime
                + (((owner != null ? owner.name.GetHashCode() : 0) & 0x7fffffff) % 15) * 0.01f;
        }

        private void LateUpdate()
        {
            if (_label == null) return;
            Camera camera = Camera.main;
            if (camera == null) { SetVisible(false); return; }

            // Matching the camera rotation keeps glyphs left-to-right from every angle.
            transform.rotation = camera.transform.rotation;
            if (Time.unscaledTime < _nextVisibilityCheck) return;
            _nextVisibilityCheck = Time.unscaledTime + 0.15f;

            Vector3 target = transform.position;
            Vector3 delta = target - camera.transform.position;
            float distance = delta.magnitude;
            if (distance > MaxDistance || distance < 0.01f)
            {
                SetVisible(false);
                return;
            }
            Vector3 viewport = camera.WorldToViewportPoint(target);
            if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f
                || viewport.y < 0f || viewport.y > 1f)
            {
                SetVisible(false);
                return;
            }
            bool visible = true;
            if (Physics.Raycast(camera.transform.position, delta / distance,
                out RaycastHit hit, Mathf.Max(0f, distance - 0.05f),
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                visible = _owner != null && (hit.transform == _owner
                    || hit.transform.IsChildOf(_owner));
            }
            SetVisible(visible);
        }

        private void SetVisible(bool visible)
        {
            Renderer renderer = _label != null ? _label.GetComponent<Renderer>() : null;
            if (renderer != null && renderer.enabled != visible)
                renderer.enabled = visible;
        }

        private static string LevelColor(int difference)
        {
            if (difference <= -10) return "9A9A9A"; // trivial: gray
            if (difference < 0) return "55D86A";    // below player: green
            if (difference <= 5) return "FFAA33";   // even/slightly above: orange
            return "F04A4A";                        // dangerous: red
        }
    }
}
