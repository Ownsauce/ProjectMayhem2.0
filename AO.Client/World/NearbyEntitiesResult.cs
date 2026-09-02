using System;
using System.Collections.Generic;

namespace AO.Client.World
{
    public sealed class NearbyEntitiesResult
    {
        public NearbyEntitiesResult(IReadOnlyList<NearbyEntity> entities, int packetsObserved)
        {
            Entities = entities ?? throw new ArgumentNullException(nameof(entities));
            PacketsObserved = packetsObserved;
        }

        public IReadOnlyList<NearbyEntity> Entities { get; }
        public int PacketsObserved { get; }
    }
}
