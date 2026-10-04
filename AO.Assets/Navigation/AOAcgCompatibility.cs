using System;
using System.Linq;
using AO.Assets.Decoders;

namespace AO.Assets.Navigation
{
    /// <summary>
    /// Checks the five-tile room-block requirement enforced by the existing AORebirth ACG adapter.
    /// Passing this check alone does not prove original-client rendering or doorway compatibility.
    /// </summary>
    public static class AOAcgCompatibility
    {
        public static bool HasWholeBlockDimensions(AOIndoorRoom room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            int width = room.TileX2 - room.TileX1, depth = room.TileY2 - room.TileY1;
            return width > 0 && depth > 0 && width % 5 == 0 && depth % 5 == 0;
        }

        public static int[] WholeBlockRooms(AOPlayfieldDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return definition.Rooms.Select((room, index) => new { room, index })
                .Where(value => HasWholeBlockDimensions(value.room)).Select(value => value.index).ToArray();
        }
    }
}
