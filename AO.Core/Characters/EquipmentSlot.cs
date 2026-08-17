using AO.Core.Items;

namespace AO.Core.Characters
{
    public class EquipmentSlot
    {
        public string Name { get; set; } = string.Empty;
        public EquipmentSlotCategory Category { get; set; }
        public ItemInstance? EquippedItem { get; set; }

        public bool IsEmpty => EquippedItem == null;
    }
}
