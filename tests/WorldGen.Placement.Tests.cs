using System;
using System.Linq;
using WorldGen.Content;
using WorldGen.Contracts;
using WorldGen.Dungeons;

static class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static DungeonLayout Generate(ulong seed, string version)
    {
        var manifest = new GenerationManifest("mission-dungeon", version, 2, seed, "placement-test", "development");
        manifest.Parameters["layoutProfile"] = "Subway";
        manifest.Parameters["visualTheme"] = "Subway";
        manifest.Parameters["assetSource"] = "Procedural";
        manifest.Parameters["roomCount"] = "30";
        return new DungeonGenerator().Generate(manifest, DungeonGenerationProfileCatalog.CreateParameters(30, DungeonLayoutProfile.Subway));
    }
    static void Main()
    {
        int bathrooms = 0, sinks = 0;
        foreach (ulong seed in Enumerable.Range(1, 32).Select(x => (ulong)x).Append(90602UL))
        {
            var current = Generate(seed, "5.13.0");
            var legacy = Generate(seed, "5.12.0");
            Check(DungeonValidator.Validate(current).IsValid, "Invalid layout " + seed);
            Check(DungeonLayoutHasher.Compute(current) == DungeonLayoutHasher.Compute(Generate(seed, "5.13.0")), "Non-deterministic layout");
            Check(current.Rooms.Select(x => x.Bounds).SequenceEqual(legacy.Rooms.Select(x => x.Bounds)), "Unexpected room topology change");
            foreach (var room in current.Rooms.Where(x => x.ModuleKind == DungeonModuleKind.Restroom))
            {
                bathrooms++;
                var elements = current.Presentation.Where(x => x.OwnerId == room.Id).ToArray();
                Check(!elements.Any(x => x.Id.Contains("/stall-door-")), "New bathroom has a stall door");
                Check(legacy.Presentation.Any(x => x.OwnerId == room.Id && x.Id.Contains("/stall-door-")), "Legacy placement changed");
                Check(elements.Count(x => x.FixtureRole == DungeonFixtureRole.Toilet) == 5, "Missing resolved toilets");
                var collision = DungeonCollisionBaker.BakeRoom(current, room);
                foreach (var sink in elements.Where(x => x.FixtureRole == DungeonFixtureRole.Sink))
                {
                    sinks++;
                    var b = sink.Bounds;
                    int wallGap = Math.Min(Math.Min(b.Minimum.X - room.Bounds.Minimum.X, room.Bounds.Maximum.X - b.Maximum.X),
                        Math.Min(b.Minimum.Z - room.Bounds.Minimum.Z, room.Bounds.Maximum.Z - b.Maximum.Z));
                    Check(wallGap == 70, "Sink is not wall-mounted: " + sink.Id);
                    foreach (var portal in current.DoorPortals.Where(x => x.RoomId == room.Id))
                    {
                        var door = portal.ClosedBlockingBounds;
                        bool overlaps = b.Minimum.X < door.Maximum.X && b.Maximum.X > door.Minimum.X
                            && b.Minimum.Y < door.Maximum.Y && b.Maximum.Y > door.Minimum.Y
                            && b.Minimum.Z < door.Maximum.Z && b.Maximum.Z > door.Minimum.Z;
                        Check(!overlaps, "Sink obstructs doorway: " + seed + " " + sink.Id);
                    }
                    Check(collision.Single(x => x.Id == "presentation-" + sink.Id).Bounds.Equals(b), "Sink visual/collision bounds differ");
                    Check(!current.ConstructiveRecipes.Any(x => x.FixtureId == sink.Id), "Old procedural sink is still drawn");
                    Check(new[] { 0, 90, 180, 270 }.Contains(sink.YawDegrees), "Invalid fixture orientation");
                }
                var oldSinks = legacy.Presentation.Where(x => x.OwnerId == room.Id && x.Id.Contains("/sink-")).ToArray();
                foreach (var old in oldSinks)
                    Check(!collision.Any(x => x.Kind == DungeonCollisionKind.Fixture && x.Bounds.Equals(old.Bounds)), "Old sink collision remains");
            }
        }
        var reusable = new CatalogAsset { theme = "Subway", allowedThemes = new[] { "Subway", "Industrial" }, excludedThemes = new[] { "Temple" } };
        Check(reusable.SupportsTheme("Industrial") && !reusable.SupportsTheme("Temple"), "Theme reuse failed");
        reusable.excludedThemes = new[] { "Industrial" };
        Check(!reusable.SupportsTheme("Industrial"), "Exclusion must override inclusion");
        var asset = new CatalogAsset { id = "reusable-metal-door-01", theme = "Subway", purpose = "Door",
            path = "Models/shared-door.glb", sha256 = new string('0', 64), pivot = "center", fitMode = "stretch",
            dimensionsMeters = new CatalogVector { x = 1, y = 2, z = .1f }, triangles = 12,
            allowedRotations = new[] { 0, 90, 180, 270 }, allowedThemes = new[] { "Subway", "Industrial" },
            collision = new CatalogCollision { mode = "none" } };
        var catalog = new AssetCatalog { schemaVersion = 2, contentVersion = "test", coordinateSystem = "right-handed-y-up", assets = new[] { asset } };
        AssetCatalogValidator.Validate(catalog);
        asset.path = "../outside.glb";
        bool rejected = false;
        try { AssetCatalogValidator.Validate(catalog); } catch (Exception) { rejected = true; }
        Check(rejected, "Unsafe catalog path accepted");
        Check(bathrooms > 0 && sinks == bathrooms * 3, "Fixture coverage missing");
        Console.WriteLine($"PASS: 33 seeds, {bathrooms} bathrooms, {sinks} wall sinks; deterministic layouts, matching collision, legacy placement, theme exclusions.");
    }
}
