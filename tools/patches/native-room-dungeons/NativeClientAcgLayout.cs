namespace ZoneEngine_New.Core.WorldGeneration
{
    using System;
    using System.Globalization;
    using System.IO;
    using Vector3 = System.Numerics.Vector3;
    using System.Security.Cryptography;
    using AORebirth.World.Collision;
    using SmokeLounge.AOtomation.Messaging.GameData;
    using WorldGen.Contracts;
    using WorldGen.Dungeons;

    internal static class NativeClientAcgLayout
    {
        public const string GeneratorId = "native-client-acg";
        public static bool IsAcg(GenerationManifest manifest) => manifest.GeneratorId == GeneratorId;

        public static AcgBuildingGeneratorData? Read(DungeonLayout? layout)
        {
            if (layout == null || !IsAcg(layout.Manifest)) return null;
            if (!layout.Manifest.Parameters.TryGetValue("acgPayload", out string text) || text.Length > 24000
                || !AcgBuildingGeneratorData.TryParse(Convert.FromBase64String(text), out var generator))
                throw new InvalidDataException("Invalid native ACG layout payload.");
            return generator;
        }

        public static string CatalogHash(string root, int style)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string file in new[] { "metadata.json", "Rooms.json", "Surfaces.dat", "GNDA.png", "DCGA.png" })
                hash.AppendData(File.ReadAllBytes(Path.Combine(root, "Playfields", style.ToString(CultureInfo.InvariantCulture), file)));
            hash.AppendData(File.ReadAllBytes(Path.Combine(root, "DungeonEntrances.json")));
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }

        /// <summary>Validate support and capsule clearance against the same placed triangles used by the server.</summary>
        public static Vector3 SupportedSpawn(DungeonWorldLayout world, Vector3 proposed)
        {
            float? floor = FloorAt(world, proposed.X, proposed.Z, proposed.Y);
            if (!floor.HasValue) throw new InvalidDataException("Native dungeon entrance has no nearby floor support.");
            foreach (var offset in new[] { (-.4f, 0f), (.4f, 0f), (0f, -.4f), (0f, .4f) })
            {
                float? support = FloorAt(world, proposed.X + offset.Item1, proposed.Z + offset.Item2, floor.Value);
                if (!support.HasValue || MathF.Abs(support.Value - floor.Value) > .35f)
                    throw new InvalidDataException("Native dungeon entrance lacks capsule floor support.");
            }
            var feet = new Vector3(proposed.X, floor.Value + .02f, proposed.Z);
            foreach (var mesh in world.Collision.SurfaceMeshes)
            foreach (var triangle in mesh.Triangles)
            {
                var a = mesh.Vertices[triangle.A]; var b = mesh.Vertices[triangle.B]; var c = mesh.Vertices[triangle.C];
                float minY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), maxY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
                if (maxY <= floor.Value + .08f || minY >= floor.Value + 1.9f) continue;
                // A small entrance envelope must not intersect wall/prop triangles.
                if (TriangleIntersectsSpawn(a, b, c, feet))
                    throw new InvalidDataException("Native dungeon entrance intersects wall or prop collision.");
            }
            return feet;
        }

        private static float? FloorAt(DungeonWorldLayout world, float x, float z, float expected)
        {
            float? best = null;
            foreach (var mesh in world.Collision.SurfaceMeshes)
            foreach (var triangle in mesh.Triangles)
            {
                var a = mesh.Vertices[triangle.A]; var b = mesh.Vertices[triangle.B]; var c = mesh.Vertices[triangle.C];
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() < 1e-10f || MathF.Abs(normal.Y) / normal.Length() < .65f) continue;
                float det = (b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
                if (MathF.Abs(det) < 1e-6f) continue;
                float s = ((x-a.X)*(c.Z-a.Z)-(z-a.Z)*(c.X-a.X))/det;
                float t = ((b.X-a.X)*(z-a.Z)-(b.Z-a.Z)*(x-a.X))/det;
                if (s < -.0001f || t < -.0001f || s+t > 1.0001f) continue;
                float y = a.Y+s*(b.Y-a.Y)+t*(c.Y-a.Y);
                if (MathF.Abs(y-expected) > 1f) continue;
                if (!best.HasValue || MathF.Abs(y-expected) < MathF.Abs(best.Value-expected)) best = y;
            }
            return best;
        }

        private static bool TriangleIntersectsSpawn(Vector3 a, Vector3 b, Vector3 c, Vector3 feet)
        {
            // Segment/triangle intersections along capsule height and two horizontal shoulder diameters.
            foreach (var segment in new[] {
                (feet, feet + new Vector3(0, 1.8f, 0)),
                (feet + new Vector3(-.4f, .9f, 0), feet + new Vector3(.4f, .9f, 0)),
                (feet + new Vector3(0, .9f, -.4f), feet + new Vector3(0, .9f, .4f)) })
            {
                Vector3 direction = segment.Item2-segment.Item1, e1=b-a, e2=c-a, p=Vector3.Cross(direction,e2);
                float det=Vector3.Dot(e1,p); if (MathF.Abs(det)<1e-7f) continue;
                float inv=1f/det; Vector3 distance=segment.Item1-a;
                float u=Vector3.Dot(distance,p)*inv; if (u<0 || u>1) continue;
                Vector3 q=Vector3.Cross(distance,e1); float v=Vector3.Dot(direction,q)*inv;
                if (v<0 || u+v>1) continue;
                float t=Vector3.Dot(e2,q)*inv; if (t>=0 && t<=1) return true;
            }
            return false;
        }
    }
}
