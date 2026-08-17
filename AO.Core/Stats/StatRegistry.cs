using AO.Core.Stats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AO.Data.Stats
{
    /// <summary>
    /// Central runtime registry of stat definitions.
    /// </summary>
    public static class StatRegistry
    {
        private static readonly ConcurrentDictionary<int, StatDefinition> _stats = new();

        public static void Register(StatDefinition def)
        {
            if (def is null)
                throw new ArgumentNullException(nameof(def));

            _stats[def.Id] = def;
        }

        public static bool TryGet(int id, out StatDefinition? def)
        {
            return _stats.TryGetValue(id, out def);
        }

        public static StatDefinition? Get(int id)
        {
            _stats.TryGetValue(id, out var def);
            return def;
        }

        public static IReadOnlyDictionary<int, StatDefinition> All => _stats;
    }
}
