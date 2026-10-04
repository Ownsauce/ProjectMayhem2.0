using System;
using System.IO;
using AO.Client.World;

namespace AO.Client.Backends.AORebirth
{
    internal static partial class AORebirthProtocol
    {
        public const int AppearanceUpdate = 0x41624F0D;

        public static bool TryReadAppearanceUpdate(AORebirthPacket packet, out int type,
            out int instance, out CharacterAppearanceSnapshot appearance)
        {
            type = instance = 0;
            appearance = null;
            if (!TryCreateN3Reader(packet, AppearanceUpdate, 24, out BigEndianReader reader))
                return false;
            try
            {
                int ownerType = reader.ReadInt32(), ownerId = reader.ReadInt32();
                reader.Skip(1);
                ReadAppearanceArrays(reader, out var textures, out var meshes);
                int visualFlags = reader.ReadInt16();
                reader.Skip(1);
                if (reader.Remaining != 0) return false;
                appearance = new CharacterAppearanceSnapshot(textures, meshes, visualFlags);
                type = ownerType; instance = ownerId;
                return true;
            }
            catch (EndOfStreamException) { return false; }
            catch (InvalidDataException) { return false; }
        }

        private static void ReadAppearanceArrays(BigEndianReader reader,
            out AppearanceTexture[] textures, out AppearanceMesh[] meshes)
        {
            int count = ReadX3F1Count(reader, "appearance textures", 4096);
            if (count > reader.Remaining / 12) throw new EndOfStreamException();
            textures = new AppearanceTexture[count];
            for (int i = 0; i < count; i++)
                textures[i] = new AppearanceTexture(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            count = ReadX3F1Count(reader, "appearance meshes", 4096);
            if (count > reader.Remaining / 10) throw new EndOfStreamException();
            meshes = new AppearanceMesh[count];
            for (int i = 0; i < count; i++)
                meshes[i] = new AppearanceMesh(reader.ReadByte(), reader.ReadUInt32(), reader.ReadInt32(), reader.ReadByte());
        }

    }
}
