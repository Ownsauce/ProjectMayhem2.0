using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace AO.Unity.Quests
{
    public sealed class QuestTargetFamilyService
    {
        private const string QuestFolderName = "Quests";
        private const string FamilyFileName = "target_families.json";

        private readonly Dictionary<string, QuestTargetFamily> _familiesById = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, QuestTargetFamily> FamiliesById => _familiesById;

        public string Load()
        {
            _familiesById.Clear();

            if (!TryLoadFromPath(GetPrimaryPath(), out int loaded))
                TryLoadFromPath(GetFallbackPath(), out loaded);

            return $"Loaded {loaded} target family definition(s).";
        }

        private bool TryLoadFromPath(string path, out int loaded)
        {
            loaded = 0;
            if (!File.Exists(path))
                return false;

            try
            {
                var raw = File.ReadAllText(path);
                var file = JsonConvert.DeserializeObject<QuestTargetFamilyFile>(raw);
                if (file?.Families == null)
                    return false;

                for (int i = 0; i < file.Families.Count; i++)
                {
                    var family = file.Families[i];
                    if (family == null || string.IsNullOrWhiteSpace(family.FamilyId))
                        continue;

                    family.TemplateIds ??= new List<int>();
                    _familiesById[family.FamilyId.Trim()] = family;
                }

                loaded = _familiesById.Count;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetPrimaryPath()
        {
            return Path.Combine(Application.streamingAssetsPath, "AOData", QuestFolderName, FamilyFileName);
        }

        private static string GetFallbackPath()
        {
            return Path.Combine(Application.persistentDataPath, "AOData", QuestFolderName, FamilyFileName);
        }
    }
}
