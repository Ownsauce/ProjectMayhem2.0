using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public static class PrototypeUiFactory
    {
        public static RectTransform CreateWindow(Transform parent, string title, Font font, float preferredHeight = -1f)
        {
            var wrap = CreatePanel(title + "Window", parent, new Color(0.1f, 0.12f, 0.16f, 0.97f));
            var le = wrap.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            if (preferredHeight > 0f) le.preferredHeight = preferredHeight; else le.flexibleHeight = 1f;

            var titleBar = CreatePanel("Title", wrap, new Color(0.16f, 0.2f, 0.28f, 1f));
            titleBar.anchorMin = new Vector2(0f, 1f);
            titleBar.anchorMax = new Vector2(1f, 1f);
            titleBar.pivot = new Vector2(0.5f, 1f);
            titleBar.offsetMin = new Vector2(8f, -32f);
            titleBar.offsetMax = new Vector2(-8f, -8f);
            var titleLayout = titleBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            titleLayout.padding = new RectOffset(6, 6, 2, 2);
            titleLayout.spacing = 4;
            titleLayout.childControlHeight = true;
            titleLayout.childControlWidth = true;
            titleLayout.childForceExpandWidth = false;
            titleLayout.childForceExpandHeight = false;

            CreateText("TitleText", titleBar, title, font, 13, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var chrome = titleBar.gameObject.AddComponent<PrototypeWindowChrome>();

            var contentRoot = new GameObject("ContentRoot", typeof(RectTransform));
            contentRoot.transform.SetParent(wrap, false);
            var contentRootRt = (RectTransform)contentRoot.transform;
            contentRootRt.anchorMin = new Vector2(0f, 0f);
            contentRootRt.anchorMax = new Vector2(1f, 1f);
            contentRootRt.offsetMin = new Vector2(8f, 8f);
            contentRootRt.offsetMax = new Vector2(-8f, -36f);

            var contentLayout = contentRootRt.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(0, 0, 0, 0);
            contentLayout.spacing = 6;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            chrome.Initialize(wrap, le, contentRootRt);

            CreateButton("Minimize", titleBar, "_", font, chrome.ToggleCollapsed, 24f);

            return contentRootRt;
        }

        public static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        public static Text CreateText(string name, Transform parent, string value, Font font, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.text = value;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static Button CreateButton(string name, Transform parent, string label, Font font, UnityEngine.Events.UnityAction onClick, float width = 84f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.34f, 1f);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 26f;

            var txt = CreateText("Label", go.transform, label, font, 13, TextAnchor.MiddleCenter);
            var rt = (RectTransform)txt.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return btn;
        }

        public static InputField CreateInputField(string name, Transform parent, string placeholder, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.15f, 0.18f, 0.24f, 1f);
            var input = go.GetComponent<InputField>();
            go.AddComponent<LayoutElement>().preferredHeight = 26f;

            var text = CreateText("Text", go.transform, "", font, 13, TextAnchor.MiddleLeft);
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8, 4);
            textRt.offsetMax = new Vector2(-8, -4);

            var ph = CreateText("Placeholder", go.transform, placeholder, font, 13, TextAnchor.MiddleLeft);
            ph.color = new Color(1f, 1f, 1f, 0.45f);
            var phRt = (RectTransform)ph.transform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(8, 4);
            phRt.offsetMax = new Vector2(-8, -4);

            input.textComponent = text;
            input.placeholder = ph;
            return input;
        }

        public static RectTransform CreateScrollVertical(Transform parent, float preferredHeight = -1f)
        {
            var host = CreatePanel("ScrollHost", parent, new Color(0.08f, 0.09f, 0.11f, 0.95f));
            var le = host.gameObject.AddComponent<LayoutElement>();
            if (preferredHeight > 0f) le.preferredHeight = preferredHeight; else le.flexibleHeight = 1f;

            var scrollRect = host.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.scrollSensitivity = 24f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(host, false);
            var viewportRt = (RectTransform)viewport.transform;
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 4;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            var fit = content.GetComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollRect.viewport = viewportRt;
            scrollRect.content = contentRt;
            return contentRt;
        }

        public static RectTransform CreateGridContent(Transform parent, int padding, Vector2 cellSize, int columns, int rows)
        {
            var host = CreatePanel("GridHost", parent, new Color(0.08f, 0.09f, 0.11f, 0.95f));
            host.gameObject.AddComponent<LayoutElement>().preferredHeight = (cellSize.y * rows) + (padding * 2) + 6;

            var scrollRect = host.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 24f;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(host, false);
            var viewportRt = (RectTransform)viewport.transform;
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var content = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var rt = (RectTransform)content.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var grid = content.GetComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(padding, padding, padding, padding);
            grid.cellSize = cellSize;
            grid.spacing = new Vector2(4, 4);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRt;
            scrollRect.content = rt;
            return rt;
        }

        public static Button CreateSlotButton(Transform parent, Sprite iconSprite, int slotNumber, Font font, out Image iconImage)
        {
            var go = new GameObject($"Slot{slotNumber}", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = iconSprite == null
                ? new Color(0.12f, 0.13f, 0.15f, 1f)
                : new Color(0.18f, 0.2f, 0.24f, 1f);

            var btn = go.GetComponent<Button>();
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(go.transform, false);
            var iconRt = (RectTransform)iconGo.transform;
            iconRt.anchorMin = new Vector2(0.08f, 0.2f);
            iconRt.anchorMax = new Vector2(0.92f, 0.95f);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            var icon = iconGo.GetComponent<Image>();
            icon.color = iconSprite == null ? new Color(0.2f, 0.2f, 0.2f, 1f) : Color.white;
            icon.sprite = iconSprite;
            iconImage = icon;

            var slotLabel = CreateText("SlotNum", go.transform, slotNumber.ToString(), font, 11, TextAnchor.LowerCenter);
            var labelRt = (RectTransform)slotLabel.transform;
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 0.2f);
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            slotLabel.color = new Color(0.85f, 0.9f, 1f, 0.95f);
            return btn;
        }

        public static void ClearChildren(Transform parent)
        {
            foreach (Transform child in parent)
                UnityEngine.Object.Destroy(child.gameObject);
        }
    }
}
