namespace AO.Core.Stats
{
    public class LevelRange
    {
        public int Min { get; set; }
        public int Max { get; set; }

        // ⭐ Used for IP cost tier / stat priority
        public int Priority { get; set; }
    }
}
