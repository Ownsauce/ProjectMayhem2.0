using System;
using System.Collections.Generic;

namespace AO.Unity.Quests
{
    [Serializable]
    public sealed class QuestAuthoringFile
    {
        public int Version = 1;
        public List<QuestDefinition> Quests = new();
    }

    [Serializable]
    public sealed class QuestDefinition
    {
        public string QuestId;
        public string Name;
        public string Description;
        public int StartNpcId;
        public int MinLevel = 1;
        public int MaxLevel = 220;
        public bool Repeatable = false;
        public QuestConditionMode StartConditionMode = QuestConditionMode.All;
        public List<QuestCondition> StartConditions = new();
        public string StartNodeId;
        public List<QuestNode> Nodes = new();
    }

    public enum QuestNodeType
    {
        Dialogue,
        Objective,
        Condition,
        Reward,
        Branch,
        End
    }

    public enum QuestObjectiveType
    {
        None,
        Talk,
        Kill,
        Target,
        Select,
        Collect,
        UseItem
    }

    [Serializable]
    public sealed class QuestNode
    {
        public string NodeId;
        public QuestNodeType NodeType;
        public string Title;
        public string DialogueText;
        public QuestObjectiveType ObjectiveType;
        public int TargetId;
        public string TargetFamilyId;
        public List<int> TargetTemplateIds = new();
        public int ItemId;
        public int RequiredCount;
        public QuestConditionMode ConditionMode = QuestConditionMode.All;
        public List<QuestCondition> Conditions = new();
        public List<QuestReward> Rewards = new();
        public List<QuestRewardRule> RewardRules = new();
        public bool RewardRulesFirstMatchOnly = true;
        public List<string> NextNodeIds = new();
    }

    public enum QuestConditionMode
    {
        All,
        Any
    }

    public enum QuestConditionType
    {
        None,
        LevelAtLeast,
        LevelAtMost,
        FactionEquals,
        FactionNotEquals,
        ProfessionEquals,
        ProfessionNotEquals,
        ProfessionIn,
        ProfessionNotIn,
        VisualProfessionEquals,
        VisualProfessionNotEquals,
        VisualProfessionIn,
        VisualProfessionNotIn,
        BreedEquals,
        BreedNotEquals,
        BreedIn,
        BreedNotIn,
        GenderEquals,
        GenderNotEquals,
        GenderIn,
        GenderNotIn,
        HasItem,
        QuestStateIs
    }

    [Serializable]
    public sealed class QuestCondition
    {
        public QuestConditionType Type;
        public int IntValue;
        public List<int> IntValues = new();
        public string StringValue;
        public List<string> StringValues = new();
    }

    public enum QuestRewardType
    {
        None,
        Experience,
        Credits,
        Item,
        Stat
    }

    [Serializable]
    public sealed class QuestReward
    {
        public QuestRewardType Type;
        public int TargetId;
        public int Amount;
    }

    [Serializable]
    public sealed class QuestRewardRule
    {
        public QuestConditionMode ConditionMode = QuestConditionMode.All;
        public List<QuestCondition> Conditions = new();
        public List<QuestReward> Rewards = new();
    }

    public enum QuestRuntimeState
    {
        Unknown,
        Started,
        Completed,
        Failed
    }

    [Serializable]
    public sealed class QuestProgress
    {
        public string QuestId;
        public string CurrentNodeId;
        public QuestRuntimeState State;
        public Dictionary<string, int> ObjectiveCounters = new();
        public long UpdatedUnixMs;
    }

    [Serializable]
    public sealed class QuestTargetFamilyFile
    {
        public int Version = 1;
        public List<QuestTargetFamily> Families = new();
    }

    [Serializable]
    public sealed class QuestTargetFamily
    {
        public string FamilyId;
        public string Name;
        public int? PlayfieldId;
        public List<int> TemplateIds = new();
    }
}
