namespace ZoneEngine_New.Core.WorldGeneration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using WorldGen.Contracts;
    using WorldGen.Dungeons;

    public static class NativeRoomPackage
    {
        private static readonly object Sync = new();
        private static NativeRoomCatalog? cachedCatalog;
        private static string cachedSha = "";
        private static (long Length, DateTime Time) catalogRevision, collisionRevision;
        private static Dictionary<int, float[]>? cachedCollision;
        public static NativeRoomCatalog Catalog(out string sha)
        {
            lock (Sync)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "NativeCopies", "pf127.rooms.json");
                var info = new FileInfo(path); var revision = (info.Length, info.LastWriteTimeUtc);
                if (cachedCatalog == null || catalogRevision != revision)
                {
                    if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("Native room catalog exceeds its size limit.");
                    byte[] bytes = File.ReadAllBytes(path);
                    var catalog = JsonSerializer.Deserialize<NativeRoomCatalog>(bytes, new JsonSerializerOptions { IncludeFields = true })
                        ?? throw new InvalidDataException("Missing native room catalog");
                    NativeRoomDungeon.Validate(catalog);
                    cachedSha = NativeRoomDungeon.Sha(bytes); cachedCatalog = catalog; catalogRevision = revision;
                }
                sha = cachedSha;
                return cachedCatalog;
            }
        }

        public static NativeRoomRecipe Recipe(GenerationManifest manifest)
        {
            var catalog = Catalog(out string sha);
            if (manifest.GeneratorId != NativeRoomDungeon.GeneratorId || manifest.GeneratorVersion != NativeRoomDungeon.GeneratorVersion
                || manifest.ParameterSchemaVersion != 1 || manifest.ContentCatalogHash != sha
                || manifest.Parameters["sourceSha256"] != catalog.SourceSha256 || manifest.Parameters["surfaceSha256"] != catalog.SurfaceSha256)
                throw new InvalidDataException("Native room content mismatch");
            if (!manifest.Parameters.TryGetValue("nativeRecipe", out string? snapshot) || snapshot.Length > 1024 * 1024)
                throw new InvalidDataException("Missing resolved native recipe.");
            var data = JsonSerializer.Deserialize<NativeRoomRecipeData>(snapshot, new JsonSerializerOptions { IncludeFields = true })
                ?? throw new InvalidDataException("Empty resolved native recipe.");
            var recipe = data.Restore(catalog);
            if (recipe.Rooms.Count != int.Parse(manifest.Parameters["roomCount"], CultureInfo.InvariantCulture)) throw new InvalidDataException("Native room count differs from recipe.");
            return recipe;
        }
        private static Dictionary<int, float[]> SourceCollision(NativeRoomRecipe recipe)
        {
            lock (Sync)
            {
                string path = Path.Combine(AppContext.BaseDirectory, "NativeCopies", "pf127.rooms.collision");
                var info = new FileInfo(path); var revision = (info.Length, info.LastWriteTimeUtc);
                if (cachedCollision != null && revision == collisionRevision) return cachedCollision;
                if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("Native source collision exceeds its size limit.");
                var source = new Dictionary<int, float[]>();
                using (var reader = new BinaryReader(File.OpenRead(path)))
                {
                    if (new string(reader.ReadChars(4)) != "AONR" || reader.ReadInt32() != 1 || reader.ReadInt32() != recipe.Catalog.SourcePlayfield)
                        throw new InvalidDataException("Invalid native room collision header");
                    int count = reader.ReadInt32();
                    if (count < 1 || count > 65535) throw new InvalidDataException("Invalid native room count");
                    for (int r = 0; r < count; r++)
                    {
                        int id = reader.ReadInt32(), triangles = reader.ReadInt32();
                        if (triangles < 1 || triangles > 400000 || source.ContainsKey(id)) throw new InvalidDataException("Invalid native room collision block");
                        var values = new float[checked(triangles * 9)];
                        for (int i = 0; i < values.Length; i++)
                        {
                            values[i] = reader.ReadSingle();
                            if (!float.IsFinite(values[i])) throw new InvalidDataException("Nonfinite native room collision");
                        }
                        source.Add(id, values);
                    }
                    if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing native collision data");
                }
                cachedCollision = source;
                collisionRevision = revision;
                return source;
            }
        }
        public static List<float[]> Collision(NativeRoomRecipe recipe)
        {
            var source = SourceCollision(recipe);
            var output = new List<float[]>();
            foreach (var room in recipe.Rooms)
            {
                var template = recipe.Template(room);
                if (!source.TryGetValue(room.SourceIndex, out var mesh)) throw new InvalidDataException("Missing native room collision resource");
                var moved = new float[mesh.Length];
                for (int i = 0; i < mesh.Length; i += 3)
                {
                    NativeRoomTransform.SourcePoint(template, room, mesh[i], mesh[i+1], mesh[i+2],
                        out moved[i], out moved[i+1], out moved[i+2]);
                }
                output.Add(moved);
            }
            var seals = new List<float>();
            foreach (var p in NativeRoomDungeon.SealTriangles(recipe))
            { seals.Add(p.X * .001f); seals.Add(p.Y * .001f); seals.Add(p.Z * .001f); }
            if (seals.Count > 0) output.Add(seals.ToArray());
            return output;
        }
    }
}
