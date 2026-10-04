using System;
using System.IO;
using System.Linq;

namespace WorldGen.Dungeons
{
    [Serializable] public sealed class NativeRoomOverride
    {
        public int SourceIndex;
        public bool Enabled = true;
        public NativeRoomAnnotation Annotation;
        public NativeClosureStyle ClosureStyle;
        public int[] BlockedSockets = Array.Empty<int>();
        public float LightMultiplier = 1, LightRangeMultiplier = 1, Emission = 8;
    }
    [Serializable] public sealed class NativeRoomOverrides
    {
        public int Version = 1, SourcePlayfield = 127;
        public long Revision;
        public string SourceSha256;
        public NativeRoomRecipeData PinnedRecipe;
        public NativeRoomOverride[] Rooms = Array.Empty<NativeRoomOverride>();
        public void Apply(NativeRoomCatalog catalog)
        {
            if (Version != 1 || SourcePlayfield != catalog.SourcePlayfield || (!string.IsNullOrEmpty(SourceSha256) && SourceSha256 != catalog.SourceSha256)
                || Rooms == null || Rooms.Any(r => r == null) || Rooms.Select(r => r.SourceIndex).Distinct().Count() != Rooms.Length)
                throw new InvalidDataException("Invalid native room authoring overrides.");
            catalog.PinnedRecipe = PinnedRecipe;
            foreach (var value in Rooms)
            {
                var room = catalog.Rooms.SingleOrDefault(r => r.SourceIndex == value.SourceIndex);
                if (room == null || value.BlockedSockets == null || !Enum.IsDefined(typeof(NativeClosureStyle), value.ClosureStyle) || value.BlockedSockets.Any(i => i < 0 || i >= room.Sockets.Length)
                    || !Valid(value.LightMultiplier, 0, 10) || !Valid(value.LightRangeMultiplier, .1f, 3) || !Valid(value.Emission, 0, 50))
                    throw new InvalidDataException("Invalid room override " + value.SourceIndex);
                if (room.SourceIndex == catalog.EntranceSourceRoom && !value.Enabled) throw new InvalidDataException("The entrance cannot be excluded.");
                room.Enabled = value.Enabled;
                if (value.Annotation != null) room.Annotation = value.Annotation;
                for (int i = 0; i < room.Sockets.Length; i++) { room.Sockets[i].Excluded = value.BlockedSockets.Contains(i); room.Sockets[i].ClosureStyle = value.ClosureStyle; }
                NativeRoomAnnotations.Validate(room, room.Annotation);
                foreach (var light in room.Lights) { light.Intensity = light.BaseIntensity * value.LightMultiplier; light.Range = light.BaseRange * value.LightRangeMultiplier; light.Emission = value.Emission; }
            }
        }
        private static bool Valid(float value, float min, float max) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }
}
