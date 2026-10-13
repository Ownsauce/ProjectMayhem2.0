using System;
using System.Collections.Generic;

namespace AO.Client.World
{
    public sealed class CharacterStateSnapshot
    {
        public CharacterStateSnapshot(IReadOnlyList<InventoryEntrySnapshot> slots,
            IReadOnlyList<int> uploadedNanoIds,
            IReadOnlyDictionary<int, int> stats = null,
            bool isStatUpdateOnly = false,
            long equipmentRevision = 0)
        {
            Slots = slots ?? Array.Empty<InventoryEntrySnapshot>();
            UploadedNanoIds = uploadedNanoIds ?? Array.Empty<int>();
            Stats = stats ?? new Dictionary<int, int>();
            IsStatUpdateOnly = isStatUpdateOnly;
            EquipmentRevision = equipmentRevision;
        }

        public IReadOnlyList<InventoryEntrySnapshot> Slots { get; }
        public IReadOnlyList<int> UploadedNanoIds { get; }
        public IReadOnlyDictionary<int, int> Stats { get; }
        public bool IsStatUpdateOnly { get; }
        // Persists across stat/nano snapshots so a batched equipment change cannot be lost.
        public long EquipmentRevision { get; }
    }
}
