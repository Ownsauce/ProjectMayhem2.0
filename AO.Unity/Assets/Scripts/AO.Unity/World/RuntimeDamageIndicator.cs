using UnityEngine;

namespace AO.Unity.World
{
    /// <summary>Small pooled-style world damage readout used by both local and authoritative combat.</summary>
    public sealed class RuntimeDamageIndicator : MonoBehaviour
    {
        const float Lifetime = 1.15f;
        TextMesh _text;
        float _start;
        Vector3 _velocity;

        public static void Spawn(Transform target, int amount)
        {
            if (target == null || amount <= 0) return;
            var go = new GameObject("DamageIndicator");
            go.transform.position = ResolveAnchor(target);
            var indicator = go.AddComponent<RuntimeDamageIndicator>();
            indicator._text = go.AddComponent<TextMesh>();
            indicator._text.text = amount.ToString();
            indicator._text.anchor = TextAnchor.MiddleCenter;
            indicator._text.alignment = TextAlignment.Center;
            indicator._text.fontSize = 48;
            indicator._text.characterSize = .035f;
            indicator._text.color = new Color(1f, .28f, .18f, 1f);
            indicator._start = Time.unscaledTime;
            indicator._velocity = new Vector3(Random.Range(-.12f, .12f), .75f, 0f);
        }

        static Vector3 ResolveAnchor(Transform target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return target.position + Vector3.up * 2f;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return new Vector3(bounds.center.x, bounds.max.y + .25f, bounds.center.z);
        }

        void LateUpdate()
        {
            float age = Time.unscaledTime - _start;
            if (age >= Lifetime) { Destroy(gameObject); return; }
            transform.position += _velocity * Time.unscaledDeltaTime;
            Camera camera = Camera.main;
            if (camera != null) transform.rotation = camera.transform.rotation;
            if (_text != null)
            {
                Color color = _text.color;
                color.a = 1f - Mathf.Clamp01(age / Lifetime);
                _text.color = color;
            }
        }
    }
}
