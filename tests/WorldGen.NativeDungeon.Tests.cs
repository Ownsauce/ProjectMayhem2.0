using System.Text.Json;
using AO.Assets.Conversion;
using AO.Assets.ResourceDatabase;
using WorldGen.Contracts;
using WorldGen.Dungeons;
using WorldGen.Authoring;
using WorldGen.Spatial;

if (args.Length != 1 && args.Length != 3) throw new ArgumentException("<catalog.json> [AO-install helper.exe]");
var json = new JsonSerializerOptions { IncludeFields = true };
var catalog = JsonSerializer.Deserialize<NativeRoomCatalog>(File.ReadAllText(args[0]), json)!;
NativeRoomCatalog Copy() => JsonSerializer.Deserialize<NativeRoomCatalog>(JsonSerializer.Serialize(catalog, json), json)!;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action, string message)
{ try { action(); } catch (InvalidDataException) { return; } catch (InvalidOperationException) { return; } throw new Exception(message); }
var recipe = NativeRoomDungeon.Generate(catalog, 90602, 24, "mixed");
var manifest = new GenerationManifest(NativeRoomDungeon.GeneratorId, NativeRoomDungeon.GeneratorVersion, 1, 90602, "native-tests", NativeRoomDungeon.Sha(File.ReadAllBytes(args[0])));
var data = NativeRoomRecipeData.Capture(recipe);
var restored = JsonSerializer.Deserialize<NativeRoomRecipeData>(JsonSerializer.Serialize(data, json), json)!.Restore(catalog);
Check(NativeRoomDungeon.Hash(manifest, restored) == NativeRoomDungeon.Hash(manifest, recipe), "Saved placements changed their hash.");
Check(restored.Rooms.All(r => r.UsedSockets.SetEquals(recipe.Rooms[r.Index].UsedSockets)), "Used doorways were not reconstructed.");
data.Joins[0].ToSocket = int.MaxValue;
Reject(() => data.Restore(catalog), "Unknown saved doorway was accepted.");
data = NativeRoomRecipeData.Capture(recipe); data.Rooms[1].X += 100;
Reject(() => data.Restore(catalog), "Mismatched saved doorway position was accepted.");
var disconnected = Copy();
var branch = recipe.Rooms.First(r => r.UsedSockets.Count > 1);
var sockets = branch.UsedSockets.ToArray();
disconnected.Rooms.Single(r => r.SourceIndex == branch.SourceIndex).Sockets[sockets[0]].Region += 10000;
Reject(() => NativeRoomRecipeData.Capture(recipe).Restore(disconnected), "Disconnected internal walking regions were accepted.");
var overrideData = new NativeRoomOverrides { Rooms = new[] { new NativeRoomOverride { SourceIndex = 16, Enabled = false, LightMultiplier = 2 } } };
var edited = Copy(); overrideData.Apply(edited); overrideData.Apply(edited);
Check(edited.Rooms.Single(r => r.SourceIndex == 16).Lights.All(l => Math.Abs(l.Intensity - l.BaseIntensity * 2) < .001), "Preview lighting overrides compounded.");
Check(NativeRoomDungeon.Generate(edited, 90602, 24, "mixed").Rooms.All(r => r.SourceIndex != 16), "Excluded source room was used.");
Reject(() => new NativeRoomOverrides { Rooms = new[] { new NativeRoomOverride { SourceIndex = catalog.EntranceSourceRoom, Enabled = false } } }.Apply(Copy()), "Excluded entrance was accepted.");
var template = recipe.Template(recipe.Rooms[1]);
for (int turn = 0; turn < 4; turn++)
{
    var room = new NativeRoomPlacement { X = 150000, Y = 120000, Z = 260000, QuarterTurns = turn };
    NativeRoomTransform.SourcePoint(template, room, template.OriginX * .001f + 2, template.OriginY * .001f + 3, template.OriginZ * .001f + 4, out float x, out float y, out float z);
    var expected = NativeRoomDungeon.Transform(room, new WorldVector3(2000, 3000, 4000));
    Check(Math.Abs(x - expected.X * .001f) < .0001f && Math.Abs(y - expected.Y * .001f) < .0001f && Math.Abs(z - expected.Z * .001f) < .0001f, "Source collision/presentation transform differs.");
}
var pinnedCatalog = Copy();
var prefix = NativeRoomDungeon.Generate(catalog, 90602, 12, "mixed");
pinnedCatalog.PinnedRecipe = NativeRoomRecipeData.Capture(prefix);
var extended = NativeRoomDungeon.Generate(pinnedCatalog, 67890, 24, "mixed");
Check(extended.Rooms.Take(12).Select(r => (r.SourceIndex,r.X,r.Y,r.Z,r.QuarterTurns)).SequenceEqual(prefix.Rooms.Select(r => (r.SourceIndex,r.X,r.Y,r.Z,r.QuarterTurns))), "Pinned rooms moved during extension.");
try { NativeRoomDungeon.Generate(pinnedCatalog, 1, 4, "mixed"); throw new Exception("Room count below pinned prefix was accepted."); } catch (ArgumentException) { }
Console.WriteLine("PASS: pinned 12-room prefix extended to 24 without changing its placements.");
Console.WriteLine("PASS: recipe round-trip/hash, doorway corruption, internal reachability, exclusions, idempotent light overrides and all four source transforms.");
var annotated = Copy();
var taggedRoom = annotated.Rooms.Single(r => r.SourceIndex == annotated.EntranceSourceRoom);
var center = taggedRoom.WalkPoints.First(p => p.Region == taggedRoom.SpawnRegion);
var annotation = new NativeRoomAnnotation {
    AllowedUses = NativeRoomUse.Arena | NativeRoomUse.Encounter, Reviewed = true, Landmark = true,
    ThemeTags = new[] { "station", "maintenance" }, Notes = "Reviewed by an author, not inferred from the room name."
};
NativeRoomAnnotations.SetEncounterCenter(annotation, center);
var settings = new NativeRoomOverrides {
    SourcePlayfield = annotated.SourcePlayfield, SourceSha256 = annotated.SourceSha256,
    Rooms = new[] { new NativeRoomOverride { SourceIndex = taggedRoom.SourceIndex, Annotation = annotation } }
};
settings.Apply(annotated);
Check(NativeRoomDungeon.Hash(manifest, NativeRoomDungeon.Generate(annotated, 90602, 24, "mixed")) == NativeRoomDungeon.Hash(manifest, recipe),
    "Room capabilities changed normal generation.");
var customPool = Copy();
foreach (var room in customPool.Rooms) room.Pool = "station-custom";
Check(NativeRoomDungeon.Generate(customPool, 90602, 24, "station-custom").Rooms.Count == 24, "Authored pool name was rejected.");
var badCenter = JsonSerializer.Deserialize<NativeRoomOverrides>(JsonSerializer.Serialize(settings, json), json)!;
badCenter.Rooms[0].Annotation!.EncounterY += 1000;
Reject(() => badCenter.Apply(Copy()), "Encounter marker floating above the supported floor was accepted.");
var badTags = JsonSerializer.Deserialize<NativeRoomOverrides>(JsonSerializer.Serialize(settings, json), json)!;
badTags.Rooms[0].Annotation!.AllowedUses = (NativeRoomUse)1024;
Reject(() => badTags.Apply(Copy()), "Undefined room role was accepted.");
badTags = JsonSerializer.Deserialize<NativeRoomOverrides>(JsonSerializer.Serialize(settings, json), json)!;
badTags.Rooms[0].Annotation!.ThemeTags = new[] { "station", "Station" };
Reject(() => badTags.Apply(Copy()), "Duplicate theme tags were accepted.");
var wrongSource = JsonSerializer.Deserialize<NativeRoomOverrides>(JsonSerializer.Serialize(settings, json), json)!;
wrongSource.SourceSha256 = "another-source-revision";
Reject(() => wrongSource.Apply(Copy()), "Room settings from another resource revision were accepted.");
string authoringFolder = Path.Combine(Path.GetTempPath(), "native-authoring-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(authoringFolder);
try
{
    INativeRoomAuthoringDao dao = new NativeRoomAuthoringFileDao(
        source => Path.Combine(authoringFolder, source + ".json"),
        value => JsonSerializer.Deserialize<NativeRoomOverrides>(value, json)!, value => JsonSerializer.Serialize(value, json));
    Check(dao.Load(annotated.SourcePlayfield) == null, "Missing authoring source should return no settings.");
    dao.Save(settings);
    var loaded = dao.Load(annotated.SourcePlayfield)!;
    Check(loaded.Revision == 1 && loaded.Rooms[0].Annotation!.AllowedUses == annotation.AllowedUses
        && loaded.Rooms[0].Annotation!.Landmark && loaded.Rooms[0].Annotation!.ThemeTags.SequenceEqual(annotation.ThemeTags),
        "DAO lost room capabilities, landmark or themes.");
    loaded.Apply(Copy());
    var stale = dao.Load(annotated.SourcePlayfield)!;
    loaded.Rooms[0].Annotation!.Notes = "New revision";
    dao.Save(loaded);
    try { dao.Save(stale); throw new Exception("Stale authoring revision overwrote newer data."); }
    catch (NativeRoomAuthoringConflictException) { }
    Check(dao.Load(annotated.SourcePlayfield)!.Rooms[0].Annotation!.Notes == "New revision", "Conflict changed stored data.");
    Check(!Directory.GetFiles(authoringFolder, "*.tmp").Any(), "Atomic save left a temporary file.");
}
finally { Directory.Delete(authoringFolder, true); }
Console.WriteLine("PASS: room annotations leave normal layouts unchanged; supported markers, roles, themes, source identity, DAO round trips and stale-write protection.");
if (args.Length == 3)
{
    var install = AOInstallLocator.Validate(args[1]); Check(install.IsValid, "Invalid local AO installation.");
    AOIndoorVisualCache.Clear();
    var source = AOIndoorVisualCache.Load(install, 127, args[2], default, new[] { 8, 11 });
    Check(source.Geometry.Rooms.Select(r => r.Index).SequenceEqual(new[] { 8, 11 }), "Reader loaded unselected rooms.");
    long decoded = AOIndoorVisualCache.DecodedRooms;
    var cached = AOIndoorVisualCache.Load(install, 127, args[2], default, new[] { 8, 11 });
    Check(AOIndoorVisualCache.DecodedRooms == decoded && AOIndoorVisualCache.CacheHits == 2, "Repeated rooms were redecoded.");
    Check(ReferenceEquals(source.Geometry.Rooms[0], cached.Geometry.Rooms[0]), "Cached geometry was copied instead of shared.");
    var expanded = AOIndoorVisualCache.Load(install, 127, args[2], default, new[] { 8, 11, 16 });
    Check(AOIndoorVisualCache.DecodedRooms == decoded + 1 && expanded.Geometry.Rooms.Count == 3, "Cache did not read only the missing template.");
    Check(AOIndoorVisualCache.MemoryBytes <= 96 * 1024 * 1024, "Decoded cache exceeded its budget.");
    Console.WriteLine($"PASS: local selected-room extraction / cache reuse; cachedRooms={AOIndoorVisualCache.CachedRooms}, bytes={AOIndoorVisualCache.MemoryBytes}.");
    AOIndoorVisualCache.Clear();
}
