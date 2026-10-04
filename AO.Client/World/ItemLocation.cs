using System;

namespace AO.Client.World
{
    public enum ItemArea { Weapons, Armor, Implants, Social, Inventory }

    /// <summary>A zero-based slot within a character's equipment or main inventory.</summary>
    public sealed class ItemLocation
    {
        public ItemLocation(ItemArea area, int index)
        {
            if (!Enum.IsDefined(typeof(ItemArea), area)) throw new ArgumentOutOfRangeException(nameof(area));
            if (index < 0 || index >= (area == ItemArea.Inventory ? 30 : 15))
                throw new ArgumentOutOfRangeException(nameof(index));
            Area = area;
            Index = index;
        }
        public ItemArea Area { get; }
        public int Index { get; }
    }
}
