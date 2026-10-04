using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WorldGen.Dungeons
{
    [Flags]
    public enum NativeRoomUse
    {
        None = 0,
        Arena = 1,
        Encounter = 2,
        Connector = 4,
        Hub = 8
    }

    /// <summary>Authored template capabilities; actual encounter roles belong to a future run plan.</summary>
    [Serializable]
    public sealed class NativeRoomAnnotation
    {
        public NativeRoomUse AllowedUses;
        public bool Reviewed, Landmark, HasEncounterCenter;
        public string[] ThemeTags = Array.Empty<string>();
        public string Notes = "";
        public int EncounterX, EncounterY, EncounterZ, EncounterRegion = -1;
    }

    public static class NativeRoomAnnotations
    {
        public const NativeRoomUse AllUses = NativeRoomUse.Arena | NativeRoomUse.Encounter | NativeRoomUse.Connector | NativeRoomUse.Hub;

        public static void Validate(NativeRoomTemplate room, NativeRoomAnnotation annotation)
        {
            if (annotation == null) return;
            if ((annotation.AllowedUses & ~AllUses) != 0 || annotation.ThemeTags == null || annotation.ThemeTags.Length > 16
                || annotation.Notes == null || annotation.Notes.Length > 2000
                || annotation.ThemeTags.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 48
                    || t.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_')))
                || annotation.ThemeTags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != annotation.ThemeTags.Length)
                throw new InvalidDataException("Invalid authoring tags for room " + room.SourceIndex);
            if (!annotation.HasEncounterCenter) return;
            if (annotation.EncounterRegion < 0 || !room.WalkPoints.Any(p => p.X == annotation.EncounterX
                && p.Y == annotation.EncounterY && p.Z == annotation.EncounterZ && p.Region == annotation.EncounterRegion))
                throw new InvalidDataException("Encounter center must lie on a validated walking sample in room " + room.SourceIndex);
            if (!room.Sockets.Any(s => IsUsable(s) && s.Region == annotation.EncounterRegion)
                && !(room.SourceIndex >= 0 && room.SpawnRegion == annotation.EncounterRegion))
                throw new InvalidDataException("Encounter center has no usable entrance in room " + room.SourceIndex);
        }

        public static bool IsUsable(NativeRoomSocket socket) => !socket.Exterior && !socket.Blocked && !socket.Excluded && socket.Region >= 0;

        public static int MostUsableDoorsInOneRegion(NativeRoomTemplate room)
            => room.Sockets.Where(IsUsable).GroupBy(s => s.Region).Select(g => g.Count()).DefaultIfEmpty(0).Max();

        public static string[] Warnings(NativeRoomTemplate room, NativeRoomAnnotation annotation)
        {
            var warnings = new List<string>();
            if (annotation == null || !annotation.Reviewed) warnings.Add("Room tags have not been reviewed.");
            if (annotation == null) return warnings.ToArray();
            int ports = MostUsableDoorsInOneRegion(room);
            if ((annotation.AllowedUses & NativeRoomUse.Hub) != 0 && ports < 3)
                warnings.Add("A hub normally needs at least three usable doorways in one walking region.");
            if ((annotation.AllowedUses & NativeRoomUse.Connector) != 0 && ports < 2)
                warnings.Add("This connector has fewer than two usable doorways in one walking region.");
            if ((annotation.AllowedUses & (NativeRoomUse.Arena | NativeRoomUse.Encounter)) != 0 && !annotation.HasEncounterCenter)
                warnings.Add("Choose a supported encounter center before using this room in a dungeon-run profile.");
            return warnings.ToArray();
        }

        public static void SetEncounterCenter(NativeRoomAnnotation annotation, NativeWalkPoint point)
        {
            annotation.HasEncounterCenter = true;
            annotation.EncounterX = point.X; annotation.EncounterY = point.Y; annotation.EncounterZ = point.Z;
            annotation.EncounterRegion = point.Region;
        }
    }
}
