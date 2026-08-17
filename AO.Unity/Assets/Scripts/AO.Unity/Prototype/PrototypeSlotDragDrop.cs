using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public class PrototypeSlotDragDrop : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public PrototypeUiContext Context;
        public PrototypeUiContext.SlotZone Zone;
        public int SlotIndex;
        public Canvas RootCanvas;
        public Image SourceIcon;

        private static PrototypeSlotDragDrop _dragSource;
        private static GameObject _dragGhost;
        private static RectTransform _dragGhostRect;
        private static Image _dragGhostImage;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Context == null || RootCanvas == null) return;

            var item = Context.GetSlotItem(Zone, SlotIndex);
            if (item == null) return;

            _dragSource = this;
            CreateGhost();
            UpdateGhostPosition(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateGhostPosition(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            CleanupGhost();
            _dragSource = null;
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (_dragSource == null || Context == null) return;
            if (_dragSource == this) return;

            if (!Context.TryMoveItem(_dragSource.Zone, _dragSource.SlotIndex, Zone, SlotIndex, out var reason))
                Debug.Log($"[DragDropBlocked] {reason}");
        }

        private void CreateGhost()
        {
            if (_dragGhost != null) CleanupGhost();

            _dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            _dragGhost.transform.SetParent(RootCanvas.transform, false);
            _dragGhostRect = (RectTransform)_dragGhost.transform;
            _dragGhostRect.sizeDelta = new Vector2(42f, 42f);
            _dragGhostImage = _dragGhost.GetComponent<Image>();
            _dragGhostImage.raycastTarget = false;
            _dragGhostImage.color = new Color(1f, 1f, 1f, 0.85f);
            _dragGhostImage.sprite = SourceIcon != null ? SourceIcon.sprite : null;
        }

        private static void UpdateGhostPosition(PointerEventData eventData)
        {
            if (_dragGhostRect == null) return;
            _dragGhostRect.position = eventData.position;
        }

        private static void CleanupGhost()
        {
            if (_dragGhost != null)
                Object.Destroy(_dragGhost);
            _dragGhost = null;
            _dragGhostRect = null;
            _dragGhostImage = null;
        }
    }
}
