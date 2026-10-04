using System;
using System.Collections.Generic;
using AO.Assets.Decoders;

namespace AO.Assets.Navigation
{
    /// <summary>Limits reusable collision to the room footprint; source collision can contain neighboring ramps.</summary>
    public static class AOIndoorRoomCollisionClipper
    {
        private readonly struct Point
        {
            public readonly float X, Y, Z;
            public Point(float x, float y, float z) { X = x; Y = y; Z = z; }
        }
        public static AOIndoorSurfaceMesh Clip(AOIndoorSurfaceMesh mesh, AOIndoorRoom room, float tileSize)
        {
            if (mesh == null || room == null) throw new ArgumentNullException(mesh == null ? nameof(mesh) : nameof(room));
            if (tileSize <= 0 || float.IsNaN(tileSize) || float.IsInfinity(tileSize)) throw new ArgumentOutOfRangeException(nameof(tileSize));
            float minLocalX = -room.CenterX * tileSize - 1, maxLocalX = (room.TileX2 - room.TileX1 - room.CenterX) * tileSize - 1;
            float minLocalZ = -room.CenterZ * tileSize - 1, maxLocalZ = (room.TileY2 - room.TileY1 - room.CenterZ) * tileSize - 1;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (float x in new[] { minLocalX, maxLocalX }) foreach (float z in new[] { minLocalZ, maxLocalZ })
            {
                float rx = x, rz = z;
                switch (room.RotationQuarterTurns & 3)
                { case 1: rx = z; rz = -x; break; case 2: rx = -x; rz = -z; break; case 3: rx = -z; rz = x; break; }
                minX = Math.Min(minX, room.X + rx); maxX = Math.Max(maxX, room.X + rx);
                minZ = Math.Min(minZ, room.Z + rz); maxZ = Math.Max(maxZ, room.Z + rz);
            }
            var vertices = new List<float>(); var indices = new List<int>();
            var polygon = new List<Point>(7); var scratch = new List<Point>(7);
            for (int triangle = 0; triangle < mesh.Triangles.Length; triangle += 3)
            {
                polygon.Clear();
                for (int k = 0; k < 3; k++)
                { int v = mesh.Triangles[triangle + k] * 3; polygon.Add(new Point(mesh.Vertices[v], mesh.Vertices[v + 1], mesh.Vertices[v + 2])); }
                Plane(true, minX, true); Plane(true, maxX, false); Plane(false, minZ, true); Plane(false, maxZ, false);
                for (int k = 1; k + 1 < polygon.Count; k++)
                {
                    Point a = polygon[0], b = polygon[k], c = polygon[k + 1];
                    float ux = b.X - a.X, uy = b.Y - a.Y, uz = b.Z - a.Z;
                    float vx = c.X - a.X, vy = c.Y - a.Y, vz = c.Z - a.Z;
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    if (nx * nx + ny * ny + nz * nz < 1e-12f) continue;
                    Add(a); Add(b); Add(c);
                }
            }
            return new AOIndoorSurfaceMesh(vertices.ToArray(), indices.ToArray(), mesh.MaterialCandidates);
            void Add(Point p) { indices.Add(vertices.Count / 3); vertices.Add(p.X); vertices.Add(p.Y); vertices.Add(p.Z); }
            void Plane(bool xAxis, float boundary, bool minimum)
            {
                if (polygon.Count == 0) return;
                scratch.Clear(); Point previous = polygon[polygon.Count - 1];
                float previousDistance = (xAxis ? previous.X : previous.Z) - boundary;
                bool previousInside = minimum ? previousDistance >= 0 : previousDistance <= 0;
                foreach (Point current in polygon)
                {
                    float distance = (xAxis ? current.X : current.Z) - boundary;
                    bool inside = minimum ? distance >= 0 : distance <= 0;
                    if (inside != previousInside)
                    {
                        float t = previousDistance / (previousDistance - distance);
                        scratch.Add(new Point(xAxis ? boundary : previous.X + (current.X - previous.X) * t,
                            previous.Y + (current.Y - previous.Y) * t,
                            xAxis ? previous.Z + (current.Z - previous.Z) * t : boundary));
                    }
                    if (inside) scratch.Add(current);
                    previous = current; previousDistance = distance; previousInside = inside;
                }
                var swap = polygon; polygon = scratch; scratch = swap;
            }
        }
    }
}
