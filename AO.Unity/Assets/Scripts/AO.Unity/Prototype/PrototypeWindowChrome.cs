using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public class PrototypeWindowChrome : MonoBehaviour
    {
        [SerializeField] private RectTransform windowRoot;
        [SerializeField] private LayoutElement windowLayout;
        [SerializeField] private RectTransform contentRoot;

        private bool _collapsed;

        public void Initialize(RectTransform root, LayoutElement layout, RectTransform content)
        {
            windowRoot = root;
            windowLayout = layout;
            contentRoot = content;
        }

        public void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            if (contentRoot != null)
                contentRoot.gameObject.SetActive(!_collapsed);
        }
    }
}
