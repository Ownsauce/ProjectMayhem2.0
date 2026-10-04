using System.Security.Cryptography;
using System.Text.Json;
using AODB.Common.RDBObjects;
using AO.Assets.Decoders;
using AO.Assets.ResourceDatabase;
using AORebirth.Core.GameData;
using AORebirth.World.Collision;
using AORebirth.DungeonGenerator;

if (args.Length != 4 && !(args.Length == 5 && args[4] == "--verify"))
    throw new ArgumentException("<AO-install> <style-id> <surface.aois> <local-GameData-root> [--verify]");
bool verify = args.Length == 5;
int style = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
using var database = new AOResourceDatabase(args[0]);
if (!database.TryReadRaw(AOResourceTypes.Playfield, style, out var raw)) throw new InvalidDataException("Missing source style.");
var definition = AOPlayfieldDefinitionDecoder.Decode(raw, style);
var surfaces = AOIndoorSurfaceStreamDecoder.Decode(args[2], style);
if (definition.RoomCount != surfaces.Rooms.Count || definition.TilemapId != surfaces.Tilemap.Id)
    throw new InvalidDataException("Surface reader does not match the source style.");
string root = Path.GetFullPath(args[3]);
string folder = Path.Combine(root, "Playfields", style.ToString());
if (Directory.Exists(folder) && !verify) throw new IOException("Refusing to overwrite an existing style folder.");
if (verify && !Directory.Exists(folder)) throw new IOException("Missing prepared style folder.");
var entries = new List<PlayfieldSurfaceEntry>();
int triangles = 0;
foreach (var room in surfaces.Rooms)
{
    if (!database.TryReadRaw(AOResourceTypes.Surface, (style << 16) | room.Instance, out var payload))
        throw new InvalidDataException("Missing room surface " + room.Instance);
    using var input = new MemoryStream(payload, 12, payload.Length - 12, false);
    using var reader = new BinaryReader(input);
    var record = new SurfaceResource { RecordVersion = 5 };
    record.Deserialize(reader);
    int actual = record.Surfaces?.Sum(mesh => mesh.Triangles.Count) ?? 0;
    int expected = room.Meshes.Sum(mesh => mesh.TriangleCount);
    if (actual != expected) throw new InvalidDataException($"Room {room.Instance}: surface triangles {actual} differ from installed DLL reader {expected}.");
    triangles += actual;
    entries.Add(new PlayfieldSurfaceEntry(room.Instance, payload));
}
var map = surfaces.Tilemap;
var metadata = new PlayfieldMetaData {
    SchemaVersion = 1, RecordType = AOResourceTypes.Playfield, TilemapResource = map.Id,
    RawRecordSha256 = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(),
    Width = map.Width, Height = map.Height, TileSize = map.TileSize, HeightScale = map.HeightmapScale,
    TilemapFormat = "GNDA", HeightFormat = "GNDA", HeightImage = "GNDA.png",
    HeightPixelsSha256 = Convert.ToHexString(SHA256.HashData(map.Heightmap)).ToLowerInvariant(),
    PlayfieldId = style, PlayfieldName = definition.Name, IsIndoor = true
};
if (!metadata.IsValid(out var error)) throw new InvalidDataException(error);
var rooms = new PlayfieldRoomsData {
    SchemaVersion = 1, RecordType = AOResourceTypes.Playfield, RecordId = style, RecordVersion = BitConverter.ToInt32(raw, 8),
    Rooms = definition.Rooms.Select((room, index) => new PlayfieldRoomEntry {
        Index = index, Name = room.Name, TileX1 = room.TileX1, TileY1 = room.TileY1,
        TileX2 = room.TileX2, TileY2 = room.TileY2, Template = new[] { room.X, room.Y, room.Z },
        Center = new[] { room.CenterX, 0f, room.CenterZ }, Rotation = room.RotationQuarterTurns,
        DoorConnections = room.DoorConnections.Select(d => new PlayfieldRoomDoorLink { ZoneLink = d.ZoneLink, PosRot = d.PosRot }).ToArray()
    }).ToArray()
};
var json = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
if (!verify) {
Directory.CreateDirectory(folder);
File.WriteAllText(Path.Combine(folder, "metadata.json"), JsonSerializer.Serialize(metadata, json));
File.WriteAllText(Path.Combine(folder, "Rooms.json"), JsonSerializer.Serialize(rooms, json));
File.WriteAllBytes(Path.Combine(folder, "Surfaces.dat"), PlayfieldSurfacesDat.Build(entries));
GrayPng.Write(Path.Combine(folder, "GNDA.png"), map.Width, map.Height, map.Heightmap);
GrayPng.Write(Path.Combine(folder, "DCGA.png"), map.Width, map.Height, map.CollisionData);
File.WriteAllText(Path.Combine(folder, "LOCAL_SOURCE.json"), JsonSerializer.Serialize(new {
    SourcePlayfield = style, SourceSha256 = metadata.RawRecordSha256,
    SurfaceReaderSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[2]))).ToLowerInvariant(),
    Purpose = "Local server collision and room metadata only; client reads rendering assets from its own installation. Do not redistribute."
}, json));
} else {
    var existing = JsonSerializer.Deserialize<PlayfieldMetaData>(File.ReadAllText(Path.Combine(folder, "metadata.json")))!;
    if (existing.RawRecordSha256 != metadata.RawRecordSha256) throw new InvalidDataException("Prepared source differs from installed database.");
}
Console.WriteLine($"Prepared style {style}: rooms={rooms.Rooms.Length}, triangles={triangles}, folder={folder}");
var generator = new DungeonGenerator(root);
var candidate = generator.Generate(new DungeonGenerationRequest { StyleId = style, Seed = 90602, FloorCount = 1, FloorSize = 12, BuildingInstance = 351 });
var collision = DungeonCollisionBuilder.BuildLayout(root, candidate.Generator);
if (!collision.Collision.HasCollision || collision.Placements.Count != candidate.Rooms.Count)
    throw new InvalidDataException("Generated room placement and collision do not agree.");
var spawn = ZoneEngine_New.Core.WorldGeneration.NativeClientAcgLayout.SupportedSpawn(collision, candidate.Spawn);
byte[] generatorPayload = candidate.Generator.ToByteArray();
if (!SmokeLounge.AOtomation.Messaging.GameData.AcgBuildingGeneratorData.TryParse(generatorPayload, out var decoded)
    || !generatorPayload.SequenceEqual(decoded.ToByteArray())) throw new InvalidDataException("Native room-list payload did not round trip.");
Console.WriteLine($"Prepared native candidate: rooms={candidate.Rooms.Count}, legal={candidate.Validation.IsValid}, collision meshes={collision.Collision.SurfaceMeshes.Count}, supported spawn={spawn}, payload bytes={generatorPayload.Length}");
