using AO.Unity.Prototype;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class NcuWindowView : MonoBehaviour
    {
        private const float GridCellWidth = 38f;
        private const float GridCellHeight = 38f;
        private const float GridSpacing = 4f;

        private PrototypeUiContext _context;
        private Font _font;
        private Canvas _canvas;
        private RectTransform _listRoot;
        private RectTransform _viewport;
        private RectTransform _gridRoot;
        private GridLayoutGroup _gridLayout;
        private LayoutElement _gridLayoutElement;
        private Text _summaryText;
        private Text _hoverText;
        private float _nextTickAt;
        private readonly Dictionary<int, Text> _remainingByActiveId = new();

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _context = context;
            _font = font;
            _canvas = GetComponentInParent<Canvas>();
            Build();
            _context.StateChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_context != null)
                _context.StateChanged -= Refresh;
            ItemTooltipPresenter.HideActive();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_gridLayout == null)
                return;

            RefreshResponsiveGrid();
            Refresh();
        }

        private void Update()
        {
            if (_context == null)
                return;

            if (Time.unscaledTime < _nextTickAt)
                return;

            _nextTickAt = Time.unscaledTime + 0.2f;
            bool changed = _context.TickActivePrograms();
            if (changed)
                Refresh();
            if (_summaryText != null)
            {
                int used = _context.GetCurrentNcuUsed();
                int max = _context.GetCurrentNcuCapacity();
                _summaryText.text = $"{used}/{max}";
            }
            UpdateRemainingCountdownLabels();
        }

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("NcuRoot", transform, new Color(0.1f, 0.15f, 0.2f, 0.95f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var summary = AOStyleUiFactory.CreatePanel("Summary", root, new Color(0.08f, 0.12f, 0.16f, 0.95f));
            summary.anchorMin = new Vector2(0f, 1f);
            summary.anchorMax = new Vector2(1f, 1f);
            summary.pivot = new Vector2(0.5f, 1f);
            summary.offsetMin = new Vector2(6f, -26f);
            summary.offsetMax = new Vector2(-6f, -4f);
            _summaryText = AOStyleUiFactory.CreateText("SummaryText", summary, string.Empty, _font, 12, TextAnchor.MiddleLeft);
            var summaryRt = (RectTransform)_summaryText.transform;
            summaryRt.anchorMin = Vector2.zero;
            summaryRt.anchorMax = Vector2.one;
            summaryRt.offsetMin = new Vector2(8f, 0f);
            summaryRt.offsetMax = new Vector2(-8f, 0f);

            var hover = AOStyleUiFactory.CreatePanel("HoverInfo", root, new Color(0.07f, 0.1f, 0.14f, 0.95f));
            hover.anchorMin = new Vector2(0f, 1f);
            hover.anchorMax = new Vector2(1f, 1f);
            hover.pivot = new Vector2(0.5f, 1f);
            hover.offsetMin = new Vector2(6f, -48f);
            hover.offsetMax = new Vector2(-6f, -28f);
            _hoverText = AOStyleUiFactory.CreateText("HoverText", hover, "Hover an icon for details. Right-click icon to cancel.", _font, 11, TextAnchor.MiddleLeft);
            var hoverRt = (RectTransform)_hoverText.transform;
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = new Vector2(8f, 0f);
            hoverRt.offsetMax = new Vector2(-8f, 0f);
            _hoverText.color = new Color(0.75f, 0.84f, 0.94f, 0.95f);

            var listHost = AOStyleUiFactory.CreatePanel("ListHost", root, new Color(0f, 0f, 0f, 0f));
            listHost.anchorMin = new Vector2(0f, 0f);
            listHost.anchorMax = new Vector2(1f, 1f);
            listHost.offsetMin = new Vector2(6f, 6f);
            listHost.offsetMax = new Vector2(-6f, -52f);

            _listRoot = AOStyleUiFactory.CreateScrollContent(listHost);
            _viewport = _listRoot?.parent as RectTransform;
            var scrollHostImage = _listRoot?.parent?.parent?.GetComponent<Image>();
            if (scrollHostImage != null)
                scrollHostImage.color = new Color(0.14f, 0.2f, 0.27f, 0.95f);
            _gridRoot = AOStyleUiFactory.CreatePanel("Grid", _listRoot, new Color(0f, 0f, 0f, 0f));
            _gridLayout = _gridRoot.gameObject.AddComponent<GridLayoutGroup>();
            _gridLayout.cellSize = new Vector2(GridCellWidth, GridCellHeight);
            _gridLayout.spacing = new Vector2(GridSpacing, GridSpacing);
            _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _gridLayout.constraintCount = 1;
            _gridLayout.childAlignment = TextAnchor.UpperLeft;
            _gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            _gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            _gridLayout.padding = new RectOffset(2, 2, 2, 2);

            _gridLayoutElement = _gridRoot.gameObject.AddComponent<LayoutElement>();
            _gridLayoutElement.preferredHeight = 46f;
            RefreshResponsiveGrid();
        }

        private void Refresh()
        {
            if (_context == null || _summaryText == null || _gridRoot == null)
                return;

            int used = _context.GetCurrentNcuUsed();
            int max = _context.GetCurrentNcuCapacity();
            _summaryText.text = $"{used}/{max}";

            AOStyleUiFactoryCleanup.Clear(_gridRoot);
            _remainingByActiveId.Clear();
            var active = _context.GetActivePrograms();
            if (active == null || active.Count == 0)
            {
                AOStyleUiFactory.CreateText("Empty", _gridRoot, "No programs running.", _font, 12, TextAnchor.MiddleLeft);
                _gridLayoutElement.preferredHeight = 46f;
                return;
            }

            int created = 0;
            for (int i = 0; i < active.Count; i++)
            {
                var program = active[i];
                if (program == null)
                    continue;

                var slot = AOStyleUiFactory.CreatePanel($"RunningIcon_{program.ActiveId}", _gridRoot, new Color(0.1f, 0.14f, 0.2f, 0.96f));
                var image = slot.GetComponent<Image>();
                if (image != null)
                    image.raycastTarget = true;

                var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObject.transform.SetParent(slot, false);
                var iconRt = (RectTransform)iconObject.transform;
                iconRt.anchorMin = new Vector2(0.08f, 0.08f);
                iconRt.anchorMax = new Vector2(0.92f, 0.92f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;
                var icon = iconObject.GetComponent<Image>();
                icon.sprite = _context.GetIconById(program.IconId);
                icon.color = icon.sprite == null ? new Color(0.24f, 0.24f, 0.24f, 1f) : Color.white;
                icon.raycastTarget = false;

                int remaining = _context.GetRemainingSecondsForActiveProgram(program.ActiveId);
                var remainingText = AOStyleUiFactory.CreateText("Remaining", slot, FormatDuration(remaining), _font, 9, TextAnchor.LowerRight);
                var remainingRt = (RectTransform)remainingText.transform;
                remainingRt.anchorMin = Vector2.zero;
                remainingRt.anchorMax = Vector2.one;
                remainingRt.offsetMin = new Vector2(1f, 1f);
                remainingRt.offsetMax = new Vector2(-1f, -1f);
                remainingText.color = new Color(0.95f, 0.98f, 1f, 0.98f);
                remainingText.fontStyle = FontStyle.Bold;
                remainingText.raycastTarget = false;
                _remainingByActiveId[program.ActiveId] = remainingText;

                var interaction = slot.gameObject.AddComponent<NcuIconInteraction>();
                interaction.ActiveId = program.ActiveId;
                interaction.Context = _context;
                interaction.Canvas = _canvas;
                interaction.Font = _font;
                interaction.OnHoverTextChanged = SetHoverText;
                interaction.RemainingText = FormatDuration(remaining);
                created++;
            }

            RefreshResponsiveGrid();
            int columns = Mathf.Max(1, _gridLayout.constraintCount);
            int rows = Mathf.CeilToInt(created / (float)columns);
            float rowHeight = _gridLayout.cellSize.y + _gridLayout.spacing.y;
            float preferredHeight = _gridLayout.padding.top + _gridLayout.padding.bottom + rows * rowHeight;
            _gridLayoutElement.preferredHeight = Mathf.Max(46f, preferredHeight);
        }

        private void UpdateRemainingCountdownLabels()
        {
            if (_context == null || _remainingByActiveId.Count == 0)
                return;

            foreach (var pair in _remainingByActiveId)
            {
                if (pair.Value == null)
                    continue;
                int remaining = _context.GetRemainingSecondsForActiveProgram(pair.Key);
                pair.Value.text = FormatDuration(remaining);
            }
        }

        private void SetHoverText(string value)
        {
            if (_hoverText == null)
                return;

            _hoverText.text = string.IsNullOrWhiteSpace(value)
                ? "Hover an icon for details. Right-click icon to cancel."
                : value;
        }

        private static string FormatDuration(int seconds)
        {
            if (seconds <= 0)
                return "0s";

            int mins = seconds / 60;
            int rem = seconds % 60;
            return mins > 0 ? $"{mins}:{rem:00}" : $"{rem}s";
        }

        private int ComputeColumnCount()
        {
            if (_viewport == null || _gridLayout == null)
                return 1;

            float usableWidth = Mathf.Max(0f, _viewport.rect.width - _gridLayout.padding.left - _gridLayout.padding.right - 2f);
            float cellPlusSpacing = _gridLayout.cellSize.x + _gridLayout.spacing.x;
            if (cellPlusSpacing <= 0f)
                return 1;

            return Mathf.Max(1, Mathf.FloorToInt((usableWidth + _gridLayout.spacing.x) / cellPlusSpacing));
        }

        private void RefreshResponsiveGrid()
        {
            if (_gridLayout == null)
                return;

            _gridLayout.constraintCount = ComputeColumnCount();
        }

        private sealed class NcuIconInteraction : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public int ActiveId;
            public PrototypeUiContext Context;
            public Canvas Canvas;
            public Font Font;
            public System.Action<string> OnHoverTextChanged;
            public string RemainingText;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
                    Context?.TryCancelActiveProgram(ActiveId);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (Context != null && Canvas != null && Font != null && Context.TryGetActiveProgramTooltip(ActiveId, out var info))
                    ItemTooltipPresenter.GetOrCreate(Canvas, Context, Font).ShowInfo(info);

                OnHoverTextChanged?.Invoke($"Remaining: {RemainingText}");
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                ItemTooltipPresenter.HideActive();
                OnHoverTextChanged?.Invoke(string.Empty);
            }

            private void OnDisable()
            {
                ItemTooltipPresenter.HideActive();
            }
        }
    }
}
