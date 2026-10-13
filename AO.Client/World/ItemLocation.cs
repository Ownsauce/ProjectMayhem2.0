using System;

namespace AO.Client.World
{
    public enum ItemArea { Weapons, Armor, Implants, Social, Inventory, Backpack }

    /// <summary>A zero-based equipment, main-inventory, or backpack slot. Backpack addresses also retain the container instance.</summary>
    public sealed class ItemLocation
    {
        public ItemLocation(ItemArea area, int index, int containerInstance = 0)
        {
            if (!Enum.IsDefined(typeof(ItemArea), area)) throw new ArgumentOutOfRangeException(nameof(area));
            if (index < 0 || index >= (area == ItemArea.Backpack ? 1024 : area == ItemArea.Inventory ? 30 : 15))
                throw new ArgumentOutOfRangeException(nameof(index));
            Area = area;
            Index = index;
            if (area == ItemArea.Backpack && containerInstance <= 0) throw new ArgumentOutOfRangeException(nameof(containerInstance));
            ContainerInstance = containerInstance;
        }
        public ItemArea Area { get; }
        public int Index { get; }
        public int ContainerInstance { get; }
    }
}
