using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AODB.Common.Enums;
using AODB.Common.RDBObjects;
using AO.Assets.ResourceDatabase;
using AO.Data.Core;

namespace AO.Unity.Assets
{
    /// <summary>Resolves and interpolates an AO ItemObject directly from the client RDB.</summary>
    public static class AOItemObjectResolver
    {
        private const int ItemObjectResourceType = 1000020;
        private const int QualityLevelStat = 54;
        private const int IconStat = 79;
        private static readonly object Sync = new object();
        private static AOResourceDatabase _database;
        private static string _installKey = string.Empty;
        private static readonly Dictionary<string, Item> Cache = new Dictionary<string, Item>();

        public static bool TryResolve(int lowId, int highId, int quality, out Item item)
        {
            item = null;
            int canonicalId = highId > 0 ? highId : lowId;
            if (canonicalId <= 0)
                return false;

            AOInstallValidation install = AOInstallConfiguration.GetConfiguredInstall();
            if (install == null || !install.IsValid)
                return false;

            lock (Sync)
            {
                EnsureDatabase(install);
                string cacheKey = lowId + ":" + highId + ":" + quality;
                if (Cache.TryGetValue(cacheKey, out item))
                    return true;
                ItemObject low = Read(lowId > 0 ? lowId : canonicalId);
                ItemObject high = highId > 0 && highId != lowId ? Read(highId) : low;
                if (low == null && high == null)
                    return false;
                low ??= high;
                high ??= low;

                int lowQl = ReadStat(low, QualityLevelStat, 1);
                int highQl = ReadStat(high, QualityLevelStat, lowQl);
                int targetQl = quality > 0 ? quality : lowQl;
                float factor = highQl == lowQl
                    ? 0f
                    : Math.Max(0f, Math.Min(1f,
                        (targetQl - lowQl) / (float)(highQl - lowQl)));

                var stats = new Dictionary<int, int>();
                CopyStats(low, stats, false, factor);
                CopyStats(high, stats, true, factor);
                stats[QualityLevelStat] = targetQl;
                // Resource identities and bit fields are categorical values, not
                // scalable numbers. In particular, interpolating icon IDs points
                // at unrelated RDB records.
                int iconId = ReadStat(low, IconStat, 0);
                if (iconId <= 0)
                    iconId = ReadStat(high, IconStat, 0);
                if (iconId > 0)
                    stats[IconStat] = iconId;

                item = new Item
                {
                    DBType = ItemObjectResourceType,
                    AOID = canonicalId,
                    Name = !string.IsNullOrWhiteSpace(low.Name) ? low.Name : high.Name,
                    Description = !string.IsNullOrWhiteSpace(low.Description)
                        ? low.Description : high.Description,
                    Type = "ItemObject",
                    Level = targetQl
                };
                foreach (KeyValuePair<int, int> stat in stats)
                    item.StatValues.Add(new RawStatValue { Stat = stat.Key, RawValue = stat.Value });
                CopyUploadNano(low, item);
                if (item.SpellData.Count == 0 && !ReferenceEquals(high, low))
                    CopyUploadNano(high, item);
                CopyWearVisuals(low, item);
                if (!ReferenceEquals(high, low))
                    CopyWearVisuals(high, item);
                Cache[cacheKey] = item;
                return true;
            }
        }

        private static void EnsureDatabase(AOInstallValidation install)
        {
            string key = install.RootPath + "|" + install.DatabaseFingerprint;
            if (_database != null && string.Equals(_installKey, key, StringComparison.Ordinal))
                return;
            _database?.Dispose();
            _database = new AOResourceDatabase(install.RootPath);
            _installKey = key;
            Cache.Clear();
        }

        private static ItemObject Read(int id)
        {
            if (id <= 0 || !_database.TryReadRaw(ItemObjectResourceType, id, out byte[] raw)
                || raw == null || raw.Length == 0)
                return null;
            var result = new ItemObject();
            using var stream = new MemoryStream(raw, false);
            using var reader = new BinaryReader(stream);
            // ItemObject records retain the RDB object's type/instance/size
            // prefix inside the indexed payload.
            if (stream.Length < 12)
                return null;
            reader.ReadInt32();
            reader.ReadInt32();
            reader.ReadInt32();
            result.Deserialize(reader);
            return result;
        }

        private static int ReadStat(ItemObject source, int statId, int fallback)
        {
            return source != null && source.Stats.TryGetValue((StatId)statId, out uint value)
                ? unchecked((int)value) : fallback;
        }

        private static void CopyStats(ItemObject source, Dictionary<int, int> destination,
            bool interpolate, float factor)
        {
            if (source == null)
                return;
            foreach (KeyValuePair<StatId, uint> pair in source.Stats)
            {
                int stat = (int)pair.Key;
                int value = unchecked((int)pair.Value);
                if (interpolate && destination.TryGetValue(stat, out int lowValue))
                    value = (int)Math.Round(lowValue + (value - (double)lowValue) * factor);
                destination[stat] = value;
            }
        }

        private static void CopyUploadNano(ItemObject source, Item destination)
        {
            if (source?.OnUse == null || destination == null)
                return;

            var modifiersField = source.OnUse.GetType().BaseType?.GetField(
                "_modifiers",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            if (!(modifiersField?.GetValue(source.OnUse) is IDictionary modifiers))
                return;

            foreach (DictionaryEntry functionEntry in modifiers)
            {
                if (!(functionEntry.Key is FunctionType function)
                    || function != FunctionType.UploadNano
                    || !(functionEntry.Value is IEnumerable operationsList))
                    continue;

                foreach (object operationsObject in operationsList)
                {
                    if (!(operationsObject is IDictionary operations)
                        || !operations.Contains(FunctionOperator.Value))
                        continue;
                    int nanoId = Convert.ToInt32(operations[FunctionOperator.Value]);
                    if (nanoId <= 0)
                        continue;

                    destination.SpellData.Add(new ItemSpellGroup
                    {
                        Event = 0,
                        Items = new List<ItemSpellData>
                        {
                            new ItemSpellData
                            {
                                NanoID = nanoId,
                                SpellID = (int)FunctionType.UploadNano,
                                SpellFormat = "Upload {NanoID}.",
                                SpellDescription = $"Upload {nanoId}."
                            }
                        }
                    });
                    return;
                }
            }
        }

        private static void CopyWearVisuals(ItemObject source, Item destination)
        {
            if (source?.OnWear == null || destination == null)
                return;

            var modifiersField = source.OnWear.GetType().BaseType?.GetField(
                "_modifiers",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
            if (!(modifiersField?.GetValue(source.OnWear) is IDictionary modifiers))
                return;

            foreach (DictionaryEntry functionEntry in modifiers)
            {
                if (!(functionEntry.Key is FunctionType function)
                    || (function != FunctionType.HeadMesh
                        && function != FunctionType.BackMesh
                        && function != FunctionType.ShoulderMesh
                        && function != FunctionType.AttractorMesh)
                    || !(functionEntry.Value is IEnumerable operationsList))
                    continue;

                foreach (object operationsObject in operationsList)
                {
                    if (!(operationsObject is IDictionary operations)
                        || !operations.Contains(FunctionOperator.MeshEffect))
                        continue;

                    int meshId = Convert.ToInt32(operations[FunctionOperator.MeshEffect]);
                    if (meshId <= 0)
                        continue;

                    int textureId = operations.Contains(FunctionOperator.Texture)
                        ? Convert.ToInt32(operations[FunctionOperator.Texture])
                        : 0;
                    var spell = new ItemSpellData
                    {
                        A = textureId,
                        B = meshId,
                        Target = 2,
                        SpellID = (int)function,
                        SpellDescription = $"{function} texture {textureId}, mesh {meshId}."
                    };
                    if (operations.Contains(FunctionOperator.Criteria)
                        && operations[FunctionOperator.Criteria] is IEnumerable criteria)
                    {
                        foreach (object criterion in criteria)
                        {
                            Type type = criterion.GetType();
                            object stat = type.GetField("Stat")?.GetValue(criterion);
                            object value = type.GetField("Value")?.GetValue(criterion);
                            object op = type.GetField("Operator")?.GetValue(criterion);
                            if (stat == null || value == null || op == null)
                                continue;
                            spell.Criteria.Add(new ItemActionCriterion
                            {
                                Value1 = Convert.ToInt32(stat),
                                Value2 = Convert.ToInt32(value),
                                Operator = Convert.ToInt32(op)
                            });
                        }
                    }

                    destination.SpellData.Add(new ItemSpellGroup
                    {
                        Event = 14, // OnWear
                        Items = new List<ItemSpellData> { spell }
                    });
                }
            }
        }
    }
}
