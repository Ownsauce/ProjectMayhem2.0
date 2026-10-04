namespace ZoneEngine_New.Core.WorldGeneration
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using WorldGen.Contracts;
    using WorldGen.Dungeons;

    /// <summary>Atomic saved layout recipes. Contains metadata only, never AO rendering assets.</summary>
    public static class NativeRoomArchive
    {
        private sealed class Record
        {
            public Record() { }
            public int Version { get; set; } = 1;
            public string WorldId { get; set; } = "";
            public string Seed { get; set; } = "";
            public int RoomCount { get; set; }
            public string Pool { get; set; } = "";
            public string GeneratorVersion { get; set; } = "";
            public string CatalogHash { get; set; } = "";
            public string LayoutHash { get; set; } = "";
            public NativeRoomRecipeData Recipe { get; set; } = null!;
        }
        private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
        private static string DirectoryPath
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable("AO_REBIRTH_DUNGEON_STORE");
                if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
                string? state = Environment.GetEnvironmentVariable("STATE_DIRECTORY");
                string root = !string.IsNullOrWhiteSpace(state) ? state.Split(Path.PathSeparator)[0]
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AORebirth");
                return Path.Combine(root, "NativeDungeons");
            }
        }
        private static string PathFor(string world) => Path.Combine(DirectoryPath, NativeRoomDungeon.Sha(System.Text.Encoding.UTF8.GetBytes(world)) + ".json");
        public static bool Exists(string world) => File.Exists(PathFor(world));
        public static GenerationManifest Manifest(string world, ulong seed, int count, string pool, NativeRoomCatalog catalog, string hash, NativeRoomRecipe recipe)
        {
            var manifest = new GenerationManifest(NativeRoomDungeon.GeneratorId, NativeRoomDungeon.GeneratorVersion, 1, seed, world, hash);
            manifest.Parameters["nativePlayfield"] = catalog.SourcePlayfield.ToString(CultureInfo.InvariantCulture);
            manifest.Parameters["sourceSha256"] = catalog.SourceSha256; manifest.Parameters["surfaceSha256"] = catalog.SurfaceSha256;
            manifest.Parameters["roomCount"] = count.ToString(CultureInfo.InvariantCulture); manifest.Parameters["pool"] = pool;
            manifest.Parameters["nativeRecipe"] = JsonSerializer.Serialize(NativeRoomRecipeData.Capture(recipe), Json);
            return manifest;
        }
        public static void Save(GenerationManifest manifest, NativeRoomRecipe recipe)
        {
            var record = new Record { WorldId = manifest.WorldId, Seed = manifest.Seed.ToString(CultureInfo.InvariantCulture),
                RoomCount = recipe.Rooms.Count, Pool = manifest.Parameters["pool"], GeneratorVersion = manifest.GeneratorVersion,
                CatalogHash = manifest.ContentCatalogHash, LayoutHash = NativeRoomDungeon.Hash(manifest, recipe), Recipe = NativeRoomRecipeData.Capture(recipe) };
            Directory.CreateDirectory(DirectoryPath);
            string path = PathFor(record.WorldId), temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, record, Json); stream.Flush(true); }
                File.Move(temporary, path); // Refuse overwriting another saved instance with the same name.
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static bool TryLoad(string world, out GenerationManifest? manifest, out NativeRoomRecipe? recipe)
        {
            manifest = null; recipe = null; string path = PathFor(world);
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Saved native recipe exceeds its size limit.");
            var record = JsonSerializer.Deserialize<Record>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Empty native recipe.");
            var catalog = NativeRoomPackage.Catalog(out string hash);
            if (record.Version != 1 || record.WorldId != world || record.GeneratorVersion != NativeRoomDungeon.GeneratorVersion || record.CatalogHash != hash)
                throw new InvalidDataException("Saved dungeon uses another generator/catalog revision; use a new instance name.");
            recipe = record.Recipe.Restore(catalog);
            if (recipe.Rooms.Count != record.RoomCount) throw new InvalidDataException("Saved room count mismatch.");
            manifest = Manifest(world, ulong.Parse(record.Seed, CultureInfo.InvariantCulture), record.RoomCount, record.Pool, catalog, hash, recipe);
            if (NativeRoomDungeon.Hash(manifest, recipe) != record.LayoutHash) throw new InvalidDataException("Saved native layout hash mismatch.");
            return true;
        }
        public static void Delete(string world) { string path = PathFor(world); if (File.Exists(path)) File.Delete(path); }
    }
}
