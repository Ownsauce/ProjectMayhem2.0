namespace ZoneEngine_New.Core.WorldGeneration
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using WorldGen.Contracts;
    using WorldGen.Dungeons;
    using SmokeLounge.AOtomation.Messaging.GameData;
    using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
    using Vector3 = AORebirth.Core.Vector.Vector3;

    /// <summary>
    /// Static indoor source-copy baseline using the existing PlayfieldAnarchyF fields.
    /// Source identity selects installed resources; runtime identity isolates the server instance.
    /// No ACG room payload is synthesized and no original-client acceptance is implied.
    /// </summary>
    internal static class NativeClientPlayfieldAdapter
    {
        public const string ModeParameter = "nativeClientMode";
        public const string SourceCopyMode = "static-source";

        public static bool IsSourceCopy(GenerationManifest manifest) => manifest != null
            && manifest.GeneratorId == "native-playfield-copy"
            && manifest.Parameters.TryGetValue(ModeParameter, out string mode) && mode == SourceCopyMode;

        public static bool IsOriginalClient(GenerationManifest manifest) => IsSourceCopy(manifest) || NativeClientAcgLayout.IsAcg(manifest);

        public static bool TryCreateMessage(DungeonLayout? layout, int runtimePlayfield, Vector3 coordinates,
            out PlayfieldAnarchyFMessage? message)
        {
            message = null;
            if (layout == null) return false;
            var generator = NativeClientAcgLayout.Read(layout);
            if (generator != null)
            {
                var configured = NativeClientPlayfieldSlots.Load();
                if (!configured.ReservedPlayfields.Contains(runtimePlayfield) || generator.Identity.Instance != runtimePlayfield
                    || generator.Style != configured.AcgStyle)
                    throw new InvalidDataException("Native ACG layout is not bound to its configured slot/style.");
                message = new PlayfieldAnarchyFMessage {
                    Identity = new Identity { Type = IdentityType.Playfield2, Instance = runtimePlayfield },
                    CharacterCoordinates = coordinates, PlayfieldId1 = generator.Identity,
                    PlayfieldId2 = new Identity { Type = IdentityType.Playfield2, Instance = runtimePlayfield },
                    GeneratorPayload = generator.ToByteArray(), PlayfieldX = -1, PlayfieldZ = -1
                };
                return true;
            }
            if (!IsSourceCopy(layout.Manifest)) return false;
            if (!layout.Manifest.Parameters.TryGetValue("nativePlayfield", out string text)
                || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int source) || source <= 0)
                throw new InvalidDataException("Invalid original-client source playfield.");
            var slots = NativeClientPlayfieldSlots.Load();
            if (slots.SourcePlayfield != source || !slots.ReservedPlayfields.Contains(runtimePlayfield)
                || !layout.Manifest.Parameters.TryGetValue("nativeClientPlayfield", out string slotText)
                || !int.TryParse(slotText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) || slot != runtimePlayfield)
                throw new InvalidDataException("Original-client copy is not bound to a reserved client playfield.");
            message = new PlayfieldAnarchyFMessage {
                Identity = new Identity { Type = IdentityType.Playfield2, Instance = runtimePlayfield },
                CharacterCoordinates = coordinates,
                PlayfieldId1 = new Identity { Type = IdentityType.Playfield1, Instance = source },
                Unknown3 = 0, Unknown4 = 0,
                PlayfieldId2 = new Identity { Type = IdentityType.Playfield2, Instance = runtimePlayfield },
                PlayfieldX = -1, PlayfieldZ = -1
            };
            return true;
        }
    }
}
