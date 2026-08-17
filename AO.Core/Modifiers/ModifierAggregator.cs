using System;
using System.Collections.Generic;

namespace AO.Core.Modifiers
{
    /// <summary>
    /// Aggregates all stat modifiers from equipped items (Core-only)
    /// </summary>
    public class ModifierAggregator
    {
        private readonly List<StatModifier> _mods = new();
        public IReadOnlyList<StatModifier> Modifiers => _mods;

        // Delegates provided by AODataManager
        public static Func<long, AO.Core.Items.ItemInstance> GetItemInstance { get; set; }
        public static Func<int, AO.Core.Items.ItemDefinition> GetItemDefinition { get; set; }

        public void Rebuild(IReadOnlyDictionary<int, long> equipped)
        {
            _mods.Clear();

            foreach (var kvp in equipped)
            {
                var instance = GetItemInstance(kvp.Value);
                if (instance == null) continue;

                var def = instance.Definition;
                if (def == null) continue;

                foreach (var m in def.Modifiers)
                    _mods.Add(m);
            }
        }
    }
}
