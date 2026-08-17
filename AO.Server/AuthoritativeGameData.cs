
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AO.Core.Characters;
using AO.Core.Modifiers;
using AO.Core.Stats;
using CoreItems = AO.Core.Items;
using DataCore = AO.Data.Core;
using System.Text.Json;

namespace AO.Server
{
    public sealed class AuthoritativeGameData
    {
        private const int ItemClassStatId = 76;
        private const int IconStatId = 79;
        private const int DefaultSlotStatId = 88;
        private static readonly JsonSerializerOptions DataJsonOptions = new() { IncludeFields = true };

        private readonly Dictionary<int, CoreItems.ItemDefinition> _coreDefs = new();
        private readonly Dictionary<long, CoreItems.ItemInstance> _coreInstances = new();
        private readonly Dictionary<long, DataCore.ItemInstance> _dataInstances = new();
        private readonly Dictionary<string, int> _statIdByName = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, Dictionary<int, float>> _skillCostFactorsByProfession = new();
        private readonly Dictionary<int, int[]> _breedAbilityInitial = new();
        private readonly Dictionary<int, int[]> _breedAbilityCapsPre201 = new();
        private readonly Dictionary<int, int[]> _breedAbilityCapsPost201PerLevel = new();
        private readonly Dictionary<int, int[]> _breedAbilityCostFactors = new();
        private readonly Dictionary<int, int> _breedBaseHp = new();
        private readonly Dictionary<int, int> _breedHpPerLevel = new();
        private readonly Dictionary<int, int> _breedBaseNp = new();
        private readonly Dictionary<int, int> _breedNpPerLevel = new();
        private readonly Dictionary<int, int> _breedBodyFactor = new();
        private readonly Dictionary<int, int> _breedNanoFactor = new();
        private readonly Dictionary<int, long> _xpNeededToLevel = new();
        private readonly Dictionary<int, string> _professionNames = new();
        private readonly Dictionary<int, float[]> _skillTrickleByStat = new();
        private readonly List<IpTitleLevelBracket> _ipTitleLevelBrackets = new();
        private readonly List<SkillAllocationRange> _skillAllocationRanges = new();
        private readonly List<TlBracket> _skillAllocationTlBrackets = new();
        private readonly Dictionary<ulong, List<int>> _weaponSlotBits = new();
        private readonly Dictionary<ulong, List<int>> _armorSlotBits = new();
        private readonly Dictionary<ulong, List<int>> _implantSlotBits = new();

        private int _abilityImprovementsPerLevel = 3;

        public ServerDataPaths Paths { get; }
        public DataCore.StatRegistry StatMap { get; } = new();
        public IReadOnlyList<Profession> Professions { get; private set; } = Array.Empty<Profession>();
        public IReadOnlyList<DataCore.Item> Items { get; private set; } = Array.Empty<DataCore.Item>();
        public IReadOnlyList<DataCore.NanoProgram> Nanos { get; private set; } = Array.Empty<DataCore.NanoProgram>();

        private AuthoritativeGameData(ServerDataPaths paths)
        {
            Paths = paths;
        }

        public static AuthoritativeGameData Load(ServerDataPaths paths)
        {
            var data = new AuthoritativeGameData(paths);
            data.LoadAll();
            data.BootstrapDelegates();
            return data;
        }

        public string GetStatName(int statId) => StatMap.Get(statId);

        public IReadOnlyCollection<int> GetKnownSkillStatIds()
        {
            return _statIdByName.Values
                .Where(id => id > 0)
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
        }

        public Profession ResolveProfession(int professionId)
        {
            if (Professions.Count == 0)
                return null;

            if (_professionNames.TryGetValue(professionId, out var name))
            {
                var match = Professions.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match;
            }

            return professionId > 0 && professionId <= Professions.Count ? Professions[professionId - 1] : Professions[0];
        }

        public DataCore.ItemInstance GetItemInstance(long instanceId)
        {
            if (_dataInstances.TryGetValue(instanceId, out var inst))
                return inst;

            if (instanceId < int.MinValue || instanceId > int.MaxValue)
                return null;

            var rawItem = Items.FirstOrDefault(i => i != null && i.AOID == (int)instanceId);
            if (rawItem == null)
                return null;

            EnsureItemCaches(rawItem);
            return _dataInstances.TryGetValue(instanceId, out inst) ? inst : null;
        }

        public int? GetStartingBaseStatForBreedValue(int statId, int breedId)
        {
            if (!TryMapAbilityStatToIndex(statId, out int idx))
                return null;
            return _breedAbilityInitial.TryGetValue(breedId, out var row) && idx < row.Length ? row[idx] : null;
        }

        private void EnsureItemCaches(DataCore.Item item)
        {
            if (item == null)
                return;

            if (_dataInstances.ContainsKey(item.AOID) && _coreInstances.ContainsKey(item.AOID) && _coreDefs.ContainsKey(item.AOID))
                return;

            var derivedMods = BuildModifiers(item);
            int itemClass = GetModifierValue(derivedMods, ItemClassStatId);
            int iconId = GetModifierValue(derivedMods, IconStatId);
            int defaultSlot = GetModifierValue(derivedMods, DefaultSlotStatId);

            var def = new CoreItems.ItemDefinition(item.Name ?? string.Empty, item.AOID, itemClass);
            foreach (var sm in derivedMods)
                def.AddModifier(new AO.Core.Modifiers.StatModifier(sm.StatId, sm.Value));
            _coreDefs[item.AOID] = def;

            int containerCapacity = IsBackpack(item.Name) ? 21 : 0;
            _coreInstances[item.AOID] = new CoreItems.ItemInstance(def, 1, item.AOID, containerCapacity);

            var dataDef = new DataCore.ItemDefinition
            {
                Id = item.AOID,
                Name = item.Name ?? string.Empty,
                Description = item.Description ?? string.Empty,
                IconId = iconId,
                SlotType = itemClass != 0 ? itemClass : defaultSlot,
                Type = item.Type,
                RequiredLevel = item.Level,
                StatModifiers = derivedMods
            };

            _dataInstances[item.AOID] = new DataCore.ItemInstance
            {
                InstanceId = item.AOID,
                DefinitionId = item.AOID,
                Definition = dataDef,
                Quantity = 1
            };
        }
        private void LoadAll()
        {
            ValidateServerOwnedAuthoritativeFiles();
            LoadProfessions();
            LoadStatMap();
            LoadIpProgression();
            LoadXpNeededToLevel();
            LoadProfessionSkillCostFactors();
            LoadBreedAbilityData();
            LoadBreedStats();
            LoadSkillAllocationAndColor();
            LoadSkillTrickleDown();
            LoadLookupNameMap("profession.json", _professionNames);
            LoadEquipmentSlotBitmaps();
            LoadItems();
            LoadNanos();
        }

        private void ValidateServerOwnedAuthoritativeFiles()
        {
            string[] requiredServerOwnedFiles =
            {
                "profession_vitals_tl.json",
                "breed.json",
                "item_classes.json",
                "can_flag.json",
                "spell_formats.json"
            };

            foreach (string fileName in requiredServerOwnedFiles)
            {
                string serverPath = Paths.ResolveServerAoDataFile(fileName);
                if (!File.Exists(serverPath))
                {
                    Console.WriteLine(
                        $"[AO.Server] WARNING: missing server-owned authoritative data '{fileName}' at '{serverPath}'. " +
                        "Fallback resolution may use Unity AOData until this file is migrated.");
                }
            }
        }

        private void LoadProfessions()
        {
            if (!File.Exists(Paths.ProfessionXmlPath))
                throw new FileNotFoundException("Missing profession XML for authoritative server.", Paths.ProfessionXmlPath);
            Professions = ProfessionLoader.LoadFromXml(Paths.ProfessionXmlPath);
        }

        private void LoadStatMap()
        {
            string json = File.ReadAllText(RequireAoDataFile("statmap.json"));
            try
            {
                var entries = JsonSerializer.Deserialize<List<DataCore.StatMapEntry>>(json, DataJsonOptions);
                if (entries != null && entries.Count > 0)
                {
                    foreach (var entry in entries)
                    {
                        StatMap.Register(entry.Id, entry.Name);
                        if (!string.IsNullOrWhiteSpace(entry.Name))
                            _statIdByName[NormalizeStatName(entry.Name)] = entry.Id;
                    }
                    return;
                }
            }
            catch
            {
            }

            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict == null)
                return;

            foreach (var pair in dict)
            {
                if (!int.TryParse(pair.Key, out int id))
                    continue;
                StatMap.Register(id, pair.Value);
                if (!string.IsNullOrWhiteSpace(pair.Value))
                    _statIdByName[NormalizeStatName(pair.Value)] = id;
            }
        }

        private void LoadIpProgression()
        {
            var file = JsonSerializer.Deserialize<IpTitleLevelProgressionFile>(
                File.ReadAllText(RequireAoDataFile("title_level_ip_progression.json"))) ?? new IpTitleLevelProgressionFile();
            _ipTitleLevelBrackets.Clear();
            _ipTitleLevelBrackets.AddRange(file.tlBrackets.OrderBy(b => b.startLevel));
        }

        private void LoadXpNeededToLevel()
        {
            var file = JsonSerializer.Deserialize<XpNeededToLevelFile>(
                File.ReadAllText(RequireAoDataFile("xp_needed_to_level.json"))) ?? new XpNeededToLevelFile();
            _xpNeededToLevel.Clear();
            foreach (var pair in file.experienceToNextLevel)
            {
                if (int.TryParse(pair.Key, out int level))
                    _xpNeededToLevel[level] = Math.Max(0L, pair.Value);
            }
        }

        private void LoadProfessionSkillCostFactors()
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, float>>>(
                File.ReadAllText(RequireAoDataFile("profession_skill_cost_factors.json")));
            _skillCostFactorsByProfession.Clear();
            if (raw == null)
                return;

            foreach (var statRow in raw)
            {
                if (!int.TryParse(statRow.Key, out int statId) || statRow.Value == null)
                    continue;
                foreach (var profRow in statRow.Value)
                {
                    if (!int.TryParse(profRow.Key, out int professionId))
                        continue;
                    if (!_skillCostFactorsByProfession.TryGetValue(professionId, out var byStat))
                    {
                        byStat = new Dictionary<int, float>();
                        _skillCostFactorsByProfession[professionId] = byStat;
                    }
                    byStat[statId] = profRow.Value;
                }
            }
        }

        private void LoadBreedAbilityData()
        {
            var file = JsonSerializer.Deserialize<BreedAbilityDataFile>(
                File.ReadAllText(RequireAoDataFile("breed_ability_data.json"))) ?? new BreedAbilityDataFile();
            CopyInto(_breedAbilityInitial, ParseBreedAbilityBlock(file.initial));
            CopyInto(_breedAbilityCapsPre201, ParseBreedAbilityBlock(file.caps_pre201));
            CopyInto(_breedAbilityCapsPost201PerLevel, ParseBreedAbilityBlock(file.caps_post201_per_level));
            CopyInto(_breedAbilityCostFactors, ParseBreedAbilityBlock(file.cost_factors));
        }

        private void LoadBreedStats()
        {
            var file = JsonSerializer.Deserialize<BreedStatsFile>(
                File.ReadAllText(RequireAoDataFile("breed_stats.json"))) ?? new BreedStatsFile();
            CopyInto(_breedBaseHp, ParseIntMap(file.base_hp));
            CopyInto(_breedHpPerLevel, ParseIntMap(file.hp_per_level));
            CopyInto(_breedBaseNp, ParseIntMap(file.base_np));
            CopyInto(_breedNpPerLevel, ParseIntMap(file.np_per_level));
            CopyInto(_breedBodyFactor, ParseIntMap(file.body_factor));
            CopyInto(_breedNanoFactor, ParseIntMap(file.nano_factor));
        }

        private void LoadSkillAllocationAndColor()
        {
            string[] candidates = { "skill_caps_and_color.json", "skill_cap_and_color.json", "skill_allocation_and_color.json" };
            string path = candidates.Select(Paths.ResolveAuthoritativeAoDataFile).FirstOrDefault(File.Exists);
            if (path == null)
                throw new FileNotFoundException("Missing skill allocation and color configuration.");

            var file = JsonSerializer.Deserialize<SkillAllocationFile>(File.ReadAllText(path)) ?? new SkillAllocationFile();
            _skillAllocationRanges.Clear();
            _skillAllocationRanges.AddRange(ParseSkillAllocationRanges(file.skillCostRanges));
            _skillAllocationTlBrackets.Clear();
            _skillAllocationTlBrackets.AddRange(ParseSkillAllocationTlBrackets(file.titleLevelBrackets));
            _abilityImprovementsPerLevel = ResolveAbilityImprovementsPerLevel(file);
        }

        private void LoadSkillTrickleDown()
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, float[]>>(
                File.ReadAllText(RequireAoDataFile("skill_trickle_down.json")));
            _skillTrickleByStat.Clear();
            if (raw == null)
                return;
            foreach (var pair in raw)
            {
                if (int.TryParse(pair.Key, out int statId) && pair.Value != null && pair.Value.Length >= 6)
                    _skillTrickleByStat[statId] = pair.Value.Take(6).ToArray();
            }
        }
        private void LoadLookupNameMap(string fileName, Dictionary<int, string> target)
        {
            target.Clear();
            string path = Paths.ResolveAuthoritativeAoDataFile(fileName);
            if (!File.Exists(path))
                return;

            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (raw == null)
                return;
            foreach (var pair in raw)
            {
                if (int.TryParse(pair.Key, out int id))
                    target[id] = pair.Value ?? id.ToString();
            }
        }

        private void LoadEquipmentSlotBitmaps()
        {
            CopyInto(_weaponSlotBits, LoadSlotBitMap(Paths.ResolveAuthoritativeAoDataFile("weapon_slots.json")));
            CopyInto(_armorSlotBits, LoadSlotBitMap(Paths.ResolveAuthoritativeAoDataFile("armor_slots.json")));
            CopyInto(_implantSlotBits, LoadSlotBitMap(Paths.ResolveAuthoritativeAoDataFile("implant_slots.json")));
        }

        private void LoadItems()
        {
            var items = JsonSerializer.Deserialize<List<DataCore.Item>>(File.ReadAllText(RequireAoDataFile("items.json")), DataJsonOptions) ?? new List<DataCore.Item>();
            Items = items;

            foreach (var item in items)
            {
                if (item == null)
                    continue;

                EnsureItemCaches(item);
            }
        }

        private void LoadNanos()
        {
            string path = Paths.ResolveAuthoritativeAoDataFile("nanos.json");
            if (!File.Exists(path))
            {
                Nanos = Array.Empty<DataCore.NanoProgram>();
                return;
            }

            Nanos = JsonSerializer.Deserialize<List<DataCore.NanoProgram>>(File.ReadAllText(path), DataJsonOptions)
                ?? new List<DataCore.NanoProgram>();
        }

        private void BootstrapDelegates()
        {
            CharacterEquipment.GetItemInstance = id => _coreInstances.TryGetValue(id, out var inst) ? inst : null;
            CharacterEquipment.GetItemDefinition = id => _coreDefs.TryGetValue(id, out var def) ? def : null;
            ModifierAggregator.GetItemInstance = CharacterEquipment.GetItemInstance;
            ModifierAggregator.GetItemDefinition = CharacterEquipment.GetItemDefinition;
            AO.Core.Characters.Character.GetItemInstance = id => GetItemInstance(id);
            AO.Core.Characters.Character.StatIdLookup = ResolveStatId;
            AO.Core.Characters.Character.GetStartingIpBonus = GetStartingIpBonus;
            AO.Core.Characters.Character.GetTitleLevelForLevel = GetTitleLevelForLevel;
            AO.Core.Characters.Character.GetIpPerLevelForLevel = GetIpPerLevelForLevel;
            AO.Core.Characters.Character.GetExperienceToNextLevelForLevel = GetExperienceToNextLevelForLevel;
            AO.Core.Characters.Character.GetSkillCostFactor = GetSkillCostFactor;
            AO.Core.Characters.Character.GetStatCapForLevel = GetStatCapForLevel;
            AO.Core.Characters.Character.GetStartingBaseStatForBreed = GetStartingBaseStatForBreedValue;
            AO.Core.Characters.Character.GetAbilityCostFactorForBreed = GetAbilityCostFactorForBreed;
            CharacterStats.GetSkillTrickleWeights = ResolveSkillTrickleWeights;
            CharacterStats.GetMaxHealthForBreedLevelAndBodyDev = ResolveMaxHealthForBreedLevelAndBodyDev;
            CharacterStats.GetMaxNanoForBreedLevelAndNanoPool = ResolveMaxNanoForBreedLevelAndNanoPool;
            EquipmentValidator.ResolveSlotsByBitmap = ResolveAllowedSlotsByBitmap;
        }

        private int ResolveStatId(string statName)
        {
            if (string.IsNullOrWhiteSpace(statName))
                return -1;
            if (_statIdByName.TryGetValue(NormalizeStatName(statName), out int id))
                return id;
            if (statName.StartsWith("stat ", StringComparison.OrdinalIgnoreCase) && int.TryParse(statName.Substring(5).Trim(), out int parsed))
                return parsed;
            return -1;
        }

        private int GetStartingIpBonus()
        {
            var bracket = _ipTitleLevelBrackets.FirstOrDefault(b => b.startLevel <= 1 && (!b.endLevel.HasValue || 1 <= b.endLevel.Value));
            return Math.Max(0, bracket?.ipStartingBonus ?? 0);
        }

        private int GetTitleLevelForLevel(int level)
        {
            if (level <= 0 || _ipTitleLevelBrackets.Count == 0)
                return 1;
            var bracket = _ipTitleLevelBrackets.FirstOrDefault(b => level >= b.startLevel && (!b.endLevel.HasValue || level <= b.endLevel.Value));
            return bracket != null ? Math.Max(1, bracket.tl) : Math.Max(1, _ipTitleLevelBrackets[^1].tl);
        }

        private int GetIpPerLevelForLevel(int level)
        {
            if (level <= 0 || _ipTitleLevelBrackets.Count == 0)
                return 0;
            var bracket = _ipTitleLevelBrackets.FirstOrDefault(b => level >= b.startLevel && (!b.endLevel.HasValue || level <= b.endLevel.Value));
            return bracket != null ? Math.Max(0, bracket.ipPerLevel) : Math.Max(0, _ipTitleLevelBrackets[^1].ipPerLevel);
        }

        private long GetExperienceToNextLevelForLevel(int level)
        {
            return level > 0 && _xpNeededToLevel.TryGetValue(level, out long needed) ? Math.Max(0L, needed) : 0L;
        }

        private float? GetSkillCostFactor(int professionId, int statId)
        {
            if (professionId <= 0 || statId <= 0)
                return null;
            return _skillCostFactorsByProfession.TryGetValue(professionId, out var byStat) && byStat.TryGetValue(statId, out float factor)
                ? factor
                : null;
        }

        private int? GetAbilityCostFactorForBreed(int statId, int breedId)
        {
            if (!TryMapAbilityStatToIndex(statId, out int idx))
                return null;
            return _breedAbilityCostFactors.TryGetValue(breedId, out var row) && idx < row.Length && row[idx] > 0 ? row[idx] : null;
        }

        private int? ResolveMaxHealthForBreedLevelAndBodyDev(int breedId, int level, int bodyDevelopmentBuffed)
        {
            if (!_breedBaseHp.TryGetValue(breedId, out int baseHp) || !_breedHpPerLevel.TryGetValue(breedId, out int hpPerLevel) || !_breedBodyFactor.TryGetValue(breedId, out int bodyFactor))
                return null;
            return Math.Max(1, (Math.Max(0, bodyDevelopmentBuffed) * bodyFactor) + (Math.Max(1, level) * hpPerLevel) + baseHp);
        }

        private int? ResolveMaxNanoForBreedLevelAndNanoPool(int breedId, int level, int nanoPoolBuffed)
        {
            if (!_breedBaseNp.TryGetValue(breedId, out int baseNp) || !_breedNpPerLevel.TryGetValue(breedId, out int npPerLevel) || !_breedNanoFactor.TryGetValue(breedId, out int nanoFactor))
                return null;
            return Math.Max(1, (Math.Max(0, nanoPoolBuffed) * nanoFactor) + (Math.Max(1, level) * npPerLevel) + baseNp);
        }

        private int? GetStatCapForLevel(int statId, int level, int breedId, int professionId)
        {
            int? allocCap = GetSkillAllocationCap(statId, level, breedId, professionId);
            if (!TryMapAbilityStatToIndex(statId, out int idx))
                return allocCap;
            if (!_breedAbilityCapsPre201.TryGetValue(breedId, out var pre201) || idx >= pre201.Length)
                return allocCap;

            int hardCap = pre201[idx];
            if (level > 200 && _breedAbilityCapsPost201PerLevel.TryGetValue(breedId, out var post201) && idx < post201.Length)
                hardCap += (level - 200) * post201[idx];

            return allocCap.HasValue ? Math.Max(0, Math.Min(hardCap, allocCap.Value)) : Math.Max(0, hardCap);
        }

        private int? GetSkillAllocationCap(int statId, int level, int breedId, int professionId)
        {
            if (level <= 0 || _skillAllocationRanges.Count == 0)
                return null;

            float? costPer = TryMapAbilityStatToIndex(statId, out _) ? GetAbilityCostFactorForBreed(statId, breedId) : GetSkillCostFactor(professionId, statId);
            if (!costPer.HasValue)
                return null;

            var range = _skillAllocationRanges.FirstOrDefault(r => costPer.Value >= r.MinCostPer && costPer.Value <= r.MaxCostPer);
            var tl = ResolveTlForLevel(level);
            if (range == null || tl == null)
                return null;

            int currentTlMax = range.MaxByTl.TryGetValue(tl.Tl, out int curMax) ? curMax : 0;
            int improvementsPerLevel = TryMapAbilityStatToIndex(statId, out _) ? Math.Max(1, _abilityImprovementsPerLevel) : range.PointsPerLevel;
            int allowedPoints = Math.Min(currentTlMax, Math.Max(0, level) * improvementsPerLevel);
            int baseAtCreation = GetStartingBaseStatForBreedValue(statId, breedId) ?? 5;
            return Math.Max(0, baseAtCreation + allowedPoints);
        }
        private TlBracket ResolveTlForLevel(int level)
        {
            if (_skillAllocationTlBrackets.Count == 0)
                return null;
            foreach (var bracket in _skillAllocationTlBrackets)
            {
                if (level >= bracket.StartLevel && level <= bracket.EndLevel)
                    return bracket;
            }
            return level < _skillAllocationTlBrackets[0].StartLevel ? _skillAllocationTlBrackets[0] : _skillAllocationTlBrackets[^1];
        }

        private IReadOnlyList<float> ResolveSkillTrickleWeights(int statId)
        {
            return _skillTrickleByStat.TryGetValue(statId, out var weights) && weights.Length >= 6 ? weights : null;
        }

        private IReadOnlyCollection<int> ResolveAllowedSlotsByBitmap(CoreItems.ItemClass itemClass, int bitmap)
        {
            var map = itemClass switch
            {
                CoreItems.ItemClass.Weapon => _weaponSlotBits,
                CoreItems.ItemClass.Armor => _armorSlotBits,
                CoreItems.ItemClass.Implant => _implantSlotBits,
                _ => null
            };
            if (map == null || map.Count == 0)
                return Array.Empty<int>();

            uint itemBitmap = unchecked((uint)bitmap);
            if (itemBitmap == 0)
                return Array.Empty<int>();

            var slots = new HashSet<int>();
            foreach (var pair in map)
            {
                if (pair.Key > uint.MaxValue)
                    continue;
                uint bit = (uint)pair.Key;
                if ((itemBitmap & bit) != bit)
                    continue;
                foreach (var slot in pair.Value)
                    slots.Add(slot);
            }

            return itemClass == CoreItems.ItemClass.Implant && slots.Count > 0
                ? slots.Select(slot => 100 + slot).ToArray()
                : slots.ToArray();
        }

        private string RequireAoDataFile(string fileName)
        {
            string path = Paths.ResolveAuthoritativeAoDataFile(fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Missing authoritative AO data file '{fileName}'.", path);
            return path;
        }

        private static List<DataCore.StatModifier> BuildModifiers(DataCore.Item item)
        {
            var result = new List<DataCore.StatModifier>();
            AddRawMetadataModifier(item, result, ItemClassStatId);
            AddRawMetadataModifier(item, result, IconStatId);
            AddRawMetadataModifier(item, result, DefaultSlotStatId);
            AddRawMetadataModifier(item, result, 298);
            AddRawMetadataModifier(item, result, 30);

            bool addedSpellEffects = false;
            if (item.SpellData != null)
            {
                foreach (var group in item.SpellData)
                {
                    if (group?.Items == null)
                        continue;
                    foreach (var spell in group.Items)
                    {
                        if (spell == null || spell.Target != 2 || spell.Stat <= 0 || spell.Amount == 0)
                            continue;
                        result.Add(new DataCore.StatModifier { StatId = spell.Stat, Value = spell.Amount });
                        addedSpellEffects = true;
                    }
                }
            }

            if (!addedSpellEffects && item.StatValues != null)
            {
                foreach (var raw in item.StatValues)
                {
                    if (raw == null || !IsLegacyFallbackBuffStat(raw.Stat))
                        continue;
                    if (raw.RawValue <= -100000000 || raw.RawValue >= 100000000)
                        continue;
                    result.Add(new DataCore.StatModifier { StatId = raw.Stat, Value = raw.RawValue });
                }
            }

            return result;
        }

        private static void AddRawMetadataModifier(DataCore.Item item, List<DataCore.StatModifier> target, int statId)
        {
            if (item?.StatValues == null)
                return;
            var raw = item.StatValues.FirstOrDefault(v => v != null && v.Stat == statId);
            if (raw != null)
                target.Add(new DataCore.StatModifier { StatId = statId, Value = raw.RawValue });
        }

        private static bool IsLegacyFallbackBuffStat(int statId)
        {
            return (statId >= 16 && statId <= 21)
                || (statId >= 90 && statId <= 97)
                || (statId >= 100 && statId <= 168)
                || statId == 180
                || statId == 181
                || statId == 221;
        }

        private static int GetModifierValue(List<DataCore.StatModifier> mods, int statId)
        {
            foreach (var mod in mods)
            {
                if (mod != null && mod.StatId == statId)
                    return mod.Value;
            }
            return 0;
        }

        private static bool IsBackpack(string itemName)
        {
            return !string.IsNullOrWhiteSpace(itemName) && itemName.Contains("backpack", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeStatName(string input)
        {
            return new string(input.Trim().Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-').Select(char.ToLowerInvariant).ToArray());
        }

        private static Dictionary<int, int> ParseIntMap(Dictionary<string, int> source)
        {
            var result = new Dictionary<int, int>();
            if (source == null)
                return result;
            foreach (var pair in source)
            {
                if (int.TryParse(pair.Key, out int id))
                    result[id] = pair.Value;
            }
            return result;
        }

        private static Dictionary<int, int[]> ParseBreedAbilityBlock(Dictionary<string, int[]> source)
        {
            var result = new Dictionary<int, int[]>();
            if (source == null)
                return result;
            foreach (var pair in source)
            {
                if (int.TryParse(pair.Key, out int breedId) && pair.Value != null && pair.Value.Length >= 6)
                    result[breedId] = pair.Value.Take(6).ToArray();
            }
            return result;
        }

        private static List<SkillAllocationRange> ParseSkillAllocationRanges(List<SkillAllocationCostRange> rows)
        {
            var result = new List<SkillAllocationRange>();
            if (rows == null)
                return result;
            foreach (var row in rows)
            {
                if (row == null || !TryParseFloatRange(row.costPer, out float min, out float max))
                    continue;
                var maxByTl = new Dictionary<int, int>();
                if (row.maxCumulativePointsByTL != null)
                {
                    foreach (var kv in row.maxCumulativePointsByTL)
                    {
                        if (int.TryParse(kv.Key, out int tl))
                            maxByTl[tl] = kv.Value;
                    }
                }
                int improvementsPerLevel = row.improvementsPerLevel > 0 ? row.improvementsPerLevel : row.pointsPerLevel;
                result.Add(new SkillAllocationRange { MinCostPer = min, MaxCostPer = max, PointsPerLevel = Math.Max(0, improvementsPerLevel), MaxByTl = maxByTl });
            }
            result.Sort((a, b) => a.MinCostPer.CompareTo(b.MinCostPer));
            return result;
        }

        private static List<TlBracket> ParseSkillAllocationTlBrackets(List<SkillAllocationTitleLevelBracket> rows)
        {
            var result = new List<TlBracket>();
            if (rows == null)
                return result;
            foreach (var row in rows)
            {
                if (row == null || !TryParseIntRange(row.levelRange, out int min, out int max))
                    continue;
                result.Add(new TlBracket { Tl = row.tl, StartLevel = min, EndLevel = max });
            }
            result.Sort((a, b) => a.StartLevel.CompareTo(b.StartLevel));
            return result;
        }

        private static int ResolveAbilityImprovementsPerLevel(SkillAllocationFile file)
        {
            if (file.abilityImprovementsPerLevel.HasValue && file.abilityImprovementsPerLevel.Value > 0)
                return file.abilityImprovementsPerLevel.Value;
            if (file.skillCostRanges != null)
            {
                foreach (var row in file.skillCostRanges)
                {
                    if (row?.abilityImprovementsPerLevel > 0)
                        return row.abilityImprovementsPerLevel.Value;
                }
            }
            return 3;
        }
        private static bool TryParseFloatRange(string raw, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            string cleaned = raw.Trim();
            if (cleaned.Contains('-'))
            {
                string[] parts = cleaned.Split('-', 2);
                if (parts.Length == 2 && float.TryParse(parts[0].Trim(), out min) && float.TryParse(parts[1].Trim(), out max))
                    return true;
            }
            if (float.TryParse(cleaned, out min))
            {
                max = min;
                return true;
            }
            return false;
        }

        private static bool TryParseIntRange(string raw, out int min, out int max)
        {
            min = 0;
            max = 0;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            string cleaned = raw.Trim();
            if (cleaned.Contains('-'))
            {
                string[] parts = cleaned.Split('-', 2);
                if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out min) && int.TryParse(parts[1].Trim(), out max))
                    return true;
            }
            if (int.TryParse(cleaned, out min))
            {
                max = min;
                return true;
            }
            return false;
        }

        private static Dictionary<ulong, List<int>> LoadSlotBitMap(string path)
        {
            var result = new Dictionary<ulong, List<int>>();
            if (!File.Exists(path))
                return result;
            string json = File.ReadAllText(path);
            List<SlotBitEntry> entries = null;
            try
            {
                entries = JsonSerializer.Deserialize<List<SlotBitEntry>>(json);
            }
            catch
            {
            }

            if (entries == null || entries.Count == 0)
            {
                var lookup = JsonSerializer.Deserialize<SlotLookupFile>(json);
                if (lookup?.bitsById != null)
                {
                    entries = new List<SlotBitEntry>();
                    foreach (var pair in lookup.bitsById)
                    {
                        if (int.TryParse(pair.Key, out int id))
                            entries.Add(new SlotBitEntry { Id = id, Bit = pair.Value });
                    }
                }
            }

            if (entries == null)
                return result;
            foreach (var entry in entries)
            {
                if (entry == null || entry.Bit == 0 || entry.Id <= 0)
                    continue;
                if (!result.TryGetValue(entry.Bit, out var slots))
                {
                    slots = new List<int>();
                    result[entry.Bit] = slots;
                }
                slots.Add(entry.Id);
            }
            return result;
        }

        private static bool TryMapAbilityStatToIndex(int statId, out int idx)
        {
            idx = statId switch
            {
                16 => 0,
                17 => 1,
                18 => 2,
                19 => 3,
                20 => 4,
                21 => 5,
                _ => -1
            };
            return idx >= 0;
        }

        private static void CopyInto<TKey, TValue>(IDictionary<TKey, TValue> target, IDictionary<TKey, TValue> source)
        {
            target.Clear();
            foreach (var pair in source)
                target[pair.Key] = pair.Value;
        }

        private sealed class SlotBitEntry
        {
            public int Id { get; set; }
            public ulong Bit { get; set; }
        }

        private sealed class SlotLookupFile
        {
            public Dictionary<string, ulong> bitsById { get; set; } = new();
        }

        private sealed class IpTitleLevelProgressionFile
        {
            public List<IpTitleLevelBracket> tlBrackets { get; set; } = new();
        }

        private sealed class XpNeededToLevelFile
        {
            public Dictionary<string, long> experienceToNextLevel { get; set; } = new();
        }

        private sealed class BreedAbilityDataFile
        {
            public Dictionary<string, int[]> initial { get; set; } = new();
            public Dictionary<string, int[]> caps_pre201 { get; set; } = new();
            public Dictionary<string, int[]> caps_post201_per_level { get; set; } = new();
            public Dictionary<string, int[]> cost_factors { get; set; } = new();
        }

        private sealed class BreedStatsFile
        {
            public Dictionary<string, int> base_hp { get; set; } = new();
            public Dictionary<string, int> hp_per_level { get; set; } = new();
            public Dictionary<string, int> base_np { get; set; } = new();
            public Dictionary<string, int> np_per_level { get; set; } = new();
            public Dictionary<string, int> body_factor { get; set; } = new();
            public Dictionary<string, int> nano_factor { get; set; } = new();
        }

        private sealed class SkillAllocationFile
        {
            public List<SkillAllocationTitleLevelBracket> titleLevelBrackets { get; set; } = new();
            public List<SkillAllocationCostRange> skillCostRanges { get; set; } = new();
            public int? abilityImprovementsPerLevel { get; set; }
        }

        private sealed class SkillAllocationTitleLevelBracket
        {
            public int tl { get; set; }
            public string levelRange { get; set; }
        }

        private sealed class SkillAllocationCostRange
        {
            public string costPer { get; set; }
            public int improvementsPerLevel { get; set; }
            public int pointsPerLevel { get; set; }
            public int? abilityImprovementsPerLevel { get; set; }
            public Dictionary<string, int> maxCumulativePointsByTL { get; set; } = new();
        }

        private sealed class SkillAllocationRange
        {
            public float MinCostPer { get; set; }
            public float MaxCostPer { get; set; }
            public int PointsPerLevel { get; set; }
            public Dictionary<int, int> MaxByTl { get; set; } = new();
        }

        private sealed class TlBracket
        {
            public int Tl { get; set; }
            public int StartLevel { get; set; }
            public int EndLevel { get; set; }
        }

        private sealed class IpTitleLevelBracket
        {
            public int tl { get; set; }
            public int startLevel { get; set; }
            public int? endLevel { get; set; }
            public int? ipStartingBonus { get; set; }
            public int ipPerLevel { get; set; }
        }
    }
}




