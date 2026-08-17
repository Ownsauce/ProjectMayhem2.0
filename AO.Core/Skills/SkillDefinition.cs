using System.Collections.Generic;

namespace AO.Core.Skills
{
    public class SkillDefinition
    {
        public string Name { get; set; }

        // Which stats affect this skill and by how much
        // Example: Strength 0.3 = 30%
        public Dictionary<string, float> StatInfluence { get; set; } = new();
    }
}
