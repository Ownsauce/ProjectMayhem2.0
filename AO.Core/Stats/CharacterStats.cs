using System;
using System.Collections.Generic;
using System.Linq;
using AO.Core.Modifiers;

namespace AO.Core.Stats
{
    /// <summary>
    /// Container for all character stats, calculates modifiers
    /// </summary>
    public class CharacterStats : IStatProvider
    {
        private static readonly int[] AbilityStatIds = { 16, 17, 18, 19, 20, 21 };
        private const int MaxHealthStatId = 1;
        private const int NanoPoolStatId = 132;
        private const int BodyDevelopmentStatId = 152;
        private const int CurrentNanoStatId = 214;
        private const int MaxNanoEnergyStatId = 221;
        private const int HealthStatId = 27;

        // Optional delegate injected by data layer:
        // statId -> [str, agi, sta, int, sen, psy] influence weights.
        public static Func<int, IReadOnlyList<float>> GetSkillTrickleWeights { get; set; } = _ => null;
        public static Func<int, int, int, int?> GetMaxHealthForBreedLevelAndBodyDev { get; set; } = (_, _, _) => null;
        public static Func<int, int, int, int?> GetMaxNanoForBreedLevelAndNanoPool { get; set; } = (_, _, _) => null;

        private readonly Dictionary<int, StatValue> _stats = new();
        private readonly Dictionary<int, int> _trickleBonuses = new();
        private readonly ModifierAggregator _aggregator;
        public int BreedId { get; set; } = 1;
        public int CharacterLevel { get; set; } = 1;
        int IStatProvider.GetBaseStat(int statId) => GetBaseStat(statId);
        int IStatProvider.GetModifiedStat(int statId) => GetModifiedStat(statId);


        public CharacterStats(ModifierAggregator aggregator)
        {
            _aggregator = aggregator;
        }

        public void SetBaseStat(int statId, int value)
        {
            if (!_stats.TryGetValue(statId, out var stat))
                _stats[statId] = stat = new StatValue(statId);

            stat.BaseValue = value;
        }

        public int GetBaseStat(int statId)
            => _stats.TryGetValue(statId, out var s) ? s.BaseValue : 0;

        public bool HasBaseStat(int statId) => _stats.ContainsKey(statId);

        public int GetModifiedStat(int statId)
            => _stats.TryGetValue(statId, out var s) ? s.ModifiedValue : 0;

        public int GetTrickleBonus(int statId)
            => _trickleBonuses.TryGetValue(statId, out var bonus) ? bonus : 0;

        public IReadOnlyDictionary<int, int> GetBaseStatsSnapshot()
            => _stats.ToDictionary(pair => pair.Key, pair => pair.Value.BaseValue);

        public int GetBaseWithTrickle(int statId)
            => GetBaseStat(statId) + GetTrickleBonus(statId);

        public void Recalculate()
        {
            _trickleBonuses.Clear();

            foreach (var stat in _stats.Values)
                stat.ModifiedValue = stat.BaseValue;

            foreach (var mod in _aggregator.Modifiers)
            {
                if (_stats.TryGetValue(mod.StatId, out var stat))
                    stat.ModifiedValue += mod.Value;
            }

            // Trickle is derived from current ability values after direct modifiers are applied.
            foreach (var stat in _stats.Values)
            {
                var weights = GetSkillTrickleWeights?.Invoke(stat.StatId);
                if (weights == null || weights.Count < 6)
                    continue;

                float trickle = 0f;
                for (int i = 0; i < 6; i++)
                {
                    int abilityValue = _stats.TryGetValue(AbilityStatIds[i], out var abilityStat)
                        ? abilityStat.ModifiedValue
                        : 0;

                    trickle += (weights[i] / 4f) * abilityValue;
                }

                int bonus = (int)System.Math.Floor(trickle);
                if (bonus <= 0)
                    continue;

                stat.ModifiedValue += bonus;
                _trickleBonuses[stat.StatId] = bonus;
            }

            ApplyBreedDerivedVitals();
        }

        private void ApplyBreedDerivedVitals()
        {
            int breedId = System.Math.Max(1, BreedId);
            int level = System.Math.Max(1, CharacterLevel);

            int bodyDevBuffed = GetModifiedStat(BodyDevelopmentStatId);
            int nanoPoolBuffed = GetModifiedStat(NanoPoolStatId);

            int maxHealthBonus = GetModifiedStat(MaxHealthStatId);
            int maxNanoBonus = GetModifiedStat(MaxNanoEnergyStatId);

            int computedMaxHealth = GetMaxHealthForBreedLevelAndBodyDev?.Invoke(breedId, level, bodyDevBuffed) ?? 0;
            int computedMaxNano = GetMaxNanoForBreedLevelAndNanoPool?.Invoke(breedId, level, nanoPoolBuffed) ?? 0;

            int finalMaxHealth = System.Math.Max(1, computedMaxHealth + maxHealthBonus);
            int finalMaxNano = System.Math.Max(1, computedMaxNano + maxNanoBonus);

            SetModifiedStat(MaxHealthStatId, finalMaxHealth);
            SetModifiedStat(MaxNanoEnergyStatId, finalMaxNano);

            // Prototype currently has no damage drain simulation, so keep current pools filled.
            SetModifiedStat(HealthStatId, finalMaxHealth);
            SetModifiedStat(CurrentNanoStatId, finalMaxNano);
        }

        private void SetModifiedStat(int statId, int value)
        {
            if (!_stats.TryGetValue(statId, out var stat))
                _stats[statId] = stat = new StatValue(statId);

            stat.ModifiedValue = System.Math.Max(0, value);
        }

        public int GetFinalStat(int statId)
        {
            return GetModifiedStat(statId);
        }

        public IReadOnlyDictionary<int, int> DebugGetAllStats()
        {
            return _stats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ModifiedValue);
        }
    }
}

