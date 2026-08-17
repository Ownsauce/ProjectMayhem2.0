using System.Collections.Generic;

namespace AO.Core.DerivedStats
{
    public class DerivedStatDefinition
    {
        public string Name { get; set; }

        // List of skill/stat influences with a weight
        // Example: "2h Blunt" 0.5 means half of skill contributes
        public Dictionary<string, float> Influences { get; set; } = new();
    }
}
