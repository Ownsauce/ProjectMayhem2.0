using System.Collections.Generic;
using AODB.Common.RDBObjects;

public static class MonsterDataResolver
{
    public const int BodyCatMeshStatId = 12;

    public static bool TryResolveBodyCatMeshId(ResourceDatabase db, int monsterDataId, out int catMeshId)
    {
        catMeshId = 0;
        if (monsterDataId <= 0 || db?.Rdb == null)
            return false;

        MonsterData monsterData = db.Get<MonsterData>(ResourceTypeId.MonsterData, monsterDataId);
        if (monsterData?.Stats == null)
            return false;

        if (!monsterData.Stats.TryGetValue(BodyCatMeshStatId, out uint bodyCatMeshId) || bodyCatMeshId == 0)
            return false;

        catMeshId = (int)bodyCatMeshId;
        return true;
    }

    static readonly Dictionary<(int monsterDataId, int animSet), List<int>> AnimIdsCache =
        new Dictionary<(int monsterDataId, int animSet), List<int>>();
    static readonly Dictionary<string, List<int>> ProfileAnimIdsCache =
        new Dictionary<string, List<int>>(System.StringComparer.OrdinalIgnoreCase);

    public static bool TryGetProfileAnimIds(ResourceDatabase db, string profile, out List<int> animIds)
    {
        animIds = null;
        string normalized = (profile ?? string.Empty).Trim().ToLowerInvariant();
        if (db?.Rdb == null || string.IsNullOrEmpty(normalized))
            return false;
        if (ProfileAnimIdsCache.TryGetValue(normalized, out animIds))
            return animIds.Count > 0;

        var result = new List<int>();
        InfoObject info = db.Get<InfoObject>(1);
        if (info?.Types != null
            && info.Types.TryGetValue(ResourceTypeId.Anim, out Dictionary<int, string> names)
            && names != null)
        {
            string prefix = normalized + "_";
            foreach (KeyValuePair<int, string> pair in names)
            {
                string name = (pair.Value ?? string.Empty).Trim().ToLowerInvariant();
                if (pair.Key > 0 && name.StartsWith(prefix, System.StringComparison.Ordinal))
                    result.Add(pair.Key);
            }
        }
        ProfileAnimIdsCache[normalized] = result;
        animIds = result;
        return result.Count > 0;
    }

    public static bool TryGetAnimIds(ResourceDatabase db, int monsterDataId, int animSet, out List<int> animIds)
    {
        animIds = null;
        if (monsterDataId <= 0 || db?.Rdb == null)
            return false;

        var key = (monsterDataId, animSet);
        if (AnimIdsCache.TryGetValue(key, out List<int> cached) && cached != null)
        {
            animIds = cached;
            return cached.Count > 0;
        }

        MonsterData monsterData = db.Get<MonsterData>(ResourceTypeId.MonsterData, monsterDataId);
        if (monsterData?.Anims == null || monsterData.Anims.Count == 0)
        {
            AnimIdsCache[key] = new List<int>();
            return false;
        }

        if (monsterData.Anims.TryGetValue(animSet, out List<int> setIds) && setIds != null && setIds.Count > 0)
        {
            animIds = setIds;
            AnimIdsCache[key] = setIds;
            return true;
        }

        var union = new List<int>();
        var seen = new HashSet<int>();
        foreach (KeyValuePair<int, List<int>> pair in monsterData.Anims)
        {
            if (pair.Value == null)
                continue;

            for (int i = 0; i < pair.Value.Count; i++)
            {
                int id = pair.Value[i];
                if (id <= 0 || !seen.Add(id))
                    continue;

                union.Add(id);
            }
        }

        AnimIdsCache[key] = union;
        if (union.Count == 0)
            return false;

        animIds = union;
        return true;
    }

    public static bool TryGetAnimEntries(
        ResourceDatabase db,
        int monsterDataId,
        int? animSetFilter,
        out List<(int AnimSet, int AnimId)> entries)
    {
        entries = null;
        if (monsterDataId <= 0 || db?.Rdb == null)
            return false;

        MonsterData monsterData = db.Get<MonsterData>(ResourceTypeId.MonsterData, monsterDataId);
        if (monsterData?.Anims == null || monsterData.Anims.Count == 0)
            return false;

        var list = new List<(int, int)>();
        var seen = new HashSet<int>();

        foreach (KeyValuePair<int, List<int>> pair in monsterData.Anims)
        {
            if (animSetFilter.HasValue && pair.Key != animSetFilter.Value)
                continue;

            if (pair.Value == null)
                continue;

            for (int i = 0; i < pair.Value.Count; i++)
            {
                int id = pair.Value[i];
                if (id <= 0 || !seen.Add(id))
                    continue;

                list.Add((pair.Key, id));
            }
        }

        if (list.Count == 0)
            return false;

        entries = list;
        return true;
    }

    static Dictionary<int, int> _catMeshToMonsterDataCache;

    public static bool TryFindMonsterDataForCatMesh(ResourceDatabase db, int catMeshId, out int monsterDataId)
    {
        monsterDataId = 0;
        if (catMeshId <= 0 || db?.Rdb == null)
            return false;

        if (_catMeshToMonsterDataCache != null
            && _catMeshToMonsterDataCache.TryGetValue(catMeshId, out int cached)
            && cached > 0)
        {
            monsterDataId = cached;
            return true;
        }

        EnsureCatMeshToMonsterDataCache(db);
        if (_catMeshToMonsterDataCache != null
            && _catMeshToMonsterDataCache.TryGetValue(catMeshId, out int mapped)
            && mapped > 0)
        {
            monsterDataId = mapped;
            return true;
        }

        return false;
    }

    static void EnsureCatMeshToMonsterDataCache(ResourceDatabase db)
    {
        if (_catMeshToMonsterDataCache != null || db?.Rdb == null)
            return;

        _catMeshToMonsterDataCache = new Dictionary<int, int>();
        if (!db.Rdb.RecordTypeToId.TryGetValue((int)ResourceTypeId.MonsterData, out Dictionary<int, ulong> records)
            || records == null)
            return;

        Dictionary<int, string> animationNames = null;
        InfoObject info = db.Get<InfoObject>(1);
        info?.Types?.TryGetValue(ResourceTypeId.Anim, out animationNames);

        var bestScores = new Dictionary<int, int>();
        foreach (int id in records.Keys)
        {
            if (!TryResolveBodyCatMeshId(db, id, out int bodyCatMeshId))
                continue;

            MonsterData monsterData = db.Get<MonsterData>(ResourceTypeId.MonsterData, id);
            int score = ScoreLocomotionCoverage(monsterData, animationNames);
            if (!bestScores.TryGetValue(bodyCatMeshId, out int previousScore)
                || score > previousScore)
            {
                _catMeshToMonsterDataCache[bodyCatMeshId] = id;
                bestScores[bodyCatMeshId] = score;
            }
        }
    }

    static int ScoreLocomotionCoverage(
        MonsterData monsterData,
        IReadOnlyDictionary<int, string> animationNames)
    {
        if (monsterData?.Anims == null || animationNames == null)
            return 0;

        bool idle = false, forward = false, backward = false;
        bool left = false, right = false, jump = false;
        foreach (List<int> ids in monsterData.Anims.Values)
        {
            if (ids == null)
                continue;
            for (int i = 0; i < ids.Count; i++)
            {
                if (!animationNames.TryGetValue(ids[i], out string raw) || string.IsNullOrEmpty(raw))
                    continue;
                string name = raw.Trim().ToLowerInvariant().Replace('_', '-');
                idle |= name.Contains("idle");
                backward |= name.Contains("run-back") || name.Contains("walk-back");
                left |= name.Contains("walk-left");
                right |= name.Contains("walk-right");
                jump |= name.Contains("jump-forward");
                forward |= (name.Contains("run") || name.Contains("walk"))
                    && !name.Contains("-back") && !name.Contains("-left") && !name.Contains("-right");
            }
        }

        return (idle ? 1 : 0) + (forward ? 8 : 0) + (backward ? 4 : 0)
            + (left ? 2 : 0) + (right ? 2 : 0) + (jump ? 1 : 0);
    }
}
