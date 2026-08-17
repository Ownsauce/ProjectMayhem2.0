using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AO.Core.Characters;
using AO.Core.Items;
using AO.Server.Transport;

namespace AO.Server
{
    public sealed class QuestDialogRuntimeService
    {
        private const int RewardTypeExperience = 1;
        private const int RewardTypeStat = 2;
        private const int RewardTypeItem = 3;
        private const int CreditsStatId = 61;

        private readonly Dictionary<string, ServerQuestDefinition> _questsById = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, List<ServerQuestDefinition>> _startQuestsByNpcId = new();
        private readonly Dictionary<Guid, PlayerQuestRuntimeState> _playerStateBySession = new();
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
        private readonly Func<int, AO.Data.Core.ItemDefinition> _resolveItemDefinitionByAoid;

        public QuestDialogRuntimeService(
            ServerDataPaths paths,
            Func<int, AO.Data.Core.ItemDefinition> resolveItemDefinitionByAoid = null)
        {
            if (paths == null)
                throw new ArgumentNullException(nameof(paths));

            _resolveItemDefinitionByAoid = resolveItemDefinitionByAoid;
            LoadQuests(paths);
        }

        public void EnsurePlayer(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                return;
            if (!_playerStateBySession.ContainsKey(sessionId))
                _playerStateBySession[sessionId] = new PlayerQuestRuntimeState();
        }

        public void RemovePlayer(Guid sessionId)
        {
            if (sessionId == Guid.Empty)
                return;
            _playerStateBySession.Remove(sessionId);
        }

        public bool TryOpenDialog(Guid sessionId, Character character, int npcId, int characterLevel, out QuestDialogState dialog, out string message)
        {
            dialog = QuestDialogState.Empty(npcId);
            message = "No dialogue was available.";
            if (sessionId == Guid.Empty || npcId <= 0)
                return false;

            EnsurePlayer(sessionId);
            var state = _playerStateBySession[sessionId];
            state.PendingOptions.Clear();
            state.LastNpcId = npcId;

            if (_startQuestsByNpcId.TryGetValue(npcId, out var startQuests))
            {
                for (int i = 0; i < startQuests.Count; i++)
                {
                    var quest = startQuests[i];
                    if (quest == null || string.IsNullOrWhiteSpace(quest.QuestId))
                        continue;

                    if (!state.ProgressByQuestId.ContainsKey(quest.QuestId)
                        && characterLevel >= quest.MinLevel
                        && characterLevel <= quest.MaxLevel)
                    {
                        state.ProgressByQuestId[quest.QuestId] = new ServerQuestProgress
                        {
                            QuestId = quest.QuestId,
                            CurrentNodeId = quest.StartNodeId ?? string.Empty,
                            State = ServerQuestRuntimeState.Started,
                            UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                    }
                }
            }

            var bodyLines = new List<string>();
            var rewardGrants = new List<QuestRewardGrantSnapshot>();
            var options = new List<QuestDialogOptionSnapshot>();
            bool anyActive = false;
            foreach (var kvp in state.ProgressByQuestId.ToList())
            {
                var progress = kvp.Value;
                if (progress == null || progress.State != ServerQuestRuntimeState.Started)
                    continue;
                if (!_questsById.TryGetValue(progress.QuestId, out var quest) || quest == null)
                    continue;

                anyActive = true;
                var node = FindNode(quest, progress.CurrentNodeId);
                if (node == null)
                    continue;

                string line = !string.IsNullOrWhiteSpace(node.DialogueText)
                    ? node.DialogueText.Trim()
                    : (!string.IsNullOrWhiteSpace(node.Title) ? node.Title.Trim() : $"{quest.Name}");
                if (!string.IsNullOrWhiteSpace(line))
                    bodyLines.Add(line);

                if (node.TurnIn != null && node.TurnIn.Requirements != null && node.TurnIn.Requirements.Count > 0)
                {
                    string optionId = $"opt_{Guid.NewGuid():N}";
                    string label = string.IsNullOrWhiteSpace(node.TurnIn.OpenTradeLabel)
                        ? "Yes, here's the items."
                        : node.TurnIn.OpenTradeLabel.Trim();
                    state.PendingOptions[optionId] = new PendingOptionPayload
                    {
                        QuestId = quest.QuestId,
                        NextNodeId = progress.CurrentNodeId,
                        Kind = PendingOptionKind.OpenTrade
                    };
                    options.Add(new QuestDialogOptionSnapshot
                    {
                        OptionId = optionId,
                        Label = label,
                        QuestId = quest.QuestId,
                        NextNodeId = progress.CurrentNodeId,
                        IsExit = false
                    });
                }
                else if (node.NextNodeIds != null && node.NextNodeIds.Count > 0)
                {
                    for (int n = 0; n < node.NextNodeIds.Count; n++)
                    {
                        string nextNodeId = node.NextNodeIds[n];
                        if (string.IsNullOrWhiteSpace(nextNodeId))
                            continue;

                        var nextNode = FindNode(quest, nextNodeId);
                        string label = !string.IsNullOrWhiteSpace(nextNode?.Title)
                            ? nextNode.Title.Trim()
                            : $"Continue ({quest.Name})";

                        string optionId = $"opt_{Guid.NewGuid():N}";
                        state.PendingOptions[optionId] = new PendingOptionPayload
                        {
                            QuestId = quest.QuestId,
                            NextNodeId = nextNodeId,
                            Kind = PendingOptionKind.NextNode
                        };

                        options.Add(new QuestDialogOptionSnapshot
                        {
                            OptionId = optionId,
                            Label = label,
                            QuestId = quest.QuestId,
                            NextNodeId = nextNodeId,
                            IsExit = false
                        });
                    }
                }
                else
                {
                    var rewardLines = ApplyNodeRewards(character, node, rewardGrants);
                    progress.State = ServerQuestRuntimeState.Completed;
                    progress.UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    bodyLines.Add($"Quest complete: {quest.Name}");
                    for (int r = 0; r < rewardLines.Count; r++)
                    {
                        if (!string.IsNullOrWhiteSpace(rewardLines[r]))
                            bodyLines.Add(rewardLines[r]);
                    }
                }
            }

            if (!anyActive)
            {
                dialog = QuestDialogState.Empty(npcId);
                message = "This NPC has nothing for you right now.";
                return false;
            }

            if (options.Count == 0)
            {
                options.Add(new QuestDialogOptionSnapshot
                {
                    OptionId = "exit",
                    Label = "Goodbye",
                    IsExit = true
                });
            }

            dialog = new QuestDialogState
            {
                NpcId = npcId,
                Title = "Quest Dialogue",
                Body = string.Join("\n\n", bodyLines.Where(l => !string.IsNullOrWhiteSpace(l))),
                Options = options,
                Journal = BuildJournal(state),
                RewardGrants = rewardGrants,
                CanClose = true
            };

            message = "Quest dialogue ready.";
            return true;
        }

        public bool TryApplyOption(Guid sessionId, Character character, string optionId, int characterLevel, out QuestDialogState dialog, out string message)
        {
            dialog = QuestDialogState.Empty(0);
            message = "Invalid quest option.";
            if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(optionId))
                return false;

            if (!_playerStateBySession.TryGetValue(sessionId, out var state) || state == null)
                return false;

            if (string.Equals(optionId.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
            {
                dialog = new QuestDialogState
                {
                    NpcId = state.LastNpcId,
                    Title = "Dialogue",
                    Body = "Conversation ended.",
                    Options = new List<QuestDialogOptionSnapshot>(),
                    Journal = BuildJournal(state),
                    CanClose = true
                };
                message = "Conversation closed.";
                return true;
            }

            if (!state.PendingOptions.TryGetValue(optionId.Trim(), out var payload) || payload == null)
                return false;

            if (!state.ProgressByQuestId.TryGetValue(payload.QuestId, out var progress) || progress == null)
                return false;

            progress.CurrentNodeId = payload.NextNodeId ?? string.Empty;
            progress.UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            return TryOpenDialog(sessionId, character, state.LastNpcId, characterLevel, out dialog, out message);
        }

        public bool TryBeginTradeForOption(
            Guid sessionId,
            Character character,
            string optionId,
            out QuestTradeState trade,
            out string message)
        {
            trade = QuestTradeState.Empty;
            message = "Trade unavailable.";
            if (sessionId == Guid.Empty || character == null || string.IsNullOrWhiteSpace(optionId))
                return false;

            if (!_playerStateBySession.TryGetValue(sessionId, out var state) || state == null)
                return false;

            if (!state.PendingOptions.TryGetValue(optionId.Trim(), out var payload)
                || payload == null
                || payload.Kind != PendingOptionKind.OpenTrade)
                return false;

            if (!state.ProgressByQuestId.TryGetValue(payload.QuestId, out var progress) || progress == null)
                return false;
            if (!_questsById.TryGetValue(payload.QuestId, out var quest) || quest == null)
                return false;

            var node = FindNode(quest, progress.CurrentNodeId);
            var turnIn = node?.TurnIn;
            if (node == null || turnIn == null || turnIn.Requirements == null || turnIn.Requirements.Count == 0)
                return false;

            var requirements = new List<QuestTradeRequirementSnapshot>();
            var autofill = new List<QuestTradeOfferEntry>();
            for (int i = 0; i < turnIn.Requirements.Count; i++)
            {
                var req = turnIn.Requirements[i];
                if (req == null || req.ItemAoid <= 0 || req.Count <= 0)
                    continue;

                int available = CountItems(character, req.ItemAoid);
                string itemName = ResolveItemName(character, req.ItemAoid);
                requirements.Add(new QuestTradeRequirementSnapshot
                {
                    ItemAoid = req.ItemAoid,
                    ItemName = itemName,
                    RequiredCount = req.Count,
                    AvailableCount = available
                });

                int offerCount = Math.Min(req.Count, available);
                if (offerCount > 0)
                {
                    autofill.Add(new QuestTradeOfferEntry
                    {
                        ItemAoid = req.ItemAoid,
                        Count = offerCount
                    });
                }
            }

            string sessionTradeId = $"trade_{Guid.NewGuid():N}";
            state.PendingTrades[sessionTradeId] = new PendingTradePayload
            {
                QuestId = payload.QuestId,
                NodeId = progress.CurrentNodeId,
                NextNodeId = turnIn.SuccessNextNodeId ?? string.Empty,
                FailureText = turnIn.FailureText ?? "What is this? This is not what I asked for.",
                Requirements = turnIn.Requirements
            };

            trade = new QuestTradeState
            {
                SessionId = sessionTradeId,
                Title = string.IsNullOrWhiteSpace(turnIn.Title) ? "Quest Trade" : turnIn.Title.Trim(),
                Prompt = string.IsNullOrWhiteSpace(turnIn.Prompt)
                    ? "Confirm the items you want to hand over."
                    : turnIn.Prompt.Trim(),
                Requirements = requirements,
                Autofill = autofill
            };

            message = "Quest trade session ready.";
            return true;
        }

        public bool TrySubmitTrade(
            Guid sessionId,
            Character character,
            string tradeSessionId,
            IReadOnlyList<QuestTradeOfferEntry> offers,
            int characterLevel,
            out QuestDialogState dialog,
            out string message)
        {
            dialog = QuestDialogState.Empty(0);
            message = "Trade failed.";
            if (sessionId == Guid.Empty || character == null || string.IsNullOrWhiteSpace(tradeSessionId))
                return false;

            if (!_playerStateBySession.TryGetValue(sessionId, out var state) || state == null)
                return false;
            if (!state.PendingTrades.TryGetValue(tradeSessionId.Trim(), out var trade) || trade == null)
                return false;
            if (!state.ProgressByQuestId.TryGetValue(trade.QuestId, out var progress) || progress == null)
                return false;

            var offeredByAoid = new Dictionary<int, int>();
            if (offers != null)
            {
                for (int i = 0; i < offers.Count; i++)
                {
                    var o = offers[i];
                    if (o == null || o.ItemAoid <= 0 || o.Count <= 0)
                        continue;
                    offeredByAoid.TryGetValue(o.ItemAoid, out int current);
                    offeredByAoid[o.ItemAoid] = current + o.Count;
                }
            }

            // Validate requirements and inventory availability.
            for (int i = 0; i < trade.Requirements.Count; i++)
            {
                var req = trade.Requirements[i];
                if (req == null || req.ItemAoid <= 0 || req.Count <= 0)
                    continue;
                offeredByAoid.TryGetValue(req.ItemAoid, out int offeredCount);
                if (offeredCount < req.Count)
                {
                    message = string.IsNullOrWhiteSpace(trade.FailureText)
                        ? "What is this? This is not what I asked for."
                        : trade.FailureText;
                    return TryOpenDialog(sessionId, character, state.LastNpcId, characterLevel, out dialog, out _);
                }

                int available = CountItems(character, req.ItemAoid);
                if (available < req.Count)
                {
                    message = "You no longer have the required items.";
                    return TryOpenDialog(sessionId, character, state.LastNpcId, characterLevel, out dialog, out _);
                }
            }

            // Consume required items.
            for (int i = 0; i < trade.Requirements.Count; i++)
            {
                var req = trade.Requirements[i];
                if (req == null || req.ItemAoid <= 0 || req.Count <= 0)
                    continue;
                if (!ConsumeItems(character, req.ItemAoid, req.Count))
                {
                    message = "Trade failed while consuming items.";
                    return false;
                }
            }

            state.PendingTrades.Remove(tradeSessionId.Trim());
            progress.CurrentNodeId = trade.NextNodeId ?? string.Empty;
            progress.UpdatedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            message = "Items accepted.";
            return TryOpenDialog(sessionId, character, state.LastNpcId, characterLevel, out dialog, out _);
        }

        public bool TryCancelTrade(Guid sessionId, Character character, string tradeSessionId, int characterLevel, out QuestDialogState dialog, out string message)
        {
            dialog = QuestDialogState.Empty(0);
            message = "Trade canceled.";
            if (sessionId == Guid.Empty || string.IsNullOrWhiteSpace(tradeSessionId))
                return false;
            if (!_playerStateBySession.TryGetValue(sessionId, out var state) || state == null)
                return false;

            state.PendingTrades.Remove(tradeSessionId.Trim());
            return TryOpenDialog(sessionId, character, state.LastNpcId, characterLevel, out dialog, out _);
        }

        public List<QuestJournalEntrySnapshot> BuildJournal(Guid sessionId)
        {
            if (!_playerStateBySession.TryGetValue(sessionId, out var state) || state == null)
                return new List<QuestJournalEntrySnapshot>();
            return BuildJournal(state);
        }

        private List<QuestJournalEntrySnapshot> BuildJournal(PlayerQuestRuntimeState state)
        {
            var list = new List<QuestJournalEntrySnapshot>();
            foreach (var kvp in state.ProgressByQuestId)
            {
                var progress = kvp.Value;
                if (progress == null)
                    continue;

                _questsById.TryGetValue(progress.QuestId, out var quest);
                list.Add(new QuestJournalEntrySnapshot
                {
                    QuestId = progress.QuestId ?? string.Empty,
                    Name = quest?.Name ?? progress.QuestId ?? string.Empty,
                    Description = quest?.Description ?? string.Empty,
                    CurrentNodeId = progress.CurrentNodeId ?? string.Empty,
                    State = progress.State.ToString(),
                    UpdatedUnixMs = progress.UpdatedUnixMs
                });
            }

            return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static ServerQuestNode FindNode(ServerQuestDefinition quest, string nodeId)
        {
            if (quest?.Nodes == null || string.IsNullOrWhiteSpace(nodeId))
                return null;
            for (int i = 0; i < quest.Nodes.Count; i++)
            {
                var node = quest.Nodes[i];
                if (node == null)
                    continue;
                if (string.Equals(node.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                    return node;
            }

            return null;
        }

        private static int CountItems(Character character, int itemAoid)
        {
            if (character?.Inventory?.Items == null || itemAoid <= 0)
                return 0;

            int total = 0;
            for (int i = 0; i < character.Inventory.Items.Count; i++)
            {
                ItemInstance inst = character.Inventory.Items[i];
                if (inst?.Definition == null || inst.Definition.AOID != itemAoid)
                    continue;
                total += Math.Max(1, inst.Quantity);
            }
            return total;
        }

        private static string ResolveItemName(Character character, int itemAoid)
        {
            if (character?.Inventory?.Items != null)
            {
                for (int i = 0; i < character.Inventory.Items.Count; i++)
                {
                    var inst = character.Inventory.Items[i];
                    if (inst?.Definition == null || inst.Definition.AOID != itemAoid)
                        continue;
                    if (!string.IsNullOrWhiteSpace(inst.Definition.Name))
                        return inst.Definition.Name.Trim();
                }
            }

            return $"Item {itemAoid}";
        }

        private static bool ConsumeItems(Character character, int itemAoid, int requiredCount)
        {
            if (character?.Inventory?.Items == null || requiredCount <= 0 || itemAoid <= 0)
                return false;

            int remaining = requiredCount;
            // Work over a copy because RemoveItem mutates inventory.
            var items = character.Inventory.Items.ToList();
            for (int i = 0; i < items.Count && remaining > 0; i++)
            {
                var inst = items[i];
                if (inst?.Definition == null || inst.Definition.AOID != itemAoid)
                    continue;

                int qty = Math.Max(1, inst.Quantity);
                if (qty <= remaining)
                {
                    remaining -= qty;
                    character.Inventory.RemoveItem(inst);
                }
                else
                {
                    inst.Quantity = qty - remaining;
                    remaining = 0;
                }
            }

            return remaining <= 0;
        }

        private List<string> ApplyNodeRewards(
            Character character,
            ServerQuestNode node,
            List<QuestRewardGrantSnapshot> rewardGrants)
        {
            var lines = new List<string>();
            if (character == null || node == null)
                return lines;

            // Legacy direct rewards
            if (node.Rewards != null && node.Rewards.Count > 0)
                ApplyRewardEntries(character, node.Rewards, lines, rewardGrants);

            // Rule-based rewards
            if (node.RewardRules != null && node.RewardRules.Count > 0)
            {
                for (int i = 0; i < node.RewardRules.Count; i++)
                {
                    var rule = node.RewardRules[i];
                    if (rule?.Rewards == null || rule.Rewards.Count == 0)
                        continue;
                    if (!EvaluateConditions(character, rule.Conditions))
                        continue;

                    ApplyRewardEntries(character, rule.Rewards, lines, rewardGrants);
                    if (node.RewardRulesFirstMatchOnly)
                        break;
                }
            }

            return lines;
        }

        private void ApplyRewardEntries(
            Character character,
            List<ServerQuestRewardEntry> rewards,
            List<string> lines,
            List<QuestRewardGrantSnapshot> rewardGrants)
        {
            if (character == null || rewards == null)
                return;

            for (int i = 0; i < rewards.Count; i++)
            {
                var reward = rewards[i];
                if (reward == null)
                    continue;

                int amount = Math.Max(0, reward.Amount);
                switch (reward.Type)
                {
                    case RewardTypeExperience:
                    {
                        if (amount <= 0)
                            continue;
                        character.AddExperience(amount);
                        rewardGrants?.Add(new QuestRewardGrantSnapshot
                        {
                            Type = RewardTypeExperience,
                            TargetId = 0,
                            Amount = amount
                        });
                        lines.Add($"Reward: +{amount} XP");
                        break;
                    }
                    case RewardTypeStat:
                    {
                        if (amount <= 0)
                            continue;
                        int statId = reward.TargetId > 0 ? reward.TargetId : CreditsStatId;
                        if (statId == CreditsStatId)
                        {
                            int current = character.StatsContainer.GetBaseStat(CreditsStatId);
                            character.StatsContainer.SetBaseStat(CreditsStatId, current + amount);
                            rewardGrants?.Add(new QuestRewardGrantSnapshot
                            {
                                Type = RewardTypeStat,
                                TargetId = CreditsStatId,
                                Amount = amount
                            });
                            lines.Add($"Reward: +{amount} Credits");
                        }
                        break;
                    }
                    case RewardTypeItem:
                    {
                        if (reward.TargetId <= 0 || amount <= 0)
                            continue;

                        for (int n = 0; n < amount; n++)
                        {
                            if (!TryGrantItem(character, reward.TargetId))
                                break;
                        }
                        rewardGrants?.Add(new QuestRewardGrantSnapshot
                        {
                            Type = RewardTypeItem,
                            TargetId = reward.TargetId,
                            Amount = amount
                        });
                        string itemName = ResolveRewardItemName(reward.TargetId);
                        lines.Add($"Reward: {itemName} x{amount}");
                        break;
                    }
                }
            }
        }

        private string ResolveRewardItemName(int itemAoid)
        {
            if (itemAoid <= 0 || _resolveItemDefinitionByAoid == null)
                return $"Item {itemAoid}";

            var def = _resolveItemDefinitionByAoid(itemAoid);
            if (def == null || string.IsNullOrWhiteSpace(def.Name))
                return $"Item {itemAoid}";

            return def.Name.Trim();
        }

        private bool TryGrantItem(Character character, int itemAoid)
        {
            if (character?.Inventory == null || itemAoid <= 0 || _resolveItemDefinitionByAoid == null)
                return false;

            var dataDef = _resolveItemDefinitionByAoid(itemAoid);
            if (dataDef == null)
                return false;

            var coreDef = new AO.Core.Items.ItemDefinition(
                dataDef.Name ?? $"Item_{itemAoid}",
                dataDef.Id > 0 ? dataDef.Id : itemAoid,
                dataDef.SlotType);

            if (dataDef.StatModifiers != null)
            {
                for (int i = 0; i < dataDef.StatModifiers.Count; i++)
                {
                    var mod = dataDef.StatModifiers[i];
                    if (mod == null)
                        continue;
                    coreDef.AddModifier(new AO.Core.Modifiers.StatModifier(mod.StatId, mod.Value));
                }
            }

            long instanceId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L + itemAoid;
            var instance = new AO.Core.Items.ItemInstance(coreDef, quantity: 1, instanceId: instanceId, containerCapacity: 0);
            return character.Inventory.TryAddToMain(instance);
        }

        private static bool EvaluateConditions(Character character, List<ServerQuestCondition> conditions)
        {
            if (conditions == null || conditions.Count == 0)
                return true;

            for (int i = 0; i < conditions.Count; i++)
            {
                var condition = conditions[i];
                if (condition == null)
                    continue;

                switch (condition.Type)
                {
                    // Profession condition
                    // IntValue: exact profession id
                    case 5:
                    {
                        int requiredProfession = condition.IntValue;
                        int actualProfession = character?.ProfessionId ?? 0;
                        if (requiredProfession > 0 && actualProfession != requiredProfession)
                            return false;
                        break;
                    }
                    default:
                    {
                        // Unknown condition types are treated as non-blocking in prototype mode.
                        break;
                    }
                }
            }

            return true;
        }

        private void LoadQuests(ServerDataPaths paths)
        {
            _questsById.Clear();
            _startQuestsByNpcId.Clear();

            string fallbackQuestRoot = Path.Combine(paths.AoDataRoot, "Quests");
            string questRoot = paths.ServerQuestsRoot;
            if (!Directory.Exists(questRoot)
                || Directory.GetFiles(questRoot, "*.quest.json", SearchOption.TopDirectoryOnly).Length == 0)
            {
                questRoot = fallbackQuestRoot;
            }
            if (!Directory.Exists(questRoot))
                return;

            string[] files = Directory.GetFiles(questRoot, "*.quest.json", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                try
                {
                    string raw = File.ReadAllText(file);
                    var quest = JsonSerializer.Deserialize<ServerQuestDefinition>(raw, _jsonOptions);
                    if (quest == null || string.IsNullOrWhiteSpace(quest.QuestId))
                        continue;

                    quest.Nodes ??= new List<ServerQuestNode>();
                    quest.StartNodeId ??= string.Empty;
                    quest.Name ??= quest.QuestId;
                    quest.Description ??= string.Empty;
                    if (quest.MinLevel <= 0)
                        quest.MinLevel = 1;
                    if (quest.MaxLevel < quest.MinLevel)
                        quest.MaxLevel = 220;

                    _questsById[quest.QuestId] = quest;
                    if (quest.StartNpcId > 0)
                    {
                        if (!_startQuestsByNpcId.TryGetValue(quest.StartNpcId, out var list))
                        {
                            list = new List<ServerQuestDefinition>();
                            _startQuestsByNpcId[quest.StartNpcId] = list;
                        }

                        list.Add(quest);
                    }
                }
                catch
                {
                    // Ignore malformed quest files on server boot for now.
                }
            }
        }

        private sealed class PlayerQuestRuntimeState
        {
            public int LastNpcId;
            public Dictionary<string, ServerQuestProgress> ProgressByQuestId { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, PendingOptionPayload> PendingOptions { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, PendingTradePayload> PendingTrades { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PendingOptionPayload
        {
            public string QuestId { get; set; } = string.Empty;
            public string NextNodeId { get; set; } = string.Empty;
            public PendingOptionKind Kind { get; set; } = PendingOptionKind.NextNode;
        }

        private enum PendingOptionKind
        {
            NextNode = 0,
            OpenTrade = 1
        }

        private sealed class PendingTradePayload
        {
            public string QuestId { get; set; } = string.Empty;
            public string NodeId { get; set; } = string.Empty;
            public string NextNodeId { get; set; } = string.Empty;
            public string FailureText { get; set; } = string.Empty;
            public List<ServerQuestTurnInRequirement> Requirements { get; set; } = new();
        }

        private sealed class ServerQuestDefinition
        {
            public string QuestId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public int StartNpcId { get; set; }
            public int MinLevel { get; set; } = 1;
            public int MaxLevel { get; set; } = 220;
            public string StartNodeId { get; set; } = string.Empty;
            public List<ServerQuestNode> Nodes { get; set; } = new();
        }

        private sealed class ServerQuestNode
        {
            public string NodeId { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string DialogueText { get; set; } = string.Empty;
            public List<string> NextNodeIds { get; set; } = new();
            public List<ServerQuestRewardEntry> Rewards { get; set; } = new();
            public List<ServerQuestRewardRule> RewardRules { get; set; } = new();
            public bool RewardRulesFirstMatchOnly { get; set; } = true;
            public ServerQuestTurnIn TurnIn { get; set; }
        }

        private sealed class ServerQuestRewardRule
        {
            public List<ServerQuestCondition> Conditions { get; set; } = new();
            public List<ServerQuestRewardEntry> Rewards { get; set; } = new();
        }

        private sealed class ServerQuestCondition
        {
            public int Type { get; set; }
            public int IntValue { get; set; }
            public List<int> IntValues { get; set; } = new();
            public string StringValue { get; set; } = string.Empty;
            public List<string> StringValues { get; set; } = new();
        }

        private sealed class ServerQuestRewardEntry
        {
            public int Type { get; set; }
            public int TargetId { get; set; }
            public int Amount { get; set; }
        }

        private sealed class ServerQuestTurnIn
        {
            public string Title { get; set; } = string.Empty;
            public string Prompt { get; set; } = string.Empty;
            public string OpenTradeLabel { get; set; } = "Yes, here's the items.";
            public string FailureText { get; set; } = "What is this? This is not what I asked for.";
            public string SuccessNextNodeId { get; set; } = string.Empty;
            public List<ServerQuestTurnInRequirement> Requirements { get; set; } = new();
        }

        private sealed class ServerQuestTurnInRequirement
        {
            public int ItemAoid { get; set; }
            public int Count { get; set; }
        }

        private enum ServerQuestRuntimeState
        {
            Unknown = 0,
            Started = 1,
            Completed = 2,
            Failed = 3
        }

        private sealed class ServerQuestProgress
        {
            public string QuestId { get; set; } = string.Empty;
            public string CurrentNodeId { get; set; } = string.Empty;
            public ServerQuestRuntimeState State { get; set; } = ServerQuestRuntimeState.Unknown;
            public long UpdatedUnixMs { get; set; }
        }
    }

    public sealed class QuestDialogState
    {
        public int NpcId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public List<QuestDialogOptionSnapshot> Options { get; set; } = new();
        public List<QuestJournalEntrySnapshot> Journal { get; set; } = new();
        public List<QuestRewardGrantSnapshot> RewardGrants { get; set; } = new();
        public bool CanClose { get; set; } = true;

        public static QuestDialogState Empty(int npcId) => new()
        {
            NpcId = npcId,
            Title = "Dialogue",
            Body = string.Empty,
            Options = new List<QuestDialogOptionSnapshot>(),
            Journal = new List<QuestJournalEntrySnapshot>(),
            RewardGrants = new List<QuestRewardGrantSnapshot>(),
            CanClose = true
        };
    }

    public sealed class QuestTradeState
    {
        public string SessionId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public List<QuestTradeRequirementSnapshot> Requirements { get; set; } = new();
        public List<QuestTradeOfferEntry> Autofill { get; set; } = new();

        public static QuestTradeState Empty => new();
    }
}
