using System;
using System.Collections.Generic;
using System.IO;

namespace AO.Assets.Decoders
{
    public sealed class AOIndoorSurfaceSet
    {
        internal AOIndoorSurfaceSet(int playfieldId, List<AOIndoorSurfaceRoom> rooms,
            AOIndoorDungeonTilemap tilemap)
        { PlayfieldId = playfieldId; Rooms = rooms; Tilemap = tilemap; }
        public int PlayfieldId { get; }
        public IReadOnlyList<AOIndoorSurfaceRoom> Rooms { get; }
        public AOIndoorDungeonTilemap Tilemap { get; }
    }

    public sealed class AOIndoorDungeonTilemap
    {
        internal AOIndoorDungeonTilemap(int id, int width, int height,
            float heightmapScale, float tileSize, byte[] heightmap, byte[] collisionData)
        {
            Id = id; Width = width; Height = height; HeightmapScale = heightmapScale;
            TileSize = tileSize; Heightmap = heightmap; CollisionData = collisionData;
        }
        public int Id { get; }
        public int Width { get; }
        public int Height { get; }
        public float HeightmapScale { get; }
        public float TileSize { get; }
        public byte[] Heightmap { get; }
        public byte[] CollisionData { get; }
        public byte GetHeight(int x, int y) => Heightmap[checked(y * Width + x)];
        public byte GetCollision(int x, int y) => CollisionData[checked(y * Width + x)];
    }

    public sealed class AOIndoorSurfaceRoom
    {
        internal AOIndoorSurfaceRoom(int instance, List<AOIndoorSurfaceMesh> meshes)
        { Instance = instance; Meshes = meshes; }
        public int Instance { get; }
        public IReadOnlyList<AOIndoorSurfaceMesh> Meshes { get; }
    }

    public sealed class AOIndoorSurfaceMesh
    {
        internal AOIndoorSurfaceMesh(float[] vertices, int[] triangles, int[] materialCandidates)
        { Vertices = vertices; Triangles = triangles; MaterialCandidates = materialCandidates; }
        public float[] Vertices { get; }
        public int[] Triangles { get; }
        public int[] MaterialCandidates { get; }
        public int VertexCount => Vertices.Length / 3;
        public int TriangleCount => Triangles.Length / 3;
    }

    public static class AOIndoorSurfaceStreamDecoder
    {
        private const int CurrentVersion = 2;
        private const int MaterialCandidateCount = 11;

        public static AOIndoorSurfaceSet Decode(string path, int expectedPlayfieldId)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("An AO indoor-surface stream path is required.", nameof(path));
            using (var stream = File.OpenRead(path))
                return Decode(stream, expectedPlayfieldId);
        }

        public static AOIndoorSurfaceSet Decode(Stream stream, int expectedPlayfieldId)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                if (stream.Length - stream.Position < 16
                    || new string(reader.ReadChars(4)) != "AOIS")
                    throw new InvalidDataException("The AO indoor-surface stream header is invalid.");
                int version = reader.ReadInt32();
                int playfieldId = reader.ReadInt32();
                int roomCount = ReadCount(reader, "room", 65535);
                if (version != CurrentVersion || playfieldId != expectedPlayfieldId)
                    throw new InvalidDataException("The AO indoor-surface stream identity is invalid.");

                var rooms = new List<AOIndoorSurfaceRoom>(roomCount);
                for (int roomIndex = 0; roomIndex < roomCount; roomIndex++)
                {
                    EnsureRemaining(stream, 8);
                    int instance = reader.ReadInt32();
                    int meshCount = ReadCount(reader, "surface mesh", 100000);
                    var meshes = new List<AOIndoorSurfaceMesh>(meshCount);
                    for (int meshIndex = 0; meshIndex < meshCount; meshIndex++)
                    {
                        int vertexCount = ReadCount(reader, "surface vertex", 200000);
                        int triangleCount = ReadCount(reader, "surface triangle", 400000);
                        long floatCount = (long)vertexCount * 3;
                        long indexCount = (long)triangleCount * 3;
                        long requiredBytes = MaterialCandidateCount * 4L
                            + floatCount * 4L + indexCount * 4L;
                        if (floatCount > int.MaxValue || indexCount > int.MaxValue
                            || requiredBytes > stream.Length - stream.Position)
                            throw new InvalidDataException("The AO indoor surface mesh is truncated.");

                        var materials = new int[MaterialCandidateCount];
                        for (int index = 0; index < materials.Length; index++)
                            materials[index] = reader.ReadInt32();
                        var vertices = new float[(int)floatCount];
                        for (int index = 0; index < vertices.Length; index++)
                            vertices[index] = reader.ReadSingle();
                        var triangles = new int[(int)indexCount];
                        for (int index = 0; index < triangles.Length; index++)
                        {
                            int value = reader.ReadInt32();
                            if (value < 0 || value >= vertexCount)
                                throw new InvalidDataException("An AO indoor surface triangle index is invalid.");
                            triangles[index] = value;
                        }
                        meshes.Add(new AOIndoorSurfaceMesh(vertices, triangles, materials));
                    }
                    rooms.Add(new AOIndoorSurfaceRoom(instance, meshes));
                }

                EnsureRemaining(stream, 24);
                if (new string(reader.ReadChars(4)) != "AOTM")
                    throw new InvalidDataException("The AO indoor tilemap header is invalid.");
                int tilemapId = reader.ReadInt32();
                int width = ReadCount(reader, "tilemap width", 32768);
                int height = ReadCount(reader, "tilemap height", 32768);
                float heightmapScale = reader.ReadSingle();
                float tileSize = reader.ReadSingle();
                long tileCount = (long)width * height;
                if (tilemapId <= 0 || width == 0 || height == 0 || tileCount > 268435456
                    || !IsFinitePositive(heightmapScale) || !IsFinitePositive(tileSize)
                    || tileCount * 2 > stream.Length - stream.Position)
                    throw new InvalidDataException("The AO indoor tilemap is invalid.");
                byte[] heightmap = reader.ReadBytes((int)tileCount);
                byte[] collision = reader.ReadBytes((int)tileCount);
                if (stream.Position != stream.Length)
                    throw new InvalidDataException("The AO indoor-surface stream has trailing data.");
                var tilemap = new AOIndoorDungeonTilemap(tilemapId, width, height,
                    heightmapScale, tileSize, heightmap, collision);
                return new AOIndoorSurfaceSet(playfieldId, rooms, tilemap);
            }
        }

        private static bool IsFinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static int ReadCount(BinaryReader reader, string label, int maximum)
        {
            EnsureRemaining(reader.BaseStream, 4);
            int value = reader.ReadInt32();
            if (value < 0 || value > maximum)
                throw new InvalidDataException("The AO indoor " + label + " count is invalid.");
            return value;
        }

        private static void EnsureRemaining(Stream stream, long bytes)
        {
            if (bytes < 0 || bytes > stream.Length - stream.Position)
                throw new InvalidDataException("The AO indoor-surface stream is truncated.");
        }
    }
}
