using System.Linq;
using AO.Data.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public class PrototypeItemBrowserWindow : PrototypeWindowBase
    {
        private RectTransform _content;
        private InputField _searchInput;
        private InputField _slotInput;
        private InputField _levelInput;

        protected override void Build()
        {
            var controls = PrototypeUiFactory.CreatePanel("Controls", transform, new Color(0f, 0f, 0f, 0f));
            controls.gameObject.AddComponent<LayoutElement>().preferredHeight = 52f;
            var v = controls.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandHeight = false;

            var row1 = PrototypeUiFactory.CreatePanel("Row1", controls, new Color(0f, 0f, 0f, 0f));
            row1.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            var h1 = row1.gameObject.AddComponent<HorizontalLayoutGroup>();
            h1.spacing = 4;
            h1.childControlWidth = false;
            h1.childControlHeight = true;
            h1.childForceExpandHeight = false;

            PrototypeUiFactory.CreateText("SearchLabel", row1, "Find", Font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 30f;

            _searchInput = PrototypeUiFactory.CreateInputField("SearchInput", row1, "find item name...", Font);
            var searchLe = _searchInput.gameObject.GetComponent<LayoutElement>();
            searchLe.preferredWidth = 220f;
            searchLe.preferredHeight = 22f;
            _searchInput.onValueChanged.AddListener(_ => RefreshView());

            var row2 = PrototypeUiFactory.CreatePanel("Row2", controls, new Color(0f, 0f, 0f, 0f));
            row2.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
            var h2 = row2.gameObject.AddComponent<HorizontalLayoutGroup>();
            h2.spacing = 4;
            h2.childControlWidth = false;
            h2.childControlHeight = true;
            h2.childForceExpandHeight = false;

            PrototypeUiFactory.CreateText("SlotLabel", row2, "Slot", Font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 26f;

            _slotInput = PrototypeUiFactory.CreateInputField("SlotInput", row2, Context.SelectedEquipSlot.ToString(), Font);
            var slotLe = _slotInput.gameObject.GetComponent<LayoutElement>();
            slotLe.preferredWidth = 34f;
            slotLe.preferredHeight = 22f;
            var slotBtn = PrototypeUiFactory.CreateButton("ApplySlot", row2, "Set Slot", Font, ApplySlotFromInput, 58f);
            slotBtn.GetComponent<LayoutElement>().preferredHeight = 22f;

            PrototypeUiFactory.CreateText("LvlLabel", row2, "Lvl", Font, 12, TextAnchor.MiddleLeft)
                .gameObject.AddComponent<LayoutElement>().preferredWidth = 20f;

            _levelInput = PrototypeUiFactory.CreateInputField("LevelInput", row2, Context.CharacterLevel.ToString(), Font);
            var levelLe = _levelInput.gameObject.GetComponent<LayoutElement>();
            levelLe.preferredWidth = 40f;
            levelLe.preferredHeight = 22f;
            var levelBtn = PrototypeUiFactory.CreateButton("ApplyLevel", row2, "Set Lvl", Font, ApplyLevelFromInput, 54f);
            levelBtn.GetComponent<LayoutElement>().preferredHeight = 22f;

            _content = PrototypeUiFactory.CreateScrollVertical(transform);
        }

        protected override void RefreshView()
        {
            if (_content == null) return;
            PrototypeUiFactory.ClearChildren(_content);

            var token = _searchInput != null ? _searchInput.text : string.Empty;
            foreach (var item in Context.QueryItems(token))
            {
                BuildItemRow(item);
            }
        }

        private void BuildItemRow(ItemInstance item)
        {
            var row = PrototypeUiFactory.CreatePanel("Row", _content, new Color(0.13f, 0.15f, 0.2f, 1f));
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(6, 6, 4, 4);
            h.spacing = 4;
            h.childControlHeight = true;
            h.childControlWidth = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(row, false);
            var iconLe = iconGo.AddComponent<LayoutElement>();
            iconLe.preferredWidth = 20;
            iconLe.preferredHeight = 20;
            var img = iconGo.GetComponent<Image>();
            img.color = new Color(0.22f, 0.22f, 0.22f, 1f);
            var sprite = Context.GetIconForData(item);
            if (sprite != null)
            {
                img.sprite = sprite;
                img.color = Color.white;
            }

            var label = PrototypeUiFactory.CreateText("Name", row, item.Definition.Name, Font, 11, TextAnchor.MiddleLeft);
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var toInv = PrototypeUiFactory.CreateButton("ToInv", row, "To Inv", Font, () => Context.AddToInventory(item), 46f);
            toInv.GetComponent<LayoutElement>().preferredHeight = 20f;
            var toBag = PrototypeUiFactory.CreateButton("ToBag", row, "To Bag", Font, () => Context.AddToOpenBackpack(item), 46f);
            toBag.GetComponent<LayoutElement>().preferredHeight = 20f;
        }

        private void ApplySlotFromInput()
        {
            if (!int.TryParse(_slotInput.text, out var slot))
            {
                Context.SetEquipSlot(1);
                return;
            }

            Context.SetEquipSlot(slot);
        }

        private void ApplyLevelFromInput()
        {
            if (!int.TryParse(_levelInput.text, out var level))
            {
                Context.SetCharacterLevel(1);
                return;
            }

            Context.SetCharacterLevel(level);
        }
    }
}
