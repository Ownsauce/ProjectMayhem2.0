using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public class PrototypeStatusWindow : MonoBehaviour
    {
        private PrototypeUiContext _context;
        private Text _statusText;

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _context = context;
            _statusText = PrototypeUiFactory.CreateText("StatusText", transform, "", font, 14, TextAnchor.MiddleLeft);
            _context.StatusChanged += OnStatusChanged;
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.StatusChanged -= OnStatusChanged;
        }

        private void OnStatusChanged(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
        }
    }
}
