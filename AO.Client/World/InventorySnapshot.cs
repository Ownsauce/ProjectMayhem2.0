using System;
using System.Collections.Generic;

namespace AO.Client.World
{
    public sealed class InventoryEntrySnapshot
    {
        public InventoryEntrySnapshot(int slot, int identityType, int identityInstance,
            int lowId, int highId, int quality, int quantity)
        {
            Slot = slot; IdentityType = identityType; IdentityInstance = identityInstance;
            LowId = lowId; HighId = highId; Quality = quality; Quantity = Math.Max(1, quantity);
        }
        public int Slot { get; }
        public int IdentityType { get; }
        public int IdentityInstance { get; }
        public int LowId { get; }
        public int HighId { get; }
        public int Quality { get; }
        public int Quantity { get; }
    }

    public sealed class InventorySnapshot
    {
        public InventorySnapshot(int capacity, int containerType, int containerInstance,
            int mainInventorySlot, IReadOnlyList<InventoryEntrySnapshot> entries)
        {
            Capacity = capacity; ContainerType = containerType;
            ContainerInstance = containerInstance; MainInventorySlot = mainInventorySlot;
            Entries = entries ?? Array.Empty<InventoryEntrySnapshot>();
        }
        public int Capacity { get; }
        public int ContainerType { get; }
        public int ContainerInstance { get; }
        public int MainInventorySlot { get; }
        public IReadOnlyList<InventoryEntrySnapshot> Entries { get; }
        public bool IsMainInventory => MainInventorySlot == 0;
    }
}
