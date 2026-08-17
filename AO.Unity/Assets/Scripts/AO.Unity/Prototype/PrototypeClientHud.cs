using System.Collections.Generic;
using System.Linq;
using AO.Core.Characters;
using AO.Core.Stats;
using AO.Data.Unity;
using UnityEngine;

namespace AO.Unity.Prototype
{
    public class PrototypeClientHud : MonoBehaviour
    {
        private Character _character;
        private readonly List<AO.Data.Core.ItemInstance> _allItems = new();
        private readonly List<AO.Data.Core.ItemInstance> _filteredItems = new();
        private Vector2 _itemScroll;
        private string _search = "";
        private string _lastSearch = "";
        private int _slotId = 6;
        private string _status = "";
        private const int MaxVisibleItems = 200;

        private void Start()
        {
            AODataManager.EnsureInstance();

            var profession = new Profession { Name = "Prototype" };
            _character = new Character("PrototypeCharacter", profession, 0, breedId: 1, professionId: 1);

            // Seed baseline stats so requirement checks do not block basic equip testing.
            for (int statId = 0; statId <= 300; statId++)
                _character.StatsContainer.SetBaseStat(statId, 500);
            _character.StatsContainer.Recalculate();

            foreach (var item in AODataManager.Instance.ItemInstances)
            {
                if (item?.Definition == null || string.IsNullOrWhiteSpace(item.Definition.Name))
                    continue;

                _allItems.Add(item);
            }

            RebuildFilter();
            _status = $"Ready. Loaded {_allItems.Count} items.";
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12, 12, 760, Screen.height - 24), GUI.skin.box);
            GUILayout.Label("AO Prototype Client HUD");
            GUILayout.Label(_status);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(48));
            _search = GUILayout.TextField(_search, GUILayout.Width(260));
            GUILayout.Label("Slot", GUILayout.Width(32));
            var slotText = GUILayout.TextField(_slotId.ToString(), GUILayout.Width(50));
            if (int.TryParse(slotText, out var parsedSlot))
                _slotId = parsedSlot;
            GUILayout.EndHorizontal();

            if (_search != _lastSearch)
                RebuildFilter();

            GUILayout.Space(8);
            GUILayout.Label($"Items (showing {_filteredItems.Count} of {_allItems.Count})");

            _itemScroll = GUILayout.BeginScrollView(_itemScroll, GUILayout.Height(300));
            foreach (var item in _filteredItems)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{item.InstanceId} - {item.Definition.Name}", GUILayout.Width(560));
                if (GUILayout.Button("Equip", GUILayout.Width(80)))
                    TryEquip(item);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.Label("Equipped");
            foreach (var kvp in _character.Equipment.GetAllEquipped())
            {
                var equipped = AODataManager.Instance.GetItemInstance(kvp.Value);
                if (equipped?.Definition != null)
                    GUILayout.Label($"Slot {kvp.Key}: {equipped.Definition.Name} ({equipped.InstanceId})");
            }

            GUILayout.Space(8);
            GUILayout.Label("Stats (non-zero, first 20)");
            foreach (var stat in _character.StatsContainer.DebugGetAllStats().Where(s => s.Value != 0).Take(20))
                GUILayout.Label($"{AODataManager.Instance.GetStatName(stat.Key)} ({stat.Key}): {stat.Value}");

            GUILayout.EndArea();
        }

        private void RebuildFilter()
        {
            _lastSearch = _search;
            _filteredItems.Clear();

            IEnumerable<AO.Data.Core.ItemInstance> query = _allItems;
            if (!string.IsNullOrWhiteSpace(_search))
            {
                var token = _search.Trim().ToLowerInvariant();
                query = query.Where(i => i.Definition.Name.ToLowerInvariant().Contains(token));
            }

            _filteredItems.AddRange(query.Take(MaxVisibleItems));
        }

        private void TryEquip(AO.Data.Core.ItemInstance item)
        {
            bool equipped = _character.EquipItem(_slotId, item.InstanceId);
            if (!equipped)
            {
                _status = $"Equip failed: slot {_slotId}, item {item.Definition.Name}";
                return;
            }

            var coreItem = AODataManager.Instance.GetCoreInstance(item.InstanceId);
            if (coreItem != null)
                _character.Inventory.AddItem(coreItem);

            _character.StatsContainer.Recalculate();
            _status = $"Equipped {item.Definition.Name} in slot {_slotId}";
        }
    }
}
