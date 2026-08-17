using AO.Data.Core;
using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.AOStyle
{
    public class ItemBrowserWindowView : MonoBehaviour
    {
        private PrototypeUiContext _context;
        private Font _font;
        private InputField _searchInput;
        private Dropdown _qlOperator;
        private InputField _qlInput;
        private RectTransform _list;

        public void Initialize(PrototypeUiContext context, Font font)
        {
            _context = context;
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

        private void Build()
        {
            var controls = AOStyleUiFactory.CreatePanel("Controls", transform, new Color(0f, 0f, 0f, 0f));
            controls.anchorMin = new Vector2(0f, 1f);
            controls.anchorMax = new Vector2(1f, 1f);
            controls.pivot = new Vector2(0.5f, 1f);
            controls.offsetMin = new Vector2(0f, -58f);
            controls.offsetMax = new Vector2(0f, 0f);
            var controlsLayout = controls.gameObject.AddComponent<VerticalLayoutGroup>();
            controlsLayout.padding = new RectOffset(0, 0, 0, 0);
            controlsLayout.spacing = 4;
            controlsLayout.childControlHeight = true;
            controlsLayout.childControlWidth = true;
            controlsLayout.childForceExpandHeight = false;

            var row1 = AOStyleUiFactory.CreatePanel("SearchRow", controls, new Color(0f, 0f, 0f, 0f));
            row1.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
            var row1Layout = row1.gameObject.AddComponent<HorizontalLayoutGroup>();
            row1Layout.spacing = 6;
            row1Layout.childControlHeight = true;
            row1Layout.childControlWidth = false;
            row1Layout.childForceExpandWidth = false;
            row1Layout.childForceExpandHeight = false;

            AOStyleUiFactory.CreateText("FindLabel", row1, "Find", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 34f;
            _searchInput = AOStyleUiFactory.CreateInputField("FindInput", row1, "name...", _font, 180f);
            _searchInput.onValueChanged.AddListener(_ => Refresh());
            AOStyleUiFactory.CreateText("QlLabel", row1, "QL", _font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 20f;
            _qlOperator = CreateOperatorDropdown(row1);
            _qlOperator.onValueChanged.AddListener(_ => Refresh());
            _qlInput = AOStyleUiFactory.CreateInputField("QlInput", row1, "value", _font, 56f);
            _qlInput.contentType = InputField.ContentType.IntegerNumber;
            _qlInput.onValueChanged.AddListener(_ => Refresh());

            var listHost = AOStyleUiFactory.CreatePanel("ListHost", transform, new Color(0.05f, 0.07f, 0.1f, 0.95f));
            listHost.anchorMin = new Vector2(0f, 0f);
            listHost.anchorMax = new Vector2(1f, 1f);
            listHost.offsetMin = Vector2.zero;
            listHost.offsetMax = new Vector2(0f, -30f);

            _list = AOStyleUiFactory.CreateScrollContent(listHost);
            AddVerticalScrollbar(listHost, _list);
        }

        private void Refresh()
        {
            if (_context == null || _list == null) return;
            AOStyleUiFactoryCleanup.Clear(_list);

            var token = _searchInput != null ? _searchInput.text : string.Empty;
            string selectedOperator = ResolveSelectedOperator();
            int? ql = TryParseQlFilter();
            foreach (var item in _context.QueryItems(token, selectedOperator, ql))
                BuildItemRow(item);
        }

        private string ResolveSelectedOperator()
        {
            if (_qlOperator == null || _qlOperator.options == null || _qlOperator.options.Count == 0)
                return "=";

            int index = Mathf.Clamp(_qlOperator.value, 0, _qlOperator.options.Count - 1);
            string value = _qlOperator.options[index]?.text;
            return string.IsNullOrWhiteSpace(value) ? "=" : value.Trim();
        }

        private int? TryParseQlFilter()
        {
            if (_qlInput == null || string.IsNullOrWhiteSpace(_qlInput.text))
                return null;

            return int.TryParse(_qlInput.text.Trim(), out int parsed) ? parsed : null;
        }

        private Dropdown CreateOperatorDropdown(Transform parent)
        {
            var go = new GameObject("QlOperator", typeof(RectTransform), typeof(Image), typeof(Dropdown));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.16f, 0.2f, 0.28f, 1f);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 62f;
            le.preferredHeight = 22f;

            var dropdown = go.GetComponent<Dropdown>();
            dropdown.options = new System.Collections.Generic.List<Dropdown.OptionData>
            {
                new Dropdown.OptionData("="),
                new Dropdown.OptionData("<"),
                new Dropdown.OptionData(">"),
                new Dropdown.OptionData("<="),
                new Dropdown.OptionData(">=")
            };

            var label = AOStyleUiFactory.CreateText("Label", go.transform, "=", _font, 12, TextAnchor.MiddleLeft);
            var labelRt = (RectTransform)label.transform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(6f, 2f);
            labelRt.offsetMax = new Vector2(-18f, -2f);
            label.color = new Color(0.96f, 0.98f, 1f, 1f);
            dropdown.captionText = label;

            var template = AOStyleUiFactory.CreatePanel("Template", go.transform, new Color(0.16f, 0.2f, 0.28f, 1f));
            template.gameObject.SetActive(false);
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, -2f);
            template.sizeDelta = new Vector2(0f, 110f);
            var viewport = AOStyleUiFactory.CreatePanel("Viewport", template, new Color(0f, 0f, 0f, 0f));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            var templateScroll = viewport.gameObject.AddComponent<ScrollRect>();
            templateScroll.horizontal = false;
            templateScroll.movementType = ScrollRect.MovementType.Clamped;

            var content = AOStyleUiFactory.CreatePanel("Content", viewport, new Color(0f, 0f, 0f, 0f));
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 0f;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;

            var item = AOStyleUiFactory.CreatePanel("Item", content, new Color(0.12f, 0.17f, 0.24f, 0.98f));
            item.gameObject.AddComponent<Toggle>();
            var itemLabel = AOStyleUiFactory.CreateText("ItemLabel", item, string.Empty, _font, 12, TextAnchor.MiddleLeft);
            var itemLabelRt = (RectTransform)itemLabel.transform;
            itemLabelRt.anchorMin = Vector2.zero;
            itemLabelRt.anchorMax = Vector2.one;
            itemLabelRt.offsetMin = new Vector2(6f, 2f);
            itemLabelRt.offsetMax = new Vector2(-6f, -2f);
            itemLabel.color = new Color(0.96f, 0.98f, 1f, 1f);
            itemLabel.raycastTarget = false;
            item.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            var itemToggle = item.GetComponent<Toggle>();
            itemToggle.targetGraphic = item.GetComponent<Image>();

            dropdown.template = template;
            dropdown.itemText = itemLabel;
            dropdown.captionText.text = "=";
            dropdown.RefreshShownValue();
            return dropdown;
        }

        private static void AddVerticalScrollbar(RectTransform listHost, RectTransform content)
        {
            if (content == null)
                return;

            var viewport = content.parent as RectTransform;
            var scrollRect = content.parent?.parent?.GetComponent<ScrollRect>();
            if (viewport == null || scrollRect == null || listHost == null)
                return;

            viewport.offsetMax = new Vector2(-16f, viewport.offsetMax.y);

            var scrollbarGo = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarGo.transform.SetParent(listHost, false);
            var sbRt = (RectTransform)scrollbarGo.transform;
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 1f);
            sbRt.offsetMin = new Vector2(-14f, 2f);
            sbRt.offsetMax = new Vector2(-2f, -2f);
            scrollbarGo.GetComponent<Image>().color = new Color(0.18f, 0.24f, 0.31f, 0.98f);

            var slidingArea = new GameObject("SlidingArea", typeof(RectTransform));
            slidingArea.transform.SetParent(sbRt, false);
            var slidingRt = (RectTransform)slidingArea.transform;
            slidingRt.anchorMin = Vector2.zero;
            slidingRt.anchorMax = Vector2.one;
            slidingRt.offsetMin = new Vector2(2f, 2f);
            slidingRt.offsetMax = new Vector2(-2f, -2f);

            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(slidingRt, false);
            var handleRt = (RectTransform)handle.transform;
            handleRt.anchorMin = new Vector2(0f, 1f);
            handleRt.anchorMax = new Vector2(1f, 1f);
            handleRt.pivot = new Vector2(0.5f, 1f);
            handleRt.sizeDelta = new Vector2(0f, 28f);
            handle.GetComponent<Image>().color = new Color(0.47f, 0.66f, 0.84f, 0.98f);

            var scrollbar = scrollbarGo.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handleRt;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        }

        private void BuildItemRow(ItemInstance item)
        {
            var row = AOStyleUiFactory.CreatePanel("Row", _list, new Color(0.13f, 0.15f, 0.2f, 1f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(6, 6, 4, 4);
            h.spacing = 6;
            h.childControlHeight = true;
            h.childControlWidth = false;
            h.childForceExpandHeight = false;

            var addBtn = AOStyleUiFactory.CreateButton("AddToInv", row, "+", _font, () => _context.AddToInventory(item), 22f);
            var addLe = addBtn.GetComponent<LayoutElement>();
            if (addLe != null)
                addLe.preferredHeight = 24f;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(row, false);
            var iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 26f;
            iconLe.preferredHeight = 26f;
            var icon = iconGo.GetComponent<Image>();
            icon.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            icon.sprite = _context.GetIconForData(item);
            if (icon.sprite != null)
                icon.color = Color.white;

            AOStyleUiFactory.CreateText("Name", row, item.Definition.Name, _font, 11, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
    }
}
