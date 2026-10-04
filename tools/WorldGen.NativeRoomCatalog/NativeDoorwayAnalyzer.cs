using AO.Assets.Decoders;

/// <summary>Measures an open doorway from source collision rather than buried tile terrain.</summary>
internal static class NativeDoorwayAnalyzer
{
    private const float CharacterHeight = 1.8f;
    private const float HeadMargin = .1f;
    private static readonly float[] Lateral = { -.45f, 0, .45f };
    private static readonly float[] Depths = { .05f, .35f, .75f, 1.1f };
    private static readonly float[] BodyHeights = { .55f, 1.05f, 1.75f };

    public static bool TryResolve(IReadOnlyList<AOIndoorSurfaceMesh> meshes, float x, float z, int facing,
        out float floor, out float clearance)
    {
        (float nx, float nz) = facing switch { 1 => (1, 0), 2 => (0, -1), 3 => (-1, 0), _ => (0, 1) };
        var center = Hits(meshes, x - nx * .35f, z - nz * .35f);
        foreach (float candidate in center.Where(h => h.NormalY >= .65f).Select(h => h.Y).Distinct().OrderByDescending(y => y))
        {
            var centerRoofs = center.Where(h => h.NormalY < -.05f && h.Y > candidate + .05f).Select(h => h.Y).ToArray();
            if (centerRoofs.Length == 0 || centerRoofs.Min() - candidate < CharacterHeight + HeadMargin) continue;
            float candidateClearance = centerRoofs.Min() - candidate;
            bool valid = true;
            foreach (float depth in Depths)
            foreach (float lateral in Lateral)
            {
                var hits = Hits(meshes, x - nx * depth + nz * lateral, z - nz * depth - nx * lateral);
                var supports = hits.Where(h => h.NormalY >= .65f && Math.Abs(h.Y - candidate) <= .45f)
                    .OrderBy(h => Math.Abs(h.Y - candidate)).ToArray();
                if (supports.Length == 0) { valid = false; break; }
                float support = supports[0].Y;
                var ceilings = hits.Where(h => h.NormalY < -.05f && h.Y > support + .05f).Select(h => h.Y).ToArray();
                // The opening can have a short unroofed seam; the adjoining room supplies
                // its roof there. An existing low ceiling must still reject the passage.
                if (ceilings.Length > 0)
                {
                    if (ceilings.Min() - support < CharacterHeight + HeadMargin) { valid = false; break; }
                    candidateClearance = Math.Min(candidateClearance, ceilings.Min() - support);
                }
            }
            if (!valid) continue;
            // A lintel can have floor-like normals and headroom above it, but a solid wall
            // across that layer. Check both sides of the plane at capsule body heights.
            foreach (float lateral in Lateral)
            foreach (float height in BodyHeights)
                if (Blocked(meshes, x - nx * .8f + nz * lateral, candidate + height, z - nz * .8f - nx * lateral,
                    nx * 1.6f, 0, nz * 1.6f)) { valid = false; break; }
            if (!valid) continue;
            var threshold = Hits(meshes, x - nx * .05f, z - nz * .05f)
                .Where(h => h.NormalY >= .65f && Math.Abs(h.Y - candidate) <= .45f)
                .OrderBy(h => Math.Abs(h.Y - candidate)).First();
            floor = threshold.Y; clearance = candidateClearance; return true;
        }
        floor = 0; clearance = 0; return false;
    }

    public static float? FloorNear(IReadOnlyList<AOIndoorSurfaceMesh> meshes, float x, float z, float expected)
    {
        var hits = Hits(meshes, x, z).Where(h => h.NormalY >= .65f && Math.Abs(h.Y - expected) <= .45f)
            .OrderBy(h => Math.Abs(h.Y - expected)).ToArray();
        return hits.Length == 0 ? null : hits[0].Y;
    }

    private static List<(float Y, float NormalY)> Hits(IReadOnlyList<AOIndoorSurfaceMesh> meshes, float x, float z)
    {
        var hits = new List<(float, float)>();
        foreach (var mesh in meshes) for (int i = 0; i < mesh.Triangles.Length; i += 3)
        {
            var p = mesh.Vertices; int a = mesh.Triangles[i] * 3, b = mesh.Triangles[i+1] * 3, c = mesh.Triangles[i+2] * 3;
            float ux=p[b]-p[a], uy=p[b+1]-p[a+1], uz=p[b+2]-p[a+2];
            float vx=p[c]-p[a], vy=p[c+1]-p[a+1], vz=p[c+2]-p[a+2];
            float nx=uy*vz-uz*vy, ny=uz*vx-ux*vz, nz=ux*vy-uy*vx;
            float length=(float)Math.Sqrt(nx*nx+ny*ny+nz*nz), det=ux*vz-uz*vx;
            if (length < 1e-6f || Math.Abs(det) < 1e-6f) continue;
            float u=((x-p[a])*vz-(z-p[a+2])*vx)/det, v=(ux*(z-p[a+2])-uz*(x-p[a]))/det;
            if (u < -.001f || v < -.001f || u+v > 1.001f) continue;
            hits.Add((p[a+1]+u*uy+v*vy, ny/length));
        }
        return hits;
    }

    private static bool Blocked(IReadOnlyList<AOIndoorSurfaceMesh> meshes,
        float x, float y, float z, float dx, float dy, float dz)
    {
        foreach (var mesh in meshes) for (int i=0; i<mesh.Triangles.Length; i+=3)
        {
            var p=mesh.Vertices; int a=mesh.Triangles[i]*3,b=mesh.Triangles[i+1]*3,c=mesh.Triangles[i+2]*3;
            float ux=p[b]-p[a],uy=p[b+1]-p[a+1],uz=p[b+2]-p[a+2];
            float vx=p[c]-p[a],vy=p[c+1]-p[a+1],vz=p[c+2]-p[a+2];
            float hx=dy*vz-dz*vy,hy=dz*vx-dx*vz,hz=dx*vy-dy*vx;
            float det=ux*hx+uy*hy+uz*hz; if (Math.Abs(det)<1e-6f) continue;
            float sx=x-p[a],sy=y-p[a+1],sz=z-p[a+2],inv=1f/det;
            float u=(sx*hx+sy*hy+sz*hz)*inv; if (u < -.001f || u > 1.001f) continue;
            float qx=sy*uz-sz*uy,qy=sz*ux-sx*uz,qz=sx*uy-sy*ux;
            float v=(dx*qx+dy*qy+dz*qz)*inv; if (v < -.001f || u+v > 1.001f) continue;
            float t=(vx*qx+vy*qy+vz*qz)*inv; if (t >= 0 && t <= 1) return true;
        }
        return false;
    }
}
