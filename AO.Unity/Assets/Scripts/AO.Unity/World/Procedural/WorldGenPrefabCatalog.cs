using System;
using System.Collections.Generic;
using UnityEngine;
using WorldGen.Content;

namespace WorldGen.UnityIntegration
{
    [CreateAssetMenu(fileName = "WorldGenPrefabCatalog", menuName = "WorldGen/Prefab Catalog")]
    public sealed class WorldGenPrefabCatalog : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public CatalogAsset Asset;
            public GameObject Prefab;
        }

        [SerializeField] private string contentVersion;
        [SerializeField] private List<Entry> entries = new List<Entry>();
        public string ContentVersion => contentVersion;
        public IReadOnlyList<Entry> Entries => entries.AsReadOnly();

        public void Replace(string version, IEnumerable<Entry> replacement)
        {
            contentVersion = version;
            entries = new List<Entry>(replacement);
            entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Asset.id, b.Asset.id));
        }

        public Entry Resolve(string id)
        {
            foreach (Entry entry in entries)
                if (entry != null && entry.Asset != null && entry.Asset.id == id) return entry;
            return null;
        }

        public Entry ResolveDoorway(string theme, float width, float height, int yaw, string stablePlacementId)
        {
            var compatible = new List<Entry>();
            foreach (Entry entry in entries)
            {
                CatalogAsset asset = entry?.Asset;
                if (asset == null || entry.Prefab == null || asset.theme != theme
                    || asset.placement == null || asset.placement.slot != "doorway-frame"
                    || Mathf.Abs(asset.placement.openingWidthMeters-width) > .001f
                    || Mathf.Abs(asset.placement.openingHeightMeters-height) > .001f
                    || Array.IndexOf(asset.allowedRotations ?? Array.Empty<int>(), yaw) < 0) continue;
                compatible.Add(entry);
            }
            if (compatible.Count == 0) return null;
            compatible.Sort((a, b) => StringComparer.Ordinal.Compare(a.Asset.id, b.Asset.id));
            // Stable across processes and platforms; does not consume layout RNG.
            uint hash = 2166136261;
            foreach (char c in stablePlacementId ?? string.Empty) hash = unchecked((hash ^ c) * 16777619);
            return compatible[(int)(hash % (uint)compatible.Count)];
        }
    }
}
