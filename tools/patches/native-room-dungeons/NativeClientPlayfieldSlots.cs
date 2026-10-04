namespace ZoneEngine_New.Core.WorldGeneration
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;

    /// <summary>Local original-client test slots, separate from installed source assets.</summary>
    internal sealed class NativeClientPlayfieldSlots
    {
        public int SchemaVersion { get; set; } = 1;
        public int SourcePlayfield { get; set; }
        public int AcgStyle { get; set; }
        public int[] ReservedPlayfields { get; set; } = Array.Empty<int>();

        public static bool IsReserved(int playfieldId)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "NativeCopies", "native-client-playfields.json");
            return File.Exists(path) && Load().ReservedPlayfields.Contains(playfieldId);
        }

        public static NativeClientPlayfieldSlots Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "NativeCopies", "native-client-playfields.json");
            var slots = JsonSerializer.Deserialize<NativeClientPlayfieldSlots>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Missing original-client playfield slots.");
            if (slots.SchemaVersion != 1 || slots.SourcePlayfield <= 0 || slots.ReservedPlayfields is not { Length: > 0 }
                || slots.ReservedPlayfields.Distinct().Count() != slots.ReservedPlayfields.Length
                || slots.ReservedPlayfields.Any(id => id <= 0 || id >= ProceduralInstanceService.FirstGeneratedPlayfield
                    || id == slots.SourcePlayfield))
                throw new InvalidDataException("Invalid original-client playfield slots.");
            return slots;
        }
    }
}
