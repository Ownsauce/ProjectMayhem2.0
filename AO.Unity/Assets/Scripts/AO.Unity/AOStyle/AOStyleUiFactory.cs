using UnityEngine;
using UnityEngine.UI;
using AO.Unity.Prototype;

namespace AO.Unity.AOStyle
{
    public static class AOStyleUiFactory
    {
        public sealed class WindowRefs
        {
            public RectTransform Root;
            public RectTransform Content;
            public Text Title;
            public Button CloseButton;
            public WindowResizeHandle ResizeHandle;
        }

        public static WindowRefs CreateWindow(Transform parent, Font font, string title, Vector2 anchorMin, Vector2 anchorMax)
        {
            var root = CreatePanel("Window_" + title, parent, new Color(0.07f, 0.09f, 0.13f, 0.92f));
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);

            if (parent is RectTransform parentRt && parentRt.rect.size.sqrMagnitude > 0f)
            {
                Vector2 parentSize = parentRt.rect.size;
                Vector2 minPx = new Vector2(parentSize.x * anchorMin.x, parentSize.y * anchorMin.y);
                Vector2 maxPx = new Vector2(parentSize.x * anchorMax.x, parentSize.y * anchorMax.y);
                Vector2 size = new Vector2(Mathf.Max(220f, maxPx.x - minPx.x), Mathf.Max(180f, maxPx.y - minPx.y));
                Vector2 center = ((minPx + maxPx) * 0.5f) - (parentSize * 0.5f);
                root.sizeDelta = size;
                root.anchoredPosition = center;
            }
            else
            {
                root.sizeDelta = new Vector2(520f, 420f);
                root.anchoredPosition = Vector2.zero;
            }

            var border = root.gameObject.AddComponent<Outline>();
            border.effectColor = new Color(0.15f, 0.7f, 0.95f, 0.5f);
            border.effectDistance = new Vector2(1f, -1f);

            var rootFocus = root.gameObject.AddComponent<WindowFocusOnPointerDown>();
            rootFocus.Target = root;
            var rootClamp = root.gameObject.AddComponent<WindowBoundsClamp>();
            rootClamp.Target = root;

            var header = CreatePanel("Header", root, new Color(0.11f, 0.16f, 0.22f, 0.98f));
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(4f, -28f);
            header.offsetMax = new Vector2(-4f, -4f);

            var headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(6, 6, 2, 2);
            headerLayout.spacing = 4;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;

            var titleText = CreateText("Title", header, title, font, 15, TextAnchor.MiddleLeft);
            var titleLe = titleText.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;

            var drag = header.gameObject.AddComponent<WindowDragHandle>();
            drag.Target = root;

            var closeBtn = CreateButton("Close", root, "X", font, () => root.gameObject.SetActive(false), 24f);
            var closeRt = (RectTransform)closeBtn.transform;
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-6f, -6f);
            closeRt.sizeDelta = new Vector2(24f, 24f);
            var closeLayout = closeBtn.GetComponent<LayoutElement>();
            closeLayout.ignoreLayout = true;
            closeLayout.preferredWidth = 24f;
            closeLayout.preferredHeight = 24f;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root, false);
            var contentRt = (RectTransform)content.transform;
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.offsetMin = new Vector2(6f, 6f);
            contentRt.offsetMax = new Vector2(-6f, -32f);
            var contentFocus = content.gameObject.AddComponent<WindowFocusOnPointerDown>();
            contentFocus.Target = root;

            var resizeHandleGo = new GameObject("ResizeHandle", typeof(RectTransform), typeof(Image), typeof(WindowResizeHandle));
            resizeHandleGo.transform.SetParent(root, false);
            var resizeRt = (RectTransform)resizeHandleGo.transform;
            resizeRt.anchorMin = new Vector2(1f, 0f);
            resizeRt.anchorMax = new Vector2(1f, 0f);
            resizeRt.pivot = new Vector2(1f, 0f);
            resizeRt.sizeDelta = new Vector2(24f, 24f);
            resizeRt.anchoredPosition = new Vector2(-2f, 2f);
            resizeHandleGo.GetComponent<Image>().color = new Color(0.35f, 0.45f, 0.62f, 0.95f);
            var resizeHandle = resizeHandleGo.GetComponent<WindowResizeHandle>();
            resizeHandle.Target = root;

            AddEdgeResizeHandle(root, "ResizeLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), Vector2.zero, WindowResizeHandle.ResizeEdges.Left);
            AddEdgeResizeHandle(root, "ResizeRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(8f, 0f), Vector2.zero, WindowResizeHandle.ResizeEdges.Right);
            AddEdgeResizeHandle(root, "ResizeTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 8f), Vector2.zero, WindowResizeHandle.ResizeEdges.Top);
            AddEdgeResizeHandle(root, "ResizeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), Vector2.zero, WindowResizeHandle.ResizeEdges.Bottom);

            AddEdgeResizeHandle(root, "ResizeTopLeft", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, 16f), new Vector2(0f, 0f), WindowResizeHandle.ResizeEdges.Top | WindowResizeHandle.ResizeEdges.Left);
            AddEdgeResizeHandle(root, "ResizeTopRight", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(16f, 16f), new Vector2(0f, 0f), WindowResizeHandle.ResizeEdges.Top | WindowResizeHandle.ResizeEdges.Right);
            AddEdgeResizeHandle(root, "ResizeBottomLeft", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(0f, 0f), WindowResizeHandle.ResizeEdges.Bottom | WindowResizeHandle.ResizeEdges.Left);
            AddEdgeResizeHandle(root, "ResizeBottomRight", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(16f, 16f), new Vector2(0f, 0f), WindowResizeHandle.ResizeEdges.Bottom | WindowResizeHandle.ResizeEdges.Right);

            return new WindowRefs { Root = root, Content = contentRt, Title = titleText, CloseButton = closeBtn, ResizeHandle = resizeHandle };
        }

        private static void AddEdgeResizeHandle(
            RectTransform root,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 size,
            Vector2 anchoredPosition,
            WindowResizeHandle.ResizeEdges edges)
        {
            var handleGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(WindowResizeHandle));
            handleGo.transform.SetParent(root, false);
            var rt = (RectTransform)handleGo.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPosition;

            var image = handleGo.GetComponent<Image>();
            image.color = new Color(0.6f, 0.78f, 0.95f, 0.01f);
            image.raycastTarget = true;

            var handle = handleGo.GetComponent<WindowResizeHandle>();
            handle.Target = root;
            handle.Edges = edges;
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

        public static Button CreateButton(string name, Transform parent, string label, Font font, UnityEngine.Events.UnityAction onClick, float width = 80f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.2f, 0.28f, 0.4f, 0.95f);
            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 22f;

            var txt = CreateText("Label", go.transform, label, font, 12, TextAnchor.MiddleCenter);
            var rt = (RectTransform)txt.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return btn;
        }

        public static InputField CreateInputField(string name, Transform parent, string placeholder, Font font, float width = 100f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.16f, 0.2f, 0.28f, 1f);
            var input = go.GetComponent<InputField>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 22f;

            var text = CreateText("Text", go.transform, "", font, 12, TextAnchor.MiddleLeft);
            var textRt = (RectTransform)text.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(6, 2);
            textRt.offsetMax = new Vector2(-6, -2);

            var ph = CreateText("Placeholder", go.transform, placeholder, font, 12, TextAnchor.MiddleLeft);
            ph.color = new Color(1f, 1f, 1f, 0.5f);
            var phRt = (RectTransform)ph.transform;
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(6, 2);
            phRt.offsetMax = new Vector2(-6, -2);

            input.textComponent = text;
            input.placeholder = ph;
            return input;
        }

        public static RectTransform CreateScrollContent(Transform parent)
        {
            var host = CreatePanel("ScrollHost", parent, new Color(0.05f, 0.07f, 0.1f, 0.95f));
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = Vector2.zero;
            host.offsetMax = Vector2.zero;

            var scroll = host.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(host, false);
            var vRt = (RectTransform)viewport.transform;
            vRt.anchorMin = Vector2.zero;
            vRt.anchorMax = Vector2.one;
            vRt.offsetMin = Vector2.zero;
            vRt.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var cRt = (RectTransform)content.transform;
            cRt.anchorMin = new Vector2(0, 1);
            cRt.anchorMax = new Vector2(1, 1);
            cRt.pivot = new Vector2(0.5f, 1f);
            cRt.offsetMin = Vector2.zero;
            cRt.offsetMax = Vector2.zero;

            var vlg = content.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.spacing = 4;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandHeight = false;

            var fit = content.GetComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = vRt;
            scroll.content = cRt;
            return cRt;
        }
    }
}
