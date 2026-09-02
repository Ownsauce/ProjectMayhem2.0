using System;
using System.Collections.Generic;

namespace AO.Client.World
{
    public sealed class WorldDeltaBatch
    {
        public WorldDeltaBatch(IReadOnlyList<WorldEntityDelta> deltas,
            IReadOnlyList<NearbyEntity> snapshot, int packetsObserved,
            int movementPacketsObserved = 0, int unmatchedMovementPackets = 0,
            IReadOnlyDictionary<string, int> packetKindsObserved = null,
            IReadOnlyDictionary<string, string> packetSamplesObserved = null,
            InventorySnapshot inventory = null)
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
        }

        public IReadOnlyList<WorldEntityDelta> Deltas { get; }
        public IReadOnlyList<NearbyEntity> Snapshot { get; }
        public int PacketsObserved { get; }
        public int MovementPacketsObserved { get; }
        public int UnmatchedMovementPackets { get; }
        public IReadOnlyDictionary<string, int> PacketKindsObserved { get; }
        public IReadOnlyDictionary<string, string> PacketSamplesObserved { get; }
        public InventorySnapshot Inventory { get; }
    }
}
