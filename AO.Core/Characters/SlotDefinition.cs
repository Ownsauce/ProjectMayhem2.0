using System;
using System.Collections.Generic;
using System.Text;

namespace AO.Core.Characters
{
    public class SlotDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // Weapon, Armor, Implant, Social
    }
}

