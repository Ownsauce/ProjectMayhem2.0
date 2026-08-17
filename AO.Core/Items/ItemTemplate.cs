using System.Collections.Generic;

namespace AO.Core.Items
{
    public class ItemTemplate
    {
        public int AOID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<int, int> StatValues { get; set; } = new();
        // Could add AttackDefenseData, SpellData, etc.
    }
}
