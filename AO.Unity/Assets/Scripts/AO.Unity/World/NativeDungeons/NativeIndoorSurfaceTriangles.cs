using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.World
{
    /// <summary>Split native collision surfaces by face so mixed meshes retain their ramps and caps.</summary>
    internal static class NativeIndoorSurfaceTriangles
    {
        public static void Append(IReadOnlyList<Vector3> source, IReadOnlyList<int> indices,
            List<Vector3> floorVertices, List<int> floorTriangles,
            List<Vector3> wallVertices, List<int> wallTriangles,
            List<Vector3> ceilingVertices, List<int> ceilingTriangles)
        {
            var floorMap = new Dictionary<int, int>();
            var wallMap = new Dictionary<int, int>();
            var ceilingMap = new Dictionary<int, int>();
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= source.Count || b >= source.Count || c >= source.Count) continue;
                Vector3 normal = Vector3.Cross(source[b] - source[a], source[c] - source[a]);
                float lengthSquared = normal.sqrMagnitude;
                if (lengthSquared <= 1e-12f) continue;
                bool horizontal = normal.y * normal.y >= .65f * .65f * lengthSquared;
                List<Vector3> vertices;
                List<int> triangles;
                Dictionary<int, int> map;
                if (horizontal && normal.y > 0f)
                { vertices = floorVertices; triangles = floorTriangles; map = floorMap; }
                else if (horizontal)
                { vertices = ceilingVertices; triangles = ceilingTriangles; map = ceilingMap; }
                else
                { vertices = wallVertices; triangles = wallTriangles; map = wallMap; }
                Add(a, source, vertices, triangles, map);
                Add(b, source, vertices, triangles, map);
                Add(c, source, vertices, triangles, map);
            }
        }

        private static void Add(int index, IReadOnlyList<Vector3> source, List<Vector3> vertices,
            List<int> triangles, Dictionary<int, int> map)
        {
            if (!map.TryGetValue(index, out int mapped))
            { mapped = vertices.Count; map.Add(index, mapped); vertices.Add(source[index]); }
            triangles.Add(mapped);
        }
    }
}
