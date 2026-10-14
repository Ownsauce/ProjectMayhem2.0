using System;
using System.Collections.Generic;

namespace AO.Client.World
{
    /// <summary>Server stock and acknowledged purchase selections for a vendor session.</summary>
    public sealed class ShopSnapshot
    {
        public ShopSnapshot(int identityType, int identityInstance, IReadOnlyList<InventoryEntrySnapshot> stock,
            bool isOpen = false, IReadOnlyList<int> selectedSlots = null)
        {
            IdentityType = identityType; IdentityInstance = identityInstance;
            Stock = stock ?? Array.Empty<InventoryEntrySnapshot>();
            IsOpen = isOpen; SelectedSlots = selectedSlots ?? Array.Empty<int>();
        }
        public int IdentityType { get; }
        public int IdentityInstance { get; }
        public IReadOnlyList<InventoryEntrySnapshot> Stock { get; }
        public bool IsOpen { get; }
        public IReadOnlyList<int> SelectedSlots { get; }
    }
}
