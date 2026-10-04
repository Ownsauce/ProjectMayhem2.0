using System.Text.Json;
using WorldGen.Dungeons;

if (args.Length < 4 || args.Length > 5)
    throw new ArgumentException("<source-definition.json> <prepared-directory> <client-NativeCopies> <server-NativeCopies> [authoring-revision]");
using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
var definition = document.RootElement;
var source = new NativeRoomSourceRegistration {
    Id = definition.GetProperty("SourceId").GetString()!, Name = definition.GetProperty("DisplayName").GetString()!,
    Playfield = definition.GetProperty("SourcePlayfield").GetInt32()
};
string prefix = Path.Combine(args[1], "pf" + source.Playfield + ".rooms");
var codec = new JsonCodec();
var catalog = codec.ReadCatalog(File.ReadAllText(prefix + ".json"));
foreach (string pool in catalog.Rooms.Select(r => r.Pool).Distinct().Prepend("mixed"))
foreach (int count in new[] { 4, 12, 24 })
foreach (ulong seed in new ulong[] { 1, 90602 })
{
    if (count < (catalog.PinnedRecipe?.Rooms.Length ?? 0)) continue;
    var recipe = NativeRoomDungeon.Generate(catalog, seed, count, pool);
    NativeRoomTraversal.Validate(recipe);
}
string revision = NativeRoomCatalogStore.Publish(source, prefix + ".json", prefix + ".collision",
    args.Length == 5 ? long.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture) : 0, codec, args[2], args[3]);
Console.WriteLine($"Published {source.Id}: PF {source.Playfield}, {catalog.Rooms.Length} rooms, revision {revision}. Matching client/server packages retained.");

sealed class JsonCodec : INativeRoomCatalogCodec
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    public NativeRoomCatalog ReadCatalog(string json) => JsonSerializer.Deserialize<NativeRoomCatalog>(json, Json)!;
    public NativeRoomSourceRegistry ReadRegistry(string json) => JsonSerializer.Deserialize<NativeRoomSourceRegistry>(json, Json)!;
    public string WriteRegistry(NativeRoomSourceRegistry value) => JsonSerializer.Serialize(value, Json);
    public NativeRoomCatalogPublication ReadPublication(string json) => JsonSerializer.Deserialize<NativeRoomCatalogPublication>(json, Json)!;
    public string WritePublication(NativeRoomCatalogPublication value) => JsonSerializer.Serialize(value, Json);
}
