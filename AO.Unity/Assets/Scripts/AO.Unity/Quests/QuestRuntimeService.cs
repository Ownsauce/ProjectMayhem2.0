using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AO.Unity.Quests
{
    public sealed class QuestEvaluationContext
    {
        public int CharacterLevel;
        public int FactionId;
        public int ProfessionId;
        public int VisualProfessionId;
        public int BreedId;
        public int GenderId;
    }

    public sealed class QuestRuntimeService
    {
        private readonly Dictionary<string, QuestProgress> _progressByQuestId = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, QuestProgress> ProgressByQuestId => _progressByQuestId;

        public bool StartQuest(QuestDefinition quest, int characterLevel, out string message)
        {
            var context = new QuestEvaluationContext { CharacterLevel = characterLevel };
            return StartQuest(quest, context, out message);
        }

        public bool StartQuest(QuestDefinition quest, QuestEvaluationContext context, out string message)
        {
            if (quest == null || string.IsNullOrWhiteSpace(quest.QuestId))
            {
                message = "Cannot start quest: invalid quest.";
                return false;
            }

            int characterLevel = Mathf.Max(1, context?.CharacterLevel ?? 1);
            if (characterLevel < quest.MinLevel || characterLevel > quest.MaxLevel)
            {
                message = $"Quest '{quest.Name ?? quest.QuestId}' requires level {quest.MinLevel}-{quest.MaxLevel}.";
                return false;
            }

            if (!EvaluateConditions(quest.StartConditions, quest.StartConditionMode, context))
            {
                message = $"Quest '{quest.Name ?? quest.QuestId}' is unavailable for this character.";
                return false;
            }

            if (_progressByQuestId.TryGetValue(quest.QuestId, out var existing))
            {
                if (existing.State == QuestRuntimeState.Completed && quest.Repeatable)
                {
                    _progressByQuestId.Remove(quest.QuestId);
                }
                else
                {
                    message = $"Quest '{quest.QuestId}' is already active or completed.";
                    return false;
                }
            }

            var progress = new QuestProgress
            {
                QuestId = quest.QuestId,
                CurrentNodeId = quest.StartNodeId,
                State = QuestRuntimeState.Started,
                UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            _progressByQuestId[quest.QuestId] = progress;
            message = $"Started quest '{quest.Name ?? quest.QuestId}'.";
            return true;
        }

        public bool CompleteQuest(string questId, out string message)
        {
            if (!_progressByQuestId.TryGetValue(questId ?? string.Empty, out var progress))
            {
                message = "Quest is not active.";
                return false;
            }

            progress.State = QuestRuntimeState.Completed;
            progress.UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            message = $"Completed quest '{questId}'.";
            return true;
        }

        public bool SetCurrentNode(string questId, string nodeId, out string message)
        {
            if (!_progressByQuestId.TryGetValue(questId ?? string.Empty, out var progress))
            {
                message = "Quest is not active.";
                return false;
            }

            progress.CurrentNodeId = nodeId ?? string.Empty;
            progress.UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            message = $"Quest '{questId}' moved to node '{progress.CurrentNodeId}'.";
            return true;
        }

        public int IncrementObjective(string questId, string objectiveKey, int amount)
        {
            if (!_progressByQuestId.TryGetValue(questId ?? string.Empty, out var progress))
                return 0;

            objectiveKey ??= "default";
            amount = Mathf.Max(1, amount);
            progress.ObjectiveCounters.TryGetValue(objectiveKey, out int current);
            int next = current + amount;
            progress.ObjectiveCounters[objectiveKey] = next;
            progress.UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return next;
        }

        public bool AbandonQuest(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
                return false;
            return _progressByQuestId.Remove(questId);
        }

        public List<QuestProgress> Snapshot()
        {
            return _progressByQuestId.Values.Select(Clone).ToList();
        }

        private static QuestProgress Clone(QuestProgress src)
        {
            return new QuestProgress
            {
                QuestId = src.QuestId,
                CurrentNodeId = src.CurrentNodeId,
                State = src.State,
                ObjectiveCounters = src.ObjectiveCounters?.ToDictionary(k => k.Key, v => v.Value) ?? new Dictionary<string, int>(),
                UpdatedUnixMs = src.UpdatedUnixMs
            };
        }

        public static bool EvaluateNodeConditions(QuestNode node, QuestEvaluationContext context)
        {
            if (node == null)
                return false;
            return EvaluateConditions(node.Conditions, node.ConditionMode, context);
        }

        public static List<QuestReward> ResolveRewardsForNode(QuestNode node, QuestEvaluationContext context)
        {
            var resolved = new List<QuestReward>();
            if (node == null)
                return resolved;

            if (node.Rewards != null && node.Rewards.Count > 0)
                resolved.AddRange(node.Rewards.Where(r => r != null));

            if (node.RewardRules == null || node.RewardRules.Count == 0)
                return resolved;

            for (int i = 0; i < node.RewardRules.Count; i++)
            {
                var rule = node.RewardRules[i];
                if (rule == null)
                    continue;

                if (!EvaluateConditions(rule.Conditions, rule.ConditionMode, context))
                    continue;

                if (rule.Rewards != null)
                    resolved.AddRange(rule.Rewards.Where(r => r != null));

                if (node.RewardRulesFirstMatchOnly)
                    break;
            }

            return resolved;
        }

        public static bool EvaluateConditions(
            IReadOnlyList<QuestCondition> conditions,
            QuestConditionMode mode,
            QuestEvaluationContext context)
        {
            if (conditions == null || conditions.Count == 0)
                return true;

            bool anyValid = false;
            bool anyMatched = false;
            for (int i = 0; i < conditions.Count; i++)
            {
                var condition = conditions[i];
                if (condition == null || condition.Type == QuestConditionType.None)
                    continue;

                anyValid = true;
                bool matched = EvaluateCondition(condition, context);
                if (mode == QuestConditionMode.All && !matched)
                    return false;
                if (matched)
                    anyMatched = true;
            }

            if (!anyValid)
                return true;
            return mode == QuestConditionMode.All ? true : anyMatched;
        }

        private static bool EvaluateCondition(QuestCondition condition, QuestEvaluationContext context)
        {
            context ??= new QuestEvaluationContext();
            int level = Mathf.Max(1, context.CharacterLevel);
            int faction = context.FactionId;
            int profession = context.ProfessionId;
            int visualProfession = context.VisualProfessionId > 0 ? context.VisualProfessionId : profession;
            int breed = context.BreedId;
            int gender = context.GenderId;
            var intValues = condition.IntValues ?? new List<int>();

            switch (condition.Type)
            {
                case QuestConditionType.LevelAtLeast:
                    return level >= condition.IntValue;
                case QuestConditionType.LevelAtMost:
                    return level <= condition.IntValue;
                case QuestConditionType.FactionEquals:
                    return faction == condition.IntValue;
                case QuestConditionType.FactionNotEquals:
                    return faction != condition.IntValue;
                case QuestConditionType.ProfessionEquals:
                    return profession == condition.IntValue;
                case QuestConditionType.ProfessionNotEquals:
                    return profession != condition.IntValue;
                case QuestConditionType.ProfessionIn:
                    return ContainsAny(intValues, condition.IntValue, profession);
                case QuestConditionType.ProfessionNotIn:
                    return !ContainsAny(intValues, condition.IntValue, profession);
                case QuestConditionType.VisualProfessionEquals:
                    return visualProfession == condition.IntValue;
                case QuestConditionType.VisualProfessionNotEquals:
                    return visualProfession != condition.IntValue;
                case QuestConditionType.VisualProfessionIn:
                    return ContainsAny(intValues, condition.IntValue, visualProfession);
                case QuestConditionType.VisualProfessionNotIn:
                    return !ContainsAny(intValues, condition.IntValue, visualProfession);
                case QuestConditionType.BreedEquals:
                    return breed == condition.IntValue;
                case QuestConditionType.BreedNotEquals:
                    return breed != condition.IntValue;
                case QuestConditionType.BreedIn:
                    return ContainsAny(intValues, condition.IntValue, breed);
                case QuestConditionType.BreedNotIn:
                    return !ContainsAny(intValues, condition.IntValue, breed);
                case QuestConditionType.GenderEquals:
                    return gender == condition.IntValue;
                case QuestConditionType.GenderNotEquals:
                    return gender != condition.IntValue;
                case QuestConditionType.GenderIn:
                    return ContainsAny(intValues, condition.IntValue, gender);
                case QuestConditionType.GenderNotIn:
                    return !ContainsAny(intValues, condition.IntValue, gender);
                case QuestConditionType.HasItem:
                case QuestConditionType.QuestStateIs:
                default:
                    return true;
            }
        }

        private static bool ContainsAny(List<int> values, int singleFallback, int candidate)
        {
            if (values != null && values.Count > 0)
                return values.Contains(candidate);
            if (singleFallback > 0)
                return singleFallback == candidate;
            return false;
        }
    }
}
