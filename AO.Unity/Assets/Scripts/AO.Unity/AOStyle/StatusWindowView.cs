using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class StatusWindowView : MonoBehaviour
    {
        private PrototypeUiContext _context;
        private InputField _statusField;

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _context = context;
            _statusField = AOStyleUiFactory.CreateInputField("Status", transform, "Status...", font, 0f);
            _statusField.readOnly = true;
            _statusField.lineType = InputField.LineType.MultiLineNewline;
            _statusField.textComponent.alignment = TextAnchor.MiddleLeft;
            _statusField.textComponent.color = Color.white;
            _statusField.placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            var rt = (RectTransform)_statusField.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 2f);
            rt.offsetMax = new Vector2(-6f, -2f);
            _context.StatusChanged += OnStatusChanged;
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.StatusChanged -= OnStatusChanged;
        }

        private void OnStatusChanged(string value)
        {
            if (_statusField != null)
                _statusField.text = value ?? string.Empty;
        }
    }
}
