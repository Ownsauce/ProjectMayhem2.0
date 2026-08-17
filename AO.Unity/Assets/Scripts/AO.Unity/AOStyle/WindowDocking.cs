using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.AOStyle
{
    public sealed class DockPaneController : MonoBehaviour
    {
        public RectTransform DockHost;
        public RectTransform DockContent;
        public RectTransform FloatingParent;
        public ScrollRect DockScrollRect;
        public Image DockHintImage;
        public float HiddenHintAlpha = 0.001f;
        public float ActiveHintAlpha = 0.14f;

        public bool ContainsScreenPoint(Vector2 screenPoint, Camera eventCamera)
        {
            return DockHost != null && RectTransformUtility.RectangleContainsScreenPoint(DockHost, screenPoint, eventCamera);
        }

        public void DockWindow(RectTransform window, Vector2 screenPoint, Camera eventCamera)
        {
            if (window == null || DockContent == null)
                return;

            var state = EnsureState(window);
            state.CaptureFloatingState(window.parent as RectTransform);
            int siblingIndex = ResolveDockInsertIndex(window, screenPoint, eventCamera);
            bool alreadyDocked = window.parent == DockContent;

            if (!alreadyDocked)
                window.SetParent(DockContent, false);

            state.SetDocked(true);

            int maxIndex = Mathf.Max(0, DockContent.childCount - 1);
            if (alreadyDocked)
            {
                int currentIndex = window.GetSiblingIndex();
                if (currentIndex < siblingIndex)
                    siblingIndex--;
            }

            window.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, maxIndex));
            window.gameObject.SetActive(true);
            window.localScale = Vector3.one;
            window.localRotation = Quaternion.identity;
            window.anchoredPosition = Vector2.zero;
            ShowDockedWindows();
            WindowDragHandle.WindowTransformCommitted?.Invoke(window);
        }

        public void SetHintVisible(bool visible)
        {
            if (DockHintImage == null)
                return;

            var color = DockHintImage.color;
            color.a = visible ? ActiveHintAlpha : HiddenHintAlpha;
            DockHintImage.color = color;
        }

        private void ShowDockedWindows()
        {
            if (DockScrollRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            DockScrollRect.verticalNormalizedPosition = 1f;
        }

        public void UndockWindow(RectTransform window, Vector2 screenPoint, Camera eventCamera)
        {
            if (window == null || FloatingParent == null)
                return;

            var state = EnsureState(window);
            state.CaptureFloatingState(FloatingParent);

            window.SetParent(FloatingParent, false);
            state.SetDocked(false);

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(FloatingParent, screenPoint, eventCamera, out var local))
                window.anchoredPosition = local;

            WindowDragHandle.ClampToParentBounds(window, FloatingParent);
            window.SetAsLastSibling();
            if (window.gameObject != null && !window.gameObject.activeSelf)
                window.gameObject.SetActive(true);
            WindowDragHandle.WindowTransformCommitted?.Invoke(window);
        }

        private int ResolveDockInsertIndex(RectTransform movingWindow, Vector2 screenPoint, Camera eventCamera)
        {
            if (DockContent == null)
                return 0;

            int fallback = DockContent.childCount;
            for (int i = 0; i < DockContent.childCount; i++)
            {
                var child = DockContent.GetChild(i) as RectTransform;
                if (child == null || child == movingWindow)
                    continue;

                if (!TryGetScreenRect(child, eventCamera, out var rect))
                    continue;
                if (!rect.Contains(screenPoint))
                    continue;

                float split = rect.yMin + (rect.height * 0.5f);
                return screenPoint.y >= split ? i : i + 1;
            }

            return fallback;
        }

        private static bool TryGetScreenRect(RectTransform rectTransform, Camera eventCamera, out Rect rect)
        {
            rect = default;
            if (rectTransform == null)
                return false;

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[2]);
            float xMin = Mathf.Min(bl.x, tr.x);
            float xMax = Mathf.Max(bl.x, tr.x);
            float yMin = Mathf.Min(bl.y, tr.y);
            float yMax = Mathf.Max(bl.y, tr.y);
            rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            return rect.width > 0f && rect.height > 0f;
        }

        private WindowDockState EnsureState(RectTransform window)
        {
            var state = window.GetComponent<WindowDockState>();
            if (state == null)
                state = window.gameObject.AddComponent<WindowDockState>();
            if (state.FloatingParent == null)
                state.FloatingParent = FloatingParent;
            return state;
        }
    }

    public sealed class WindowDockState : MonoBehaviour
    {
        public RectTransform FloatingParent;

        private RectTransform _rect;
        private bool _initialized;
        private bool _wasDocked;
        private Vector2 _savedAnchorMin;
        private Vector2 _savedAnchorMax;
        private Vector2 _savedPivot;
        private Vector2 _savedSizeDelta;
        private Vector2 _savedAnchoredPosition;
        private LayoutElement _layoutElement;
        private WindowDragHandle[] _dragHandles;
        private WindowResizeHandle[] _resizeHandles;
        private WindowBoundsClamp _boundsClamp;

        public void CaptureFloatingState(RectTransform fallbackParent)
        {
            EnsureInitialized();
            if (_rect == null)
                return;

            if (!_wasDocked)
            {
                FloatingParent = _rect.parent as RectTransform ?? fallbackParent;
                _savedAnchorMin = _rect.anchorMin;
                _savedAnchorMax = _rect.anchorMax;
                _savedPivot = _rect.pivot;
                _savedSizeDelta = _rect.sizeDelta;
                _savedAnchoredPosition = _rect.anchoredPosition;
            }
        }

        public void SetDocked(bool docked)
        {
            EnsureInitialized();
            if (_rect == null)
                return;

            if (docked)
            {
                _wasDocked = true;
                _layoutElement.ignoreLayout = false;
                float dockWidth = Mathf.Max(140f, _savedSizeDelta.x <= 0f ? _rect.rect.width : _savedSizeDelta.x);
                _layoutElement.flexibleWidth = 0f;
                _layoutElement.minWidth = dockWidth;
                _layoutElement.preferredWidth = dockWidth;
                _layoutElement.minHeight = Mathf.Max(120f, _savedSizeDelta.y <= 0f ? _rect.rect.height : _savedSizeDelta.y);
                _layoutElement.preferredHeight = _layoutElement.minHeight;

                _rect.anchorMin = new Vector2(0f, 1f);
                _rect.anchorMax = new Vector2(0f, 1f);
                _rect.pivot = new Vector2(0f, 1f);
                _rect.sizeDelta = new Vector2(_layoutElement.preferredWidth, _layoutElement.preferredHeight);
                _rect.anchoredPosition = Vector2.zero;

                SetHandlesEnabled(false);
                if (_boundsClamp != null)
                    _boundsClamp.enabled = false;
                return;
            }

            _wasDocked = false;
            _layoutElement.ignoreLayout = true;
            _rect.anchorMin = _savedAnchorMin;
            _rect.anchorMax = _savedAnchorMax;
            _rect.pivot = _savedPivot;
            _rect.sizeDelta = _savedSizeDelta;
            _rect.anchoredPosition = _savedAnchoredPosition;

            SetHandlesEnabled(true);
            if (_boundsClamp != null)
                _boundsClamp.enabled = true;
        }

        public bool IsDocked => _wasDocked;

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            _rect = transform as RectTransform;
            if (_rect == null)
                return;

            _savedAnchorMin = _rect.anchorMin;
            _savedAnchorMax = _rect.anchorMax;
            _savedPivot = _rect.pivot;
            _savedSizeDelta = _rect.sizeDelta;
            _savedAnchoredPosition = _rect.anchoredPosition;

            _layoutElement = GetComponent<LayoutElement>();
            if (_layoutElement == null)
                _layoutElement = gameObject.AddComponent<LayoutElement>();
            _layoutElement.ignoreLayout = true;

            _dragHandles = GetComponentsInChildren<WindowDragHandle>(true);
            _resizeHandles = GetComponentsInChildren<WindowResizeHandle>(true);
            _boundsClamp = GetComponent<WindowBoundsClamp>();
        }

        private void SetHandlesEnabled(bool enabled)
        {
            if (_dragHandles != null)
            {
                for (int i = 0; i < _dragHandles.Length; i++)
                {
                    if (_dragHandles[i] != null)
                        _dragHandles[i].enabled = enabled;
                }
            }

            if (_resizeHandles != null)
            {
                for (int i = 0; i < _resizeHandles.Length; i++)
                {
                    if (_resizeHandles[i] != null)
                        _resizeHandles[i].enabled = enabled;
                }
            }
        }
    }

    public sealed class WindowDockHandle : MonoBehaviour, IPointerClickHandler
    {
        public RectTransform Target;
        public DockPaneController DockController;

        private RectTransform _floatingParent;
        private Vector2 _grabOffset;
        private bool _isPickedUp;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right || Target == null || DockController == null)
                return;

            BeginPickup(eventData.position, eventData.pressEventCamera);
        }

        private void Update()
        {
            if (!_isPickedUp || Target == null || DockController == null)
                return;

            Vector2 mouse = GetMouseScreenPosition();
            UpdateFloatingPosition(mouse, null);

            if (WasPrimaryClickPressed())
                Drop(mouse, null);
        }

        private void BeginPickup(Vector2 screenPoint, Camera eventCamera)
        {
            var state = Target.GetComponent<WindowDockState>();
            if (state != null && state.IsDocked)
                DockController.UndockWindow(Target, screenPoint, eventCamera);

            _floatingParent = Target.parent as RectTransform;
            if (_floatingParent == null)
                return;

            Target.SetAsLastSibling();
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_floatingParent, screenPoint, eventCamera, out var localPointer))
                _grabOffset = Target.anchoredPosition - localPointer;
            else
                _grabOffset = Vector2.zero;

            _isPickedUp = true;
            DockController.SetHintVisible(true);
        }

        private void UpdateFloatingPosition(Vector2 screenPoint, Camera eventCamera)
        {
            if (_floatingParent == null || Target == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_floatingParent, screenPoint, eventCamera, out var localPointer))
            {
                Target.anchoredPosition = localPointer + _grabOffset;
                WindowDragHandle.ClampToParentBounds(Target, _floatingParent);
            }
        }

        private void Drop(Vector2 screenPoint, Camera eventCamera)
        {
            if (Target == null || DockController == null)
                return;

            if (DockController.ContainsScreenPoint(screenPoint, eventCamera))
                DockController.DockWindow(Target, screenPoint, eventCamera);
            else
                DockController.UndockWindow(Target, screenPoint, eventCamera);

            _isPickedUp = false;
            _floatingParent = null;
            DockController.SetHintVisible(false);
            WindowDragHandle.WindowTransformCommitted?.Invoke(Target);
        }

        private void OnDisable()
        {
            _isPickedUp = false;
            _floatingParent = null;
            if (DockController != null)
                DockController.SetHintVisible(false);
        }

        private static Vector2 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        private static bool WasPrimaryClickPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}
