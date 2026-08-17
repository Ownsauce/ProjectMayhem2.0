using AO.Unity.Prototype;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AO.Unity.AOStyle
{
    public sealed class ItemHoverTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public PrototypeUiContext Context;
        public Canvas Canvas;
        public Font Font;
        public long InstanceId;

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Context == null || Canvas == null || Font == null || InstanceId == 0)
                return;
            if (ItemSlotContextMenu.IsOpen)
                return;

            ItemTooltipPresenter.GetOrCreate(Canvas, Context, Font).Show(InstanceId);
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

    public sealed class ItemTooltipPresenter : MonoBehaviour
    {
        private static ItemTooltipPresenter _active;

        private Canvas _canvas;
        private PrototypeUiContext _context;
        private RectTransform _root;
        private Text _title;
        private Text _body;
        private bool _visible;

        public static ItemTooltipPresenter GetOrCreate(Canvas canvas, PrototypeUiContext context, Font font)
        {
            if (_active != null)
            {
                _active.Bind(canvas, context, font);
                return _active;
            }

            var go = new GameObject("ItemTooltipPresenter");
            _active = go.AddComponent<ItemTooltipPresenter>();
            _active.Bind(canvas, context, font);
            _active.Build(font);
            return _active;
        }

        public static void HideActive()
        {
            if (_active != null)
                _active.Hide();
        }

        private void Bind(Canvas canvas, PrototypeUiContext context, Font font)
        {
            _canvas = canvas;
            _context = context;

            if (_root != null && _root.parent != canvas.transform)
                _root.SetParent(canvas.transform, false);

            if (_title != null && _title.font != font)
                _title.font = font;
            if (_body != null && _body.font != font)
                _body.font = font;
        }

        private void Build(Font font)
        {
            var panel = AOStyleUiFactory.CreatePanel("ItemTooltip", _canvas.transform, new Color(0.06f, 0.09f, 0.12f, 0.96f));
            _root = panel;
            _root.pivot = new Vector2(0f, 1f);
            _root.anchorMin = new Vector2(0f, 0f);
            _root.anchorMax = new Vector2(0f, 0f);
            _root.sizeDelta = new Vector2(420f, 180f);

            var outline = _root.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);

            var layout = _root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 7, 8);
            layout.spacing = 5;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = _root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _title = AOStyleUiFactory.CreateText("Title", _root, "Item", font, 14, TextAnchor.MiddleLeft);
            _title.supportRichText = true;
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Overflow;
            _title.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

            _body = AOStyleUiFactory.CreateText("Body", _root, string.Empty, font, 12, TextAnchor.UpperLeft);
            _body.supportRichText = true;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.gameObject.AddComponent<LayoutElement>().minHeight = 100f;

            var bgImage = _root.GetComponent<Image>();
            if (bgImage != null)
                bgImage.raycastTarget = false;
            _title.raycastTarget = false;
            _body.raycastTarget = false;

            _root.gameObject.SetActive(false);
        }

        public void Show(long instanceId)
        {
            if (_context == null || _root == null)
                return;
            if (ItemSlotContextMenu.IsOpen)
            {
                Hide();
                return;
            }

            if (!_context.TryGetItemTooltip(instanceId, out var info))
            {
                Hide();
                return;
            }

            _title.text = info.Title;
            _title.color = info.TitleColor;
            _body.text = info.Body;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
            Canvas.ForceUpdateCanvases();
            _root.gameObject.SetActive(true);
            _visible = true;
            RepositionToPointer();
        }

        public void ShowInfo(PrototypeUiContext.ItemTooltipInfo info)
        {
            if (_root == null || info == null)
            {
                Hide();
                return;
            }

            _title.text = info.Title ?? string.Empty;
            _title.color = info.TitleColor;
            _body.text = info.Body ?? string.Empty;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
            Canvas.ForceUpdateCanvases();
            _root.gameObject.SetActive(true);
            _visible = true;
            RepositionToPointer();
        }

        private void Update()
        {
            if (_visible)
            {
                if (ItemSlotContextMenu.IsOpen)
                {
                    Hide();
                    return;
                }
                RepositionToPointer();
            }
        }

        private void RepositionToPointer()
        {
            if (_canvas == null || _root == null)
                return;

            Vector2 pointer = ReadPointerScreenPosition();
            bool rightHalf = pointer.x >= (Screen.width * 0.5f);
            bool topHalf = pointer.y >= (Screen.height * 0.5f);
            var offset = new Vector2(12f, 12f);
            Vector2 screenPos;

            // Flip relative tooltip side by hovered screen corner.
            if (topHalf && rightHalf) // top-right -> bottom-left
            {
                _root.pivot = new Vector2(1f, 1f);
                screenPos = pointer + new Vector2(-offset.x, -offset.y);
            }
            else if (topHalf && !rightHalf) // top-left -> bottom-right
            {
                _root.pivot = new Vector2(0f, 1f);
                screenPos = pointer + new Vector2(offset.x, -offset.y);
            }
            else if (!topHalf && !rightHalf) // bottom-left -> top-right
            {
                _root.pivot = new Vector2(0f, 0f);
                screenPos = pointer + new Vector2(offset.x, offset.y);
            }
            else // bottom-right -> top-left
            {
                _root.pivot = new Vector2(1f, 0f);
                screenPos = pointer + new Vector2(-offset.x, offset.y);
            }

            float width = _root.rect.width;
            float height = _root.rect.height;
            float left = screenPos.x - (width * _root.pivot.x);
            float right = screenPos.x + (width * (1f - _root.pivot.x));
            float bottom = screenPos.y - (height * _root.pivot.y);
            float top = screenPos.y + (height * (1f - _root.pivot.y));

            const float pad = 6f;
            if (left < pad) screenPos.x += pad - left;
            if (right > Screen.width - pad) screenPos.x -= right - (Screen.width - pad);
            if (bottom < pad) screenPos.y += pad - bottom;
            if (top > Screen.height - pad) screenPos.y -= top - (Screen.height - pad);

            _root.position = screenPos;
        }

        public void Hide()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
            _visible = false;
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
}
