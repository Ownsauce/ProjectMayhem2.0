using System.Collections.Generic;

namespace AO.Core.Stats
{
    public class Profession
    {
        public string Name { get; set; } = "";

        // ⭐ Each profession defines stat caps / priorities
        public List<StatDefinition> Stats { get; set; } = new();

        public Profession() { }

        public Profession(string name)
        {
            Name = name;
        }

        public override string ToString() => Name;
    }
}
