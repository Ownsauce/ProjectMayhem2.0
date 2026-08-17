using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public class PrototypeBackpackWindow : PrototypeWindowBase
    {
        private Text _label;
        private RectTransform _gridContent;
        private Canvas _rootCanvas;

        protected override void Build()
        {
            _label = PrototypeUiFactory.CreateText("BagLabel", transform, "Open backpack: none", Font, 13, TextAnchor.MiddleLeft);
            _gridContent = PrototypeUiFactory.CreateGridContent(transform, 5, new Vector2(34, 34), 7, 3);
            _rootCanvas = GetComponentInParent<Canvas>();
        }

        protected override void RefreshView()
        {
            if (_gridContent == null) return;
            PrototypeUiFactory.ClearChildren(_gridContent);

            if (!Context.TryGetOpenBackpackContainer(out var container, out var bagItem))
            {
                _label.text = "Open backpack: none";
                for (int i = 0; i < 21; i++)
                    PrototypeUiFactory.CreateSlotButton(_gridContent, null, i + 1, Font, out _);
                return;
            }

            _label.text = $"Open backpack: {bagItem?.Definition?.Name ?? bagItem?.InstanceId.ToString() ?? "unknown"}";

            for (int i = 0; i < container.Capacity; i++)
            {
                int index = i;
                var item = container.Slots[index];
                var sprite = Context.GetIconForCore(item);
                var btn = PrototypeUiFactory.CreateSlotButton(_gridContent, sprite, i + 1, Font, out var iconImage);
                btn.onClick.AddListener(() => Context.SelectBackpackSlot(index));

                var dd = btn.gameObject.AddComponent<PrototypeSlotDragDrop>();
                dd.Context = Context;
                dd.Zone = PrototypeUiContext.SlotZone.Backpack;
                dd.SlotIndex = index;
                dd.RootCanvas = _rootCanvas;
                dd.SourceIcon = iconImage;
            }
        }
    }
}
