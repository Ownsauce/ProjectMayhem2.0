using System;
using System.Collections.Generic;
using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class ProgramsWindowView : MonoBehaviour
    {
        private static readonly string[] TopTabNames = { "All", "Favorites", "Combat" };
        private static readonly string[] BottomTabNames = { "Medical", "Prot", "Space", "Psi" };
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
        private string _activeTab = "All";
        private readonly Dictionary<string, Button> _tabButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Text> _tabLabels = new(StringComparer.OrdinalIgnoreCase);

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

        private void Build()
        {
            var root = AOStyleUiFactory.CreatePanel("ProgramsRoot", transform, new Color(0.1f, 0.15f, 0.2f, 0.95f));
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var tabs = AOStyleUiFactory.CreatePanel("Tabs", root, new Color(0f, 0f, 0f, 0f));
            tabs.anchorMin = new Vector2(0f, 1f);
            tabs.anchorMax = new Vector2(1f, 1f);
            tabs.pivot = new Vector2(0.5f, 1f);
            tabs.offsetMin = new Vector2(4f, -46f);
            tabs.offsetMax = new Vector2(-4f, -2f);
            var tabsLayout = tabs.gameObject.AddComponent<VerticalLayoutGroup>();
            tabsLayout.spacing = 2f;
            tabsLayout.padding = new RectOffset(0, 0, 0, 0);
            tabsLayout.childControlHeight = true;
            tabsLayout.childControlWidth = true;
            tabsLayout.childForceExpandHeight = true;
            tabsLayout.childForceExpandWidth = true;

            BuildTabRow(tabs, TopTabNames);
            BuildTabRow(tabs, BottomTabNames);

            var summary = AOStyleUiFactory.CreatePanel("Summary", root, new Color(0.08f, 0.12f, 0.16f, 0.95f));
            summary.anchorMin = new Vector2(0f, 1f);
            summary.anchorMax = new Vector2(1f, 1f);
            summary.pivot = new Vector2(0.5f, 1f);
            summary.offsetMin = new Vector2(6f, -70f);
            summary.offsetMax = new Vector2(-6f, -48f);
            _summaryText = AOStyleUiFactory.CreateText("SummaryText", summary, string.Empty, _font, 12, TextAnchor.MiddleLeft);
            var summaryRt = (RectTransform)_summaryText.transform;
            summaryRt.anchorMin = Vector2.zero;
            summaryRt.anchorMax = Vector2.one;
            summaryRt.offsetMin = new Vector2(8f, 0f);
            summaryRt.offsetMax = new Vector2(-8f, 0f);

            var listHost = AOStyleUiFactory.CreatePanel("ListHost", root, new Color(0f, 0f, 0f, 0f));
            listHost.anchorMin = new Vector2(0f, 0f);
            listHost.anchorMax = new Vector2(1f, 1f);
            listHost.offsetMin = new Vector2(6f, 6f);
            listHost.offsetMax = new Vector2(-6f, -72f);

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
            RefreshTabVisuals();
        }

        private void BuildTabRow(RectTransform parent, IReadOnlyList<string> tabNames)
        {
            var row = AOStyleUiFactory.CreatePanel("TabRow", parent, new Color(0f, 0f, 0f, 0f));
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 2f;
            rowLayout.childControlHeight = true;
            rowLayout.childControlWidth = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = true;

            for (int i = 0; i < tabNames.Count; i++)
                CreateTabButton(row, tabNames[i]);
        }

        private void CreateTabButton(RectTransform parent, string tabName)
        {
            var button = AOStyleUiFactory.CreateButton($"Tab_{tabName}", parent, tabName, _font, () =>
            {
                _activeTab = tabName;
                RefreshTabVisuals();
                Refresh();
            }, 64f);

            _tabButtons[tabName] = button;
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
                _tabLabels[tabName] = label;
        }

        private void RefreshTabVisuals()
        {
            foreach (var kvp in _tabButtons)
            {
                bool active = string.Equals(kvp.Key, _activeTab, StringComparison.OrdinalIgnoreCase);
                var button = kvp.Value;
                if (button == null)
                    continue;

                var bg = button.GetComponent<Image>();
                if (bg != null)
                {
                    bg.color = active
                        ? new Color(0.12f, 0.54f, 0.82f, 0.98f)
                        : new Color(0.16f, 0.22f, 0.32f, 0.92f);
                }

                var colors = button.colors;
                colors.normalColor = active
                    ? new Color(0.12f, 0.54f, 0.82f, 0.98f)
                    : new Color(0.16f, 0.22f, 0.32f, 0.92f);
                colors.highlightedColor = active
                    ? new Color(0.16f, 0.62f, 0.92f, 1f)
                    : new Color(0.21f, 0.29f, 0.41f, 0.98f);
                colors.pressedColor = active
                    ? new Color(0.1f, 0.46f, 0.72f, 1f)
                    : new Color(0.14f, 0.2f, 0.29f, 0.98f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.12f, 0.14f, 0.18f, 0.8f);
                button.colors = colors;

                if (_tabLabels.TryGetValue(kvp.Key, out var label) && label != null)
                {
                    label.color = active ? new Color(0.95f, 0.98f, 1f, 1f) : new Color(0.82f, 0.9f, 1f, 0.95f);
                    label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private void Refresh()
        {
            if (_context == null || _gridRoot == null || _summaryText == null)
                return;

            AOStyleUiFactoryCleanup.Clear(_gridRoot);
            var uploaded = _context.GetUploadedPrograms();
            int totalCount = uploaded?.Count ?? 0;
            var filtered = new List<PrototypeUiContext.UploadedNanoProgram>();
            if (uploaded != null)
            {
                for (int i = 0; i < uploaded.Count; i++)
                {
                    var program = uploaded[i];
                    if (program == null)
                        continue;

                    if (string.Equals(_activeTab, "All", StringComparison.OrdinalIgnoreCase)
                        || (!string.Equals(_activeTab, "Favorites", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(_context.GetProgramSchoolTabName(program.NanoId), _activeTab, StringComparison.OrdinalIgnoreCase)))
                    {
                        filtered.Add(program);
                    }
                }
            }

            _summaryText.text = $"Programs: {filtered.Count}/{totalCount}   Target: {ResolveTargetName()}";

            if (filtered.Count == 0)
            {
                AOStyleUiFactory.CreateText("Empty", _gridRoot, "No programs in this tab.", _font, 12, TextAnchor.MiddleLeft);
                _gridLayoutElement.preferredHeight = 46f;
                return;
            }

            for (int i = 0; i < filtered.Count; i++)
            {
                var program = filtered[i];
                var slot = AOStyleUiFactory.CreatePanel($"ProgramIcon_{program.NanoId}_{i}", _gridRoot, new Color(0.1f, 0.14f, 0.2f, 0.96f));
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

                var interaction = slot.gameObject.AddComponent<ProgramIconInteraction>();
                interaction.Context = _context;
                interaction.Canvas = _canvas;
                interaction.Font = _font;
                interaction.NanoId = program.NanoId;
            }

            RefreshResponsiveGrid();
            int columns = Mathf.Max(1, _gridLayout.constraintCount);
            int rows = Mathf.CeilToInt(filtered.Count / (float)columns);
            float rowHeight = _gridLayout.cellSize.y + _gridLayout.spacing.y;
            float preferredHeight = _gridLayout.padding.top + _gridLayout.padding.bottom + rows * rowHeight;
            _gridLayoutElement.preferredHeight = Mathf.Max(46f, preferredHeight);
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

        private string ResolveTargetName()
        {
            var target = _context.SelectedTarget;
            if (target == null)
                return "(none)";

            var character = target.Character;
            if (character != null)
            {
                var prop = character.GetType().GetProperty("Name");
                if (prop != null)
                {
                    string value = prop.GetValue(character) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                        return value.Trim();
                }
            }

            return string.IsNullOrWhiteSpace(target.gameObject?.name) ? "Unknown" : target.gameObject.name;
        }

        private sealed class ProgramIconInteraction : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public PrototypeUiContext Context;
            public Canvas Canvas;
            public Font Font;
            public int NanoId;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
                    return;
                Context?.RequestProgramCast(NanoId);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (Context == null || Canvas == null || Font == null)
                    return;
                if (!Context.TryGetProgramTooltip(NanoId, out var info))
                    return;

                ItemTooltipPresenter.GetOrCreate(Canvas, Context, Font).ShowInfo(info);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                ItemTooltipPresenter.HideActive();
            }

            private void OnDisable()
            {
                ItemTooltipPresenter.HideActive();
            }
        }
    }
}
