using System.Numerics;
using AO.Assets.Decoders;

/// <summary>Spatially indexed, two-sided queries over captured source triangles.</summary>
internal sealed class NativeSurfaceQuery
{
    private readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C, float NormalY);
    private readonly Dictionary<(int X, int Z), List<Triangle>> bins = new();
    public NativeSurfaceQuery(IEnumerable<AOIndoorSurfaceMesh> meshes)
    {
        foreach (var mesh in meshes)
        for (int i = 0; i < mesh.Triangles.Length; i += 3)
        {
            Vector3 Vertex(int j) { int v = mesh.Triangles[i + j] * 3; return new(mesh.Vertices[v], mesh.Vertices[v + 1], mesh.Vertices[v + 2]); }
            var a = Vertex(0); var b = Vertex(1); var c = Vertex(2);
            var n = Vector3.Cross(b - a, c - a); if (n.LengthSquared() < 1e-12f) continue;
            var triangle = new Triangle(a, b, c, Vector3.Normalize(n).Y);
            for (int x = (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))); x <= Math.Floor(Math.Max(a.X, Math.Max(b.X, c.X))); x++)
            for (int z = (int)Math.Floor(Math.Min(a.Z, Math.Min(b.Z, c.Z))); z <= Math.Floor(Math.Max(a.Z, Math.Max(b.Z, c.Z))); z++)
            {
                if (!bins.TryGetValue((x, z), out var list)) bins.Add((x, z), list = new());
                list.Add(triangle);
            }
        }
    }
    public List<(float Y, float NormalY)> Hits(float x, float z)
    {
        var result = new List<(float, float)>();
        if (!bins.TryGetValue(((int)Math.Floor(x), (int)Math.Floor(z)), out var list)) return result;
        foreach (var t in list)
        {
            var u = t.B - t.A; var v = t.C - t.A; float det = u.X * v.Z - u.Z * v.X;
            if (Math.Abs(det) < 1e-6f) continue;
            float s = ((x - t.A.X) * v.Z - (z - t.A.Z) * v.X) / det;
            float w = (u.X * (z - t.A.Z) - u.Z * (x - t.A.X)) / det;
            if (s >= -.0001f && w >= -.0001f && s + w <= 1.0001f) result.Add((t.A.Y + s * u.Y + w * v.Y, t.NormalY));
        }
        return result;
    }
    public bool Blocked(Vector3 start, Vector3 end)
    {
        var direction = end - start;
        for (int x = (int)Math.Floor(Math.Min(start.X, end.X)); x <= Math.Floor(Math.Max(start.X, end.X)); x++)
        for (int z = (int)Math.Floor(Math.Min(start.Z, end.Z)); z <= Math.Floor(Math.Max(start.Z, end.Z)); z++)
        {
            if (!bins.TryGetValue((x, z), out var list)) continue;
            foreach (var t in list)
            {
                var u = t.B - t.A; var v = t.C - t.A; var h = Vector3.Cross(direction, v);
                float det = Vector3.Dot(u, h); if (Math.Abs(det) < 1e-6f) continue;
                var s = start - t.A; float a = Vector3.Dot(s, h) / det; if (a < 0 || a > 1) continue;
                var q = Vector3.Cross(s, u); float b = Vector3.Dot(direction, q) / det; if (b < 0 || a + b > 1) continue;
                float d = Vector3.Dot(v, q) / det; if (d > .0001f && d < .9999f) return true;
            }
        }
        return false;
    }
}
