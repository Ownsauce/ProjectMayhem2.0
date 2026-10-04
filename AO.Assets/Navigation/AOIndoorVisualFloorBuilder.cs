using System;
using System.Collections.Generic;
using AO.Assets.Decoders;

namespace AO.Assets.Navigation
{
    /// <summary>Floor/stair support using the same world-space triangles as indoor presentation.</summary>
    public static class AOIndoorVisualFloorBuilder
    {
        public static AOIndoorSurfaceMesh Build(AOIndoorVisualRoom room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            var vertices = new List<float>();
            var triangles = new List<int>();
            foreach (var source in room.Meshes.Values)
            {
                var remap = new Dictionary<int, int>();
                for (int i = 0; i < source.Triangles.Length; i += 3)
                {
                    int a = source.Triangles[i], b = source.Triangles[i + 1], c = source.Triangles[i + 2];
                    int av = a * 3, bv = b * 3, cv = c * 3;
                    float ux = source.Positions[bv] - source.Positions[av];
                    float uy = source.Positions[bv + 1] - source.Positions[av + 1];
                    float uz = source.Positions[bv + 2] - source.Positions[av + 2];
                    float vx = source.Positions[cv] - source.Positions[av];
                    float vy = source.Positions[cv + 1] - source.Positions[av + 1];
                    float vz = source.Positions[cv + 2] - source.Positions[av + 2];
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    float lengthSquared = nx * nx + ny * ny + nz * nz;
                    // Match the ground probe's upward-normal cutoff. Ceilings, walls and
                    // degenerate triangles must not become floor support.
                    if (lengthSquared <= 1e-12f || ny <= 0f || ny * ny < .45f * .45f * lengthSquared) continue;
                    Append(a, source, remap, vertices, triangles);
                    Append(b, source, remap, vertices, triangles);
                    Append(c, source, remap, vertices, triangles);
                }
            }
            return triangles.Count == 0 ? null : new AOIndoorSurfaceMesh(
                vertices.ToArray(), triangles.ToArray(), Array.Empty<int>());
        }

        private static void Append(int index, AOIndoorVisualMesh source, Dictionary<int, int> remap,
            List<float> vertices, List<int> triangles)
        {
            if (!remap.TryGetValue(index, out int mapped))
            {
                mapped = vertices.Count / 3; remap.Add(index, mapped);
                int offset = index * 3;
                vertices.Add(source.Positions[offset]);
                vertices.Add(source.Positions[offset + 1]);
                vertices.Add(source.Positions[offset + 2]);
            }
            triangles.Add(mapped);
        }
    }
}
