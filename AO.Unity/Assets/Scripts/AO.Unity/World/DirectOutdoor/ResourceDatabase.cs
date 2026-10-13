using AODB;
using AODB.Common.RDBObjects;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using AODB.Common;
using AODB.Common.DbClasses;

public sealed class ResourceDatabase : IDisposable
{
    public RdbController Rdb { get; private set; }

    public bool IsInitialized => Rdb != null;

    public void Initialize(string aoBasePath)
    {
        Rdb?.Dispose();
        Rdb = new RdbController(aoBasePath);
    }

    public T Get<T>(int instance) where T : RDBObject, new()
    {
        if (Rdb == null)
            throw new InvalidOperationException("ResourceDatabase has not been initialized.");

        T resource = Rdb.Get<T>(instance);
        if (resource is Tilemap tilemap && tilemap.Heightmap?.Count > 0)
            ReadOutdoorGround(tilemap, Rdb.GetRaw(1000009, instance));
        return resource;
    }

    public T Get<T>(ResourceTypeId type, int instance) where T : RDBObject, new()
    {
        if (Rdb == null)
            throw new InvalidOperationException("ResourceDatabase has not been initialized.");

        T resource = Rdb.Get<T>(type, instance);
        if (resource is Tilemap tilemap && tilemap.Heightmap?.Count > 0)
            ReadOutdoorGround(tilemap, Rdb.GetRaw(1000009, instance));
        return resource;
    }

    public byte[] GetRaw(int recordType, int instance)
    {
        if (Rdb == null)
            throw new InvalidOperationException("ResourceDatabase has not been initialized.");

        return Rdb.GetRaw(recordType, instance);
    }

    public void Dispose()
    {
        Rdb?.Dispose();
        Rdb = null;
    }

    // The bundled AODB inflater returns MemoryStream.GetBuffer(), including unused
    // capacity. A 9x9 backyard height block becomes 16x16, and 64 tiles become 128.
    // Decode the exact stream lengths here without changing unrelated AODB readers.
    private static void ReadOutdoorGround(Tilemap tilemap, byte[] raw)
    {
        if (raw == null || raw.Length < 38 || raw[12] != 'C' || raw[13] != 'H'
            || raw[14] != 'G' || raw[15] != 'A') return;
        using var reader = new BinaryReader(new MemoryStream(raw));
        reader.BaseStream.Position = 24;
        uint width = reader.ReadUInt16(), height = reader.ReadUInt16();
        reader.ReadSingle(); reader.ReadSingle();
        int textures = reader.ReadUInt16();
        reader.BaseStream.Position += textures * 2L + 4;
        var ground = (RDBTilemap_t.AnarchyGroundDataDB_t)
            new AODBSerializer().Deserialize<RDBTilemap_t>(reader).Members[0];
        var heights = InflateGroundBlocks(ground.heightmap_compressed_data);
        var tiles = InflateGroundBlocks(ground.tilemap_compressed_data);
        if (heights.Count == 0 || heights.Count != tiles.Count)
            throw new InvalidDataException("Outdoor height and tile chunks do not match.");
        int side = (int)Math.Sqrt(heights[0].Length);
        bool shorts = side * side != heights[0].Length;
        if (shorts) side = (int)Math.Sqrt(heights[0].Length / 2);
        int sampleBytes = shorts ? 2 : 1;
        int extent = side - 1;
        if (extent <= 0 || (extent & (extent - 1)) != 0)
            throw new InvalidDataException("Outdoor height chunk is not a vertex grid.");
        var decodedHeights = new List<ushort[,]>(heights.Count);
        tilemap.TileMapDatas.Clear();
        for (int index = 0; index < heights.Count; index++)
        {
            byte[] block = heights[index];
            if (block.Length != side * side * sampleBytes || tiles[index].Length != extent * extent * 2)
                throw new InvalidDataException("Outdoor chunk size is inconsistent.");
            var samples = new ushort[side, side];
            int mask = shorts ? 65535 : 255;
            // AO stores horizontal differences of vertically differenced heights.
            for (int z = 0; z < side; z++)
            {
                int row = 0;
                for (int x = 0; x < side; x++)
                {
                    int offset = (z * side + x) * sampleBytes;
                    int delta = shorts ? block[offset] | (block[offset + 1] << 8) : block[offset];
                    row = (row + delta) & mask;
                    samples[x, z] = (ushort)((row + (z > 0 ? samples[x, z - 1] : 0)) & mask);
                }
            }
            decodedHeights.Add(samples);
            var decodedTiles = new List<Tilemap.TileMapData>(extent * extent);
            for (int offset = 0; offset < tiles[index].Length; offset += 2)
                decodedTiles.Add(new Tilemap.TileMapData
                { TextureId = tiles[index][offset], Rotation = tiles[index][offset + 1] });
            tilemap.TileMapDatas[index] = decodedTiles;
        }
        tilemap.Heightmap = decodedHeights;
        tilemap.ChunkSize = side;
        tilemap.MapWidth = width;
        tilemap.MapHeight = height;
    }

    private static List<byte[]> InflateGroundBlocks(byte[] compressed)
    {
        var blocks = new List<byte[]>();
        using var reader = new BinaryReader(new MemoryStream(compressed));
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int length = reader.ReadInt32();
            if (length < 6 || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Outdoor compressed block is truncated.");
            byte[] bytes = reader.ReadBytes(length);
            using var input = new MemoryStream(bytes, 2, length - 6);
            using var inflater = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflater.CopyTo(output);
            blocks.Add(output.ToArray());
        }
        return blocks;
    }
}
