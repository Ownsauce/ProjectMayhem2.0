using System.Text.Json;
using WorldGen.Contracts;
using WorldGen.Dungeons;

if (args.Length != 2) throw new ArgumentException("Supply client and server NativeCopies roots.");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    try { action(); } catch (InvalidDataException) { return; } catch (IOException) { return; }
    throw new Exception("Invalid catalog publication was accepted.");
}
var codec = new Codec();
var installed = new NativeRoomCatalogStore(args[0], codec);
var server = new NativeRoomCatalogStore(args[1], codec);
Check(installed.Registry().DefaultSource == "ao-subway", "Existing default source changed.");
foreach (var source in installed.Registry().Sources)
{
    var package = installed.Load(source.Id);
    Check(package.Revision == server.Load(source.Id).Revision, "Client/server catalog revisions differ.");
    Check(package.Publication.CollisionSha256 == server.Load(source.Id).Publication.CollisionSha256, "Client/server collision differs.");
    foreach (int count in new[] { 4, 12, 24 })
    foreach (ulong seed in new ulong[] { 1, 90602, 90603 })
    {
        var recipe = NativeRoomDungeon.Generate(package.Catalog, seed, count, "mixed");
        var manifest = new GenerationManifest(NativeRoomDungeon.GeneratorId, NativeRoomDungeon.GeneratorVersion, 1, seed, "catalog-test", package.Revision);
        var result = DungeonGenerationResults.FromRooms(manifest, recipe,
            new DungeonResourceCatalog("ao-install", source.Id, package.Revision),
            new DungeonResourceCatalog("native-collision", source.Id, package.Revision));
        Check(result.LayoutHash == NativeRoomDungeon.Hash(manifest, recipe), "Common result changed recipe identity.");
        Check(result.Rooms.All(r => r.Presentation.Catalog.CatalogId == source.Id), "Room references lost source scope.");
    }
}
string temporary = Path.Combine(Path.GetTempPath(), "worldgen-catalog-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    string a = Path.Combine(temporary, "a"), z = Path.Combine(temporary, "z");
    var original = installed.Load("ao-subway");
    string originalPath = Path.Combine(args[0], "RoomCatalogs", "ao-subway", original.Revision, "catalog.json");
    string first = NativeRoomCatalogStore.Publish(original.Source, originalPath, original.CollisionPath, 0, codec, a, z);
    var changed = codec.ReadCatalog(File.ReadAllText(originalPath)); changed.Rooms[0].Name += " reviewed";
    string updatedPath = Path.Combine(temporary, "updated.json"); File.WriteAllText(updatedPath, JsonSerializer.Serialize(changed, Codec.Json));
    string second = NativeRoomCatalogStore.Publish(original.Source, updatedPath, original.CollisionPath, 1, codec, a, z);
    Check(first != second, "Changed catalog did not create a new revision.");
    var store = new NativeRoomCatalogStore(a, codec);
    Check(store.Load("ao-subway").Revision == second && store.Load("ao-subway", first).Revision == first, "Historical revision lookup failed.");
    Check(store.Load("ao-subway", first).Catalog.Rooms[0].Name == original.Catalog.Rooms[0].Name, "Publishing changed an old revision.");
    Reject(() => store.Load("../escape")); Reject(() => store.Load("ao-subway", "../escape"));
    var wrongSource = new NativeRoomSourceRegistration { Id = "ao-wrong", Name = "Wrong", Playfield = 1931 };
    Reject(() => NativeRoomCatalogStore.Publish(wrongSource, originalPath, original.CollisionPath, 0, codec, a, z));
    string corrupt = Path.Combine(temporary, "wrong.collision"); File.Copy(original.CollisionPath, corrupt);
    using (var file = File.OpenWrite(corrupt)) { file.Position = 8; file.Write(BitConverter.GetBytes(1931)); }
    Reject(() => NativeRoomCatalogStore.Publish(original.Source, originalPath, corrupt, 0, codec, a, z));
    // A failure in the second destination restores the first destination's selected revision.
    var badRegistry = codec.ReadRegistry(File.ReadAllText(Path.Combine(z, "sources.json")));
    badRegistry.Sources[0].Playfield = 1931;
    File.WriteAllText(Path.Combine(z, "sources.json"), codec.WriteRegistry(badRegistry));
    changed.Rooms[0].Name += " again"; File.WriteAllText(updatedPath, JsonSerializer.Serialize(changed, Codec.Json));
    Reject(() => NativeRoomCatalogStore.Publish(original.Source, updatedPath, original.CollisionPath, 2, codec, a, z));
    Check(new NativeRoomCatalogStore(a, codec).Load().Revision == second, "Failed publication changed an active client catalog.");
    // A file with the same published path must still match the recorded content hash on a fresh load.
    string publishedCollision = store.Load("ao-subway", first).CollisionPath;
    using (var file = File.OpenWrite(publishedCollision)) { file.Position = 30; file.WriteByte(123); }
    Reject(() => new NativeRoomCatalogStore(a, codec).Load("ao-subway", first));
    // In-process caches retain already verified immutable snapshots.
    Check(store.Load("ao-subway", first).Revision == first, "A previously resolved immutable revision disappeared.");
    Console.WriteLine("PASS: source-scoped results, 4/12/24-room layouts, matching publication, retained revisions, rollback and corrupt/mismatched data rejection.");
}
finally { Directory.Delete(temporary, true); }

sealed class Codec : INativeRoomCatalogCodec
{
    public static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    public NativeRoomCatalog ReadCatalog(string json) => JsonSerializer.Deserialize<NativeRoomCatalog>(json, Json)!;
    public NativeRoomSourceRegistry ReadRegistry(string json) => JsonSerializer.Deserialize<NativeRoomSourceRegistry>(json, Json)!;
    public string WriteRegistry(NativeRoomSourceRegistry value) => JsonSerializer.Serialize(value, Json);
    public NativeRoomCatalogPublication ReadPublication(string json) => JsonSerializer.Deserialize<NativeRoomCatalogPublication>(json, Json)!;
    public string WritePublication(NativeRoomCatalogPublication value) => JsonSerializer.Serialize(value, Json);
}
