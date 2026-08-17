using System.Collections.Generic;
using System.Linq;
using AO.Core.Items;

namespace AO.Core.Characters
{
    public class Inventory
    {
        private const int CanFlagStatId = 30;
        private const int StackableCanBit = 512;
        private readonly Dictionary<long, InventoryContainer> _containers = new();

        // Legacy flat list. Kept for compatibility while systems migrate to container APIs.
        public List<ItemInstance> Items { get; } = new();

        public InventoryContainer Main { get; } = new InventoryContainer(30);

        public bool AddItem(ItemInstance item)
        {
            return TryAddToMain(item);
        }

        public bool TryAddToMain(ItemInstance item)
        {
            if (TryMergeIntoStack(Main.Slots, item))
                return true;

            if (!Main.TryAdd(item))
                return false;

            Items.Add(item);

            if (item.IsContainer && item.InstanceId != 0)
                EnsureContainer(item.InstanceId, item.ContainerCapacity);

            return true;
        }

        public bool TrySetMainSlot(int slotIndex, ItemInstance item)
        {
            if (!Main.TrySetAt(slotIndex, item))
                return false;

            Items.Add(item);

            if (item.IsContainer && item.InstanceId != 0)
                EnsureContainer(item.InstanceId, item.ContainerCapacity);

            return true;
        }

        public bool TryRemoveFromMain(int slotIndex, out ItemInstance removed)
        {
            return Main.TryRemoveAt(slotIndex, out removed);
        }

        public bool RemoveItem(ItemInstance item)
        {
            bool removed = Main.Remove(item);

            foreach (var container in _containers.Values)
                removed = container.Remove(item) || removed;

            if (removed)
                Items.Remove(item);

            return removed;
        }

        public ItemInstance FindItemByName(string name)
        {
            return Items.FirstOrDefault(i => i.Definition.Name == name);
        }

        public bool TryGetContainer(long containerInstanceId, out InventoryContainer container)
        {
            return _containers.TryGetValue(containerInstanceId, out container);
        }

        public bool TryAddToContainer(long containerInstanceId, ItemInstance item)
        {
            if (!_containers.TryGetValue(containerInstanceId, out var container))
                return false;

            // Backpacks cannot be nested.
            if (item.IsContainer)
                return false;

            if (TryMergeIntoStack(container.Slots, item))
                return true;

            if (!container.TryAdd(item))
                return false;

            if (!Items.Contains(item))
                Items.Add(item);

            return true;
        }

        public bool TryRemoveFromContainer(long containerInstanceId, int slotIndex, out ItemInstance removed)
        {
            removed = null;

            if (!_containers.TryGetValue(containerInstanceId, out var container))
                return false;

            return container.TryRemoveAt(slotIndex, out removed);
        }

        public bool TryRemoveAnyByInstanceId(long instanceId, out ItemInstance removed)
        {
            removed = null;
            if (instanceId == 0)
                return false;

            for (int i = 0; i < Main.Capacity; i++)
            {
                var item = Main.Slots[i];
                if (item == null || item.InstanceId != instanceId)
                    continue;
                if (!Main.TryRemoveAt(i, out removed))
                    continue;

                if (removed != null)
                    Items.Remove(removed);
                return removed != null;
            }

            foreach (var container in _containers.Values)
            {
                for (int i = 0; i < container.Capacity; i++)
                {
                    var item = container.Slots[i];
                    if (item == null || item.InstanceId != instanceId)
                        continue;
                    if (!container.TryRemoveAt(i, out removed))
                        continue;

                    if (removed != null)
                        Items.Remove(removed);
                    return removed != null;
                }
            }

            return false;
        }

        public bool TryRemoveAnyByAoid(int aoid, out ItemInstance removed)
        {
            removed = null;
            if (aoid <= 0)
                return false;

            for (int i = 0; i < Main.Capacity; i++)
            {
                var item = Main.Slots[i];
                if (item?.Definition == null || item.Definition.AOID != aoid)
                    continue;
                if (!Main.TryRemoveAt(i, out removed))
                    continue;

                if (removed != null)
                    Items.Remove(removed);
                return removed != null;
            }

            foreach (var container in _containers.Values)
            {
                for (int i = 0; i < container.Capacity; i++)
                {
                    var item = container.Slots[i];
                    if (item?.Definition == null || item.Definition.AOID != aoid)
                        continue;
                    if (!container.TryRemoveAt(i, out removed))
                        continue;

                    if (removed != null)
                        Items.Remove(removed);
                    return removed != null;
                }
            }

            return false;
        }

        public bool TrySetInContainer(long containerInstanceId, int slotIndex, ItemInstance item)
        {
            if (!_containers.TryGetValue(containerInstanceId, out var container))
                return false;

            // Backpacks cannot be nested.
            if (item.IsContainer)
                return false;

            if (!container.TrySetAt(slotIndex, item))
                return false;

            if (!Items.Contains(item))
                Items.Add(item);

            return true;
        }

        private void EnsureContainer(long containerInstanceId, int capacity)
        {
            if (_containers.ContainsKey(containerInstanceId))
                return;

            _containers[containerInstanceId] = new InventoryContainer(capacity <= 0 ? 21 : capacity);
        }

        private bool TryMergeIntoStack(IReadOnlyList<ItemInstance> slots, ItemInstance incoming)
        {
            if (!CanStack(incoming))
                return false;

            int incomingQuantity = incoming.Quantity <= 0 ? 1 : incoming.Quantity;
            for (int i = 0; i < slots.Count; i++)
            {
                var existing = slots[i];
                if (!CanStackTogether(existing, incoming))
                    continue;

                existing.Quantity += incomingQuantity;
                Items.Remove(incoming);
                if (!Items.Contains(existing))
                    Items.Add(existing);
                return true;
            }

            return false;
        }

        private static bool CanStackTogether(ItemInstance existing, ItemInstance incoming)
        {
            if (!CanStack(existing) || !CanStack(incoming))
                return false;

            if (existing.Definition == null || incoming.Definition == null)
                return false;

            return existing.Definition.AOID == incoming.Definition.AOID;
        }

        private static bool CanStack(ItemInstance item)
        {
            if (item == null || item.IsContainer || item.Definition == null)
                return false;

            var canFlags = item.Definition.Modifiers?.FirstOrDefault(m => m.StatId == CanFlagStatId).Value ?? 0;
            return (canFlags & StackableCanBit) != 0;
        }
    }
}
