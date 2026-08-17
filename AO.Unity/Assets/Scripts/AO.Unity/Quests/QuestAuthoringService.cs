using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace AO.Unity.Quests
{
    public sealed class QuestAuthoringService
    {
        private const string QuestFolderName = "Quests";
        private const string LegacyQuestFileName = "quests_authoring.json";
        private const string QuestFileSuffix = ".quest.json";
        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        private readonly Dictionary<string, string> _questFileById = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _managedFilePaths = new(StringComparer.OrdinalIgnoreCase);
        private QuestAuthoringFile _file = new();
        public QuestAuthoringFile File => _file;

        public string Load()
        {
            _file = new QuestAuthoringFile();
            _questFileById.Clear();
            _managedFilePaths.Clear();

            string primaryDir = GetPrimaryDirectory();
            EnsureDirectory(primaryDir);

            int loadedCount = LoadQuestFilesFromDirectory(primaryDir);
            bool loadedAny = loadedCount > 0;

            if (!loadedAny)
            {
                string fallbackDir = GetFallbackDirectory();
                EnsureDirectory(fallbackDir);
                loadedCount = LoadQuestFilesFromDirectory(fallbackDir);
                loadedAny = loadedCount > 0;
            }

            if (!loadedAny)
            {
                if (TryLoadLegacyCombinedFile(primaryDir, out int migratedCount))
                {
                    Save(out var migrateSaveMsg);
                    return $"Migrated {migratedCount} quest(s) from legacy combined file. {migrateSaveMsg}";
                }

                _file = BuildDefault();
                Save(out var saveMsg);
                return $"Created default quest file set. {saveMsg}";
            }

            _file.Quests ??= new List<QuestDefinition>();
            return $"Loaded {_file.Quests.Count} quest file(s).";
        }

        public bool Save(out string message)
        {
            string primaryDir = GetPrimaryDirectory();
            EnsureDirectory(primaryDir);

            try
            {
                SaveQuestFilesToDirectory(primaryDir, out int savedCount, out int deletedStale);
                message = $"Saved {savedCount} quest file(s) to {primaryDir}."
                    + (deletedStale > 0 ? $" Removed {deletedStale} stale quest file(s)." : string.Empty);
                return true;
            }
            catch (Exception primaryEx)
            {
                try
                {
                    string fallbackDir = GetFallbackDirectory();
                    EnsureDirectory(fallbackDir);
                    SaveQuestFilesToDirectory(fallbackDir, out int savedCount, out int deletedStale);
                    message = $"Primary save failed ({primaryEx.Message}). Saved {savedCount} quest file(s) to fallback {fallbackDir}."
                        + (deletedStale > 0 ? $" Removed {deletedStale} stale quest file(s)." : string.Empty);
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    message = $"Save failed. Primary: {primaryEx.Message}. Fallback: {fallbackEx.Message}.";
                    return false;
                }
            }
        }

        public QuestDefinition CreateQuest()
        {
            string id = BuildUniqueQuestId();
            var node = new QuestNode
            {
                NodeId = "start",
                NodeType = QuestNodeType.Dialogue,
                Title = "Start",
                DialogueText = "Hello there."
            };

            var quest = new QuestDefinition
            {
                QuestId = id,
                Name = id,
                Description = string.Empty,
                StartNodeId = node.NodeId,
                Nodes = new List<QuestNode> { node }
            };

            _file.Quests.Add(quest);
            return quest;
        }

        public bool DeleteQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return false;

            var match = _file.Quests.FirstOrDefault(q => string.Equals(q.QuestId, questId, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return false;

            _file.Quests.Remove(match);
            _questFileById.Remove(match.QuestId ?? string.Empty);
            return true;
        }

        public QuestNode CreateNode(QuestDefinition quest, QuestNodeType type)
        {
            if (quest == null)
                return null;

            quest.Nodes ??= new List<QuestNode>();
            string nodeId = BuildUniqueNodeId(quest, type.ToString().ToLowerInvariant());
            var node = new QuestNode
            {
                NodeId = nodeId,
                NodeType = type,
                Title = type.ToString()
            };
            quest.Nodes.Add(node);
            return node;
        }

        public bool DeleteNode(QuestDefinition quest, string nodeId)
        {
            if (quest?.Nodes == null || string.IsNullOrWhiteSpace(nodeId))
                return false;

            var node = quest.Nodes.FirstOrDefault(n => string.Equals(n.NodeId, nodeId, StringComparison.OrdinalIgnoreCase));
            if (node == null)
                return false;

            quest.Nodes.Remove(node);
            foreach (var n in quest.Nodes)
            {
                n.NextNodeIds?.RemoveAll(next => string.Equals(next, nodeId, StringComparison.OrdinalIgnoreCase));
            }

            if (string.Equals(quest.StartNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                quest.StartNodeId = quest.Nodes.FirstOrDefault()?.NodeId ?? string.Empty;

            return true;
        }

        public static string BuildValidationSummary(QuestValidationResult validation)
        {
            if (validation == null)
                return "Validation did not run.";

            string status = validation.IsValid ? "Validation passed." : "Validation failed.";
            return $"{status} Errors={validation.Errors.Count}, Warnings={validation.Warnings.Count}.";
        }

        private int LoadQuestFilesFromDirectory(string directoryPath)
        {
            int loaded = 0;
            if (!Directory.Exists(directoryPath))
                return loaded;

            string[] files = Directory.GetFiles(directoryPath, "*" + QuestFileSuffix, SearchOption.TopDirectoryOnly);
            foreach (string file in files)
            {
                try
                {
                    string raw = System.IO.File.ReadAllText(file);
                    var quest = JsonConvert.DeserializeObject<QuestDefinition>(raw);
                    if (quest == null || string.IsNullOrWhiteSpace(quest.QuestId))
                        continue;

                    NormalizeQuestDefaults(quest);

                    if (_file.Quests.Any(q => string.Equals(q.QuestId, quest.QuestId, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _file.Quests.Add(quest);
                    _questFileById[quest.QuestId] = file;
                    _managedFilePaths.Add(file);
                    loaded++;
                }
                catch
                {
                    // Ignore malformed file and continue loading others.
                }
            }

            return loaded;
        }

        private bool TryLoadLegacyCombinedFile(string directoryPath, out int migratedCount)
        {
            migratedCount = 0;
            string legacyPath = Path.Combine(directoryPath, LegacyQuestFileName);
            if (!System.IO.File.Exists(legacyPath))
                return false;

            try
            {
                string raw = System.IO.File.ReadAllText(legacyPath);
                var legacyFile = JsonConvert.DeserializeObject<QuestAuthoringFile>(raw);
                if (legacyFile?.Quests == null || legacyFile.Quests.Count == 0)
                    return false;

                foreach (var quest in legacyFile.Quests)
                {
                    if (quest == null || string.IsNullOrWhiteSpace(quest.QuestId))
                        continue;

                    NormalizeQuestDefaults(quest);
                    if (_file.Quests.Any(q => string.Equals(q.QuestId, quest.QuestId, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _file.Quests.Add(quest);
                    migratedCount++;
                }

                return migratedCount > 0;
            }
            catch
            {
                return false;
            }
        }

        private void SaveQuestFilesToDirectory(string directoryPath, out int savedCount, out int deletedStale)
        {
            savedCount = 0;
            deletedStale = 0;
            _file.Quests ??= new List<QuestDefinition>();

            var validQuests = _file.Quests
                .Where(q => q != null && !string.IsNullOrWhiteSpace(q.QuestId))
                .ToList();

            var retainedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var quest in validQuests)
            {
                NormalizeQuestDefaults(quest);
                string questFile = ResolveQuestFilePath(directoryPath, quest.QuestId);
                string json = JsonConvert.SerializeObject(quest, JsonSettings);
                System.IO.File.WriteAllText(questFile, json);
                retainedFiles.Add(questFile);
                _questFileById[quest.QuestId] = questFile;
                _managedFilePaths.Add(questFile);
                savedCount++;
            }

            var stale = _managedFilePaths.Where(path => !retainedFiles.Contains(path)).ToList();
            foreach (var staleFile in stale)
            {
                if (!staleFile.EndsWith(QuestFileSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!System.IO.File.Exists(staleFile))
                    continue;

                try
                {
                    System.IO.File.Delete(staleFile);
                    deletedStale++;
                }
                catch
                {
                    // No-op.
                }
            }

            _managedFilePaths.Clear();
            foreach (var file in retainedFiles)
                _managedFilePaths.Add(file);
        }

        private string ResolveQuestFilePath(string directoryPath, string questId)
        {
            if (_questFileById.TryGetValue(questId ?? string.Empty, out var existing) && !string.IsNullOrWhiteSpace(existing))
                return existing;

            string safeId = SanitizeFileName(questId);
            if (string.IsNullOrWhiteSpace(safeId))
                safeId = "quest";

            string basePath = Path.Combine(directoryPath, safeId + QuestFileSuffix);
            if (!System.IO.File.Exists(basePath))
                return basePath;

            int i = 2;
            while (true)
            {
                string candidate = Path.Combine(directoryPath, $"{safeId}_{i}{QuestFileSuffix}");
                if (!System.IO.File.Exists(candidate))
                    return candidate;
                i++;
            }
        }

        private static void NormalizeQuestDefaults(QuestDefinition quest)
        {
            if (quest == null)
                return;

            quest.MinLevel = Mathf.Max(1, quest.MinLevel <= 0 ? 1 : quest.MinLevel);
            quest.MaxLevel = Mathf.Max(quest.MinLevel, quest.MaxLevel <= 0 ? 220 : quest.MaxLevel);
            quest.StartConditions ??= new List<QuestCondition>();
            quest.Nodes ??= new List<QuestNode>();
            NormalizeConditionList(quest.StartConditions);
            foreach (var node in quest.Nodes)
            {
                if (node == null)
                    continue;
                node.Conditions ??= new List<QuestCondition>();
                node.Rewards ??= new List<QuestReward>();
                node.RewardRules ??= new List<QuestRewardRule>();
                node.NextNodeIds ??= new List<string>();
                node.TargetTemplateIds ??= new List<int>();
                node.TargetFamilyId ??= string.Empty;
                NormalizeConditionList(node.Conditions);
                for (int i = 0; i < node.RewardRules.Count; i++)
                {
                    var rule = node.RewardRules[i];
                    if (rule == null)
                    {
                        node.RewardRules[i] = new QuestRewardRule();
                        rule = node.RewardRules[i];
                    }

                    rule.Conditions ??= new List<QuestCondition>();
                    rule.Rewards ??= new List<QuestReward>();
                    NormalizeConditionList(rule.Conditions);
                }
            }
        }

        private static void NormalizeConditionList(List<QuestCondition> conditions)
        {
            if (conditions == null)
                return;

            for (int i = 0; i < conditions.Count; i++)
            {
                var condition = conditions[i];
                if (condition == null)
                {
                    conditions[i] = new QuestCondition();
                    condition = conditions[i];
                }

                condition.IntValues ??= new List<int>();
                condition.StringValues ??= new List<string>();
                condition.StringValue ??= string.Empty;
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        private static void EnsureDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
        }

        private static QuestAuthoringFile BuildDefault()
        {
            var file = new QuestAuthoringFile();
            var starter = new QuestDefinition
            {
                QuestId = "starter_hello",
                Name = "Starter Hello",
                Description = "Sample quest scaffold.",
                StartNpcId = 0,
                MinLevel = 1,
                MaxLevel = 220,
                Repeatable = false,
                StartNodeId = "start",
                Nodes = new List<QuestNode>
                {
                    new QuestNode
                    {
                        NodeId = "start",
                        NodeType = QuestNodeType.Dialogue,
                        Title = "Greeting",
                        DialogueText = "Welcome to Rubi-Ka.",
                        NextNodeIds = new List<string> { "end" }
                    },
                    new QuestNode
                    {
                        NodeId = "end",
                        NodeType = QuestNodeType.End,
                        Title = "Done"
                    }
                }
            };
            file.Quests.Add(starter);
            return file;
        }

        private string BuildUniqueQuestId()
        {
            const string prefix = "quest_";
            int i = _file.Quests.Count + 1;
            while (true)
            {
                string id = $"{prefix}{i}";
                if (_file.Quests.All(q => !string.Equals(q.QuestId, id, StringComparison.OrdinalIgnoreCase)))
                    return id;
                i++;
            }
        }

        private static string BuildUniqueNodeId(QuestDefinition quest, string prefix)
        {
            int i = 1;
            while (true)
            {
                string id = $"{prefix}_{i}";
                if (quest.Nodes.All(n => !string.Equals(n.NodeId, id, StringComparison.OrdinalIgnoreCase)))
                    return id;
                i++;
            }
        }

        private static string GetPrimaryDirectory()
        {
            return Path.Combine(Application.streamingAssetsPath, "AOData", QuestFolderName);
        }

        private static string GetFallbackDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "AOData", QuestFolderName);
        }
    }
}
