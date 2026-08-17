namespace AO.Core.Characters
{
    public static class LevelSystem
    {
        public static long GetXpRequired(int level)
        {
            // Simple exponential curve
            return 1000 * level * level;
        }

        public static int GetIpReward(int level)
        {
            // Rough AO-style scaling
            return 150 + (level * 20);
        }
    }
}
