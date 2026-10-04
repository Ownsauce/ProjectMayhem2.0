using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using AO.Assets.ResourceDatabase;

namespace AO.Assets.Decoders
{
    public sealed class AOPlayfieldDefinition
    {
        internal AOPlayfieldDefinition(int id, int version, string name, int tilemapId,
            List<AOIndoorRoom> rooms)
        {
            Id = id;
            Version = version;
            Name = name;
            TilemapId = tilemapId;
            Rooms = rooms;
        }

        public int Id { get; }
        public int Version { get; }
        public string Name { get; }
        public int TilemapId { get; }
        public int RoomCount => Rooms.Count;
        public IReadOnlyList<AOIndoorRoom> Rooms { get; }
        public bool IsIndoor => TilemapId != Id;
    }

    public sealed class AOIndoorRoom
    {
        internal AOIndoorRoom(string name, int rotationQuarterTurns, short tileX1,
            short tileY1, short tileX2, short tileY2, float x, float y, float z,
            IReadOnlyList<AOIndoorDoorConnection> doors = null)
        {
            Name = name;
            RotationQuarterTurns = rotationQuarterTurns;
            TileX1 = tileX1; TileY1 = tileY1; TileX2 = tileX2; TileY2 = tileY2;
            X = x; Y = y; Z = z;
            DoorConnections = doors ?? Array.Empty<AOIndoorDoorConnection>();
        }
        public string Name { get; }
        public int RotationQuarterTurns { get; }
        public short TileX1 { get; }
        public short TileY1 { get; }
        public short TileX2 { get; }
        public short TileY2 { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public IReadOnlyList<AOIndoorDoorConnection> DoorConnections { get; }
        public float CenterX => ((((TileX2 - TileX1 - 1) & ~1) + 1) * 0.5f);
        public float CenterZ => ((((TileY2 - TileY1 - 1) & ~1) + 1) * 0.5f);
    }

    public sealed class AOIndoorDoorConnection
    {
        internal AOIndoorDoorConnection(short zoneLink, short posRot) { ZoneLink = zoneLink; PosRot = posRot; }
        public short ZoneLink { get; }
        public short PosRot { get; }
    }

    public static class AOPlayfieldDefinitionDecoder
    {
        private const int MinimumRecordSize = 64;

        public static AOPlayfieldDefinition Decode(byte[] record, int expectedId)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.Length < MinimumRecordSize)
                throw new InvalidDataException("The AO playfield record is truncated.");

            using (var stream = new MemoryStream(record, false))
            using (var reader = new BinaryReader(stream))
            {
                int recordType = reader.ReadInt32();
                int recordId = reader.ReadInt32();
                reader.ReadInt32(); // database record version
                if (recordType != AOResourceTypes.Playfield || recordId != expectedId)
                    throw new InvalidDataException("The AO playfield record identity is invalid.");

                int version = reader.ReadInt32();
                int id = reader.ReadInt32();
                string name = Encoding.ASCII.GetString(reader.ReadBytes(32)).TrimEnd('\0');
                int tilemapId = reader.ReadInt32();
                reader.ReadInt32(); // unknown
                int roomCount = reader.ReadInt32();
                if (id != expectedId || version < 0 || roomCount < 0 || roomCount > 1_000_000)
                    throw new InvalidDataException("The AO playfield metadata is invalid.");
                if (version >= 9)
                {
                    EnsureRemaining(stream, 44);
                    stream.Position += 44;
                }

                var rooms = new List<AOIndoorRoom>(roomCount);
                if (tilemapId != id)
                    for (int index = 0; index < roomCount; index++)
                        rooms.Add(ReadIndoorRoom(reader, version, index));
                return new AOPlayfieldDefinition(id, version, name, tilemapId, rooms);
            }
        }

        private static AOIndoorRoom ReadIndoorRoom(BinaryReader reader, int version,
            int roomIndex)
        {
            Stream stream = reader.BaseStream;
            EnsureRemaining(stream, 24);
            sbyte rawRotation = reader.ReadSByte();
            reader.ReadByte();
            short x1 = reader.ReadInt16();
            short y1 = reader.ReadInt16();
            short x2 = reader.ReadInt16();
            short y2 = reader.ReadInt16();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            float z = reader.ReadSingle();
            short doorCount = reader.ReadInt16();
            if (doorCount < 0 || doorCount > 4096)
                throw new InvalidDataException("The AO indoor-room door count is invalid.");
            EnsureRemaining(stream, doorCount * 4);
            var doors = new List<AOIndoorDoorConnection>(doorCount);
            for (int door = 0; door < doorCount; door++)
                doors.Add(new AOIndoorDoorConnection(reader.ReadInt16(), reader.ReadInt16()));

            string name = "Room " + roomIndex;
            if (rawRotation < 0)
            {
                EnsureRemaining(stream, 32);
                name = Encoding.ASCII.GetString(reader.ReadBytes(32)).TrimEnd('\0');
            }

            EnsureRemaining(stream, 8);
            int packedSize = reader.ReadInt32() - 4;
            reader.ReadInt32(); // unpacked lightmap size
            if (packedSize < 0) throw new InvalidDataException("The AO room lightmap size is invalid.");
            EnsureRemaining(stream, packedSize);
            stream.Position += packedSize;

            EnsureRemaining(stream, 4);
            int waterCount = (reader.ReadInt32() / 0x3F1) - 1;
            if (waterCount < -1 || waterCount > 4096)
                throw new InvalidDataException("The AO room water count is invalid.");
            for (int water = 0; water < waterCount; water++)
            {
                EnsureRemaining(stream, 8);
                reader.ReadInt32();
                int vertexCount = reader.ReadInt32();
                SkipElements(stream, vertexCount, 12, "water vertex");
                int triangleCount = ReadInt32(reader, "water triangle");
                SkipElements(stream, triangleCount, 6, "water triangle");
            }

            if (version > 4)
            {
                int attractorCount = ReadInt32(reader, "camera attractor");
                int bytesPerAttractor = version > 5 ? 44 : 32;
                SkipElements(stream, attractorCount, bytesPerAttractor, "camera attractor");
            }

            return new AOIndoorRoom(name, rawRotation & 3, x1, y1, x2, y2, x, y, z, doors);
        }

        private static int ReadInt32(BinaryReader reader, string label)
        {
            EnsureRemaining(reader.BaseStream, 4);
            int value = reader.ReadInt32();
            if (value < 0 || value > 1_000_000)
                throw new InvalidDataException("The AO " + label + " count is invalid.");
            return value;
        }

        private static void SkipElements(Stream stream, int count, int size, string label)
        {
            if (count < 0 || count > 1_000_000)
                throw new InvalidDataException("The AO " + label + " count is invalid.");
            long bytes = (long)count * size;
            if (bytes > int.MaxValue)
                throw new InvalidDataException("The AO " + label + " data is too large.");
            EnsureRemaining(stream, (int)bytes);
            stream.Position += bytes;
        }

        private static void EnsureRemaining(Stream stream, int bytes)
        {
            if (bytes < 0 || bytes > stream.Length - stream.Position)
                throw new InvalidDataException("The AO playfield record is truncated.");
        }
    }
}
