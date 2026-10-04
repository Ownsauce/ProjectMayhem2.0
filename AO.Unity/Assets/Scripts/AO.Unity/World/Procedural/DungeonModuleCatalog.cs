using System;
using System.Collections.Generic;
using WorldGen.Dungeons;
using UnityEngine;

namespace AO.Unity.World.Procedural
{
    public enum DungeonModuleSlot
    {
        Room = 0,
        Corridor = 1,
        Stairwell = 2,
        Door = 3
    }

    [Serializable]
    public sealed class DungeonModuleCatalogEntry
    {
        public string Id = string.Empty;
        public DungeonLayoutProfile Profile = DungeonLayoutProfile.Facility;
        public DungeonVisualTheme Theme = DungeonVisualTheme.Industrial;
        public DungeonModuleSlot Slot = DungeonModuleSlot.Room;
        public DungeonModuleKind RoomKind = DungeonModuleKind.CombatRoom;
        public GameObject Prefab;
        public Vector3 AuthoredSize = Vector3.one;
        public bool FitToGeneratedBounds = true;
        public int Priority;

        public bool Matches(DungeonLayoutProfile profile, DungeonVisualTheme theme,
            DungeonModuleSlot slot, DungeonModuleKind roomKind)
        {
            return Profile == profile && Theme == theme && Slot == slot
                   && (slot != DungeonModuleSlot.Room || RoomKind == roomKind);
        }
    }

    [CreateAssetMenu(fileName = "DungeonModuleCatalog",
        menuName = "AO/World Generation/Dungeon Module Catalog")]
    public sealed class DungeonModuleCatalog : ScriptableObject
    {
        [SerializeField] private List<DungeonModuleCatalogEntry> entries = new();

        public IReadOnlyList<DungeonModuleCatalogEntry> Entries => entries;

        public DungeonModuleCatalogEntry Resolve(DungeonLayoutProfile profile,
            DungeonVisualTheme theme, DungeonModuleSlot slot,
            DungeonModuleKind roomKind = DungeonModuleKind.CombatRoom)
        {
            DungeonModuleCatalogEntry best = null;
            for (int i = 0; i < entries.Count; i++)
            {
                DungeonModuleCatalogEntry candidate = entries[i];
                if (candidate == null || !candidate.Matches(profile, theme, slot, roomKind))
                    continue;
                if (best == null || candidate.Priority > best.Priority
                                 || candidate.Priority == best.Priority
                                 && string.CompareOrdinal(candidate.Id, best.Id) < 0)
                    best = candidate;
            }
            return best;
        }

        public void ReplaceEntries(IEnumerable<DungeonModuleCatalogEntry> nextEntries)
        {
            entries.Clear();
            if (nextEntries != null) entries.AddRange(nextEntries);
        }

        public static DungeonModuleCatalog CreateBuiltInCatalog()
        {
            var catalog = CreateInstance<DungeonModuleCatalog>();
            catalog.name = "Built-in Dungeon Module Catalog";
            var builtIns = new List<DungeonModuleCatalogEntry>();
            foreach (DungeonModuleKind kind in Enum.GetValues(typeof(DungeonModuleKind)))
            {
                builtIns.Add(BuiltIn("subway.room." + kind.ToString().ToLowerInvariant(),
                    DungeonModuleSlot.Room, kind));
                builtIns.Add(BuiltInKeep("keep.room." + kind.ToString().ToLowerInvariant(),
                    DungeonModuleSlot.Room, kind));
                builtIns.Add(BuiltInCavern("cavern.room." + kind.ToString().ToLowerInvariant(),
                    DungeonModuleSlot.Room, kind));
            }
            builtIns.Add(BuiltIn("subway.tunnel.curved-shell", DungeonModuleSlot.Corridor));
            builtIns.Add(BuiltIn("subway.stairwell.enclosed", DungeonModuleSlot.Stairwell));
            builtIns.Add(BuiltIn("subway.door.transit-double-panel", DungeonModuleSlot.Door));
            builtIns.Add(BuiltInKeep("keep.corridor.vaulted", DungeonModuleSlot.Corridor));
            builtIns.Add(BuiltInKeep("keep.door.stone-arch", DungeonModuleSlot.Door));
            builtIns.Add(BuiltInCavern("cavern.tunnel.faceted", DungeonModuleSlot.Corridor));
            builtIns.Add(BuiltInCavern("cavern.door.rock-cut", DungeonModuleSlot.Door));
            catalog.ReplaceEntries(builtIns);
            return catalog;
        }

        public static DungeonModuleCatalog CreateBuiltInSubwayCatalog() =>
            CreateBuiltInCatalog();

        private static DungeonModuleCatalogEntry BuiltIn(string id, DungeonModuleSlot slot,
            DungeonModuleKind kind = DungeonModuleKind.CombatRoom) => new()
        {
            Id = id,
            Profile = DungeonLayoutProfile.Subway,
            Theme = DungeonVisualTheme.Subway,
            Slot = slot,
            RoomKind = kind,
            Priority = 0
        };

        private static DungeonModuleCatalogEntry BuiltInKeep(string id, DungeonModuleSlot slot,
            DungeonModuleKind kind = DungeonModuleKind.CombatRoom) => new()
        {
            Id = id,
            Profile = DungeonLayoutProfile.GroupDungeon,
            Theme = DungeonVisualTheme.Keep,
            Slot = slot,
            RoomKind = kind,
            Priority = 0
        };

        private static DungeonModuleCatalogEntry BuiltInCavern(string id, DungeonModuleSlot slot,
            DungeonModuleKind kind = DungeonModuleKind.CombatRoom) => new()
        {
            Id = id,
            Profile = DungeonLayoutProfile.CaveDungeon,
            Theme = DungeonVisualTheme.Cavern,
            Slot = slot,
            RoomKind = kind,
            Priority = 0
        };
    }

    /// <summary>
    /// Resolves authored catalog entries and instantiates user-supplied prefabs.
    /// Entries without a prefab are stable built-in module IDs consumed by the
    /// procedural presentation builder; RDB providers can use the same IDs.
    /// </summary>
    public sealed class DungeonPrefabModuleProvider
    {
        private readonly DungeonModuleCatalog catalog;

        public DungeonPrefabModuleProvider(DungeonModuleCatalog catalog)
        {
            this.catalog = catalog != null
                ? catalog : DungeonModuleCatalog.CreateBuiltInCatalog();
        }

        public DungeonModuleCatalogEntry ResolveRoom(DungeonLayoutProfile profile,
            DungeonVisualTheme theme, DungeonModuleKind kind) =>
            catalog.Resolve(profile, theme, DungeonModuleSlot.Room, kind);

        public DungeonModuleCatalogEntry ResolvePassage(DungeonLayoutProfile profile,
            DungeonVisualTheme theme, bool stairs) => catalog.Resolve(profile, theme,
            stairs ? DungeonModuleSlot.Stairwell : DungeonModuleSlot.Corridor);

        public DungeonModuleCatalogEntry ResolveDoor(DungeonLayoutProfile profile,
            DungeonVisualTheme theme) =>
            catalog.Resolve(profile, theme, DungeonModuleSlot.Door);

        public static GameObject InstantiatePrefab(DungeonModuleCatalogEntry entry,
            Transform parent, Vector3 center, Vector3 generatedSize)
        {
            if (entry?.Prefab == null || parent == null) return null;
            GameObject instance = UnityEngine.Object.Instantiate(entry.Prefab, parent, false);
            instance.name = entry.Id;
            instance.transform.localPosition = center;
            if (entry.FitToGeneratedBounds)
            {
                Vector3 authored = entry.AuthoredSize;
                instance.transform.localScale = new Vector3(
                    authored.x > 0.001f ? generatedSize.x / authored.x : 1f,
                    authored.y > 0.001f ? generatedSize.y / authored.y : 1f,
                    authored.z > 0.001f ? generatedSize.z / authored.z : 1f);
            }
            return instance;
        }
    }
}
