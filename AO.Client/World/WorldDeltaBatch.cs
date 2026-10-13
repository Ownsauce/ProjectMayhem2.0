using System;
using System.Collections.Generic;

namespace AO.Client.World
{
    public static class NanoCastTargetPolicy
    {
        public static (int IdentityType, int IdentityInstance) Resolve(int selfId, int canFlags, int nanoFlags, NearbyEntity selected)
        {
            if (selfId <= 0) throw new ArgumentOutOfRangeException(nameof(selfId));
            const int friendly = 1 << 21, hostile = 1 << 22, self = 1 << 23;
            bool selfOnly = (canFlags & self) != 0;
            bool isHostile = (nanoFlags & (1 << 15)) != 0
                || ((canFlags & hostile) != 0 && (canFlags & (friendly | self)) == 0);
            if (selfOnly) return (50000, selfId);
            if (isHostile)
            {
                if (selected == null || selected.IdentityInstance <= 0
                    || (selected.IdentityType == 50000 && selected.IdentityInstance == selfId))
                    throw new InvalidOperationException("This nano needs a hostile target.");
                return (selected.IdentityType, selected.IdentityInstance);
            }
            // Unflagged personal buffs default to the caster with an NPC combat target selected.
            // Keep player recipients and explicitly supported friendly NPC/pet recipients.
            if (selected != null && selected.IdentityInstance > 0
                && (selected.Kind == NearbyEntityKind.Player || (canFlags & friendly) != 0))
                return (selected.IdentityType, selected.IdentityInstance);
            return (50000, selfId);
        }
    }

    public enum CombatUpdateKind { Started, Stopped, Hit, Miss }

    public enum NanoCastUpdateKind { Started, Finished, Interrupted }

    public sealed class NanoCastUpdate
    {
        public NanoCastUpdate(NanoCastUpdateKind kind, int casterType, int casterInstance,
            int nanoId, int targetType = 0, int targetInstance = 0, int result = 0)
        {
            Kind = kind; CasterType = casterType; CasterInstance = casterInstance;
            NanoId = nanoId; TargetType = targetType; TargetInstance = targetInstance; Result = result;
        }
        public NanoCastUpdateKind Kind { get; }
        public int CasterType { get; }
        public int CasterInstance { get; }
        public int NanoId { get; }
        public int TargetType { get; }
        public int TargetInstance { get; }
        public int Result { get; }
    }

    public sealed class CombatUpdate
    {
        public CombatUpdate(CombatUpdateKind kind, int attackerType, int attackerInstance,
            int targetType = 0, int targetInstance = 0, int damage = 0, int specialStatId = 0, int handSlot = 0,
            bool targetDied = false)
        {
            Kind = kind; AttackerType = attackerType; AttackerInstance = attackerInstance;
            TargetType = targetType; TargetInstance = targetInstance; Damage = damage; SpecialStatId = specialStatId; HandSlot = handSlot;
            TargetDied = targetDied;
        }
        public CombatUpdateKind Kind { get; }
        public int AttackerType { get; }
        public int AttackerInstance { get; }
        public int TargetType { get; }
        public int TargetInstance { get; }
        public int Damage { get; }
        public int SpecialStatId { get; }
        public int HandSlot { get; }
        public bool TargetDied { get; }
    }

    public sealed class SpecialCooldownUpdate
    {
        public SpecialCooldownUpdate(int statId, int seconds) { StatId = statId; Seconds = seconds; }
        public int StatId { get; }
        public int Seconds { get; }
    }

    public sealed class NanoEffectUpdate
    {
        public NanoEffectUpdate(int nanoId, int durationCentiseconds) { NanoId = nanoId; DurationCentiseconds = durationCentiseconds; }
        public int NanoId { get; }
        // Zero means an explicit removal; positive durations come from the server.
        public int DurationCentiseconds { get; }
    }

    public sealed class WorldDeltaBatch
    {
        public WorldDeltaBatch(IReadOnlyList<WorldEntityDelta> deltas,
            IReadOnlyList<NearbyEntity> snapshot, int packetsObserved,
            int movementPacketsObserved = 0, int unmatchedMovementPackets = 0,
            IReadOnlyDictionary<string, int> packetKindsObserved = null,
            IReadOnlyDictionary<string, string> packetSamplesObserved = null,
            InventorySnapshot inventory = null,
            IReadOnlyList<string> serverMessages = null,
            WorldBootstrapResult zoneTransfer = null,
            IReadOnlyList<InventorySnapshot> containerInventories = null,
            IReadOnlyList<NanoEffectUpdate> nanoEffects = null,
            IReadOnlyList<CombatUpdate> combatUpdates = null,
            IReadOnlyList<SpecialCooldownUpdate> specialCooldowns = null,
            IReadOnlyList<NanoCastUpdate> nanoCasts = null,
            IReadOnlyList<NanoEffectUpdate> nanoEffectSnapshot = null)
        {
            Deltas = deltas ?? throw new ArgumentNullException(nameof(deltas));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            PacketsObserved = packetsObserved;
            MovementPacketsObserved = movementPacketsObserved;
            UnmatchedMovementPackets = unmatchedMovementPackets;
            PacketKindsObserved = packetKindsObserved
                ?? new Dictionary<string, int>();
            PacketSamplesObserved = packetSamplesObserved
                ?? new Dictionary<string, string>();
            Inventory = inventory;
            ServerMessages = serverMessages ?? Array.Empty<string>();
            ZoneTransfer = zoneTransfer;
            ContainerInventories = containerInventories ?? Array.Empty<InventorySnapshot>();
            NanoEffects = nanoEffects ?? Array.Empty<NanoEffectUpdate>();
            CombatUpdates = combatUpdates ?? Array.Empty<CombatUpdate>();
            SpecialCooldowns = specialCooldowns ?? Array.Empty<SpecialCooldownUpdate>();
            NanoCasts = nanoCasts ?? Array.Empty<NanoCastUpdate>();
            NanoEffectSnapshot = nanoEffectSnapshot;
        }

        public IReadOnlyList<SpecialCooldownUpdate> SpecialCooldowns { get; }
        public IReadOnlyList<NanoCastUpdate> NanoCasts { get; }
        public IReadOnlyList<NanoEffectUpdate> NanoEffectSnapshot { get; }
        public IReadOnlyList<CombatUpdate> CombatUpdates { get; }
        public IReadOnlyList<NanoEffectUpdate> NanoEffects { get; }
        public IReadOnlyList<WorldEntityDelta> Deltas { get; }
        public IReadOnlyList<NearbyEntity> Snapshot { get; }
        public int PacketsObserved { get; }
        public int MovementPacketsObserved { get; }
        public int UnmatchedMovementPackets { get; }
        public IReadOnlyDictionary<string, int> PacketKindsObserved { get; }
        public IReadOnlyDictionary<string, string> PacketSamplesObserved { get; }
        public InventorySnapshot Inventory { get; }
        public IReadOnlyList<InventorySnapshot> ContainerInventories { get; }
        public IReadOnlyList<string> ServerMessages { get; }
        public WorldBootstrapResult ZoneTransfer { get; }
    }
}
