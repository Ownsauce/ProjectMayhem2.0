using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AO.Server;

public sealed class LootService
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, LootItemFamily> _familiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LootPoolDefinition> _poolsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, PlayfieldLootDefinition> _playfieldLootById = new();

    public LootService(ServerDataPaths paths)
    {
        if (paths == null)
            throw new ArgumentNullException(nameof(paths));

        string lootRoot = Path.Combine(paths.ServerAoDataRoot, "Loot");
        LoadFamilies(Path.Combine(lootRoot, "item_families.json"));
        LoadPools(Path.Combine(lootRoot, "loot_pools.json"));
        LoadPlayfieldLootFiles(lootRoot);
    }

    public RolledLoot RollLoot(RuntimeEntity entity)
    {
        if (entity == null || entity.PlayfieldId <= 0)
            return RolledLoot.Empty;

        if (!_playfieldLootById.TryGetValue(entity.PlayfieldId, out PlayfieldLootDefinition? playfieldLoot) || playfieldLoot == null)
            return RolledLoot.Empty;

        LootTableDefinition? table = ResolveTable(playfieldLoot, entity);
        if (table == null)
            return RolledLoot.Empty;

        int credits = RollCredits(table.Credits);
        int qlOffsetMin = table.QlOffsetMin ?? playfieldLoot.Defaults?.QlOffsetMin ?? 0;
        int qlOffsetMax = table.QlOffsetMax ?? playfieldLoot.Defaults?.QlOffsetMax ?? 0;
        if (qlOffsetMax < qlOffsetMin)
            (qlOffsetMin, qlOffsetMax) = (qlOffsetMax, qlOffsetMin);

        int maxItems = table.MaxItems ?? playfieldLoot.Defaults?.MaxItems ?? int.MaxValue;
        if (maxItems < 0)
            maxItems = int.MaxValue;

        var hits = new List<RolledLootItem>();
        if (maxItems != 0 && table.PoolRefs != null)
        {
            for (int i = 0; i < table.PoolRefs.Count; i++)
            {
                string poolRef = table.PoolRefs[i];
                if (string.IsNullOrWhiteSpace(poolRef) || !_poolsById.TryGetValue(poolRef, out LootPoolDefinition? pool) || pool?.Entries == null)
                    continue;

                foreach (LootPoolEntry entry in pool.Entries)
                {
                    if (entry == null || !RollChance(entry.ChancePercent))
                        continue;

                    RolledLootItem? item = ResolveEntry(entry, entity.Level, qlOffsetMin, qlOffsetMax);
                    if (item.HasValue)
                        hits.Add(item.Value);
                }
            }
        }

        if (hits.Count > maxItems)
            hits = hits.OrderBy(_ => Random.Shared.Next()).Take(maxItems).ToList();

        return hits.Count == 0 && credits <= 0
            ? RolledLoot.Empty
            : new RolledLoot(credits, hits);
    }

    public LootRuntimeSettings ResolveRuntimeSettings(RuntimeEntity entity)
    {
        const int defaultCorpseSeconds = 120;
        const int defaultRespawnSeconds = 120;
        const int defaultEmptyCorpseSeconds = 3;

        if (entity == null || entity.PlayfieldId <= 0)
            return new LootRuntimeSettings(defaultCorpseSeconds, defaultRespawnSeconds, defaultEmptyCorpseSeconds);

        if (!_playfieldLootById.TryGetValue(entity.PlayfieldId, out PlayfieldLootDefinition? playfieldLoot) || playfieldLoot == null)
            return new LootRuntimeSettings(defaultCorpseSeconds, defaultRespawnSeconds, defaultEmptyCorpseSeconds);

        LootTableDefinition? table = ResolveTable(playfieldLoot, entity);
        int corpseSeconds = table?.CorpseSeconds
            ?? playfieldLoot.Defaults?.CorpseSeconds
            ?? defaultCorpseSeconds;
        int respawnSeconds = table?.RespawnSeconds
            ?? playfieldLoot.Defaults?.RespawnSeconds
            ?? defaultRespawnSeconds;
        int emptyCorpseSeconds = table?.EmptyCorpseSeconds
            ?? playfieldLoot.Defaults?.EmptyCorpseSeconds
            ?? defaultEmptyCorpseSeconds;

        return new LootRuntimeSettings(
            Math.Max(0, corpseSeconds),
            Math.Max(0, respawnSeconds),
            Math.Max(0, emptyCorpseSeconds));
    }

    private void LoadFamilies(string path)
    {
        if (!File.Exists(path))
            return;

        var file = JsonSerializer.Deserialize<ItemFamiliesFile>(File.ReadAllText(path), _jsonOptions);
        if (file?.Families == null)
            return;

        foreach (LootItemFamily family in file.Families)
        {
            if (family == null || string.IsNullOrWhiteSpace(family.Id))
                continue;
            _familiesById[family.Id.Trim()] = family;
        }
    }

    private void LoadPools(string path)
    {
        if (!File.Exists(path))
            return;

        var file = JsonSerializer.Deserialize<LootPoolsFile>(File.ReadAllText(path), _jsonOptions);
        if (file?.Pools == null)
            return;

        foreach (LootPoolDefinition pool in file.Pools)
        {
            if (pool == null || string.IsNullOrWhiteSpace(pool.Id))
                continue;
            _poolsById[pool.Id.Trim()] = pool;
        }
    }

    private void LoadPlayfieldLootFiles(string lootRoot)
    {
        if (!Directory.Exists(lootRoot))
            return;

        foreach (string path in Directory.EnumerateFiles(lootRoot, "playfield_*_loot.json"))
        {
            var file = JsonSerializer.Deserialize<PlayfieldLootDefinition>(File.ReadAllText(path), _jsonOptions);
            if (file == null || file.PlayfieldId <= 0)
                continue;
            _playfieldLootById[file.PlayfieldId] = file;
        }
    }

    private static LootTableDefinition? ResolveTable(PlayfieldLootDefinition playfieldLoot, RuntimeEntity entity)
    {
        if (playfieldLoot?.Tables == null || entity == null)
            return null;

        return playfieldLoot.Tables
            .Where(t => t != null && IsMatch(t.Match, entity))
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => MatchSpecificity(t.Match))
            .FirstOrDefault();
    }

    private static bool IsMatch(LootMatchDefinition? match, RuntimeEntity entity)
    {
        if (match == null)
            return false;

        if (match.IdentityInstance.HasValue && match.IdentityInstance.Value != entity.IdentityInstance)
            return false;

        if (match.TemplateId.HasValue && match.TemplateId.Value != entity.TemplateId)
            return false;

        string displayName = entity.DisplayName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(match.DisplayName)
            && !string.Equals(displayName, match.DisplayName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(match.DisplayNameContains)
            && displayName.IndexOf(match.DisplayNameContains, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (match.DisplayNameContainsAny != null && match.DisplayNameContainsAny.Count > 0)
        {
            bool any = match.DisplayNameContainsAny.Any(s =>
                !string.IsNullOrWhiteSpace(s) && displayName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            if (!any)
                return false;
        }

        return match.IdentityInstance.HasValue
               || match.TemplateId.HasValue
               || !string.IsNullOrWhiteSpace(match.DisplayName)
               || !string.IsNullOrWhiteSpace(match.DisplayNameContains)
               || (match.DisplayNameContainsAny != null && match.DisplayNameContainsAny.Count > 0);
    }

    private static int MatchSpecificity(LootMatchDefinition? match)
    {
        if (match == null)
            return 0;

        int score = 0;
        if (match.IdentityInstance.HasValue)
            score += 100;
        if (match.TemplateId.HasValue)
            score += 50;
        if (!string.IsNullOrWhiteSpace(match.DisplayName))
            score += 25;
        if (!string.IsNullOrWhiteSpace(match.DisplayNameContains))
            score += 10;
        if (match.DisplayNameContainsAny != null && match.DisplayNameContainsAny.Count > 0)
            score += 5;
        return score;
    }

    private RolledLootItem? ResolveEntry(LootPoolEntry entry, int mobLevel, int qlOffsetMin, int qlOffsetMax)
    {
        if (entry.Aoid > 0)
            return new RolledLootItem(entry.Aoid, entry.Name ?? string.Empty, entry.FixedQl ? entry.Ql : null);

        if (string.IsNullOrWhiteSpace(entry.FamilyRef) || !_familiesById.TryGetValue(entry.FamilyRef, out LootItemFamily? family))
            return null;

        if (family?.Items == null || family.Items.Count == 0)
            return null;

        int targetQl = Math.Max(1, Math.Max(1, mobLevel) + Random.Shared.Next(qlOffsetMin, qlOffsetMax + 1));
        LootFamilyItem selected = family.Items
            .Where(i => i != null && i.Aoid > 0)
            .OrderBy(i => Math.Abs((i.Ql <= 0 ? targetQl : i.Ql) - targetQl))
            .ThenBy(i => i.Ql)
            .FirstOrDefault();

        if (selected == null)
            return null;

        string name = !string.IsNullOrWhiteSpace(selected.Name)
            ? selected.Name
            : family.DisplayName ?? family.Id ?? selected.Aoid.ToString();
        return new RolledLootItem(selected.Aoid, name, selected.Ql > 0 ? selected.Ql : targetQl);
    }

    private static bool RollChance(double chancePercent)
    {
        if (chancePercent <= 0)
            return false;
        if (chancePercent >= 100)
            return true;
        return Random.Shared.NextDouble() * 100.0 < chancePercent;
    }

    private static int RollCredits(LootCreditsDefinition? credits)
    {
        if (credits == null)
            return 0;

        int min = Math.Max(0, credits.Min);
        int max = Math.Max(min, credits.Max);
        return max <= min ? min : Random.Shared.Next(min, max + 1);
    }

    private sealed class ItemFamiliesFile
    {
        public List<LootItemFamily> Families { get; set; } = new();
    }

    private sealed class LootPoolsFile
    {
        public List<LootPoolDefinition> Pools { get; set; } = new();
    }

    private sealed class PlayfieldLootDefinition
    {
        public int PlayfieldId { get; set; }
        public LootDefaultsDefinition? Defaults { get; set; }
        public List<LootTableDefinition> Tables { get; set; } = new();
    }

    private sealed class LootDefaultsDefinition
    {
        public int? QlOffsetMin { get; set; }
        public int? QlOffsetMax { get; set; }
        public int? MaxItems { get; set; }
        public int? CorpseSeconds { get; set; }
        public int? RespawnSeconds { get; set; }
        public int? EmptyCorpseSeconds { get; set; }
    }

    private sealed class LootTableDefinition
    {
        public string Id { get; set; } = string.Empty;
        public int Priority { get; set; }
        public LootMatchDefinition? Match { get; set; }
        public LootCreditsDefinition? Credits { get; set; }
        public int? QlOffsetMin { get; set; }
        public int? QlOffsetMax { get; set; }
        public int? MaxItems { get; set; }
        public int? CorpseSeconds { get; set; }
        public int? RespawnSeconds { get; set; }
        public int? EmptyCorpseSeconds { get; set; }
        public List<string> PoolRefs { get; set; } = new();
    }

    private sealed class LootMatchDefinition
    {
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayNameContains { get; set; } = string.Empty;
        public List<string> DisplayNameContainsAny { get; set; } = new();
        public int? IdentityInstance { get; set; }
        public int? TemplateId { get; set; }
    }

    private sealed class LootCreditsDefinition
    {
        public int Min { get; set; }
        public int Max { get; set; }
    }

    private sealed class LootPoolDefinition
    {
        public string Id { get; set; } = string.Empty;
        public List<LootPoolEntry> Entries { get; set; } = new();
    }

    private sealed class LootPoolEntry
    {
        public string FamilyRef { get; set; } = string.Empty;
        public int Aoid { get; set; }
        public string Name { get; set; } = string.Empty;
        public double ChancePercent { get; set; }
        public bool FixedQl { get; set; }
        public int? Ql { get; set; }
    }

    private sealed class LootItemFamily
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public List<LootFamilyItem> Items { get; set; } = new();
    }

    private sealed class LootFamilyItem
    {
        public int Aoid { get; set; }
        public int Ql { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}

public readonly record struct RolledLoot(int Credits, IReadOnlyList<RolledLootItem> Items)
{
    public static RolledLoot Empty => new(0, Array.Empty<RolledLootItem>());
}

public readonly record struct RolledLootItem(int Aoid, string Name, int? Ql);
public readonly record struct LootRuntimeSettings(int CorpseSeconds, int RespawnSeconds, int EmptyCorpseSeconds);
