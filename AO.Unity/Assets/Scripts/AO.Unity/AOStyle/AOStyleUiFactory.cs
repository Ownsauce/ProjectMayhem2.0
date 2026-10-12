using UnityEngine;
using UnityEngine.UI;
using AO.Unity.Prototype;

namespace AO.Unity.AOStyle
{
    public static class AOStyleUiFactory
    {
        public static readonly Color Surface = new Color(0.18f, 0.29f, 0.40f, 0.98f);
        public static readonly Color RaisedSurface = new Color(0.25f, 0.39f, 0.52f, 1f);
        public static readonly Color TextColor = new Color(0.89f, 0.94f, 0.96f, 1f);
        public static readonly Color MutedText = new Color(0.75f, 0.86f, 0.94f, 1f);
        public static readonly Color Accent = new Color(0.22f, 0.70f, 0.73f, 1f);
        private static Sprite _surfaceSprite;

        // A single nine-sliced surface keeps the radius constant as windows resize.
        private static Sprite SurfaceSprite
        {
            get
            {
                if (_surfaceSprite != null) return _surfaceSprite;
                const int size = 24;
                const float radius = 6f;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.name = "UI Rounded Surface";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.hideFlags = HideFlags.HideAndDontSave;
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius));
                    float dy = Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius));
                    float distance = new Vector2(Mathf.Max(0f, dx), Mathf.Max(0f, dy)).magnitude;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance + 0.5f));
                }
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                _surfaceSprite = Sprite.Create(texture, new Rect(0, 0, size, size),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(7f, 7f, 7f, 7f));
                _surfaceSprite.hideFlags = HideFlags.HideAndDontSave;
                return _surfaceSprite;
            }
        }

        public static void StyleButton(Button button, bool primary = false)
        {
            var image = button.GetComponent<Image>();
            image.sprite = SurfaceSprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = primary ? new Color(0.12f, 0.47f, 0.50f, 1f) : RaisedSurface;
            colors.highlightedColor = primary ? new Color(0.18f, 0.61f, 0.64f, 1f) : new Color(0.34f, 0.51f, 0.65f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = primary ? new Color(0.08f, 0.35f, 0.38f, 1f) : Surface;
            colors.disabledColor = new Color(0.07f, 0.09f, 0.11f, 0.5f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            button.transition = Selectable.Transition.ColorTint;
        }

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
            var root = CreatePanel("Window_" + title, parent, Surface);
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
            border.effectColor = new Color(0.36f, 0.48f, 0.54f, 0.25f);
            var shadow = root.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, -4f);
            border.effectDistance = new Vector2(1f, -1f);

            var rootFocus = root.gameObject.AddComponent<WindowFocusOnPointerDown>();
            rootFocus.Target = root;
            var rootClamp = root.gameObject.AddComponent<WindowBoundsClamp>();
            rootClamp.Target = root;

            var header = CreatePanel("Header", root, RaisedSurface);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.offsetMin = new Vector2(1f, -30f);
            header.offsetMax = new Vector2(-1f, -1f);

            var headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(12, 38, 2, 2);
            headerLayout.spacing = 4;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = true;

            var titleText = CreateText("Title", header, title, font, 13, TextAnchor.MiddleLeft);
            titleText.fontStyle = FontStyle.Bold;
            var titleLe = titleText.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;

            var drag = header.gameObject.AddComponent<WindowDragHandle>();
            drag.Target = root;

            var closeBtn = CreateButton("Close", root, "×", font, () => root.gameObject.SetActive(false), 24f);
            var closeRt = (RectTransform)closeBtn.transform;
            closeRt.anchorMin = new Vector2(1f, 1f);
            closeRt.anchorMax = new Vector2(1f, 1f);
            closeRt.pivot = new Vector2(1f, 1f);
            closeRt.anchoredPosition = new Vector2(-5f, -4f);
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
            contentRt.offsetMin = new Vector2(8f, 8f);
            contentRt.offsetMax = new Vector2(-8f, -36f);
            var contentFocus = content.gameObject.AddComponent<WindowFocusOnPointerDown>();
            contentFocus.Target = root;

            AddEdgeResizeHandle(root, "ResizeLeft", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), Vector2.zero, WindowResizeHandle.ResizeEdges.Left);
            AddEdgeResizeHandle(root, "ResizeRight", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(8f, 0f), Vector2.zero, WindowResizeHandle.ResizeEdges.Right);
            AddEdgeResizeHandle(root, "ResizeTop", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 8f), Vector2.zero, WindowResizeHandle.ResizeEdges.Top);
            AddEdgeResizeHandle(root, "ResizeBottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), Vector2.zero, WindowResizeHandle.ResizeEdges.Bottom);

            AddEdgeResizeHandle(root, "ResizeTopLeft", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, 16f), new Vector2(0f, 0f), WindowResizeHandle.ResizeEdges.Top | WindowResizeHandle.ResizeEdges.Left);
            AddEdgeResizeHandle(root, "ResizeTopRight", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(16f, 16f), new Vector2(0f, 0f), WindowResizeHandle.ResizeEdges.Top | WindowResizeHandle.ResizeEdges.Right);
            AddEdgeResizeHandle(root, "ResizeBottomLeft", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(0f, 0f), WindowResizeHandle.ResizeEdges.Bottom | WindowResizeHandle.ResizeEdges.Left);
            var resizeHandle = AddEdgeResizeHandle(root, "ResizeBottomRight", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(16f, 16f), new Vector2(0f, 0f), WindowResizeHandle.ResizeEdges.Bottom | WindowResizeHandle.ResizeEdges.Right);

            return new WindowRefs { Root = root, Content = contentRt, Title = titleText, CloseButton = closeBtn, ResizeHandle = resizeHandle };
        }

        private static WindowResizeHandle AddEdgeResizeHandle(
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
            image.color = Color.clear;
            image.raycastTarget = true;

            var handle = handleGo.GetComponent<WindowResizeHandle>();
            handle.Target = root;
            handle.Edges = edges;
            return handle;
        }

        public static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            // Recolor neutral blue panels consistently; preserve semantic and transparent colors.
            if (color.a > 0.1f && color.b >= color.r && color.b < 0.4f && color.r < 0.25f)
            {
                var surface = color.maxColorComponent > 0.18f ? RaisedSurface : Surface;
                surface.a = color.a;
                color = surface;
                image.sprite = SurfaceSprite;
                image.type = Image.Type.Sliced;
            }
            if (color == Surface || color == RaisedSurface)
            {
                image.sprite = SurfaceSprite;
                image.type = Image.Type.Sliced;
            }
            image.color = color;
            image.raycastTarget = color.a > 0f;
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
            t.color = TextColor;
            t.raycastTarget = false;
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
            StyleButton(btn);
            btn.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 26f;

            var txt = CreateText("Label", go.transform, label, font, 12, TextAnchor.MiddleCenter);
            var rt = (RectTransform)txt.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return btn;
        }

        public static Dropdown CreateDropdown(string name, Transform parent, Font font, float width = 180f)
        {
            var go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);
            var dropdown = go.GetComponent<Dropdown>();
            var image = go.GetComponent<Image>();
            image.sprite = SurfaceSprite;
            image.type = Image.Type.Sliced;
            image.color = RaisedSurface;
            dropdown.targetGraphic = image;
            var colors = dropdown.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.12f;
            dropdown.colors = colors;
            foreach (var text in go.GetComponentsInChildren<Text>(true))
            {
                text.font = font;
                text.fontSize = 13;
                text.color = TextColor;
                text.raycastTarget = false;
            }
            var arrow = go.transform.Find("Arrow");
            if (arrow != null)
            {
                arrow.GetComponent<Image>().enabled = false;
                var glyph = CreateText("Chevron", arrow, "▾", font, 18, TextAnchor.MiddleCenter);
                glyph.rectTransform.anchorMin = Vector2.zero;
                glyph.rectTransform.anchorMax = Vector2.one;
                glyph.rectTransform.offsetMin = glyph.rectTransform.offsetMax = Vector2.zero;
            }
            dropdown.template.GetComponent<Image>().color = Surface;
            var item = dropdown.template.GetComponentInChildren<Toggle>(true);
            if (item != null)
            {
                if (item.targetGraphic != null) item.targetGraphic.color = RaisedSurface;
                if (item.graphic != null)
                {
                    var checkImage = item.graphic as Image;
                    if (checkImage != null) { checkImage.sprite = SurfaceSprite; checkImage.color = Accent; }
                }
            }
            dropdown.captionText.rectTransform.offsetMax = new Vector2(-36f, -4f);
            var layout = go.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.minWidth = 80f;
            layout.preferredHeight = 28f;
            dropdown.template.sizeDelta = new Vector2(0f, 180f);
            return dropdown;
        }

        public static InputField CreateInputField(string name, Transform parent, string placeholder, Font font, float width = 100f)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.16f, 0.2f, 0.28f, 1f);
            var input = go.GetComponent<InputField>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 26f;

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

            var inputImage = go.GetComponent<Image>();
            inputImage.sprite = SurfaceSprite;
            inputImage.type = Image.Type.Sliced;
            inputImage.color = Surface;
            var inputBorder = go.AddComponent<Outline>();
            inputBorder.effectColor = new Color(0.36f, 0.48f, 0.54f, 0.3f);
            inputBorder.effectDistance = new Vector2(1f, -1f);
            input.textComponent = text;
            input.placeholder = ph;
            return input;
        }

        public static Scrollbar AddVerticalScrollbar(ScrollRect scroll)
        {
            var go = DefaultControls.CreateScrollbar(new DefaultControls.Resources());
            go.name = "VerticalScrollbar";
            go.transform.SetParent(scroll.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(-8f, 0f);
            rt.offsetMax = Vector2.zero;
            var bar = go.GetComponent<Scrollbar>();
            bar.SetDirection(Scrollbar.Direction.BottomToTop, false);
            // DefaultControls adds 20 pixels to a horizontal handle. A narrow vertical
            // track must instead own the full handle width, including after resize.
            bar.handleRect.sizeDelta = Vector2.zero;
            var slidingArea = bar.handleRect.parent as RectTransform;
            if (slidingArea != null)
            {
                slidingArea.offsetMin = new Vector2(0f, 4f);
                slidingArea.offsetMax = new Vector2(0f, -4f);
            }
            go.GetComponent<Image>().color = Surface;
            var handle = bar.targetGraphic as Image;
            if (handle != null)
            {
                handle.sprite = SurfaceSprite;
                handle.type = Image.Type.Sliced;
                handle.color = new Color(0.55f, 0.75f, 0.90f, 1f);
            }
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 4f;
            return bar;
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
