using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using Newtonsoft.Json;
using UnityEngine;
using AO.Core.Characters;
using AO.Core.Modifiers;
using AO.Core.Stats;
using DataCore = AO.Data.Core;
using CoreItems = AO.Core.Items;

namespace AO.Data.Unity
{
    public class AODataManager : MonoBehaviour
    {
        public sealed class ItemTextureApplication
        {
            public int TextureId { get; set; }
            public int LocationId { get; set; }
            public string LocationName { get; set; } = string.Empty;
        }

        private const int ItemClassStatId = 76;
        private const int IconStatId = 79;
        private const int DefaultSlotStatId = 88;

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

        private sealed class IpTitleLevelBracket
        {
            public int tl { get; set; }
            public int startLevel { get; set; }
            public int? endLevel { get; set; }
            public int? ipStartingBonus { get; set; }
            public int ipPerLevel { get; set; }
        }

        private sealed class BreedAbilityDataFile
        {
            public Dictionary<string, int[]> initial { get; set; } = new();
            public Dictionary<string, int[]> caps_pre201 { get; set; } = new();
            public Dictionary<string, int[]> caps_post201_per_level { get; set; } = new();
            public Dictionary<string, int[]> cost_factors { get; set; } = new();
            public Dictionary<string, string> breed_ability_colors { get; set; } = new();
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
            public string color { get; set; }
            public int improvementsPerLevel { get; set; }
            public int pointsPerLevel { get; set; }
            public int? abilityImprovementsPerLevel { get; set; }
            public Dictionary<string, int> maxCumulativePointsByTL { get; set; } = new();
        }

        private sealed class ParsedSkillAllocationRange
        {
            public float MinCostPer { get; set; }
            public float MaxCostPer { get; set; }
            public string Color { get; set; }
            public int PointsPerLevel { get; set; }
            public Dictionary<int, int> MaxByTl { get; set; } = new();
        }

        private sealed class ParsedTlBracket
        {
            public int Tl { get; set; }
            public int StartLevel { get; set; }
            public int EndLevel { get; set; }
        }

        private sealed class SkillTrickleDownFile : Dictionary<string, float[]>
        {
        }

        private Dictionary<int, CoreItems.ItemDefinition> _coreDefs = new();
        private Dictionary<long, CoreItems.ItemInstance> _coreInstances = new();
        private Dictionary<long, DataCore.ItemInstance> _dataInstances = new();
        private Dictionary<ulong, List<int>> _weaponSlotBits = new();
        private Dictionary<ulong, List<int>> _armorSlotBits = new();
        private Dictionary<ulong, List<int>> _implantSlotBits = new();
        private Dictionary<string, int> _statIdByName = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<int, Dictionary<int, float>> _skillCostFactorsByProfession = new();
        private List<IpTitleLevelBracket> _ipTitleLevelBrackets = new();
        private Dictionary<int, long> _xpNeededToLevel = new();
        private Dictionary<int, int[]> _breedAbilityInitial = new();
        private Dictionary<int, int[]> _breedAbilityCapsPre201 = new();
        private Dictionary<int, int[]> _breedAbilityCapsPost201PerLevel = new();
        private Dictionary<int, int[]> _breedAbilityCostFactors = new();
        private Dictionary<int, string> _breedAbilityColors = new();
        private Dictionary<int, int> _breedBaseHp = new();
        private Dictionary<int, int> _breedHpPerLevel = new();
        private Dictionary<int, int> _breedBaseNp = new();
        private Dictionary<int, int> _breedNpPerLevel = new();
        private Dictionary<int, int> _breedBodyFactor = new();
        private Dictionary<int, int> _breedNanoFactor = new();
        private List<ParsedSkillAllocationRange> _skillAllocationRanges = new();
        private List<ParsedTlBracket> _skillAllocationTlBrackets = new();
        private int _abilityImprovementsPerLevel = 3;
        private readonly Dictionary<int, string> _breedNames = new();
        private readonly Dictionary<int, string> _professionNames = new();
        private Dictionary<int, float[]> _skillTrickleByStat = new();
        private readonly Dictionary<int, string> _textureLocationNames = new();
        private readonly Dictionary<int, DataCore.Item> _rawItemsByAoid = new();
        private readonly Dictionary<int, DataCore.Item> _nanosByAoid = new();
        private readonly Dictionary<int, ItemTextureApplication[]> _itemTextureApplicationsByAoid = new();

        public List<DataCore.Item> Items { get; private set; } = new();
        public List<DataCore.NanoProgram> Nanos { get; private set; } = new();
        public List<DataCore.Item> NanoItems { get; private set; } = new();
        public DataCore.StatRegistry StatMap { get; private set; } = new();
        public IReadOnlyCollection<DataCore.ItemInstance> ItemInstances => _dataInstances.Values;

        public static AODataManager Instance { get; private set; }

        public static void EnsureInstance()
        {
            if (Instance != null) return;
            var go = new GameObject("AODataManager");
            Instance = go.AddComponent<AODataManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            LoadStatMap();
            LoadIpProgression();
            LoadXpNeededToLevel();
            LoadProfessionSkillCostFactors();
            LoadBreedAbilityData();
            LoadBreedStats();
            LoadSkillAllocationAndColor();
            LoadSkillTrickleDown();
            LoadLookupNameMap("breed.json", _breedNames);
            LoadLookupNameMap("profession.json", _professionNames);
            LoadTextureLocations();
            LoadEquipmentSlotBitmaps();
            LoadItems();
            LoadNanos();
            BootstrapDelegates();
        }

        private void LoadTextureLocations()
        {
            _textureLocationNames.Clear();
            string path = Path.Combine(Application.streamingAssetsPath, "AOData", "texture_locations.json");
            if (!File.Exists(path))
                return;

            try
            {
                var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (raw == null)
                    return;

                foreach (var pair in raw)
                {
                    if (!int.TryParse(pair.Key, out int id))
                        continue;

                    _textureLocationNames[id] = pair.Value ?? id.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading texture_locations.json: {ex.Message}");
            }
        }

        private void LoadLookupNameMap(string fileName, Dictionary<int, string> target)
        {
            target.Clear();
            string path = Path.Combine(Application.streamingAssetsPath, "AOData", fileName);
            if (!File.Exists(path))
                return;

            try
            {
                var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                if (raw == null)
                    return;

                foreach (var pair in raw)
                {
                    if (!int.TryParse(pair.Key, out int id))
                        continue;

                    target[id] = pair.Value ?? id.ToString();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading {fileName}: {ex.Message}");
            }
        }

        private void LoadStatMap()
        {
            // Stat IDs are part of the client domain model. Seed them from the
            // compiled catalog so runtime stat storage and requirement checks do
            // not depend on a migrated statmap.json export.
            StatMap = new DataCore.StatRegistry();
            _statIdByName.Clear();
            foreach (var field in typeof(StatIds).GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static))
            {
                if (field.FieldType != typeof(StatId)
                    || !(field.GetValue(null) is StatId statId))
                    continue;

                string name = field.Name;
                StatMap.Register(statId.Value, name);
                _statIdByName[NormalizeStatName(name)] = statId.Value;
            }

            string path = Path.Combine(Application.streamingAssetsPath, "AOData/statmap.json");
            if (!File.Exists(path))
            {
                Debug.Log("statmap.json missing; using the built-in AO stat catalog.");
                return;
            }

            string json = File.ReadAllText(path);

            // Supports both legacy array format and current object-map format.
            List<DataCore.StatMapEntry> entries = null;
            try
            {
                entries = JsonConvert.DeserializeObject<List<DataCore.StatMapEntry>>(json);
            }
            catch
            {
                // Ignore and try object-map format below.
            }

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

            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            if (dict == null || dict.Count == 0)
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
            string path = Path.Combine(Application.streamingAssetsPath, "AOData/title_level_ip_progression.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("title_level_ip_progression.json missing");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<IpTitleLevelProgressionFile>(json);
                _ipTitleLevelBrackets = file?.tlBrackets?
                    .OrderBy(b => b.startLevel)
                    .ToList() ?? new List<IpTitleLevelBracket>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading title_level_ip_progression.json: {ex.Message}");
                _ipTitleLevelBrackets = new List<IpTitleLevelBracket>();
            }
        }

        private void LoadProfessionSkillCostFactors()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "AOData/profession_skill_cost_factors.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("profession_skill_cost_factors.json missing");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var raw = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, float>>>(json);
                _skillCostFactorsByProfession = new Dictionary<int, Dictionary<int, float>>();

                if (raw == null)
                    return;

                foreach (var statRow in raw)
                {
                    if (!int.TryParse(statRow.Key, out int statId))
                        continue;

                    if (statRow.Value == null)
                        continue;

                    foreach (var profRow in statRow.Value)
                    {
                        if (!int.TryParse(profRow.Key, out int professionId))
                            continue;

                        if (!_skillCostFactorsByProfession.TryGetValue(professionId, out var statMap))
                        {
                            statMap = new Dictionary<int, float>();
                            _skillCostFactorsByProfession[professionId] = statMap;
                        }

                        statMap[statId] = profRow.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading profession_skill_cost_factors.json: {ex.Message}");
                _skillCostFactorsByProfession = new Dictionary<int, Dictionary<int, float>>();
            }
        }

        private void LoadBreedAbilityData()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "AOData/breed_ability_data.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("breed_ability_data.json missing");
                return;
            }

            try
            {
                var file = JsonConvert.DeserializeObject<BreedAbilityDataFile>(File.ReadAllText(path));
                _breedAbilityInitial = ParseBreedAbilityBlock(file?.initial);
                _breedAbilityCapsPre201 = ParseBreedAbilityBlock(file?.caps_pre201);
                _breedAbilityCapsPost201PerLevel = ParseBreedAbilityBlock(file?.caps_post201_per_level);
                _breedAbilityCostFactors = ParseBreedAbilityBlock(file?.cost_factors);
                _breedAbilityColors = ParseAbilityColorMap(file?.breed_ability_colors);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading breed_ability_data.json: {ex.Message}");
                _breedAbilityInitial = new Dictionary<int, int[]>();
                _breedAbilityCapsPre201 = new Dictionary<int, int[]>();
                _breedAbilityCapsPost201PerLevel = new Dictionary<int, int[]>();
                _breedAbilityCostFactors = new Dictionary<int, int[]>();
                _breedAbilityColors = new Dictionary<int, string>();
            }
        }

        private void LoadXpNeededToLevel()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "AOData/xp_needed_to_level.json");
            if (!File.Exists(path))
                path = Path.Combine(Application.streamingAssetsPath, "AOData/xp_needed_to_level");

            if (!File.Exists(path))
            {
                Debug.LogWarning("xp_needed_to_level missing");
                return;
            }

            try
            {
                var file = JsonConvert.DeserializeObject<XpNeededToLevelFile>(File.ReadAllText(path));
                _xpNeededToLevel = new Dictionary<int, long>();

                if (file?.experienceToNextLevel == null)
                    return;

                foreach (var pair in file.experienceToNextLevel)
                {
                    if (!int.TryParse(pair.Key, out int level))
                        continue;

                    _xpNeededToLevel[level] = Math.Max(0L, pair.Value);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading xp_needed_to_level: {ex.Message}");
                _xpNeededToLevel = new Dictionary<int, long>();
            }
        }

        private static Dictionary<int, int> ParseIntMap(Dictionary<string, int> source)
        {
            var result = new Dictionary<int, int>();
            if (source == null)
                return result;

            foreach (var pair in source)
            {
                if (!int.TryParse(pair.Key, out int id))
                    continue;

                result[id] = pair.Value;
            }

            return result;
        }

        private void LoadBreedStats()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "AOData/breed_stats.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("breed_stats.json missing");
                return;
            }

            try
            {
                var file = JsonConvert.DeserializeObject<BreedStatsFile>(File.ReadAllText(path));
                _breedBaseHp = ParseIntMap(file?.base_hp);
                _breedHpPerLevel = ParseIntMap(file?.hp_per_level);
                _breedBaseNp = ParseIntMap(file?.base_np);
                _breedNpPerLevel = ParseIntMap(file?.np_per_level);
                _breedBodyFactor = ParseIntMap(file?.body_factor);
                _breedNanoFactor = ParseIntMap(file?.nano_factor);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading breed_stats.json: {ex.Message}");
                _breedBaseHp = new Dictionary<int, int>();
                _breedHpPerLevel = new Dictionary<int, int>();
                _breedBaseNp = new Dictionary<int, int>();
                _breedNpPerLevel = new Dictionary<int, int>();
                _breedBodyFactor = new Dictionary<int, int>();
                _breedNanoFactor = new Dictionary<int, int>();
            }
        }

        private static Dictionary<int, string> ParseAbilityColorMap(Dictionary<string, string> source)
        {
            var result = new Dictionary<int, string>();
            if (source == null)
                return result;

            foreach (var pair in source)
            {
                if (!int.TryParse(pair.Key, out int costFactor))
                    continue;
                result[costFactor] = pair.Value ?? string.Empty;
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
                if (!int.TryParse(pair.Key, out int breedId))
                    continue;

                if (pair.Value == null || pair.Value.Length < 6)
                    continue;

                result[breedId] = pair.Value.Take(6).ToArray();
            }

            return result;
        }

        private void LoadSkillAllocationAndColor()
        {
            string[] candidateFiles =
            {
                "skill_caps_and_color.json",
                "skill_cap_and_color.json",
                "skill_allocation_and_color.json"
            };

            string path = null;
            foreach (var fileName in candidateFiles)
            {
                string candidate = Path.Combine(Application.streamingAssetsPath, "AOData", fileName);
                if (!File.Exists(candidate))
                    continue;

                path = candidate;
                break;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning("Skill caps config missing. Expected one of: skill_caps_and_color.json, skill_cap_and_color.json, skill_allocation_and_color.json");
                return;
            }

            try
            {
                var file = JsonConvert.DeserializeObject<SkillAllocationFile>(File.ReadAllText(path));
                _skillAllocationRanges = ParseSkillAllocationRanges(file?.skillCostRanges);
                _skillAllocationTlBrackets = ParseSkillAllocationTlBrackets(file?.titleLevelBrackets);
                _abilityImprovementsPerLevel = ResolveAbilityImprovementsPerLevel(file);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading skill caps config ({Path.GetFileName(path)}): {ex.Message}");
                _skillAllocationRanges = new List<ParsedSkillAllocationRange>();
                _skillAllocationTlBrackets = new List<ParsedTlBracket>();
                _abilityImprovementsPerLevel = 3;
            }
        }

        private void LoadSkillTrickleDown()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "AOData/skill_trickle_down.json");
            _skillTrickleByStat = new Dictionary<int, float[]>();

            if (!File.Exists(path))
            {
                Debug.LogWarning("skill_trickle_down.json missing");
                return;
            }

            try
            {
                var raw = JsonConvert.DeserializeObject<SkillTrickleDownFile>(File.ReadAllText(path));
                if (raw == null)
                    return;

                foreach (var pair in raw)
                {
                    if (!int.TryParse(pair.Key, out int statId))
                        continue;

                    if (pair.Value == null || pair.Value.Length < 6)
                        continue;

                    _skillTrickleByStat[statId] = pair.Value.Take(6).ToArray();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading skill_trickle_down.json: {ex.Message}");
                _skillTrickleByStat = new Dictionary<int, float[]>();
            }
        }

        private static int ResolveAbilityImprovementsPerLevel(SkillAllocationFile file)
        {
            if (file?.abilityImprovementsPerLevel.HasValue == true && file.abilityImprovementsPerLevel.Value > 0)
                return file.abilityImprovementsPerLevel.Value;

            if (file?.skillCostRanges != null)
            {
                foreach (var row in file.skillCostRanges)
                {
                    if (row?.abilityImprovementsPerLevel.HasValue == true && row.abilityImprovementsPerLevel.Value > 0)
                        return row.abilityImprovementsPerLevel.Value;
                }
            }

            return 3;
        }

        private static List<ParsedTlBracket> ParseSkillAllocationTlBrackets(List<SkillAllocationTitleLevelBracket> source)
        {
            var result = new List<ParsedTlBracket>();
            if (source == null)
                return result;

            foreach (var row in source)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.levelRange))
                    continue;

                var parts = row.levelRange.Split('-');
                if (parts.Length != 2)
                    continue;

                if (!int.TryParse(parts[0], out int start))
                    continue;
                if (!int.TryParse(parts[1], out int end))
                    continue;

                result.Add(new ParsedTlBracket
                {
                    Tl = Math.Max(1, row.tl),
                    StartLevel = Math.Max(1, start),
                    EndLevel = Math.Max(start, end)
                });
            }

            result.Sort((a, b) => a.StartLevel.CompareTo(b.StartLevel));
            return result;
        }

        private static List<ParsedSkillAllocationRange> ParseSkillAllocationRanges(List<SkillAllocationCostRange> source)
        {
            var result = new List<ParsedSkillAllocationRange>();
            if (source == null)
                return result;

            foreach (var row in source)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.costPer))
                    continue;

                var parts = row.costPer.Split('-');
                if (parts.Length != 2)
                    continue;

                if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float min))
                    continue;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float max))
                    continue;

                var maxByTl = new Dictionary<int, int>();
                if (row.maxCumulativePointsByTL != null)
                {
                    foreach (var kv in row.maxCumulativePointsByTL)
                    {
                        if (!int.TryParse(kv.Key, out int tl))
                            continue;
                        maxByTl[tl] = kv.Value;
                    }
                }

                int improvementsPerLevel = row.improvementsPerLevel > 0
                    ? row.improvementsPerLevel
                    : row.pointsPerLevel;

                result.Add(new ParsedSkillAllocationRange
                {
                    MinCostPer = min,
                    MaxCostPer = max,
                    Color = row.color ?? string.Empty,
                    PointsPerLevel = Math.Max(0, improvementsPerLevel),
                    MaxByTl = maxByTl
                });
            }

            result.Sort((a, b) => a.MinCostPer.CompareTo(b.MinCostPer));
            return result;
        }

        private void LoadItems()
        {
            // Server-owned items are registered lazily from ItemObject records in
            // the configured AO ResourceDatabase. Avoid parsing the old 400+ MB
            // export on startup; it was both slow and easy to mismatch with the
            // selected server/client version.
            Items = new List<DataCore.Item>();
            _coreDefs.Clear();
            _coreInstances.Clear();
            _dataInstances.Clear();
            _rawItemsByAoid.Clear();
            _itemTextureApplicationsByAoid.Clear();
            Debug.Log("Item definitions will be resolved lazily from the configured AO ResourceDatabase.");
        }

        public void RegisterRuntimeItem(DataCore.Item item, params int[] aliases)
        {
            if (item == null || item.AOID <= 0)
                return;

                var derivedMods = BuildModifiers(item);
                int itemClass = GetModifierValue(derivedMods, ItemClassStatId);
                int iconId = GetModifierValue(derivedMods, IconStatId);
                int defaultSlot = GetModifierValue(derivedMods, DefaultSlotStatId);

                // Core ItemDefinition
                var def = new CoreItems.ItemDefinition(item.Name ?? string.Empty, item.AOID, itemClass);

                // Add Core StatModifiers
                if (derivedMods != null)
                {
                    foreach (var sm in derivedMods)
                        def.AddModifier(new AO.Core.Modifiers.StatModifier(sm.StatId, sm.Value));
                }

                _coreDefs[item.AOID] = def;

                // Core ItemInstance
                int containerCapacity = IsBackpack(item.Name) ? 21 : 0;
                var inst = new CoreItems.ItemInstance(def, 1, item.AOID, containerCapacity);
                _coreInstances[item.AOID] = inst;

                // Data ItemDefinition/Instance compatibility for systems still expecting AO.Data.Core instances
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

                var dataInst = new DataCore.ItemInstance
                {
                    InstanceId = item.AOID,
                    DefinitionId = item.AOID,
                    Definition = dataDef,
                    Quantity = 1
                };

                _dataInstances[item.AOID] = dataInst;
                _rawItemsByAoid[item.AOID] = item;
                _itemTextureApplicationsByAoid[item.AOID] = BuildTextureApplications(item);
                if (Items != null && !Items.Any(existing => existing?.AOID == item.AOID))
                    Items.Add(item);

                if (aliases == null)
                    return;
                foreach (int alias in aliases)
                {
                    if (alias <= 0 || alias == item.AOID)
                        continue;
                    _coreDefs[alias] = def;
                    _coreInstances[alias] = inst;
                    _dataInstances[alias] = dataInst;
                    _rawItemsByAoid[alias] = item;
                    _itemTextureApplicationsByAoid[alias] = _itemTextureApplicationsByAoid[item.AOID];
                }
        }

        public void RegisterRuntimeInstance(
            CoreItems.ItemDefinition definition,
            long instanceId,
            int quantity = 1,
            int containerCapacity = 0)
        {
            if (definition == null || instanceId <= 0)
                return;

            quantity = Math.Max(1, quantity);
            _coreInstances[instanceId] = new CoreItems.ItemInstance(
                definition, quantity, instanceId, Math.Max(0, containerCapacity));

            if (_dataInstances.TryGetValue(definition.AOID, out var template) && template?.Definition != null)
            {
                _dataInstances[instanceId] = new DataCore.ItemInstance
                {
                    // AO.Data.Core retains a 32-bit identity field, while locally
                    // synthesized runtime identities may use the wider Core key.
                    InstanceId = instanceId >= int.MinValue && instanceId <= int.MaxValue
                        ? (int)instanceId
                        : definition.AOID,
                    DefinitionId = definition.AOID,
                    Definition = template.Definition,
                    Quantity = quantity
                };
            }
        }

        private void LoadNanos()
        {
            _nanosByAoid.Clear();
            NanoItems = new List<DataCore.Item>();

            string path = Path.Combine(Application.streamingAssetsPath, "AOData/nanos.json");
            if (!File.Exists(path))
            {
                Debug.LogWarning("nanos.json missing");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                var nanos = JsonConvert.DeserializeObject<List<DataCore.Item>>(json);
                if (nanos == null)
                    return;

                foreach (var nano in nanos)
                {
                    if (nano == null || nano.AOID <= 0)
                        continue;

                    NanoItems.Add(nano);
                    _nanosByAoid[nano.AOID] = nano;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed loading nanos.json: {ex.Message}");
                NanoItems = new List<DataCore.Item>();
                _nanosByAoid.Clear();
            }
        }

        private ItemTextureApplication[] BuildTextureApplications(DataCore.Item item)
        {
            if (item?.SpellData == null || item.SpellData.Count == 0)
                return Array.Empty<ItemTextureApplication>();

            var list = new List<ItemTextureApplication>();
            foreach (var group in item.SpellData)
            {
                if (group?.Items == null)
                    continue;

                foreach (var spell in group.Items)
                {
                    if (spell == null)
                        continue;
                    if (spell.Target != 2 || spell.Texture <= 0 || spell.Location < 0)
                        continue;

                    list.Add(new ItemTextureApplication
                    {
                        TextureId = spell.Texture,
                        LocationId = spell.Location,
                        LocationName = _textureLocationNames.TryGetValue(spell.Location, out var locationName)
                            ? (locationName ?? spell.Location.ToString())
                            : spell.Location.ToString()
                    });
                }
            }

            return list.ToArray();
        }

        private static bool IsBackpack(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return false;
            string normalized = itemName.Trim().ToLowerInvariant();
            return normalized.Contains("backpack")
                || normalized.Contains("survival pack");
        }

        private static List<DataCore.StatModifier> BuildModifiers(DataCore.Item item)
        {
            var result = new List<DataCore.StatModifier>();
            if (item == null)
                return result;

            // Always keep core metadata from raw stats for slot/class/icon resolution.
            AddRawMetadataModifier(item, result, ItemClassStatId);
            AddRawMetadataModifier(item, result, IconStatId);
            AddRawMetadataModifier(item, result, DefaultSlotStatId);
            AddRawMetadataModifier(item, result, 298); // Equip slot bitmap
            AddRawMetadataModifier(item, result, 30);  // Can flags (special attacks/use flags)
            AddRawMetadataModifier(item, result, 294); // AttackDelay
            AddRawMetadataModifier(item, result, 210); // RechargeDelay
            AddRawMetadataModifier(item, result, 3);   // AttackSpeed (fallback)

            // Gameplay buffs should come from spell effects applied to user (Target=2).
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

                        result.Add(new DataCore.StatModifier
                        {
                            StatId = spell.Stat,
                            Value = spell.Amount
                        });
                        addedSpellEffects = true;
                    }
                }
            }

            // Fallback for items without spell-based modifiers.
            if (!addedSpellEffects && item.StatValues != null)
            {
                foreach (var raw in item.StatValues)
                {
                    if (raw == null)
                        continue;
                    if (!IsLegacyFallbackBuffStat(raw.Stat))
                        continue;

                    // Ignore sentinel/unbounded values that are not meaningful for gameplay checks.
                    if (raw.RawValue <= -100000000 || raw.RawValue >= 100000000)
                        continue;

                    result.Add(new DataCore.StatModifier
                    {
                        StatId = raw.Stat,
                        Value = raw.RawValue
                    });
                }
            }

            return result;
        }

        private static void AddRawMetadataModifier(DataCore.Item item, List<DataCore.StatModifier> target, int statId)
        {
            if (item?.StatValues == null || target == null)
                return;

            var raw = item.StatValues.FirstOrDefault(v => v != null && v.Stat == statId);
            if (raw == null)
                return;

            target.Add(new DataCore.StatModifier
            {
                StatId = statId,
                Value = raw.RawValue
            });
        }

        private static bool IsLegacyFallbackBuffStat(int statId)
        {
            // Conservative fallback only for obvious character stat modifiers.
            return (statId >= 16 && statId <= 21)   // abilities
                || (statId >= 90 && statId <= 97)   // ACs
                || (statId >= 100 && statId <= 168) // skills
                || statId == 180 || statId == 181   // NCU
                || statId == 221;                   // max nano
        }

        private static int GetModifierValue(List<DataCore.StatModifier> mods, int statId)
        {
            if (mods == null) return 0;

            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (mod == null) continue;
                if (mod.StatId == statId) return mod.Value;
            }

            return 0;
        }

        private void BootstrapDelegates()
        {
            CharacterEquipment.GetItemInstance = id =>
                _coreInstances.TryGetValue(id, out var inst) ? inst : null;

            CharacterEquipment.GetItemDefinition = id =>
                _coreDefs.TryGetValue(id, out var def) ? def : null;

            ModifierAggregator.GetItemInstance = CharacterEquipment.GetItemInstance;
            ModifierAggregator.GetItemDefinition = CharacterEquipment.GetItemDefinition;
            Character.GetItemInstance = id => GetItemInstance(id);
            Character.StatIdLookup = ResolveStatId;
            Character.GetStartingIpBonus = GetStartingIpBonus;
            Character.GetTitleLevelForLevel = GetTitleLevelForLevel;
            Character.GetIpPerLevelForLevel = GetIpPerLevelForLevel;
            Character.GetExperienceToNextLevelForLevel = GetExperienceToNextLevelForLevel;
            Character.GetSkillCostFactor = GetSkillCostFactor;
            Character.GetStatCapForLevel = GetStatCapForLevel;
            Character.GetStartingBaseStatForBreed = GetStartingBaseStatForBreed;
            Character.GetAbilityCostFactorForBreed = GetAbilityCostFactorForBreed;
            CharacterStats.GetSkillTrickleWeights = ResolveSkillTrickleWeights;
            CharacterStats.GetMaxHealthForBreedLevelAndBodyDev = ResolveMaxHealthForBreedLevelAndBodyDev;
            CharacterStats.GetMaxNanoForBreedLevelAndNanoPool = ResolveMaxNanoForBreedLevelAndNanoPool;
            EquipmentValidator.ResolveSlotsByBitmap = ResolveAllowedSlotsByBitmap;
        }

        private IReadOnlyList<float> ResolveSkillTrickleWeights(int statId)
        {
            if (_skillTrickleByStat.TryGetValue(statId, out var weights) && weights != null && weights.Length >= 6)
                return weights;

            return null;
        }

        private int ResolveStatId(string statName)
        {
            if (string.IsNullOrWhiteSpace(statName))
                return -1;

            if (_statIdByName.TryGetValue(NormalizeStatName(statName), out int id))
                return id;

            const string fallbackPrefix = "stat ";
            if (statName.StartsWith(fallbackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = statName.Substring(fallbackPrefix.Length).Trim();
                if (int.TryParse(suffix, out int parsed))
                    return parsed;
            }

            return -1;
        }

        private static string NormalizeStatName(string input)
        {
            var chars = input
                .Trim()
                .Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-')
                .Select(char.ToLowerInvariant)
                .ToArray();
            return new string(chars);
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
            if (bracket != null)
                return Math.Max(1, bracket.tl);

            return Math.Max(1, _ipTitleLevelBrackets[_ipTitleLevelBrackets.Count - 1].tl);
        }

        private int GetIpPerLevelForLevel(int level)
        {
            if (level <= 0 || _ipTitleLevelBrackets.Count == 0)
                return 0;

            var bracket = _ipTitleLevelBrackets.FirstOrDefault(b => level >= b.startLevel && (!b.endLevel.HasValue || level <= b.endLevel.Value));
            if (bracket != null)
                return Math.Max(0, bracket.ipPerLevel);

            return Math.Max(0, _ipTitleLevelBrackets[_ipTitleLevelBrackets.Count - 1].ipPerLevel);
        }

        private long GetExperienceToNextLevelForLevel(int level)
        {
            if (level <= 0)
                return 0;

            return _xpNeededToLevel.TryGetValue(level, out long needed)
                ? Math.Max(0L, needed)
                : 0;
        }

        private float? GetSkillCostFactor(int professionId, int statId)
        {
            if (professionId <= 0 || statId <= 0)
                return null;

            if (!_skillCostFactorsByProfession.TryGetValue(professionId, out var byStat))
                return null;

            return byStat.TryGetValue(statId, out float factor)
                ? factor
                : null;
        }

        private int? GetStartingBaseStatForBreed(int statId, int breedId)
        {
            if (!TryMapAbilityStatToIndex(statId, out int idx))
                return null;

            if (!_breedAbilityInitial.TryGetValue(breedId, out var row) || idx < 0 || idx >= row.Length)
                return null;

            return row[idx];
        }

        private int? ResolveMaxHealthForBreedLevelAndBodyDev(int breedId, int level, int bodyDevelopmentBuffed)
        {
            if (!_breedBaseHp.TryGetValue(breedId, out int baseHp))
                return null;
            if (!_breedHpPerLevel.TryGetValue(breedId, out int hpPerLevel))
                return null;
            if (!_breedBodyFactor.TryGetValue(breedId, out int bodyFactor))
                return null;

            int safeLevel = Math.Max(1, level);
            int safeBodyDev = Math.Max(0, bodyDevelopmentBuffed);
            int value = (safeBodyDev * bodyFactor) + (safeLevel * hpPerLevel) + baseHp;
            return Math.Max(1, value);
        }

        private int? ResolveMaxNanoForBreedLevelAndNanoPool(int breedId, int level, int nanoPoolBuffed)
        {
            if (!_breedBaseNp.TryGetValue(breedId, out int baseNp))
                return null;
            if (!_breedNpPerLevel.TryGetValue(breedId, out int npPerLevel))
                return null;
            if (!_breedNanoFactor.TryGetValue(breedId, out int nanoFactor))
                return null;

            int safeLevel = Math.Max(1, level);
            int safeNanoPool = Math.Max(0, nanoPoolBuffed);
            int value = (safeNanoPool * nanoFactor) + (safeLevel * npPerLevel) + baseNp;
            return Math.Max(1, value);
        }

        private int? GetStatCapForLevel(int statId, int level, int breedId, int professionId)
        {
            int? allocCap = GetSkillAllocationCap(statId, level, breedId, professionId);

            if (!TryMapAbilityStatToIndex(statId, out int idx))
                return allocCap;

            if (!_breedAbilityCapsPre201.TryGetValue(breedId, out var pre201) || idx < 0 || idx >= pre201.Length)
                return allocCap;

            int hardCap = pre201[idx];
            if (level > 200 && _breedAbilityCapsPost201PerLevel.TryGetValue(breedId, out var post201) && idx < post201.Length)
                hardCap += (level - 200) * post201[idx];

            if (!allocCap.HasValue)
                return Math.Max(0, hardCap);

            return Math.Max(0, Math.Min(hardCap, allocCap.Value));
        }

        private int? GetSkillAllocationCap(int statId, int level, int breedId, int professionId)
        {
            if (level <= 0 || _skillAllocationRanges.Count == 0)
                return null;

            float? costPer = GetSkillCostPer(statId, breedId, professionId);
            if (!costPer.HasValue)
                return null;

            var range = FindSkillAllocationRange(costPer.Value);
            if (range == null)
                return null;

            var tl = ResolveTlForLevel(level);
            if (tl == null)
                return null;

            int currentTlMax = 0;
            if (range.MaxByTl.TryGetValue(tl.Tl, out int curMax))
                currentTlMax = curMax;

            int improvementsPerLevel = range.PointsPerLevel;
            if (TryMapAbilityStatToIndex(statId, out _))
                improvementsPerLevel = Math.Max(1, _abilityImprovementsPerLevel);

            // Per-level allowance is global by character level.
            // TL max is an upper ceiling, not a baseline carry value.
            int levelBasedAllowance = Math.Max(0, level) * improvementsPerLevel;
            int allowedPoints = Math.Min(currentTlMax, levelBasedAllowance);

            int baseAtCreation = GetStartingBaseStatForBreed(statId, breedId) ?? 5;
            return Math.Max(0, baseAtCreation + allowedPoints);
        }

        private int? GetAbilityCostFactorForBreed(int statId, int breedId)
        {
            if (!TryMapAbilityStatToIndex(statId, out int idx))
                return null;

            if (!_breedAbilityCostFactors.TryGetValue(breedId, out var row) || idx < 0 || idx >= row.Length)
                return null;

            int factor = row[idx];
            return factor > 0 ? factor : null;
        }

        private float? GetSkillCostPer(int statId, int breedId, int professionId)
        {
            if (TryMapAbilityStatToIndex(statId, out _))
            {
                int? abilityCost = GetAbilityCostFactorForBreed(statId, breedId);
                return abilityCost.HasValue ? abilityCost.Value : null;
            }

            return GetSkillCostFactor(professionId, statId);
        }

        private ParsedSkillAllocationRange FindSkillAllocationRange(float costPer)
        {
            foreach (var range in _skillAllocationRanges)
            {
                if (costPer >= range.MinCostPer && costPer <= range.MaxCostPer)
                    return range;
            }

            return null;
        }

        private ParsedTlBracket ResolveTlForLevel(int level)
        {
            if (_skillAllocationTlBrackets.Count == 0)
                return null;

            foreach (var b in _skillAllocationTlBrackets)
            {
                if (level >= b.StartLevel && level <= b.EndLevel)
                    return b;
            }

            if (level < _skillAllocationTlBrackets[0].StartLevel)
                return _skillAllocationTlBrackets[0];

            return _skillAllocationTlBrackets[_skillAllocationTlBrackets.Count - 1];
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

        private void LoadEquipmentSlotBitmaps()
        {
            string basePath = Path.Combine(Application.streamingAssetsPath, "AOData");
            _weaponSlotBits = LoadSlotBitMap(Path.Combine(basePath, "weapon_slots.json"));
            _armorSlotBits = LoadSlotBitMap(Path.Combine(basePath, "armor_slots.json"));
            _implantSlotBits = LoadSlotBitMap(Path.Combine(basePath, "implant_slots.json"));

            // Slot identities are protocol structure, not AO-derived display data.
            // Keep the wear system functional when migration lookup files are absent.
            if (_weaponSlotBits.Count == 0)
                _weaponSlotBits = CreateCanonicalSlotBitMap(15);
            if (_armorSlotBits.Count == 0)
                _armorSlotBits = CreateCanonicalSlotBitMap(15);
            if (_implantSlotBits.Count == 0)
                _implantSlotBits = CreateCanonicalSlotBitMap(13);
        }

        private static Dictionary<ulong, List<int>> CreateCanonicalSlotBitMap(int slotCount)
        {
            var result = new Dictionary<ulong, List<int>>();
            for (int slotId = 1; slotId <= slotCount; slotId++)
                result[1UL << slotId] = new List<int> { slotId };
            return result;
        }

        private static Dictionary<ulong, List<int>> LoadSlotBitMap(string path)
        {
            var result = new Dictionary<ulong, List<int>>();
            if (!File.Exists(path))
                return result;

            var json = File.ReadAllText(path);

            List<SlotBitEntry> entries = null;
            try
            {
                entries = JsonConvert.DeserializeObject<List<SlotBitEntry>>(json);
            }
            catch
            {
                // Ignore and try lookup-object format.
            }

            if ((entries == null || entries.Count == 0))
            {
                var lookup = JsonConvert.DeserializeObject<SlotLookupFile>(json);
                if (lookup?.bitsById != null && lookup.bitsById.Count > 0)
                {
                    entries = new List<SlotBitEntry>();
                    foreach (var pair in lookup.bitsById)
                    {
                        if (!int.TryParse(pair.Key, out int id))
                            continue;

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
                return System.Array.Empty<int>();

            uint itemBitmap = unchecked((uint)bitmap);
            if (itemBitmap == 0)
                return System.Array.Empty<int>();

            var slots = new HashSet<int>();
            foreach (var pair in map)
            {
                if (pair.Key > uint.MaxValue)
                    continue;

                uint bit = (uint)pair.Key;
                if ((itemBitmap & bit) != bit)
                    continue;

                foreach (var slot in pair.Value)
                {
                    if (itemClass == CoreItems.ItemClass.Weapon && slot != 6 && slot != 8)
                    {
                        slots.Add(1000 + slot);
                        continue;
                    }
                    if (itemClass == CoreItems.ItemClass.Armor && (slot == 6 || slot == 8))
                    {
                        slots.Add(3000 + slot);
                        continue;
                    }

                    slots.Add(slot);
                }
            }

            if (itemClass == CoreItems.ItemClass.Implant && slots.Count > 0)
                slots = new HashSet<int>(slots.Select(slot => 100 + slot));

            return slots;
        }

        public string GetStatName(int statId) => StatMap.Get(statId);

        public DataCore.ItemInstance GetItemInstance(long instanceId) =>
            _dataInstances.TryGetValue(instanceId, out var inst) ? inst : null;

        public CoreItems.ItemDefinition GetCoreDefinition(int aoid) =>
            _coreDefs.TryGetValue(aoid, out var def) ? def : null;

        public CoreItems.ItemInstance GetCoreInstance(long aoid) =>
            _coreInstances.TryGetValue(aoid, out var inst) ? inst : null;

        public DataCore.Item GetRawItemByAoid(int aoid) =>
            _rawItemsByAoid.TryGetValue(aoid, out var item) ? item : null;

        public DataCore.Item GetRawNanoByAoid(int aoid) =>
            _nanosByAoid.TryGetValue(aoid, out var nano) ? nano : null;

        public void RegisterRuntimeNano(DataCore.Item nano)
        {
            if (nano == null || nano.AOID <= 0)
                return;
            _nanosByAoid[nano.AOID] = nano;
            NanoItems ??= new List<DataCore.Item>();
            int index = NanoItems.FindIndex(existing => existing?.AOID == nano.AOID);
            if (index >= 0)
                NanoItems[index] = nano;
            else
                NanoItems.Add(nano);
        }

        public IReadOnlyList<ItemTextureApplication> GetItemTextureApplications(int aoid) =>
            _itemTextureApplicationsByAoid.TryGetValue(aoid, out var list) ? list : Array.Empty<ItemTextureApplication>();

        public IReadOnlyDictionary<int, string> GetBreedLookup() => _breedNames;

        public IReadOnlyDictionary<int, string> GetProfessionLookup() => _professionNames;

        public IReadOnlyCollection<int> GetKnownSkillStatIds()
        {
            var result = new HashSet<int>();
            foreach (var byStat in _skillCostFactorsByProfession.Values)
            {
                foreach (var statId in byStat.Keys)
                    result.Add(statId);
            }

            // Canonical AO player skills. Skill-cost files refine color/IP costs,
            // but their absence must not remove the skills from the runtime model.
            for (int statId = 100; statId <= 168; statId++)
                result.Add(statId);

            return result.OrderBy(v => v).ToArray();
        }

        public string GetSkillColorName(int statId, int breedId, int professionId)
        {
            float? costPer = GetSkillCostPer(statId, breedId, professionId);
            if (!costPer.HasValue)
                return string.Empty;

            if (TryMapAbilityStatToIndex(statId, out _))
            {
                int rounded = (int)Math.Round(costPer.Value);
                if (_breedAbilityColors.TryGetValue(rounded, out var colorName))
                    return colorName ?? string.Empty;
            }

            var range = FindSkillAllocationRange(costPer.Value);
            return range?.Color ?? string.Empty;
        }

        public int GetAbilityImprovementsPerLevel()
        {
            return Math.Max(1, _abilityImprovementsPerLevel);
        }

        public float? GetSkillCostFactorFor(int professionId, int statId)
        {
            return GetSkillCostFactor(professionId, statId);
        }

        public IReadOnlyList<float> GetSkillTrickleWeights(int statId)
        {
            if (_skillTrickleByStat.TryGetValue(statId, out var weights) && weights != null && weights.Length >= 6)
                return weights;

            return null;
        }
    }
}
