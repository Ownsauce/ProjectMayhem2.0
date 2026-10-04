using System.Numerics;
using AO.Assets.Decoders;
using WorldGen.Dungeons;

/// <summary>Conservative bidirectional walking regions; cannot depend on jumping or falling.</summary>
internal static class NativeTraversalBuilder
{
    private const float Step = .5f, Radius = .4f, Height = 1.9f;
    private sealed class Node { public Vector3 Position; public int Region = -1; public List<int> Neighbors = new(); }
    private static readonly (float X, float Z)[] Footprint = { (0, 0), (-Radius, 0), (Radius, 0), (0, -Radius), (0, Radius) };
    public static void Build(NativeRoomTemplate room, IReadOnlyList<AOIndoorSurfaceMesh> meshes, bool entrance)
    {
        var query = new NativeSurfaceQuery(meshes);
        var nodes = new List<Node>(); var cells = new Dictionary<(int X, int Z), List<int>>();
        float ox = room.OriginX * .001f, oy = room.OriginY * .001f, oz = room.OriginZ * .001f;
        int minX = (int)Math.Ceiling(room.MinX * .001f / Step), maxX = (int)Math.Floor(room.MaxX * .001f / Step);
        int minZ = (int)Math.Ceiling(room.MinZ * .001f / Step), maxZ = (int)Math.Floor(room.MaxZ * .001f / Step);
        for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
        {
            float px = ox + x * Step, pz = oz + z * Step;
            var hits = query.Hits(px, pz);
            foreach (float y in hits.Where(h => h.NormalY >= .65f).Select(h => h.Y).OrderBy(y => y).DistinctBy(y => (int)Math.Round(y * 100)))
            {
                if (!Supported(query, px, y, pz)) continue;
                int index = nodes.Count; nodes.Add(new Node { Position = new(px, y, pz) });
                if (!cells.TryGetValue((x, z), out var list)) cells.Add((x, z), list = new()); list.Add(index);
            }
        }
        foreach (var cell in cells)
        foreach (var offset in new[] { (X: 1, Z: 0), (X: 0, Z: 1) })
        {
            if (!cells.TryGetValue((cell.Key.X + offset.X, cell.Key.Z + offset.Z), out var next)) continue;
            foreach (int a in cell.Value) foreach (int b in next)
            {
                var from = nodes[a].Position; var to = nodes[b].Position;
                if (Math.Abs(from.Y - to.Y) > .45f || !Passage(query, from, to)) continue;
                nodes[a].Neighbors.Add(b); nodes[b].Neighbors.Add(a);
            }
        }
        int regions = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Region >= 0) continue;
            var pending = new Queue<int>(); pending.Enqueue(i); nodes[i].Region = regions;
            while (pending.Count > 0) foreach (int neighbor in nodes[pending.Dequeue()].Neighbors)
                if (nodes[neighbor].Region < 0) { nodes[neighbor].Region = regions; pending.Enqueue(neighbor); }
            regions++;
        }
        foreach (var socket in room.Sockets)
        {
            if (socket.Exterior || socket.Blocked) continue;
            float nx = socket.Facing == 1 ? 1 : socket.Facing == 3 ? -1 : 0;
            float nz = socket.Facing == 0 ? 1 : socket.Facing == 2 ? -1 : 0;
            var target = new Vector3(ox + socket.X * .001f - nx * .8f, oy + socket.Y * .001f, oz + socket.Z * .001f - nz * .8f);
            socket.Region = FindRegion(query, nodes, target);
            if (socket.Region < 0) { socket.Blocked = true; Console.WriteLine($"Traversal rejected: source={room.SourceIndex} socket={Array.IndexOf(room.Sockets, socket)}"); }
        }
        room.SpawnRegion = entrance ? FindRegion(query, nodes, new(ox + room.SpawnX * .001f, oy + room.SpawnY * .001f - .08f, oz + room.SpawnZ * .001f)) : -1;
        if (entrance && room.SpawnRegion < 0) throw new InvalidDataException("Entrance spawn has no capsule walking region.");
        // Keep only usable regions; roof/terrain fragments cannot influence runtime lighting or previews.
        var useful = room.Sockets.Where(s => !s.Blocked && !s.Exterior).Select(s => s.Region).Append(room.SpawnRegion).Where(r => r >= 0).ToHashSet();
        room.WalkPoints = nodes.Where(n => useful.Contains(n.Region)).Select(n => new NativeWalkPoint {
            X = (int)Math.Round((n.Position.X - ox) * 1000), Y = (int)Math.Round((n.Position.Y - oy) * 1000),
            Z = (int)Math.Round((n.Position.Z - oz) * 1000), Region = n.Region,
            Ceiling = Ceiling(query, n.Position) is float ceiling ? (int)Math.Round((ceiling - oy) * 1000) : 0
        }).ToArray();
        room.Lights = room.WalkPoints.Where(p => p.Ceiling > p.Y + 1900 && p.Ceiling < p.Y + 20000)
            .GroupBy(p => ((int)Math.Floor(p.X / 6000f), (int)Math.Floor(p.Z / 6000f), p.Region))
            .Select(group => group.OrderBy(p => Math.Pow(p.X - (group.Key.Item1 * 6000 + 3000), 2) + Math.Pow(p.Z - (group.Key.Item2 * 6000 + 3000), 2)).First())
            .Select(p => new NativeRoomLight { X = p.X, Y = p.Ceiling - 120, Z = p.Z,
                Range = Math.Min(24, (p.Ceiling - p.Y) * .001f + 5), BaseRange = Math.Min(24, (p.Ceiling - p.Y) * .001f + 5),
                Intensity = 3 + (p.Ceiling - p.Y) * .0004f, BaseIntensity = 3 + (p.Ceiling - p.Y) * .0004f }).ToArray();
        Console.WriteLine($"Traversal: source={room.SourceIndex} points={room.WalkPoints.Length}, regions={useful.Count}, spawnRegion={room.SpawnRegion}");
    }
    private static float? Ceiling(NativeSurfaceQuery query, Vector3 p)
    {
        var above = query.Hits(p.X, p.Z).Where(h => h.Y > p.Y + .1f).Select(h => h.Y).ToArray();
        return above.Length == 0 ? null : above.Min();
    }
    private static bool Supported(NativeSurfaceQuery query, float x, float y, float z)
    {
        foreach (var foot in Footprint)
        {
            var hits = query.Hits(x + foot.X, z + foot.Z);
            var floors = hits.Where(h => h.NormalY >= .65f && Math.Abs(h.Y - y) < .4f).OrderByDescending(h => h.Y).ToArray();
            if (floors.Length == 0) return false;
            float floor = floors[0].Y;
            if (hits.Any(h => h.Y > floor + .1f && h.Y < floor + Height)) return false;
            foreach (float height in new[] { .45f, 1f, 1.7f })
                if (query.Blocked(new(x, y + height, z), new(x + foot.X, y + height, z + foot.Z))) return false;
        }
        return true;
    }
    private static bool Passage(NativeSurfaceQuery query, Vector3 a, Vector3 b)
    {
        var midpoint = (a + b) * .5f;
        if (!Supported(query, midpoint.X, midpoint.Y, midpoint.Z)) return false;
        var delta = new Vector3(b.X - a.X, 0, b.Z - a.Z);
        if (delta.LengthSquared() < 1e-8f) return true;
        var direction = Vector3.Normalize(delta);
        var tangent = new Vector3(direction.Z, 0, -direction.X) * Radius;
        foreach (float lateral in new[] { -1f, 0, 1f }) foreach (float height in new[] { .45f, 1f, 1.7f })
            if (query.Blocked(a + tangent * lateral + Vector3.UnitY * height, b + tangent * lateral + Vector3.UnitY * height)) return false;
        return true;
    }
    private static int FindRegion(NativeSurfaceQuery query, List<Node> nodes, Vector3 target)
    {
        foreach (var node in nodes.Where(n => Math.Abs(n.Position.Y - target.Y) < .5f && Vector2.Distance(new(n.Position.X, n.Position.Z), new(target.X, target.Z)) < 1.25f)
            .OrderBy(n => Vector3.DistanceSquared(n.Position, target)))
            if (Passage(query, target, node.Position)) return node.Region;
        return -1;
    }
}
