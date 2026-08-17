using System.Collections.Generic;

namespace AO.Core.Stats
{
    public class StatDefinition
    {
        public StatId Id { get; init; }

        public string Name { get; init; } = "";

        // Gameplay tuning
        public List<LevelRange> LevelRanges { get; init; } = new();
    }
}
