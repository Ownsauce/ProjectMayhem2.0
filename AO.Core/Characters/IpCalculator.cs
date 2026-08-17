using System;
using System.Linq;
using AO.Core.Stats;

namespace AO.Core.Characters
{
    public static class IpCalculator
    {
        public static int GetPriorityForLevel(Profession profession, string statName, int level)
        {
            var stat = profession.Stats.FirstOrDefault(s => s.Name == statName);
            if (stat == null)
                return 3;

            var range = stat.LevelRanges.FirstOrDefault(r => level >= r.Min && level <= r.Max);
            return range?.Priority ?? 3;
        }

        public static int CalculateCost(int priority, int amount)
        {
            int baseCost = priority switch
            {
                1 => 4,
                2 => 8,
                3 => 12,
                4 => 16,
                _ => 12
            };

            return baseCost * amount;
        }

        public static int CalculateCostFromFactor(float factor, int amount)
        {
            if (amount <= 0)
                return 0;
            // Kept for compatibility; the character now computes factor-cost from current base.
            int perPoint = System.Math.Max(1, (int)System.Math.Ceiling(4f * factor));
            return perPoint * amount;
        }

        public static int CalculateCostFromFactorAndBase(int currentBase, float factor, int amount)
        {
            if (amount <= 0 || factor <= 0f)
                return 0;

            int total = 0;
            for (int i = 0; i < amount; i++)
            {
                int value = currentBase + i;
                total += System.Math.Max(1, (int)System.Math.Ceiling(value * factor));
            }

            return total;
        }
    }
}
