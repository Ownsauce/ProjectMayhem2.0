using System;
using System.Collections.Generic;
using AO.Assets.Decoders;

namespace AO.Assets.Navigation
{
    /// <summary>Port of AOSharp DungeonTerrain.CreateMesh for an AO room.</summary>
    public static class AOIndoorDungeonTerrainBuilder
    {
        public static AOIndoorSurfaceMesh Build(AOIndoorRoom room,
            AOIndoorDungeonTilemap tilemap)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (tilemap == null) throw new ArgumentNullException(nameof(tilemap));
            int tilesX = room.TileX2 - room.TileX1;
            int tilesZ = room.TileY2 - room.TileY1;
            if (tilesX <= 0 || tilesZ <= 0) return null;

            int columns = tilesX + 1;
            int rows = tilesZ + 1;
            var vertices = new float[checked(columns * rows * 3)];
            float width = tilesX * tilemap.TileSize;
            float length = tilesZ * tilemap.TileSize;
            float anchorX = 1f + (room.CenterX - tilesX * 0.5f) * tilemap.TileSize;
            float anchorZ = 1f + (room.CenterZ - tilesZ * 0.5f) * tilemap.TileSize;
            for (int z = 0; z < rows; z++)
            for (int x = 0; x < columns; x++)
            {
                int mapX = x + room.TileX1 - 1;
                int mapZ = z + room.TileY1 - 1;
                if (mapX < 0 || mapZ < 0 || mapX >= tilemap.Width || mapZ >= tilemap.Height)
                    throw new InvalidOperationException("AO dungeon room exceeds its tilemap.");
                int offset = (z * columns + x) * 3;
                float localX = x * tilemap.TileSize - width * 0.5f - anchorX;
                float localZ = z * tilemap.TileSize - length * 0.5f - anchorZ;
                Rotate(localX, localZ, room.RotationQuarterTurns,
                    out float rotatedX, out float rotatedZ);
                vertices[offset] = room.X + rotatedX;
                vertices[offset + 1] = room.Y
                    + tilemap.GetHeight(mapX, mapZ) * tilemap.HeightmapScale;
                vertices[offset + 2] = room.Z + rotatedZ;
            }

            var triangles = new List<int>(tilesX * tilesZ * 6);
            for (int z = 0; z < tilesZ; z++)
            for (int x = 0; x < tilesX; x++)
            {
                int mapX = x + room.TileX1;
                int mapZ = z + room.TileY1;
                byte collision = tilemap.GetCollision(mapX, mapZ);
                if (collision == 0 || collision == 0x80) continue;
                int a = z * columns + x;
                int b = (z + 1) * columns + x;
                int c = a + 1;
                int d = b + 1;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
            return triangles.Count == 0 ? null : new AOIndoorSurfaceMesh(
                vertices, triangles.ToArray(), Array.Empty<int>());
        }

        private static void Rotate(float x, float z, int quarterTurns,
            out float rotatedX, out float rotatedZ)
        {
            switch (quarterTurns & 3)
            {
                case 1: rotatedX = z; rotatedZ = -x; break;
                case 2: rotatedX = -x; rotatedZ = -z; break;
                case 3: rotatedX = -z; rotatedZ = x; break;
                default: rotatedX = x; rotatedZ = z; break;
            }
        }
    }
}
