using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using AO.Unity.Prototype;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.AOStyle
{
    public static class ItemDragPayload
    {
        public static long InstanceId;
        public static Sprite Icon;
        public static bool IsDragging;
        public static PrototypeUiContext.SlotZone SourceZone;
        public static int SourceIndex;
        public static bool FromEquipment;
        public static int SourceEquipSlotId;

        public static void Reset()
        {
            InstanceId = 0;
            Icon = null;
            IsDragging = false;
            SourceIndex = -1;
            SourceZone = PrototypeUiContext.SlotZone.Inventory;
            FromEquipment = false;
            SourceEquipSlotId = -1;
        }
    }

    public class InventoryDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public long InstanceId;
        public Image Icon;
        public Canvas Canvas;
        public PrototypeUiContext.SlotZone SourceZone;
        public int SourceIndex;

        public static void ForceEndDragVisual()
        {
            ItemDragPayload.Reset();
            ItemPickupCursorFollower.Hide();
        }

        public static void BeginClickPickup(long instanceId, Sprite icon, Canvas canvas, PrototypeUiContext.SlotZone sourceZone, int sourceIndex)
        {
            if (instanceId == 0 || canvas == null || ItemDragPayload.IsDragging)
                return;

            ItemDragPayload.InstanceId = instanceId;
            ItemDragPayload.Icon = icon;
            ItemDragPayload.IsDragging = true;
            ItemDragPayload.SourceZone = sourceZone;
            ItemDragPayload.SourceIndex = sourceIndex;
            ItemDragPayload.FromEquipment = false;
            ItemDragPayload.SourceEquipSlotId = -1;

            ItemPickupCursorFollower.Show(canvas, icon);
        }

        public static void BeginEquippedClickPickup(long instanceId, Sprite icon, Canvas canvas, int sourceEquipSlotId)
        {
            if (instanceId == 0 || canvas == null || sourceEquipSlotId <= 0 || ItemDragPayload.IsDragging)
                return;

            ItemDragPayload.InstanceId = instanceId;
            ItemDragPayload.Icon = icon;
            ItemDragPayload.IsDragging = true;
            ItemDragPayload.SourceZone = PrototypeUiContext.SlotZone.Inventory;
            ItemDragPayload.SourceIndex = -1;
            ItemDragPayload.FromEquipment = true;
            ItemDragPayload.SourceEquipSlotId = sourceEquipSlotId;

            ItemPickupCursorFollower.Show(canvas, icon);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (InstanceId == 0 || Canvas == null) return;

            ItemDragPayload.InstanceId = InstanceId;
            ItemDragPayload.Icon = Icon != null ? Icon.sprite : null;
            ItemDragPayload.IsDragging = true;
            ItemDragPayload.SourceZone = SourceZone;
            ItemDragPayload.SourceIndex = SourceIndex;
            ItemDragPayload.FromEquipment = false;
            ItemDragPayload.SourceEquipSlotId = -1;
            ItemPickupCursorFollower.Show(Canvas, ItemDragPayload.Icon);
        }

        public void OnDrag(PointerEventData eventData)
        {
            // Cursor follower handles icon position.
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            ForceEndDragVisual();
        }
    }

    public class SlotDropTarget : MonoBehaviour, IDropHandler
    {
        public PrototypeUiContext Context;
        public PrototypeUiContext.SlotZone Zone;
        public int SlotIndex;

        public void OnDrop(PointerEventData eventData)
        {
            if (Context == null) return;
            if (!ItemDragPayload.IsDragging || ItemDragPayload.InstanceId == 0) return;

            bool moved = ItemDragPayload.FromEquipment
                ? Context.TryMoveEquippedItemToZone(ItemDragPayload.SourceEquipSlotId, Zone, SlotIndex, out _)
                : Context.TryMoveItem(
                    ItemDragPayload.SourceZone,
                    ItemDragPayload.SourceIndex,
                    Zone,
                    SlotIndex,
                    out _);

            if (moved)
                InventoryDragSource.ForceEndDragVisual();
        }
    }

    public class SlotPointerClickHandler : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        private const float RightHoldSeconds = 0.35f;

        public PrototypeUiContext Context;
        public PrototypeUiContext.SlotZone Zone;
        public int SlotIndex;
        public long InstanceId;
        public Image Icon;
        public Canvas Canvas;

        private bool _rightHoldArmed;
        private bool _rightMenuShown;
        private Coroutine _rightHoldRoutine;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right)
                return;

            _rightMenuShown = false;
            _rightHoldArmed = true;
            if (_rightHoldRoutine != null)
                StopCoroutine(_rightHoldRoutine);
            _rightHoldRoutine = StartCoroutine(RightHoldCoroutine());
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right)
                return;

            _rightHoldArmed = false;
            if (_rightHoldRoutine != null)
            {
                StopCoroutine(_rightHoldRoutine);
                _rightHoldRoutine = null;
            }

            if (Context == null)
                return;

            if (_rightMenuShown)
            {
                _rightMenuShown = false;
                return;
            }

            // Old right-click behavior: quick right-click performs default action.
            if (ItemDragPayload.IsDragging)
            {
                InventoryDragSource.ForceEndDragVisual();
                return;
            }

            if (Zone == PrototypeUiContext.SlotZone.Inventory)
            {
                Context.SelectInventorySlot(SlotIndex);
                return;
            }

            Context.SelectBackpackSlot(SlotIndex);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (Context == null)
                return;

            if (eventData.button == PointerEventData.InputButton.Right)
                return;

            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            // AO's quick right-click remains the default action. Also support the
            // familiar double-left-click without sacrificing single-click pickup.
            if (eventData.clickCount >= 2)
            {
                InventoryDragSource.ForceEndDragVisual();
                if (Zone == PrototypeUiContext.SlotZone.Inventory)
                    Context.SelectInventorySlot(SlotIndex);
                else
                    Context.SelectBackpackSlot(SlotIndex);
                return;
            }

            if (ItemDragPayload.IsDragging && ItemDragPayload.InstanceId != 0)
            {
                bool moved = ItemDragPayload.FromEquipment
                    ? Context.TryMoveEquippedItemToZone(ItemDragPayload.SourceEquipSlotId, Zone, SlotIndex, out _)
                    : Context.TryMoveItem(
                        ItemDragPayload.SourceZone,
                        ItemDragPayload.SourceIndex,
                        Zone,
                        SlotIndex,
                        out _);

                if (moved)
                    InventoryDragSource.ForceEndDragVisual();

                return;
            }

            if (InstanceId == 0 || Canvas == null)
                return;

            InventoryDragSource.BeginClickPickup(InstanceId, Icon != null ? Icon.sprite : null, Canvas, Zone, SlotIndex);
        }

        private System.Collections.IEnumerator RightHoldCoroutine()
        {
            yield return new WaitForSecondsRealtime(RightHoldSeconds);
            _rightHoldRoutine = null;

            if (!_rightHoldArmed || Context == null || Canvas == null)
                yield break;

            if (ItemDragPayload.IsDragging)
                yield break;

            _rightMenuShown = true;
            ItemSlotContextMenu.Show(
                Canvas,
                ReadPointerScreenPosition(),
                onUse: () =>
                {
                    if (Zone == PrototypeUiContext.SlotZone.Inventory)
                        Context.SelectInventorySlot(SlotIndex);
                    else
                        Context.SelectBackpackSlot(SlotIndex);
                },
                onDelete: () => Context.TryDeleteItemFromZone(Zone, SlotIndex));
        }

        private static Vector2 ReadPointerScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }
    }

    public sealed class ItemPickupCursorFollower : MonoBehaviour
    {
        private static ItemPickupCursorFollower _instance;
        private RectTransform _rt;
        private Image _img;

        public static void Show(Canvas canvas, Sprite sprite)
        {
            if (canvas == null)
                return;

            if (_instance == null)
            {
                var go = new GameObject("ItemPickupCursor", typeof(RectTransform), typeof(Image), typeof(ItemPickupCursorFollower));
                go.transform.SetParent(canvas.transform, false);
                _instance = go.GetComponent<ItemPickupCursorFollower>();
                _instance._rt = (RectTransform)go.transform;
                _instance._img = go.GetComponent<Image>();
                _instance._img.raycastTarget = false;
                _instance._img.color = new Color(1f, 1f, 1f, 0.9f);
                _instance._rt.sizeDelta = new Vector2(36f, 36f);
            }

            if (_instance._rt.parent != canvas.transform)
                _instance._rt.SetParent(canvas.transform, false);

            _instance._img.sprite = sprite;
            _instance.gameObject.SetActive(true);
            _instance.UpdatePosition();
        }

        public static void Hide()
        {
            if (_instance != null)
                _instance.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (WasCancelPressed())
            {
                InventoryDragSource.ForceEndDragVisual();
                return;
            }

            if (!ItemDragPayload.IsDragging || ItemDragPayload.InstanceId == 0)
            {
                Hide();
                return;
            }

            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (_rt == null)
                return;

            _rt.SetAsLastSibling();
            _rt.position = ReadPointerPosition() + new Vector2(14f, -14f);
        }

        private static Vector2 ReadPointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null)
                return mouse.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }

        private static bool WasCancelPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }
    }

    internal sealed class ItemSlotContextMenu : MonoBehaviour
    {
        private static ItemSlotContextMenu _active;
        private const float ScreenPad = 6f;
        private static readonly Vector2 PointerOffset = new Vector2(12f, 12f);

        private Canvas _canvas;
        private RectTransform _root;
        private Button _useButton;
        private Button _deleteButton;
        private Button _cancelButton;
        private Action _onUse;
        private Action _onDelete;

        public static bool IsOpen => _active != null && _active.gameObject.activeSelf;

        public static void Show(Canvas canvas, Vector2 screenPos, Action onUse, Action onDelete)
        {
            if (canvas == null)
                return;

            if (_active == null)
            {
                var go = new GameObject("ItemSlotContextMenu", typeof(RectTransform), typeof(ItemSlotContextMenu));
                _active = go.GetComponent<ItemSlotContextMenu>();
                _active.Initialize(canvas);
            }

            if (_active._canvas != canvas)
            {
                _active._root.SetParent(canvas.transform, false);
                _active._canvas = canvas;
            }

            _active._onUse = onUse;
            _active._onDelete = onDelete;
            ItemTooltipPresenter.HideActive();
            _active.Reposition(screenPos);
            _active.gameObject.SetActive(true);
            _active._root.SetAsLastSibling();
        }

        private void Initialize(Canvas canvas)
        {
            _canvas = canvas;
            _root = GetComponent<RectTransform>();
            _root.SetParent(canvas.transform, false);
            _root.anchorMin = new Vector2(0f, 1f);
            _root.anchorMax = new Vector2(0f, 1f);
            _root.pivot = new Vector2(0f, 1f);
            _root.sizeDelta = new Vector2(120f, 82f);

            var bg = gameObject.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.08f, 0.11f, 0.97f);

            var layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _useButton = AOStyleUiFactory.CreateButton("UseBtn", transform, "Use / Equip", font, OnUse, 108f);
            _deleteButton = AOStyleUiFactory.CreateButton("DeleteBtn", transform, "Delete", font, OnDelete, 108f);
            _cancelButton = AOStyleUiFactory.CreateButton("CancelBtn", transform, "Cancel", font, HideActive, 108f);
            _deleteButton.GetComponent<Image>().color = new Color(0.44f, 0.16f, 0.16f, 0.95f);

            gameObject.SetActive(false);
        }

        private void Reposition(Vector2 screenPos)
        {
            if (_root == null)
                return;

            bool rightHalf = screenPos.x >= (Screen.width * 0.5f);
            bool topHalf = screenPos.y >= (Screen.height * 0.5f);
            Vector2 target;

            if (topHalf && rightHalf)
            {
                _root.pivot = new Vector2(1f, 1f);
                target = screenPos + new Vector2(-PointerOffset.x, -PointerOffset.y);
            }
            else if (topHalf && !rightHalf)
            {
                _root.pivot = new Vector2(0f, 1f);
                target = screenPos + new Vector2(PointerOffset.x, -PointerOffset.y);
            }
            else if (!topHalf && !rightHalf)
            {
                _root.pivot = new Vector2(0f, 0f);
                target = screenPos + new Vector2(PointerOffset.x, PointerOffset.y);
            }
            else
            {
                _root.pivot = new Vector2(1f, 0f);
                target = screenPos + new Vector2(-PointerOffset.x, PointerOffset.y);
            }

            float width = _root.rect.width;
            float height = _root.rect.height;
            float left = target.x - (width * _root.pivot.x);
            float right = target.x + (width * (1f - _root.pivot.x));
            float bottom = target.y - (height * _root.pivot.y);
            float top = target.y + (height * (1f - _root.pivot.y));

            if (left < ScreenPad) target.x += ScreenPad - left;
            if (right > Screen.width - ScreenPad) target.x -= right - (Screen.width - ScreenPad);
            if (bottom < ScreenPad) target.y += ScreenPad - bottom;
            if (top > Screen.height - ScreenPad) target.y -= top - (Screen.height - ScreenPad);

            _root.position = target;
        }

        private void OnUse()
        {
            _onUse?.Invoke();
            HideActive();
        }

        private void OnDelete()
        {
            _onDelete?.Invoke();
            HideActive();
        }

        private static void HideActive()
        {
            if (_active != null)
                _active.gameObject.SetActive(false);
        }
    }
}
