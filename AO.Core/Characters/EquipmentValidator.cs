using System;
using System.Collections.Generic;
using System.Linq;
using AO.Core.Items;
using AO.Core.Stats;

namespace AO.Core.Characters
{
    public static class EquipmentValidator
    {
        private const int ItemClassStat = 76;
        private const int EquipSlotBitmapStat = 298;

        public static Func<ItemClass, int, IReadOnlyCollection<int>> ResolveSlotsByBitmap { get; set; }

        public static bool CanEquip(Character character, ItemDefinition itemDef, int slotId)
        {
            return CanEquip(character, itemDef, slotId, out _);
        }

        public static bool CanEquip(Character character, ItemDefinition itemDef, int slotId, out string reason)
        {
            reason = string.Empty;

            if (itemDef == null)
            {
                reason = "Item definition is null.";
                return false;
            }

            var itemClass = GetItemClass(itemDef);
            var allowedSlots = GetAllowedSlots(itemDef, itemClass);
            if (allowedSlots.Count > 0 && !allowedSlots.Contains(slotId))
            {
                reason = $"{itemClass} cannot equip in slot {slotId}.";
                return false;
            }

            return true;
        }

        public static IReadOnlyCollection<int> GetAllowedSlots(ItemDefinition itemDef)
        {
            if (itemDef == null)
                return Array.Empty<int>();

            return GetAllowedSlots(itemDef, GetItemClass(itemDef));
        }

        private static ItemClass GetItemClass(ItemDefinition item)
        {
            var statEntry = item.Modifiers.FirstOrDefault(s => s.StatId == ItemClassStat);
            if (statEntry.Value != 0)
                return (ItemClass)statEntry.Value;

            return (ItemClass)item.DBType;
        }

        private static IReadOnlyCollection<int> GetAllowedSlots(ItemDefinition itemDef, ItemClass itemClass)
        {
            int bitmap = itemDef.Modifiers
                .Where(m => m.StatId == EquipSlotBitmapStat)
                .Select(m => m.Value)
                .FirstOrDefault();

            if (bitmap > 0 && ResolveSlotsByBitmap != null)
            {
                var resolved = ResolveSlotsByBitmap(itemClass, bitmap);
                if (resolved != null && resolved.Count > 0)
                    return resolved;
            }

            // Fallbacks if bitmap data is unavailable.
            return itemClass switch
            {
                ItemClass.Weapon => new[] { 6, 8 },
                ItemClass.Armor => Enumerable.Range(1, 15).ToArray(),
                ItemClass.Implant => Enumerable.Range(101, 13).ToArray(),
                _ => Array.Empty<int>()
            };
        }

    }
}
