using System.Text.Json;
using WorldGen.Contracts;
using WorldGen.Dungeons;
using WorldGen.Spatial;

if (args.Length != 1) throw new ArgumentException("Supply the prepared native-room catalog JSON.");
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    try { action(); } catch (ArgumentException) { return; } catch (InvalidDataException) { return; }
    throw new Exception("Invalid input was accepted.");
}

foreach (var profile in new[] { DungeonLayoutProfile.Facility, DungeonLayoutProfile.Subway, DungeonLayoutProfile.Temple })
{
    var manifest = new GenerationManifest("mission-dungeon", "5.13.0", 2, 90602, "result-" + profile, "development");
    manifest.Parameters[DungeonGenerationProfileCatalog.ProfileParameter] = profile.ToString();
    var parameters = DungeonGenerationProfileCatalog.CreateParameters(12, profile);
    var original = new DungeonGenerator().Generate(manifest, parameters);
    var result = DungeonGenerationResults.FromModular(original);
    var generated = DungeonGenerationResults.GenerateModular(manifest, parameters);
    Check(result.LayoutHash == DungeonLayoutHasher.Compute(original) && result.LayoutHash == generated.LayoutHash,
        "Modular result changed generation or hashing.");
    Check(result.Rooms.Count == original.Rooms.Count && result.Connections.Count == original.Connections.Count,
        "Common result lost modular topology.");
    Check(ReferenceEquals(result.Layout, original) && ReferenceEquals(result.SpawnPoints, original.SpawnPoints),
        "Common result replaced the authoritative layout or spawns.");
    foreach (var room in result.Rooms)
    {
        Check(room.SourceKind == DungeonRoomSourceKind.GeneratedLayout && room.Presentation.ResourceId == room.Room.Id
            && room.Collision.ResourceId == room.Room.Id && room.Collision.Catalog.Revision == result.LayoutHash,
            "Modular room source references are not scoped to the actual layout.");
        room.Transform.TransformPoint(-.125f, 1.234567f, 15437.875f, out float x, out float y, out float z);
        Check(x == -.125f && y == 1.234567f && z == 15437.875f, "Generated world coordinates were moved.");
    }
    Check(result.Openings.Count == original.DoorPortals.Count && result.Closures.Count == 0,
        "Interactive modular doors became static source-room closures.");
    foreach (var opening in result.Openings)
    {
        var portal = original.DoorPortals.Single(p => p.Id == opening.Id);
        Check(opening.Position.Equals(portal.Center) && opening.Width > 0 && opening.Height > 0,
            "Modular opening geometry changed.");
    }
}

byte[] catalogBytes = File.ReadAllBytes(args[0]);
var catalog = JsonSerializer.Deserialize<NativeRoomCatalog>(catalogBytes, new JsonSerializerOptions { IncludeFields = true })!;
var nativeManifest = new GenerationManifest(NativeRoomDungeon.GeneratorId, NativeRoomDungeon.GeneratorVersion, 1,
    90602, "native-result", NativeRoomDungeon.Sha(catalogBytes));
var recipe = NativeRoomDungeon.Generate(catalog, nativeManifest.Seed, 12, "mixed");
string originalHash = NativeRoomDungeon.Hash(nativeManifest, recipe);
var visuals = new DungeonResourceCatalog("local-assets", "test-room-catalog", catalog.SourceSha256);
var physics = new DungeonResourceCatalog("prepared-collision", "test-room-catalog", catalog.SurfaceSha256);
var native = DungeonGenerationResults.FromRooms(nativeManifest, recipe, visuals, physics);
var nativeGenerated = DungeonGenerationResults.GenerateRooms(nativeManifest, catalog, 12, "mixed", visuals, physics);
Check(native.LayoutHash == originalHash && nativeGenerated.LayoutHash == originalHash
    && NativeRoomDungeon.Hash(nativeManifest, recipe) == originalHash, "Native result changed generation or mutated its input.");
Check(native.Rooms.Count == recipe.Rooms.Count && native.Connections.Count == recipe.Joins.Count
    && native.Openings.Count == recipe.Joins.Count * 2, "Common result lost native topology or sockets.");
Check(native.Corridors.Count == 0 && native.DoorPortals.Count == 0,
    "Native connections acquired invented corridor geometry or interactive doors.");
Check(native.SpawnPoints[0].Position.Equals(NativeRoomDungeon.Layout(nativeManifest, recipe).SpawnPoints[0].Position),
    "Native spawn moved.");
foreach (var room in native.Rooms)
{
    var placement = recipe.Rooms.Single(p => p.Index == room.Room.Index);
    var template = recipe.Template(placement);
    Check(room.SourceKind == DungeonRoomSourceKind.CatalogRoom
        && room.Presentation.ResourceId == placement.SourceIndex.ToString()
        && ReferenceEquals(room.Presentation.Catalog, visuals) && ReferenceEquals(room.Collision.Catalog, physics),
        "Native provider references were replaced with a baked modular room or fixed PF binding.");
    float sx = template.OriginX * .001f + .1234567f, sy = template.OriginY * .001f + 1.987654f,
        sz = template.OriginZ * .001f - 3.456789f;
    room.Transform.TransformPoint(sx, sy, sz, out float x, out float y, out float z);
    NativeRoomTransform.SourcePoint(template, placement, sx, sy, sz, out float nx, out float ny, out float nz);
    Check(BitConverter.SingleToInt32Bits(x) == BitConverter.SingleToInt32Bits(nx)
        && BitConverter.SingleToInt32Bits(y) == BitConverter.SingleToInt32Bits(ny)
        && BitConverter.SingleToInt32Bits(z) == BitConverter.SingleToInt32Bits(nz), "Common/native transforms disagree.");
}
foreach (var connection in native.Connections)
{
    var ends = native.Openings.Where(o => o.ConnectionId == connection.Id).ToArray();
    Check(ends.Length == 2 && ends[0].Position.Equals(ends[1].Position) && ends[0].Width == ends[1].Width
        && ends[0].Outward.X == -ends[1].Outward.X && ends[0].Outward.Z == -ends[1].Outward.Z,
        "Native openings no longer join at the same elevation with opposing directions.");
}
var seals = NativeRoomDungeon.Seals(recipe).ToArray();
Check(native.Closures.Count == seals.Length && native.Closures.Select(c => c.Id).Distinct().Count() == seals.Length,
    "Unused sockets lost closures or closure identities collided.");
for (int i = 0; i < seals.Length; i++)
    Check(native.Closures[i].Center.Equals(seals[i].Center) && native.Closures[i].Size.Equals(seals[i].Size)
        && native.Closures[i].QuarterTurns == seals[i].Facing, "Native static collision closure moved.");

// Preserve the prior source-coordinate arithmetic bit for bit, including negative coordinates and masked turns.
foreach (int turn in new[] { 0, 1, 2, 3, 5 })
foreach (var point in new[] { (-17.123456f, 107.23456f, 326.76543f), (1.000001f, -.123456f, 10001.125f) })
{
    var template = new NativeRoomTemplate { OriginX = 91876, OriginY = 107235, OriginZ = -325679 };
    var placement = new NativeRoomPlacement { X = -126789, Y = 203457, Z = 876543, QuarterTurns = turn };
    float px = point.Item1-template.OriginX*.001f, pz = point.Item3-template.OriginZ*.001f, rx=px, rz=pz;
    switch (turn & 3) { case 1: rx=pz; rz=-px; break; case 2: rx=-px; rz=-pz; break; case 3: rx=-pz; rz=px; break; }
    float expectedX=placement.X*.001f+rx, expectedY=placement.Y*.001f+point.Item2-template.OriginY*.001f,
        expectedZ=placement.Z*.001f+rz;
    NativeRoomTransform.SourcePoint(template, placement, point.Item1, point.Item2, point.Item3, out float x, out float y, out float z);
    Check(BitConverter.SingleToInt32Bits(x)==BitConverter.SingleToInt32Bits(expectedX)
        && BitConverter.SingleToInt32Bits(y)==BitConverter.SingleToInt32Bits(expectedY)
        && BitConverter.SingleToInt32Bits(z)==BitConverter.SingleToInt32Bits(expectedZ), "Legacy collision arithmetic changed.");
}
Reject(() => DungeonGenerationResults.FromModular(native.Layout));
Reject(() => new DungeonResourceCatalog("", "catalog", "revision"));
Reject(() => new DungeonSourceTransform(default, default, 4));
Reject(() => DungeonGenerationResults.FromRooms(new GenerationManifest("mission-dungeon", "5.13.0", 2,
    90602, "wrong", "revision"), recipe, visuals, physics));
var mismatched = NativeRoomRecipeData.Capture(recipe).Restore(catalog);
mismatched.Rooms[1].X += 100;
Reject(() => DungeonGenerationResults.FromRooms(nativeManifest, mismatched, visuals, physics));
var missingSocket = NativeRoomRecipeData.Capture(recipe).Restore(catalog);
missingSocket.Rooms[0].UsedSockets.Clear();
Reject(() => DungeonGenerationResults.FromRooms(nativeManifest, missingSocket, visuals, physics));
var invalidRotation = NativeRoomRecipeData.Capture(recipe).Restore(catalog);
invalidRotation.Rooms[1].QuarterTurns = 4;
Reject(() => DungeonGenerationResults.FromRooms(nativeManifest, invalidRotation, visuals, physics));
// Result placement/closure records must not follow later edits to a caller-owned recipe.
var savedPosition = native.Rooms[1].Transform.Position;
recipe.Rooms[1].X += 1000;
Check(native.Rooms[1].Transform.Position.Equals(savedPosition), "Result retained a mutable source placement.");
Console.WriteLine("PASS: modular/native common results, unchanged hashes/spawns, exact source transforms, connected openings, closures and provider boundaries.");
