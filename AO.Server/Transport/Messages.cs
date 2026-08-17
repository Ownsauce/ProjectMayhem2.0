using System.Collections.Generic;

namespace AO.Server.Transport
{
    public sealed class ClientMessage
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int BreedId { get; set; } = 1;
        public int ProfessionId { get; set; } = 1;
        public int Level { get; set; } = 1;
        public int Sex { get; set; }
        public long Experience { get; set; }
        public int PlayfieldId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int SlotId { get; set; }
        public long InstanceId { get; set; }
        public string StatName { get; set; } = string.Empty;
        public int Amount { get; set; }
        public string ZoneLinkId { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string QuestOptionId { get; set; } = string.Empty;
        public int QuestNpcId { get; set; }
        public float TargetYaw { get; set; }
        public float SpawnAoX { get; set; }
        public float SpawnAoY { get; set; }
        public float SpawnAoZ { get; set; }
        public bool UseTeleportDefaultSpawn { get; set; }
        public int BootstrapVersion { get; set; }
        public string QuestTradeSessionId { get; set; } = string.Empty;
        public List<QuestTradeOfferEntry> QuestTradeOffers { get; set; } = new();
    }

    public sealed class ServerMessage
    {
        public string Type { get; set; } = string.Empty;
        public int Tick { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string StatName { get; set; } = string.Empty;
        public int Amount { get; set; }
        public int AvailableIp { get; set; }
        public int SlotId { get; set; }
        public long InstanceId { get; set; }
        public int BreedId { get; set; }
        public int ProfessionId { get; set; }
        public int Level { get; set; }
        public int Sex { get; set; }
        public long Experience { get; set; }
        public int PlayfieldId { get; set; }
        public string ZoneLinkId { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string InteractionType { get; set; } = string.Empty;
        public string InteractionId { get; set; } = string.Empty;
        public int QuestNpcId { get; set; }
        public string DialogTitle { get; set; } = string.Empty;
        public string DialogText { get; set; } = string.Empty;
        public bool DialogCanClose { get; set; } = true;
        public List<QuestDialogOptionSnapshot> QuestDialogOptions { get; set; } = new();
        public List<QuestJournalEntrySnapshot> QuestJournal { get; set; } = new();
        public List<QuestRewardGrantSnapshot> QuestRewardGrants { get; set; } = new();
        public bool IsAdminAction { get; set; }
        public float TargetYaw { get; set; }
        public int TemplatePlayfieldId { get; set; }
        public string RouteKey { get; set; } = string.Empty;
        public int ShardId { get; set; }
        public bool UsedReturnContext { get; set; }
        public float SpawnAoX { get; set; }
        public float SpawnAoY { get; set; }
        public float SpawnAoZ { get; set; }
        public int BootstrapVersion { get; set; }
        public List<PlayerSnapshot> Players { get; set; } = new();
        public List<RuntimeEntitySnapshot> RuntimeEntities { get; set; } = new();
        public List<RuntimeEntityDelta> RuntimeEntityDeltas { get; set; } = new();
        public List<LootItemSnapshot> LootItems { get; set; } = new();
        public int LootCredits { get; set; }
        public int LootTakenAoid { get; set; }
        public string QuestTradeSessionId { get; set; } = string.Empty;
        public string QuestTradeTitle { get; set; } = string.Empty;
        public string QuestTradePrompt { get; set; } = string.Empty;
        public List<QuestTradeRequirementSnapshot> QuestTradeRequirements { get; set; } = new();
        public List<QuestTradeOfferEntry> QuestTradeAutofill { get; set; } = new();
    }

    public sealed class PlayerSnapshot
    {
        public string SessionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int PlayfieldId { get; set; }
        public int TemplatePlayfieldId { get; set; }
        public string RouteKey { get; set; } = string.Empty;
        public int ShardId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public int Nano { get; set; }
        public int MaxNano { get; set; }
        public long Experience { get; set; }
    }

    public sealed class RuntimeEntitySnapshot
    {
        public string EntityId { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int PlayfieldId { get; set; }
        public int IdentityInstance { get; set; }
        public int? TemplateId { get; set; }
        public int? MeshId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float YawDegrees { get; set; }
        public string ImportKey { get; set; } = string.Empty;
        public string MeshName { get; set; } = string.Empty;
        public string InteractionType { get; set; } = string.Empty;
        public string InteractionId { get; set; } = string.Empty;
        public float InteractionRadius { get; set; }
        public string InteractionLabel { get; set; } = string.Empty;
        public bool InteractionEnabled { get; set; } = true;
        public int InteractionUseCount { get; set; }
        public float InteractionCooldownRemainingSeconds { get; set; }
        public string LastInteractionUtc { get; set; } = string.Empty;
        public int MaxHealth { get; set; }
        public int Health { get; set; }
        public int MaxNano { get; set; }
        public int Level { get; set; }
        public string CombatState { get; set; } = string.Empty;
        public string CombatTargetSessionId { get; set; } = string.Empty;
        public float AttackRange { get; set; }
        public float AttackRechargeSeconds { get; set; }
        public int DamageMin { get; set; }
        public int DamageMax { get; set; }
        public int LastDamage { get; set; }
        public int LastAttackTick { get; set; }
        public int TemporaryRemainingSeconds { get; set; }
        public long TemporaryExpiresUnixMs { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public sealed class RuntimeEntityDelta
    {
        public string Operation { get; set; } = "upsert";
        public RuntimeEntitySnapshot Entity { get; set; } = new();
    }

    public sealed class LootItemSnapshot
    {
        public int Aoid { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public int? Ql { get; set; }
    }

    public sealed class QuestDialogOptionSnapshot
    {
        public string OptionId { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string QuestId { get; set; } = string.Empty;
        public string NextNodeId { get; set; } = string.Empty;
        public bool IsExit { get; set; }
    }

    public sealed class QuestJournalEntrySnapshot
    {
        public string QuestId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CurrentNodeId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public long UpdatedUnixMs { get; set; }
    }

    public sealed class QuestTradeRequirementSnapshot
    {
        public int ItemAoid { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int RequiredCount { get; set; }
        public int AvailableCount { get; set; }
    }

    public sealed class QuestTradeOfferEntry
    {
        public int ItemAoid { get; set; }
        public int Count { get; set; }
    }

    public sealed class QuestRewardGrantSnapshot
    {
        public int Type { get; set; }
        public int TargetId { get; set; }
        public int Amount { get; set; }
    }
}
