using System;
using System.Collections.Generic;
using System.Linq;

namespace AO.Unity.Quests
{
    public sealed class QuestValidationResult
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public bool IsValid => Errors.Count == 0;
    }

    public static class QuestValidator
    {
        public static QuestValidationResult Validate(QuestAuthoringFile file)
        {
            var result = new QuestValidationResult();
            if (file == null)
            {
                result.Errors.Add("Quest file is null.");
                return result;
            }

            if (file.Quests == null)
            {
                result.Errors.Add("Quest list is null.");
                return result;
            }

            var questIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < file.Quests.Count; i++)
            {
                var quest = file.Quests[i];
                if (quest == null)
                {
                    result.Errors.Add($"Quest[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(quest.QuestId))
                    result.Errors.Add($"Quest[{i}] is missing QuestId.");
                else if (!questIds.Add(quest.QuestId.Trim()))
                    result.Errors.Add($"Duplicate QuestId '{quest.QuestId}'.");

                ValidateQuest(quest, result);
            }

            return result;
        }

        private static void ValidateQuest(QuestDefinition quest, QuestValidationResult result)
        {
            if (quest.MinLevel < 1)
                result.Errors.Add($"Quest '{quest.QuestId}' MinLevel must be >= 1.");
            if (quest.MaxLevel < quest.MinLevel)
                result.Errors.Add($"Quest '{quest.QuestId}' MaxLevel must be >= MinLevel.");

            if (quest.Nodes == null || quest.Nodes.Count == 0)
            {
                result.Errors.Add($"Quest '{quest.QuestId}' has no nodes.");
                return;
            }

            if (string.IsNullOrWhiteSpace(quest.StartNodeId))
                result.Errors.Add($"Quest '{quest.QuestId}' is missing StartNodeId.");

            var nodesById = new Dictionary<string, QuestNode>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < quest.Nodes.Count; i++)
            {
                var node = quest.Nodes[i];
                if (node == null)
                {
                    result.Errors.Add($"Quest '{quest.QuestId}' Node[{i}] is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.NodeId))
                {
                    result.Errors.Add($"Quest '{quest.QuestId}' Node[{i}] is missing NodeId.");
                    continue;
                }

                if (!nodesById.TryAdd(node.NodeId.Trim(), node))
                    result.Errors.Add($"Quest '{quest.QuestId}' has duplicate NodeId '{node.NodeId}'.");
            }

            if (!string.IsNullOrWhiteSpace(quest.StartNodeId) && !nodesById.ContainsKey(quest.StartNodeId.Trim()))
                result.Errors.Add($"Quest '{quest.QuestId}' StartNodeId '{quest.StartNodeId}' was not found.");

            foreach (var node in nodesById.Values)
            {
                var next = node.NextNodeIds ?? new List<string>();
                for (int i = 0; i < next.Count; i++)
                {
                    string nextId = next[i];
                    if (string.IsNullOrWhiteSpace(nextId))
                        continue;

                    if (!nodesById.ContainsKey(nextId.Trim()))
                        result.Errors.Add($"Quest '{quest.QuestId}' Node '{node.NodeId}' points to missing node '{nextId}'.");
                }

                bool shouldLink = node.NodeType != QuestNodeType.End && node.NodeType != QuestNodeType.Reward;
                if (shouldLink && next.Count == 0)
                    result.Warnings.Add($"Quest '{quest.QuestId}' Node '{node.NodeId}' has no outgoing path.");

                if (node.NodeType == QuestNodeType.Objective
                    && (node.ObjectiveType == QuestObjectiveType.Target || node.ObjectiveType == QuestObjectiveType.Select))
                {
                    bool hasTargetId = node.TargetId > 0;
                    bool hasTemplateIds = node.TargetTemplateIds != null && node.TargetTemplateIds.Any(id => id > 0);
                    bool hasFamily = !string.IsNullOrWhiteSpace(node.TargetFamilyId);
                    if (!hasTargetId && !hasTemplateIds && !hasFamily)
                    {
                        result.Warnings.Add(
                            $"Quest '{quest.QuestId}' Objective node '{node.NodeId}' has no target filter. " +
                            "Set TargetId, TargetTemplateIds, or TargetFamilyId.");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(quest.StartNodeId))
                return;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(quest.StartNodeId.Trim());
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                if (!visited.Add(id))
                    continue;

                if (!nodesById.TryGetValue(id, out var node) || node?.NextNodeIds == null)
                    continue;

                foreach (var nextId in node.NextNodeIds)
                {
                    if (string.IsNullOrWhiteSpace(nextId))
                        continue;
                    queue.Enqueue(nextId.Trim());
                }
            }

            var unreachable = nodesById.Keys.Where(id => !visited.Contains(id)).ToList();
            foreach (var id in unreachable)
                result.Warnings.Add($"Quest '{quest.QuestId}' Node '{id}' is unreachable from StartNodeId.");
        }
    }
}
