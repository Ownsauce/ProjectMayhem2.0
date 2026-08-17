using AO.Core.Items;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public class PrototypeInventoryWindow : PrototypeWindowBase
    {
        private RectTransform _gridContent;
        private Canvas _rootCanvas;

        protected override void Build()
        {
            _gridContent = PrototypeUiFactory.CreateGridContent(transform, 5, new Vector2(42, 42), 6, 5);
            _rootCanvas = GetComponentInParent<Canvas>();
        }

        protected override void RefreshView()
        {
            if (_gridContent == null || Context?.Character == null) return;
            PrototypeUiFactory.ClearChildren(_gridContent);

            for (int i = 0; i < Context.Character.Inventory.Main.Capacity; i++)
            {
                int index = i;
                var item = Context.Character.Inventory.Main.Slots[index];
                var sprite = Context.GetIconForCore(item);
                var btn = PrototypeUiFactory.CreateSlotButton(_gridContent, sprite, index + 1, Font, out var iconImage);
                btn.onClick.AddListener(() => Context.SelectInventorySlot(index));

                var dd = btn.gameObject.AddComponent<PrototypeSlotDragDrop>();
                dd.Context = Context;
                dd.Zone = PrototypeUiContext.SlotZone.Inventory;
                dd.SlotIndex = index;
                dd.RootCanvas = _rootCanvas;
                dd.SourceIcon = iconImage;
            }
        }
    }
}
