using System;
using System.Collections.Generic;
using AO.Assets.Decoders;
using SharpNav;
using SharpNav.Geometry;

namespace AO.Assets.Navigation
{
    public sealed class AOIndoorNavigationMesh
    {
        internal AOIndoorNavigationMesh(int roomInstance, float[] vertices, int[] triangles)
        { RoomInstance = roomInstance; Vertices = vertices; Triangles = triangles; }

        public int RoomInstance { get; }
        public float[] Vertices { get; }
        public int[] Triangles { get; }
        public int VertexCount => Vertices.Length / 3;
        public int TriangleCount => Triangles.Length / 3;
    }

    /// <summary>Bakes AO's materialized indoor surface geometry with SharpNav.</summary>
    public static class AOIndoorSharpNavBuilder
    {
        public static AOIndoorNavigationMesh Build(
            AOIndoorSurfaceRoom room, NavMeshGenerationSettings settings = null,
            AOIndoorSurfaceMesh dungeonTerrain = null)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            settings = settings ?? NavMeshGenerationSettings.HighDensity;

            var source = new List<Triangle3>();
            foreach (AOIndoorSurfaceMesh mesh in room.Meshes)
                AppendTriangles(source, mesh);
            if (dungeonTerrain != null)
                AppendTriangles(source, dungeonTerrain);

            if (source.Count == 0)
                return new AOIndoorNavigationMesh(room.Instance,
                    Array.Empty<float>(), Array.Empty<int>());

            BBox3 bounds = source.GetBoundingBox(settings.CellSize);
            var heightfield = new Heightfield(bounds, settings);
            heightfield.RasterizeTriangles(source);
            heightfield.FilterLedgeSpans(settings.VoxelAgentHeight, settings.VoxelMaxClimb);
            heightfield.FilterLowHangingWalkableObstacles(settings.VoxelMaxClimb);
            heightfield.FilterWalkableLowHeightSpans(settings.VoxelAgentHeight);

            var compact = new CompactHeightfield(heightfield, settings);
            compact.Erode(settings.VoxelAgentRadius);
            compact.BuildDistanceField();
            compact.BuildRegions(2, settings.MinRegionSize, settings.MergedRegionSize,
                settings.FilterLargestSection);
            var polyMesh = new PolyMesh(compact.BuildContourSet(settings), settings);
            return Export(room.Instance, polyMesh);
        }

        private static void AppendTriangles(List<Triangle3> source, AOIndoorSurfaceMesh mesh)
        {
            if (mesh == null) return;
            {
                float[] vertices = mesh.Vertices;
                int[] indices = mesh.Triangles;
                for (int index = 0; index < indices.Length; index += 3)
                {
                    // AO's stored winding is opposite SharpNav's upward-facing winding.
                    source.Add(new Triangle3(
                        ReadVertex(vertices, indices[index]),
                        ReadVertex(vertices, indices[index + 2]),
                        ReadVertex(vertices, indices[index + 1])));
                }
            }
        }

        private static Vector3 ReadVertex(float[] vertices, int index)
        {
            int offset = checked(index * 3);
            return new Vector3(vertices[offset], vertices[offset + 1], vertices[offset + 2]);
        }

        private static AOIndoorNavigationMesh Export(int roomInstance, PolyMesh mesh)
        {
            var vertices = new float[mesh.Verts.Length * 3];
            for (int index = 0; index < mesh.Verts.Length; index++)
            {
                PolyVertex vertex = mesh.Verts[index];
                int offset = index * 3;
                vertices[offset] = mesh.Bounds.Min.X + vertex.X * mesh.CellSize;
                vertices[offset + 1] = mesh.Bounds.Min.Y + vertex.Y * mesh.CellHeight;
                vertices[offset + 2] = mesh.Bounds.Min.Z + vertex.Z * mesh.CellSize;
            }

            var triangles = new List<int>(mesh.PolyCount * 6);
            foreach (PolyMesh.Polygon polygon in mesh.Polys)
            {
                int vertexCount = 0;
                while (vertexCount < polygon.Vertices.Length
                    && polygon.Vertices[vertexCount] != PolyMesh.NullId)
                    vertexCount++;
                for (int index = 2; index < vertexCount; index++)
                {
                    int a = polygon.Vertices[0];
                    int b = polygon.Vertices[index - 1];
                    int c = polygon.Vertices[index];
                    if (CalculateNormalY(vertices, a, b, c) < 0f)
                    {
                        int swap = b;
                        b = c;
                        c = swap;
                    }
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                }
            }
            return new AOIndoorNavigationMesh(roomInstance, vertices, triangles.ToArray());
        }

        private static float CalculateNormalY(float[] vertices, int a, int b, int c)
        {
            int ao = a * 3;
            int bo = b * 3;
            int co = c * 3;
            float abX = vertices[bo] - vertices[ao];
            float abZ = vertices[bo + 2] - vertices[ao + 2];
            float acX = vertices[co] - vertices[ao];
            float acZ = vertices[co + 2] - vertices[ao + 2];
            return abZ * acX - abX * acZ;
        }
    }
}
