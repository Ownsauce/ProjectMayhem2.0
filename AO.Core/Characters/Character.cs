using AO.Core.DerivedStats;
using AO.Core.Items;
using AO.Core.Modifiers;
using AO.Core.Stats;
using AO.Data.Core;
using System;
using System.Collections.Generic;

using DataItemInstance = AO.Data.Core.ItemInstance;

namespace AO.Core.Characters
{
    public class Character
    {
        private static readonly HashSet<int> AbilityStatIds = new() { 16, 17, 18, 19, 20, 21 };

        public string Name { get; }
        public Profession Profession { get; private set; }
        public int BreedId { get; private set; }
        public int ProfessionId { get; private set; }

        public CharacterEquipment Equipment { get; private set; }
        public Inventory Inventory { get; } = new Inventory();
        public int AvailableIp { get; private set; }
        public CharacterLevel Level { get; } = new CharacterLevel();

        private readonly ModifierAggregator _aggregator = new ModifierAggregator();
        public CharacterStats StatsContainer { get; }

        public Dictionary<string, CharacterStat> Stats { get; } = new();
        public Dictionary<string, CharacterDerivedStat> DerivedStats { get; } = new();

        // Unity-independent delegates
        public static Func<string, int> StatIdLookup { get; set; } = _ => -1;
        public static Func<long, DataItemInstance> GetItemInstance { get; set; } = _ => null;

        // IP/skill model delegates
        public static Func<int> GetStartingIpBonus { get; set; } = () => 0;
        public static Func<int, int> GetTitleLevelForLevel { get; set; } = _ => 1;
        public static Func<int, int> GetIpPerLevelForLevel { get; set; } = _ => 0;
        public static Func<int, int, float?> GetSkillCostFactor { get; set; } = (_, _) => null;
        public static Func<int, int, int, int, int?> GetStatCapForLevel { get; set; } = (_, _, _, _) => null;
        public static Func<int, int, int?> GetStartingBaseStatForBreed { get; set; } = (_, _) => null;
        public static Func<int, int, int?> GetAbilityCostFactorForBreed { get; set; } = (_, _) => null;
        public static Func<int, long> GetExperienceToNextLevelForLevel { get; set; } = _ => 0;

        public Character(
            string name,
            Profession profession,
            int startingIp = 0,
            int breedId = 1,
            int professionId = 1)
        {
            Name = name;
            Profession = profession;
            BreedId = breedId;
            ProfessionId = professionId;

            int bootIp = System.Math.Max(0, startingIp);
            if (bootIp == 0 && GetStartingIpBonus != null)
                bootIp += System.Math.Max(0, GetStartingIpBonus());
            AvailableIp = bootIp;

            Equipment = new CharacterEquipment(this);
            StatsContainer = new CharacterStats(_aggregator);
            SyncStatContext();

            foreach (var statDef in profession.Stats)
            {
                Stats[statDef.Name] = new CharacterStat(statDef.Name, 5);
                int statId = StatIdLookup(statDef.Name);
                if (statId >= 0)
                    StatsContainer.SetBaseStat(statId, 5);
            }
        }

        public void AddIp(int amount) => AvailableIp += System.Math.Max(0, amount);

        public void ApplyAuthoritativeStats(IReadOnlyDictionary<int, int> values,
            Func<int, string> statNameResolver = null)
        {
            if (values == null)
                return;

            foreach (var pair in values)
            {
                int value = pair.Value;
                StatsContainer.SetBaseStat(pair.Key, value);
                string statName = statNameResolver?.Invoke(pair.Key);
                if (!string.IsNullOrWhiteSpace(statName))
                {
                    if (!Stats.TryGetValue(statName, out var stat))
                        Stats[statName] = new CharacterStat(statName, value);
                    else
                        stat.SetValue(value);
                }
            }

            if (values.TryGetValue(53, out int ip)) AvailableIp = System.Math.Max(0, ip);
            if (values.TryGetValue(54, out int level)) Level.SetLevel(level);
            if (values.TryGetValue(52, out int xp))
            {
                Level.SetTotalExperience(System.Math.Max(0, xp));
                int previousThreshold = values.TryGetValue(57, out int lastXp)
                    ? System.Math.Max(0, lastXp)
                    : 0;
                Level.SetExperience(System.Math.Max(0, xp - previousThreshold));
            }
            if (values.TryGetValue(4, out int breed) && breed > 0) BreedId = breed;
            if (values.TryGetValue(60, out int profession) && profession > 0) ProfessionId = profession;
            SyncStatContext();
            StatsContainer.Recalculate();
            RecalculateDerivedStats();
        }

        public int GetCurrentTitleLevel()
        {
            return GetTitleLevelForLevel != null
                ? System.Math.Max(1, GetTitleLevelForLevel(Level.Level))
                : 1;
        }

        public int GetIpGainForLevel(int level)
        {
            if (GetIpPerLevelForLevel == null)
                return 0;
            return System.Math.Max(0, GetIpPerLevelForLevel(level));
        }

        public int GrantIpForNextLevel()
        {
            Level.LevelUp();
            int gain = GetIpGainForLevel(Level.Level);
            AddIp(gain);
            SyncStatContext();
            return gain;
        }

        public long GetExperienceToNextLevel()
        {
            return GetExperienceToNextLevelForLevel?.Invoke(Level.Level) ?? 0;
        }

        public int AddExperience(long amount)
        {
            if (amount <= 0)
                return 0;

            long remaining = amount;
            int levelsGained = 0;

            while (remaining > 0)
            {
                long neededForNext = System.Math.Max(0, GetExperienceToNextLevel());
                if (neededForNext <= 0)
                {
                    Level.AddExperience(remaining);
                    remaining = 0;
                    break;
                }

                long currentInLevel = System.Math.Max(0, Level.Experience);
                long leftToLevel = System.Math.Max(0, neededForNext - currentInLevel);

                if (leftToLevel == 0)
                {
                    GrantIpForNextLevel();
                    Level.SetExperience(0);
                    levelsGained++;
                    continue;
                }

                if (remaining >= leftToLevel)
                {
                    Level.AddExperience(leftToLevel);
                    remaining -= leftToLevel;
                    GrantIpForNextLevel();
                    Level.SetExperience(0);
                    levelsGained++;
                }
                else
                {
                    Level.AddExperience(remaining);
                    remaining = 0;
                }
            }

            SyncStatContext();
            StatsContainer.Recalculate();
            RecalculateDerivedStats();
            return levelsGained;
        }

        public int GetIpCostForStatIncrease(string statName, int amount)
        {
            if (amount <= 0 || !TryGetOrCreateStat(statName, out var stat))
                return 0;

            int statId = StatIdLookup(statName);
            if (AbilityStatIds.Contains(statId))
                return CalculateAbilityCostFromBase(stat.Value, amount, statId, BreedId);

            if (statId >= 0 && GetSkillCostFactor != null)
            {
                float? factor = GetSkillCostFactor(ProfessionId, statId);
                if (factor.HasValue && factor.Value > 0f)
                    return IpCalculator.CalculateCostFromFactorAndBase(stat.Value, factor.Value, amount);
            }

            int pri = IpCalculator.GetPriorityForLevel(Profession, statName, stat.Value);
            return IpCalculator.CalculateCost(pri, amount);
        }

        public bool TryIncreaseStat(string statName, int amount)
        {
            if (amount <= 0)
                return false;

            if (!TryGetOrCreateStat(statName, out var stat))
                return false;

            int statId = StatIdLookup(statName);
            int? cap = GetStatCap(statId);
            if (cap.HasValue && stat.Value + amount > cap.Value)
                return false;

            int cost = GetIpCostForStatIncrease(statName, amount);
            if (AvailableIp < cost)
                return false;

            AvailableIp -= cost;
            stat.Increase(amount);

            if (statId >= 0)
                StatsContainer.SetBaseStat(statId, stat.Value);

            SyncStatContext();
            StatsContainer.Recalculate();
            RecalculateDerivedStats();

            return true;
        }

        public int? GetStatCap(string statName)
        {
            int statId = StatIdLookup(statName);
            return GetStatCap(statId);
        }

        public int? GetStatCap(int statId)
        {
            if (statId < 0 || GetStatCapForLevel == null)
                return null;

            return GetStatCapForLevel(statId, Level.Level, BreedId, ProfessionId);
        }

        public void SetIdentity(int breedId, int professionId, Profession profession = null)
        {
            BreedId = breedId <= 0 ? 1 : breedId;
            ProfessionId = professionId <= 0 ? 1 : professionId;
            Profession = profession ?? Profession ?? new Profession { Name = "Prototype" };
            SyncStatContext();
        }

        public void SetLevelAndRebuildIp(int level, bool preserveSpent = true)
        {
            int spent = 0;
            if (preserveSpent)
            {
                int previousLevel = Level.Level < 1 ? 1 : Level.Level;
                int previousTotal = GetTotalIpForLevel(previousLevel);
                spent = System.Math.Max(0, previousTotal - AvailableIp);
            }

            int targetLevel = level < 1 ? 1 : level;
            Level.SetLevel(targetLevel);
            Level.SetExperience(0);
            SyncStatContext();

            int targetTotal = GetTotalIpForLevel(targetLevel);
            AvailableIp = System.Math.Max(0, targetTotal - spent);
        }

        private int GetTotalIpForLevel(int level)
        {
            int targetLevel = level < 1 ? 1 : level;
            int total = System.Math.Max(0, GetStartingIpBonus?.Invoke() ?? 0);
            for (int l = 2; l <= targetLevel; l++)
                total += GetIpGainForLevel(l);
            return total;
        }

        public void SetBaseStatValue(string statName, int value)
        {
            if (string.IsNullOrWhiteSpace(statName))
                return;

            int statId = StatIdLookup(statName);
            if (statId < 0)
                return;

            int safeValue = System.Math.Max(0, value);
            if (!Stats.TryGetValue(statName, out var stat))
            {
                stat = new CharacterStat(statName, safeValue);
                Stats[statName] = stat;
            }
            else
            {
                stat.SetValue(safeValue);
            }

            StatsContainer.SetBaseStat(statId, safeValue);
            SyncStatContext();
        }

        private bool TryGetOrCreateStat(string statName, out CharacterStat stat)
        {
            if (Stats.TryGetValue(statName, out stat))
                return true;

            int statId = StatIdLookup(statName);
            if (statId < 0)
            {
                stat = null;
                return false;
            }

            int current = StatsContainer.GetBaseStat(statId);
            stat = new CharacterStat(statName, current);
            Stats[statName] = stat;
            return true;
        }

        private static int CalculateAbilityCostFromBase(int currentBase, int amount, int statId, int breedId)
        {
            if (amount <= 0)
                return 0;

            int factor = 2;
            if (GetAbilityCostFactorForBreed != null)
            {
                int? f = GetAbilityCostFactorForBreed(statId, breedId);
                if (f.HasValue && f.Value > 0)
                    factor = f.Value;
            }

            int total = 0;
            for (int i = 0; i < amount; i++)
            {
                int nextFrom = currentBase + i;
                total += factor * nextFrom;
            }

            return System.Math.Max(0, total);
        }

        public void RecalculateDerivedStats()
        {
            foreach (var ds in DerivedStats.Values)
            {
                float total = 0f;
                foreach (var influence in ds.Definition.Influences)
                {
                    int statId = StatIdLookup(influence.Key);
                    if (statId < 0)
                        continue;

                    int statValue = StatsContainer.GetFinalStat(statId);
                    total += statValue * influence.Value;
                }

                ds.SetFinal((int)total);
            }
        }

        public bool EquipItem(int slotId, long instanceId)
        {
            var dataInstance = GetItemInstance(instanceId);
            if (dataInstance == null)
                return false;

            var dataDef = dataInstance.Definition;
            if (dataDef == null)
                return false;

            var coreDef = new AO.Core.Items.ItemDefinition(
                name: dataDef.Name,
                aoid: dataDef.Id,
                dbType: dataDef.SlotType
            );

            if (dataDef.StatModifiers != null)
            {
                foreach (var mod in dataDef.StatModifiers)
                    coreDef.AddModifier(new AO.Core.Modifiers.StatModifier(mod.StatId, mod.Value));
            }

            if (!Equipment.EquipItem(slotId, instanceId))
                return false;

            _aggregator.Rebuild(Equipment.GetAllEquipped());
            SyncStatContext();
            StatsContainer.Recalculate();
            RecalculateDerivedStats();
            return true;
        }

        public bool UnequipSlot(int slotId)
        {
            if (!Equipment.UnequipSlot(slotId))
                return false;

            _aggregator.Rebuild(Equipment.GetAllEquipped());
            SyncStatContext();
            StatsContainer.Recalculate();
            RecalculateDerivedStats();
            return true;
        }

        /// <summary>
        /// Replaces locally cached equipment with a server-authoritative snapshot and
        /// refreshes every stat surface that depends on worn items.
        /// </summary>
        public void ApplyAuthoritativeEquipment(IReadOnlyDictionary<int, long> equipped)
        {
            Equipment.ApplyAuthoritative(equipped);
            _aggregator.Rebuild(Equipment.GetAllEquipped());
            SyncStatContext();
            StatsContainer.Recalculate();
            RecalculateDerivedStats();
        }

        private void SyncStatContext()
        {
            if (StatsContainer == null)
                return;

            StatsContainer.BreedId = BreedId <= 0 ? 1 : BreedId;
            StatsContainer.CharacterLevel = Level?.Level < 1 ? 1 : Level.Level;
        }
    }
}
