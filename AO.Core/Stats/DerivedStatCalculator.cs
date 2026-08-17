namespace AO.Core.Stats
{
    public static class DerivedStatCalculator
    {
        public static int Calculate(int statId, IStatProvider stats)
        {
            return stats.GetModifiedStat(statId);
        }
    }
}
