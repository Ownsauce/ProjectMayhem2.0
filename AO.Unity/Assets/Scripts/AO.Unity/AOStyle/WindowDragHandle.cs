using UnityEngine;
using UnityEngine.EventSystems;
using System;

namespace AO.Unity.AOStyle
{
    public class WindowFocusOnPointerDown : MonoBehaviour, IPointerDownHandler
    {
        public RectTransform Target;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Target == null)
                return;

            var dockState = Target.GetComponent<WindowDockState>();
            if (dockState != null && dockState.IsDocked)
                return;

            if (Target != null)
                Target.SetAsLastSibling();
        }
    }

    public class WindowDragHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public static Action<RectTransform> WindowTransformCommitted;

        public RectTransform Target;
        private RectTransform _parent;
        private Vector2 _dragOffset;
        private bool _isLeftDrag;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Target == null)
                return;

            var dockState = Target.GetComponent<WindowDockState>();
            if (dockState != null && dockState.IsDocked)
                return;

            if (Target != null)
                Target.SetAsLastSibling();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _isLeftDrag = eventData != null && eventData.button == PointerEventData.InputButton.Left;
            if (!_isLeftDrag)
                return;

            if (Target == null)
                return;

            var dockState = Target.GetComponent<WindowDockState>();
            if (dockState != null && dockState.IsDocked)
                return;

            Target.SetAsLastSibling();
            _parent = Target.parent as RectTransform;
            if (_parent == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parent,
                eventData.position,
                eventData.pressEventCamera,
                out var localPointer);

            _dragOffset = Target.anchoredPosition - localPointer;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isLeftDrag || Target == null || _parent == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parent,
                eventData.position,
                eventData.pressEventCamera,
                out var localPointer);

            Target.anchoredPosition = localPointer + _dragOffset;
            ClampToParentBounds(Target, _parent);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_isLeftDrag || Target == null)
                return;

            _isLeftDrag = false;
            if (_parent != null)
                ClampToParentBounds(Target, _parent);
            WindowTransformCommitted?.Invoke(Target);
        }

        internal static void ClampToParentBounds(RectTransform target, RectTransform parent)
        {
            if (target == null || parent == null)
                return;

            Vector2 parentSize = parent.rect.size;
            float width = target.rect.width;
            float height = target.rect.height;
            if (width <= 0f || height <= 0f || parentSize.x <= 0f || parentSize.y <= 0f)
                return;

            // Keep size within parent first so position clamping is meaningful.
            if (target.sizeDelta.x > parentSize.x || target.sizeDelta.y > parentSize.y)
            {
                target.sizeDelta = new Vector2(
                    Mathf.Min(target.sizeDelta.x, parentSize.x),
                    Mathf.Min(target.sizeDelta.y, parentSize.y));
                width = target.rect.width;
                height = target.rect.height;
            }

            float minX = -parentSize.x * 0.5f + width * target.pivot.x;
            float maxX = parentSize.x * 0.5f - width * (1f - target.pivot.x);
            float minY = -parentSize.y * 0.5f + height * target.pivot.y;
            float maxY = parentSize.y * 0.5f - height * (1f - target.pivot.y);

            Vector2 anchored = target.anchoredPosition;
            anchored.x = Mathf.Clamp(anchored.x, minX, maxX);
            anchored.y = Mathf.Clamp(anchored.y, minY, maxY);
            target.anchoredPosition = anchored;
        }
    }

    public class WindowResizeHandle : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [System.Flags]
        public enum ResizeEdges
        {
            None = 0,
            Left = 1 << 0,
            Right = 1 << 1,
            Top = 1 << 2,
            Bottom = 1 << 3
        }

        public RectTransform Target;
        public Vector2 MinSize = new Vector2(210f, 210f);
        public Vector2 MaxSize = Vector2.zero;
        public ResizeEdges Edges = ResizeEdges.Right | ResizeEdges.Bottom;
        private RectTransform _parent;
        private Vector2 _startSize;
        private Vector2 _startPointerLocal;
        private Vector2 _startAnchoredPosition;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Target != null)
                Target.SetAsLastSibling();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Target == null)
                return;

            _parent = Target.parent as RectTransform;
            _startSize = Target.sizeDelta;
            _startAnchoredPosition = Target.anchoredPosition;
            if (_parent == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parent,
                eventData.position,
                eventData.pressEventCamera,
                out _startPointerLocal);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Target == null || _parent == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parent,
                eventData.position,
                eventData.pressEventCamera,
                out var localPointer);

            var delta = localPointer - _startPointerLocal;
            float requestedWidth = _startSize.x;
            float requestedHeight = _startSize.y;

            if ((Edges & ResizeEdges.Right) != 0)
                requestedWidth = _startSize.x + delta.x;
            else if ((Edges & ResizeEdges.Left) != 0)
                requestedWidth = _startSize.x - delta.x;

            if ((Edges & ResizeEdges.Top) != 0)
                requestedHeight = _startSize.y + delta.y;
            else if ((Edges & ResizeEdges.Bottom) != 0)
                requestedHeight = _startSize.y - delta.y;

            var size = new Vector2(requestedWidth, requestedHeight);
            size.x = Mathf.Max(MinSize.x, size.x);
            size.y = Mathf.Max(MinSize.y, size.y);
            if (MaxSize.x > 0f)
                size.x = Mathf.Min(size.x, MaxSize.x);
            if (MaxSize.y > 0f)
                size.y = Mathf.Min(size.y, MaxSize.y);

            float actualDeltaW = size.x - _startSize.x;
            float actualDeltaH = size.y - _startSize.y;
            var anchored = _startAnchoredPosition;
            float px = Target.pivot.x;
            float py = Target.pivot.y;

            if ((Edges & ResizeEdges.Right) != 0)
                anchored.x = _startAnchoredPosition.x + (actualDeltaW * px);
            else if ((Edges & ResizeEdges.Left) != 0)
                anchored.x = _startAnchoredPosition.x - (actualDeltaW * (1f - px));

            if ((Edges & ResizeEdges.Top) != 0)
                anchored.y = _startAnchoredPosition.y + (actualDeltaH * py);
            else if ((Edges & ResizeEdges.Bottom) != 0)
                anchored.y = _startAnchoredPosition.y - (actualDeltaH * (1f - py));

            Target.sizeDelta = size;
            Target.anchoredPosition = anchored;
            WindowDragHandle.ClampToParentBounds(Target, _parent);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (Target == null)
                return;
            if (_parent != null)
                WindowDragHandle.ClampToParentBounds(Target, _parent);
            WindowDragHandle.WindowTransformCommitted?.Invoke(Target);
        }
    }

    public class WindowBoundsClamp : MonoBehaviour
    {
        public RectTransform Target;

        private void LateUpdate()
        {
            if (Target == null)
                return;
            var parent = Target.parent as RectTransform;
            WindowDragHandle.ClampToParentBounds(Target, parent);
        }
    }
}
