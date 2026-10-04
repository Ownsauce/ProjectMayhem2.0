using System;
using System.Collections.Generic;
using System.IO;

namespace AO.Assets.Decoders
{
    public sealed class AOIndoorStatelPlacement
    {
        public int RoomIndex { get; internal set; }
        public int MeshId { get; internal set; }
        public float X { get; internal set; }
        public float Y { get; internal set; }
        public float Z { get; internal set; }
        public int Flags { get; internal set; }
        public byte ScaleFlags { get; internal set; }
        public int[] TextureOverrides { get; internal set; }
    }

    /// <summary>Indoor .pf files have one lightmap/placement block per source room, unlike outdoor files.</summary>
    public static class AOIndoorStatelPlacementDecoder
    {
        public static IReadOnlyList<AOIndoorStatelPlacement> Decode(byte[] file, int roomCount)
        {
            if (file == null || roomCount < 1 || roomCount > 65535
                || file.Length < 4 + roomCount * 4) throw new InvalidDataException("Truncated indoor statel header");
            using (var stream = new MemoryStream(file, false))
            using (var reader = new BinaryReader(stream))
            {
                try
                {
                    if (reader.ReadInt32() != 1) throw new InvalidDataException("Unsupported indoor statel version");
                    var offsets = new int[roomCount + 1];
                    for (int i = 0; i < roomCount; i++) offsets[i] = reader.ReadInt32();
                    offsets[roomCount] = file.Length;
                    if (offsets[0] != 4 + roomCount * 4) throw new InvalidDataException("Indoor statel room count mismatch");
                    var placements = new List<AOIndoorStatelPlacement>();
                    for (int room = 0; room < roomCount; room++)
                    {
                        int start = offsets[room], end = offsets[room + 1];
                        if (start < 0 || end <= start || end > file.Length) throw new InvalidDataException("Invalid indoor statel room offsets");
                        stream.Position = start; Need(reader, end, 8);
                        int packed = reader.ReadInt32(), unpacked = reader.ReadInt32();
                        // The opaque lightmap is separate from the model placements. Packed size includes the unpacked-size field.
                        if (packed < 4 || unpacked < 0 || packed > end - start - 4) throw new InvalidDataException("Invalid indoor statel lightmap envelope");
                        stream.Position = start + packed + 4;
                        int extras = Count(reader, end); Need(reader, end, extras * 2); stream.Position += extras * 2;
                        SkipShortModels(reader, end);
                        ReadModels(reader, end, room, placements);
                        ReadModels(reader, end, room, placements);
                        // Remaining records describe lights/sound/fog, not full mesh placements.
                    }
                    return placements;
                }
                catch (EndOfStreamException error) { throw new InvalidDataException("Truncated indoor statel data", error); }
            }
        }

        private static void SkipShortModels(BinaryReader reader, int end)
        {
            int count = Count(reader, end);
            for (int i = 0; i < count; i++)
            {
                Need(reader, end, 18); reader.BaseStream.Position += 17;
                int textures = reader.ReadByte();
                if (textures == 0) continue;
                Need(reader, end, 4); int mask = reader.ReadInt32();
                int words = textures + ((mask & 0x7f0000) == 0x7f0000 ? 1 : 0);
                Need(reader, end, words * 4); reader.BaseStream.Position += words * 4;
            }
        }
        private static void ReadModels(BinaryReader reader, int end, int room, List<AOIndoorStatelPlacement> placements)
        {
            int count = Count(reader, end);
            for (int i = 0; i < count; i++)
            {
                Need(reader, end, 22);
                var placement = new AOIndoorStatelPlacement { RoomIndex = room,
                    X = Finite(reader), Y = Finite(reader), Z = Finite(reader), Flags = reader.ReadInt32() };
                uint mesh = reader.ReadUInt32(); placement.ScaleFlags = reader.ReadByte(); int textures = reader.ReadByte();
                placement.TextureOverrides = Array.Empty<int>();
                if (textures > 0)
                {
                    Need(reader, end, 4); int mask = reader.ReadInt32();
                    int words = textures + ((mask & 0x7f0000) == 0x7f0000 ? 1 : 0);
                    Need(reader, end, words * 4); var overrides = new int[words + 1]; overrides[0] = mask;
                    for (int j = 1; j < overrides.Length; j++) overrides[j] = reader.ReadInt32();
                    placement.TextureOverrides = overrides;
                }
                if (mesh == 0 || mesh > 300000) continue;
                placement.MeshId = (int)mesh; placements.Add(placement);
            }
        }
        private static int Count(BinaryReader reader, int end) { Need(reader, end, 2); return reader.ReadUInt16(); }
        private static float Finite(BinaryReader reader)
        { float value = reader.ReadSingle(); if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Nonfinite indoor statel position"); return value; }
        private static void Need(BinaryReader reader, int end, int bytes)
        { if (bytes < 0 || reader.BaseStream.Position > end - bytes) throw new InvalidDataException("Indoor statel record exceeds its room block"); }
    }
}
