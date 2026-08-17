using System.Linq;
using AO.Data.Unity;
using UnityEngine;
using UnityEngine.UI;

namespace AO.Unity.Prototype
{
    public class PrototypeEquipmentStatsWindow : PrototypeWindowBase
    {
        private RectTransform _equippedContent;
        private RectTransform _statsContent;

        protected override void Build()
        {
            PrototypeUiFactory.CreateText("EqHeader", transform, "Equipped", Font, 15, TextAnchor.MiddleLeft);
            _equippedContent = PrototypeUiFactory.CreateScrollVertical(transform, 90f);
            PrototypeUiFactory.CreateText("StatsHeader", transform, "Stats", Font, 15, TextAnchor.MiddleLeft);
            _statsContent = PrototypeUiFactory.CreateScrollVertical(transform, 120f);
        }

        protected override void RefreshView()
        {
            RefreshEquipped();
            RefreshStats();
        }

        private void RefreshEquipped()
        {
            if (_equippedContent == null) return;
            PrototypeUiFactory.ClearChildren(_equippedContent);

            foreach (var pair in Context.GetEquipped().OrderBy(k => k.Key))
            {
                var inst = Context.GetDataItem(pair.Value);
                if (inst?.Definition == null) continue;

                var row = PrototypeUiFactory.CreatePanel("EqRow", _equippedContent, new Color(0.13f, 0.15f, 0.2f, 1f));
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(6, 6, 3, 3);
                h.spacing = 6;
                h.childControlHeight = true;
                h.childControlWidth = false;

                var label = PrototypeUiFactory.CreateText("EqText", row, $"Slot {pair.Key}: {inst.Definition.Name}", Font, 12, TextAnchor.MiddleLeft);
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                PrototypeUiFactory.CreateButton("Unequip", row, "Unequip", Font, () => Context.UnequipSlot(pair.Key), 66f);
            }
        }

        private void RefreshStats()
        {
            if (_statsContent == null) return;
            PrototypeUiFactory.ClearChildren(_statsContent);

            foreach (var pair in Context.GetStats().Where(s => s.Value != 0).Take(50))
            {
                string statName = AODataManager.Instance.GetStatName(pair.Key);
                PrototypeUiFactory.CreateText("Stat", _statsContent, $"{statName} ({pair.Key}): {pair.Value}", Font, 12, TextAnchor.MiddleLeft);
            }
        }
    }
}
