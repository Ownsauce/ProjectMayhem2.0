using AO.Core.Modifiers;
using AO.Core.Stats;
using System.Linq;

namespace AO.Core.Items
{
    public static class ItemDefinitionExtensions
    {
        public static ItemClass GetItemClass(this ItemDefinition item)
        {
            var stat = item.Modifiers.FirstOrDefault(s => s.StatId == StatIds.ItemClass);
            return (ItemClass)(stat.Equals(default(StatModifier)) ? 0 : stat.Value);
        }

        public static int GetDefaultSlot(this ItemDefinition item)
        {
            var stat = item.Modifiers.FirstOrDefault(s => s.StatId == StatIds.DefaultSlot);
            return stat.Equals(default(StatModifier)) ? 0 : stat.Value;
        }

        public static int GetRequiredLevel(this ItemDefinition item)
        {
            var stat = item.Modifiers.FirstOrDefault(s => s.StatId == StatIds.Level);
            return stat.Equals(default(StatModifier)) ? 1 : stat.Value;
        }
    }
}
