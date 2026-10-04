using System.Globalization;
using System.Text.Json;
using AO.Assets.Decoders;
using AO.Assets.Navigation;
using AO.Assets.ResourceDatabase;
using WorldGen.Dungeons;

if (args.Length != 3 && args.Length != 5)
    throw new ArgumentException("<AO-install> <native-catalog.json> <report.json> [seed room-count]");
var json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
var catalog = JsonSerializer.Deserialize<NativeRoomCatalog>(File.ReadAllText(args[1]), json)
    ?? throw new InvalidDataException("Missing native catalog.");
NativeRoomDungeon.Validate(catalog);
using var database = new AOResourceDatabase(args[0]);
if (!database.TryReadRaw(AOResourceTypes.Playfield, catalog.SourcePlayfield, out byte[] record))
    throw new InvalidDataException("Missing source playfield.");
if (NativeRoomDungeon.Sha(record) != catalog.SourceSha256)
    throw new InvalidDataException("Source playfield differs from the catalog.");
var definition = AOPlayfieldDefinitionDecoder.Decode(record, catalog.SourcePlayfield);
ulong seed = args.Length == 5 ? ulong.Parse(args[3], CultureInfo.InvariantCulture) : 90602;
int count = args.Length == 5 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 12;
var recipe = NativeRoomDungeon.Generate(catalog, seed, count, "mixed");
var wholeBlocks = AOAcgCompatibility.WholeBlockRooms(definition);
var invalid = recipe.Rooms.Where(r => !wholeBlocks.Contains(r.SourceIndex)).Select(r => r.SourceIndex).Distinct().ToArray();
var report = new {
    Version = 1, SourcePlayfield = catalog.SourcePlayfield, catalog.SourceSha256,
    ExistingAdapter = "AORebirth.DungeonGenerator / AcgBuildingGeneratorData v3",
    Seed = seed, RoomCount = count, PassesRoomSizePreflight = invalid.Length == 0,
    WholeBlockSourceRooms = wholeBlocks, RejectedSelectedRooms = invalid,
    EntranceFitsWholeBlocks = wholeBlocks.Contains(catalog.EntranceSourceRoom),
    SourceRooms = definition.Rooms.Select((r, i) => new {
        SourceIndex = i, r.Name, TilesX = r.TileX2 - r.TileX1, TilesZ = r.TileY2 - r.TileY1,
        WholeBlockDimensions = AOAcgCompatibility.HasWholeBlockDimensions(r), r.Y,
        DoorCells = r.DoorConnections.Count,
        StaticLinks = r.DoorConnections.Count(d => d.ZoneLink >= 0)
    }).ToArray(),
    Limitations = new[] {
        "This report checks the existing adapter's five-tile size requirement, not every native client legality rule.",
        "Unity's arbitrary room poses, measured vertical offsets and custom collision/lighting are not an AO ACG payload.",
        "No source assets or client files are modified. No client is started. No experimental packet is sent.",
        "Use the separate pf127-client copy command to verify native source loading before attempting rearrangement."
    }
};
string output = Path.GetFullPath(args[2]); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, json));
Console.WriteLine($"PF {catalog.SourcePlayfield}: whole-block templates={wholeBlocks.Length}/{definition.RoomCount}, entrance-compatible={report.EntranceFitsWholeBlocks}, selected rejected={invalid.Length}; report={output}");
Console.WriteLine(report.PassesRoomSizePreflight
    ? "Size preflight passed; doorway, placement and original-client validation still required."
    : "Current ACG adapter cannot encode this selected layout. Existing Unity generation remains available.");
