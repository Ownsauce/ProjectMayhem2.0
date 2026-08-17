namespace AO.Core.Modifiers
{
    public class ModifierDefinition
    {
        public string Name { get; set; }

        // Target derived stat or skill
        public string Target { get; set; }

        // Flat value added
        public int Flat { get; set; } = 0;

        // Percentage multiplier (0.1 = +10%)
        public float Percent { get; set; } = 0f;

        // Duration in seconds (0 = permanent)
        public float Duration { get; set; } = 0f;
    }
}
