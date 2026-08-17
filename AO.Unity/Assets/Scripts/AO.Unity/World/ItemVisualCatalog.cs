using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace AO.Unity.World
{
    public sealed class ItemVisualCatalog
    {
        [Serializable]
        private sealed class ItemVisualMapFile
        {
            public List<ItemVisualDefinition> Items = new();
        }

        [Serializable]
        private sealed class ItemMeshReferencesFileEntry
        {
            public int AOID;
            public string Name;
            public int StatId;
            public int MeshId;
            public string SourceKind;
            public string MeshKey;
        }

        [Serializable]
        public sealed class ItemVisualDefinition
        {
            public int AOID;
            public string Name;
            public List<ItemVisualEntry> Visuals = new();
        }

        [Serializable]
        public sealed class ItemVisualEntry
        {
            public int StatId;
            public int MeshId;
            public string SourceKind;
            public string MeshKey;
        }

        private static ItemVisualCatalog _instance;
        private readonly Dictionary<int, ItemVisualDefinition> _itemsByAoid = new();

        public static ItemVisualCatalog Instance => _instance ??= Load();

        public bool TryGetDefinition(int aoid, out ItemVisualDefinition definition)
        {
            return _itemsByAoid.TryGetValue(aoid, out definition);
        }

        public bool TryGetVisual(int aoid, int statId, out ItemVisualEntry visual)
        {
            visual = null;
            if (!_itemsByAoid.TryGetValue(aoid, out var definition) || definition?.Visuals == null)
                return false;

            visual = definition.Visuals.FirstOrDefault(v =>
                v != null &&
                v.StatId == statId &&
                !string.IsNullOrWhiteSpace(v.MeshKey));
            return visual != null;
        }

        public IReadOnlyDictionary<int, string> GetVisualLookupByStat(int statId)
        {
            var result = new Dictionary<int, string>();
            foreach (var pair in _itemsByAoid)
            {
                var definition = pair.Value;
                if (definition?.Visuals == null)
                    continue;

                if (!definition.Visuals.Any(v => v != null && v.StatId == statId && !string.IsNullOrWhiteSpace(v.MeshKey)))
                    continue;

                string name = string.IsNullOrWhiteSpace(definition.Name)
                    ? pair.Key.ToString()
                    : definition.Name;
                result[pair.Key] = name;
            }

            return result;
        }

        private static ItemVisualCatalog Load()
        {
            var catalog = new ItemVisualCatalog();
            catalog.LoadFromDisk();
            return catalog;
        }

        private void LoadFromDisk()
        {
            string aoDataRoot = Path.Combine(Application.streamingAssetsPath, "AOData");
            string visualMapPath = Path.Combine(aoDataRoot, "item_visual_map.json");
            string referencesPath = Path.Combine(aoDataRoot, "item_mesh_references.json");

            try
            {
                if (File.Exists(visualMapPath))
                {
                    var file = JsonConvert.DeserializeObject<ItemVisualMapFile>(File.ReadAllText(visualMapPath));
                    if (file?.Items != null)
                    {
                        foreach (var item in file.Items)
                        {
                            if (item == null || item.AOID <= 0)
                                continue;
                            item.Visuals ??= new List<ItemVisualEntry>();
                            _itemsByAoid[item.AOID] = item;
                        }
                    }
                }
                else if (File.Exists(referencesPath))
                {
                    var refs = JsonConvert.DeserializeObject<List<ItemMeshReferencesFileEntry>>(File.ReadAllText(referencesPath));
                    if (refs != null)
                    {
                        foreach (var group in refs
                                     .Where(r => r != null && r.AOID > 0 && !string.IsNullOrWhiteSpace(r.MeshKey))
                                     .GroupBy(r => r.AOID))
                        {
                            var definition = new ItemVisualDefinition
                            {
                                AOID = group.Key,
                                Name = group.FirstOrDefault()?.Name ?? string.Empty,
                                Visuals = group
                                    .GroupBy(r => new { r.StatId, r.MeshId, r.MeshKey, r.SourceKind })
                                    .Select(g => new ItemVisualEntry
                                    {
                                        StatId = g.Key.StatId,
                                        MeshId = g.Key.MeshId,
                                        MeshKey = g.Key.MeshKey,
                                        SourceKind = g.Key.SourceKind ?? string.Empty
                                    })
                                    .ToList()
                            };

                            _itemsByAoid[definition.AOID] = definition;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load item visual catalog: {ex.Message}");
            }
        }
    }
}
