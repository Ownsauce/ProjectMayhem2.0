using System;

namespace AO.Core.Modifiers
{
    public class CharacterModifier
    {
        public ModifierDefinition Definition { get; }
        public DateTime AppliedTime { get; }

        public CharacterModifier(ModifierDefinition def)
        {
            Definition = def;
            AppliedTime = DateTime.UtcNow;
        }

        public bool IsExpired => Definition.Duration > 0 &&
                                 (DateTime.UtcNow - AppliedTime).TotalSeconds >= Definition.Duration;
    }
}
