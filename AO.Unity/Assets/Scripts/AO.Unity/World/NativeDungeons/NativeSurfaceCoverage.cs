using System;
using System.Collections.Generic;
using UnityEngine;

namespace AO.Unity.World
{
    /// <summary>Spatially matches diagnostic triangles to source visual surfaces.</summary>
    internal sealed class NativeSurfaceCoverage
    {
        private struct Triangle { public Vector3 A, B, C, Normal; }
        private readonly Dictionary<Vector3Int, List<int>> _cells = new Dictionary<Vector3Int, List<int>>();
        private readonly List<Triangle> _triangles = new List<Triangle>();
        private readonly float _cellSize, _tolerance, _squaredTolerance;
        public NativeSurfaceCoverage(float cellSize, float tolerance)
        {
            if (cellSize <= 0 || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
            _cellSize = cellSize; _tolerance = tolerance; _squaredTolerance = tolerance * tolerance;
        }
        private Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / _cellSize), Mathf.FloorToInt(p.y / _cellSize), Mathf.FloorToInt(p.z / _cellSize));
        public void Add(List<Vector3> positions, List<int> indices)
        {
            for (int i = 0; i < indices.Count; i += 3)
            {
                Vector3 a = positions[indices[i]], b = positions[indices[i+1]], c = positions[indices[i+2]];
                Vector3 normal = Vector3.Cross(b-a, c-a); if (normal.sqrMagnitude < 1e-12f) continue;
                int id = _triangles.Count; _triangles.Add(new Triangle { A=a, B=b, C=c, Normal=normal.normalized });
                Vector3 padding = Vector3.one * _tolerance;
                Vector3Int min = Cell(Vector3.Min(a, Vector3.Min(b,c)) - padding), max = Cell(Vector3.Max(a, Vector3.Max(b,c)) + padding);
                if ((long)(max.x-min.x+1)*(max.y-min.y+1)*(max.z-min.z+1) > 4096) throw new InvalidOperationException("Visual triangle exceeds spatial coverage limits");
                for (int x=min.x; x<=max.x; x++) for (int y=min.y; y<=max.y; y++) for (int z=min.z; z<=max.z; z++)
                { var key = new Vector3Int(x,y,z); if (!_cells.TryGetValue(key, out var list)) _cells.Add(key, list=new List<int>()); list.Add(id); }
            }
        }
        public bool Covers(Vector3 point, Vector3? expectedNormal = null)
        {
            if (!_cells.TryGetValue(Cell(point), out var candidates)) return false;
            foreach (int id in candidates)
            {
                Triangle triangle = _triangles[id];
                if (expectedNormal.HasValue && Mathf.Abs(Vector3.Dot(triangle.Normal, expectedNormal.Value)) < 0.9f) continue;
                float distance = Vector3.Dot(point-triangle.A, triangle.Normal);
                if (Mathf.Abs(distance) > _tolerance) continue;
                Vector3 projected = point-distance*triangle.Normal;
                float a = Vector3.Dot(Vector3.Cross(triangle.B-triangle.A, projected-triangle.A), triangle.Normal);
                float b = Vector3.Dot(Vector3.Cross(triangle.C-triangle.B, projected-triangle.B), triangle.Normal);
                float c = Vector3.Dot(Vector3.Cross(triangle.A-triangle.C, projected-triangle.C), triangle.Normal);
                if (a>=0 && b>=0 && c>=0) return true;
                if (distance*distance + Mathf.Min(SegmentDistanceSquared(projected,triangle.A,triangle.B), Mathf.Min(SegmentDistanceSquared(projected,triangle.B,triangle.C), SegmentDistanceSquared(projected,triangle.C,triangle.A))) <= _squaredTolerance) return true;
            }
            return false;
        }
        private static float SegmentDistanceSquared(Vector3 p, Vector3 a, Vector3 b)
        { Vector3 edge=b-a; float length=edge.sqrMagnitude; float t=length>0 ? Mathf.Clamp01(Vector3.Dot(p-a,edge)/length) : 0; return (p-a-edge*t).sqrMagnitude; }
    }
}
