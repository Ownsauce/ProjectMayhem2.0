using AO.Core.Items;
using System.Collections.Generic;

namespace AO.Core.Characters
{
    public class InventoryContainer
    {
        private readonly ItemInstance[] _slots;
        public int Capacity => _slots.Length;
        public IReadOnlyList<ItemInstance> Slots => _slots;

        public InventoryContainer(int capacity)
        {
            _slots = new ItemInstance[capacity];
        }

        public bool TryAdd(ItemInstance item)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null) continue;
                _slots[i] = item;
                return true;
            }
            return false;
        }

        public bool TrySetAt(int slotIndex, ItemInstance item)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
            if (_slots[slotIndex] != null) return false;
            _slots[slotIndex] = item;
            return true;
        }

        public bool TryRemoveAt(int slotIndex, out ItemInstance removed)
        {
            removed = null;
            if (slotIndex < 0 || slotIndex >= _slots.Length) return false;
            if (_slots[slotIndex] == null) return false;

            removed = _slots[slotIndex];
            _slots[slotIndex] = null;
            return true;
        }

        public bool Remove(ItemInstance item)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!ReferenceEquals(_slots[i], item)) continue;
                _slots[i] = null;
                return true;
            }
            return false;
        }
    }
}
