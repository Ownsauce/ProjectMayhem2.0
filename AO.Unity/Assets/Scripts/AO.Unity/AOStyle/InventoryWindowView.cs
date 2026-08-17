using System.Collections;
using System.Collections.Generic;
using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class InventoryWindowView : MonoBehaviour
    {
        private const float CellSize = 50f;
        private const float CellSpacing = 4f;

        private PrototypeUiContext _context;
        private Canvas _canvas;
        private Font _font;
        private RectTransform _viewport;
        private RectTransform _grid;
        private GridLayoutGroup _gridLayout;
        private bool _refreshQueued;

        public void Initialize(PrototypeUiContext context, Font font, Canvas canvas)
        {
            _context = context;
            _canvas = canvas;
            _font = font;
            Build();
            _context.StateChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.StateChanged -= Refresh;
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_gridLayout != null && gameObject.activeInHierarchy)
                _refreshQueued = true;
        }

        private void LateUpdate()
        {
            if (!_refreshQueued)
                return;

            _refreshQueued = false;
            Refresh();
        }

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("InventoryRoot", transform, new Color(0.05f, 0.07f, 0.1f, 0.95f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var scrollGo = new GameObject("ScrollRect", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(root, false);
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(6f, 6f);
            scrollRt.offsetMax = new Vector2(-22f, -6f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            _viewport = (RectTransform)viewportGo.transform;
            _viewport.anchorMin = Vector2.zero;
            _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = Vector2.zero;
            _viewport.offsetMax = Vector2.zero;
            var viewportImage = viewportGo.GetComponent<Image>();
            viewportImage.color = new Color(0, 0, 0, 0.01f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            _grid = (RectTransform)contentGo.transform;
            _grid.anchorMin = new Vector2(0f, 1f);
            _grid.anchorMax = new Vector2(0f, 1f);
            _grid.pivot = new Vector2(0f, 1f);
            _grid.anchoredPosition = Vector2.zero;
            _grid.sizeDelta = Vector2.zero;

            _gridLayout = contentGo.GetComponent<GridLayoutGroup>();
            _gridLayout.cellSize = new Vector2(CellSize, CellSize);
            _gridLayout.spacing = new Vector2(CellSpacing, CellSpacing);
            _gridLayout.padding = new RectOffset(0, 0, 0, 0);
            _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _gridLayout.constraintCount = 3;
            _gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            _gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;

            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = _viewport;
            scroll.content = _grid;

            var barGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            barGo.transform.SetParent(root, false);
            var barRt = (RectTransform)barGo.transform;
            barRt.anchorMin = new Vector2(1f, 0f);
            barRt.anchorMax = new Vector2(1f, 1f);
            barRt.pivot = new Vector2(1f, 1f);
            barRt.offsetMin = new Vector2(-14f, 6f);
            barRt.offsetMax = new Vector2(-6f, -6f);
            barGo.GetComponent<Image>().color = new Color(0.14f, 0.18f, 0.24f, 1f);

            var handleArea = new GameObject("SlidingArea", typeof(RectTransform));
            handleArea.transform.SetParent(barGo.transform, false);
            var areaRt = (RectTransform)handleArea.transform;
            areaRt.anchorMin = Vector2.zero;
            areaRt.anchorMax = Vector2.one;
            areaRt.offsetMin = new Vector2(0f, 6f);
            areaRt.offsetMax = new Vector2(0f, -6f);

            var handleGo = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleGo.transform.SetParent(handleArea.transform, false);
            var handleRt = (RectTransform)handleGo.transform;
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.one;
            handleRt.offsetMin = Vector2.zero;
            handleRt.offsetMax = Vector2.zero;
            handleGo.GetComponent<Image>().color = new Color(0.35f, 0.45f, 0.62f, 1f);

            var scrollbar = barGo.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handleGo.GetComponent<Image>();
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        }

        private int ComputeColumnCount()
        {
            if (_viewport == null)
                return 3;

            float width = Mathf.Max(0f, _viewport.rect.width - 2f);
            int columns = Mathf.FloorToInt((width + CellSpacing) / (CellSize + CellSpacing));
            return Mathf.Clamp(columns, 3, 10);
        }

        private void Refresh()
        {
            if (_grid == null || _context?.Character == null) return;
            AOStyleUiFactoryCleanup.Clear(_grid);
            _gridLayout.constraintCount = ComputeColumnCount();

            for (int i = 0; i < _context.Character.Inventory.Main.Capacity; i++)
            {
                int index = i;
                var item = _context.Character.Inventory.Main.Slots[index];
                var sprite = _context.GetIconForCore(item);

                var slot = AOStyleUiFactory.CreatePanel("Slot", _grid, new Color(0.13f, 0.16f, 0.2f, 1f));
                var drop = slot.gameObject.AddComponent<SlotDropTarget>();
                drop.Context = _context;
                drop.Zone = PrototypeUiContext.SlotZone.Inventory;
                drop.SlotIndex = index;
                var click = slot.gameObject.AddComponent<SlotPointerClickHandler>();
                click.Context = _context;
                click.Zone = PrototypeUiContext.SlotZone.Inventory;
                click.SlotIndex = index;

                var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObj.transform.SetParent(slot, false);
                var iconRt = (RectTransform)iconObj.transform;
                iconRt.anchorMin = new Vector2(0.08f, 0.08f);
                iconRt.anchorMax = new Vector2(0.92f, 0.92f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;
                var icon = iconObj.GetComponent<Image>();
                icon.sprite = sprite;
                icon.color = sprite == null ? new Color(0.2f, 0.2f, 0.2f, 1f) : Color.white;
                icon.raycastTarget = false;
                click.InstanceId = item?.InstanceId ?? 0;
                click.Icon = icon;
                click.Canvas = _canvas;

                if (item != null)
                {
                    var cooldownObj = new GameObject("CooldownOverlay", typeof(RectTransform), typeof(Image));
                    cooldownObj.transform.SetParent(iconObj.transform, false);
                    var cooldownRt = (RectTransform)cooldownObj.transform;
                    cooldownRt.anchorMin = new Vector2(0.5f, 0f);
                    cooldownRt.anchorMax = new Vector2(0.5f, 1f);
                    cooldownRt.pivot = new Vector2(0.5f, 0.5f);
                    cooldownRt.anchoredPosition = Vector2.zero;
                    cooldownRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, iconRt.rect.width);
                    cooldownRt.offsetMin = new Vector2(cooldownRt.offsetMin.x, 0f);
                    cooldownRt.offsetMax = new Vector2(cooldownRt.offsetMax.x, 0f);
                    var cooldownImage = cooldownObj.GetComponent<Image>();
                    cooldownImage.color = new Color(0f, 0f, 0f, 0.62f);
                    cooldownImage.type = Image.Type.Simple;
                    cooldownImage.raycastTarget = false;
                    cooldownImage.enabled = false;

                    var cooldownTextObj = new GameObject("CooldownText", typeof(RectTransform), typeof(Text));
                    cooldownTextObj.transform.SetParent(iconObj.transform, false);
                    var cooldownTextRt = (RectTransform)cooldownTextObj.transform;
                    cooldownTextRt.anchorMin = Vector2.zero;
                    cooldownTextRt.anchorMax = Vector2.one;
                    cooldownTextRt.offsetMin = Vector2.zero;
                    cooldownTextRt.offsetMax = Vector2.zero;
                    var cooldownText = cooldownTextObj.GetComponent<Text>();
                    cooldownText.font = _font;
                    cooldownText.fontSize = 11;
                    cooldownText.alignment = TextAnchor.MiddleCenter;
                    cooldownText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    cooldownText.verticalOverflow = VerticalWrapMode.Overflow;
                    cooldownText.color = Color.white;
                    cooldownText.raycastTarget = false;
                    cooldownText.enabled = false;

                    var cooldown = slot.gameObject.AddComponent<ItemCooldownOverlay>();
                    cooldown.Context = _context;
                    cooldown.InstanceId = item.InstanceId;
                    cooldown.OverlayRect = cooldownRt;
                    cooldown.OverlayImage = cooldownImage;
                    cooldown.CountdownText = cooldownText;

                    string countBadge = _context.GetItemCountBadgeText(item);
                    if (!string.IsNullOrWhiteSpace(countBadge))
                    {
                        var qty = AOStyleUiFactory.CreateText("Quantity", slot, $"<b>{countBadge}</b>", _font, 11, TextAnchor.UpperLeft);
                        qty.supportRichText = true;
                        qty.color = _context.GetItemCountBadgeColor(item);
                        var qtyRt = (RectTransform)qty.transform;
                        qtyRt.anchorMin = new Vector2(0f, 1f);
                        qtyRt.anchorMax = new Vector2(1f, 1f);
                        qtyRt.pivot = new Vector2(0f, 1f);
                        qtyRt.offsetMin = new Vector2(4f, -16f);
                        qtyRt.offsetMax = new Vector2(-2f, -2f);
                    }

                    var hover = slot.gameObject.AddComponent<ItemHoverTooltip>();
                    hover.Context = _context;
                    hover.Canvas = _canvas;
                    hover.Font = _font;
                    hover.InstanceId = item.InstanceId;

                    var drag = slot.gameObject.AddComponent<InventoryDragSource>();
                    drag.InstanceId = item.InstanceId;
                    drag.Icon = icon;
                    drag.Canvas = _canvas;
                    drag.SourceZone = PrototypeUiContext.SlotZone.Inventory;
                    drag.SourceIndex = index;
                }
            }
        }
    }

    internal static class AOStyleUiFactoryCleanup
    {
        private static readonly HashSet<int> PendingIds = new();
        private static readonly List<Transform> PendingParents = new();
        private static DeferredRunner _runner;

        public static void Clear(Transform parent)
        {
            if (parent == null)
                return;

            if (CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout())
            {
                EnqueueDeferred(parent);
                return;
            }

            ClearImmediate(parent);
        }

        private static void EnqueueDeferred(Transform parent)
        {
            int id = parent.GetInstanceID();
            if (!PendingIds.Add(id))
                return;

            PendingParents.Add(parent);
            EnsureRunner();
        }

        private static void EnsureRunner()
        {
            if (_runner != null)
                return;

            var go = new GameObject("AOStyleUiFactoryCleanupRunner");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<DeferredRunner>();
            _runner.StartCoroutine(_runner.FlushLoop());
        }

        private static void FlushDeferred()
        {
            if (PendingParents.Count == 0)
                return;
            if (CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout())
                return;

            for (int i = PendingParents.Count - 1; i >= 0; i--)
            {
                var parent = PendingParents[i];
                if (parent != null)
                    ClearImmediate(parent);
            }

            PendingParents.Clear();
            PendingIds.Clear();
        }

        private static void ClearImmediate(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                Object.Destroy(child.gameObject);
            }
        }

        private sealed class DeferredRunner : MonoBehaviour
        {
            public IEnumerator FlushLoop()
            {
                while (true)
                {
                    yield return new WaitForEndOfFrame();
                    FlushDeferred();
                }
            }
        }
    }
}
