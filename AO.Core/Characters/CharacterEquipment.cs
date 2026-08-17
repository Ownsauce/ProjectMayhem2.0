using System;
using System.Collections.Generic;
using AO.Core.Items;

namespace AO.Core.Characters
{
    public class CharacterEquipment
    {
        private readonly Dictionary<int, long> _equipped = new();

        // Delegates provided by AODataManager
        public static Func<long, ItemInstance> GetItemInstance { get; set; }
        public static Func<int, ItemDefinition> GetItemDefinition { get; set; }

        private readonly Character _owner;

        public CharacterEquipment(Character owner)
        {
            _owner = owner;
        }

        public bool EquipItem(int slotId, long instanceId)
        {
            var instance = GetItemInstance(instanceId);
            if (instance == null) return false;

            var def = instance.Definition;
            if (def == null) return false;

            // ✅ Core-only CanEquip
            if (!EquipmentValidator.CanEquip(_owner, def, slotId))
                return false;

            _equipped[slotId] = instanceId;
            return true;
        }

        public bool UnequipSlot(int slotId)
        {
            return _equipped.Remove(slotId);
        }

        public IReadOnlyDictionary<int, long> GetAllEquipped() => _equipped;
    }
}
