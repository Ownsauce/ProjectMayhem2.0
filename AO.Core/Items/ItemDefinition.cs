using AO.Core.Modifiers;
using System.Collections.Generic;

namespace AO.Core.Items
{
    public class ItemDefinition
    {
        public string Name { get; }
        public int AOID { get; }
        public int DBType { get; }
        private List<StatModifier> _modifiers = new();

        public ItemDefinition(string name, int aoid, int dbType)
        {
            Name = name;
            AOID = aoid;
            DBType = dbType;
        }

        public void AddModifier(StatModifier mod) => _modifiers.Add(mod);
        public IReadOnlyList<StatModifier> Modifiers => _modifiers;
    }
}
