using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AO.Assets.Resolution;
using AO.Assets.ResourceDatabase;
using AO.Assets.Decoders;
using AO.Assets.Navigation;
using SharpNav;

internal static class AOAssetsTests
{
    private static async Task<int> Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "projectmayhem-assets-test-"
            + Guid.NewGuid().ToString("N"));
        try
        {
            string install = Path.Combine(root, "ao");
            Directory.CreateDirectory(Path.Combine(install, "cd_image", "data", "db"));
            File.WriteAllText(Path.Combine(install, "Anarchy.exe"), "test");
            File.WriteAllText(Path.Combine(install, "version.id"), "18.8.62_EP1\n");
            File.WriteAllText(Path.Combine(install, "cd_image", "data", "db",
                "ResourceDatabase.dat"), "test-rdb");
            CreateSyntheticResourceDatabase(install);

            AOInstallValidation validation = AOInstallLocator.Validate(install);
            Require(validation.IsValid, "fake AO install validation");
            Require(validation.ClientVersion == "18.8.62_EP1", "client version");

            using (var database = new AOResourceDatabase(install))
            {
                Require(database.RecordCount == 1 && database.Contains(1000010, 1),
                    "resource index");
                Require(database.TryReadRaw(1000010, 1, out byte[] raw)
                    && System.Text.Encoding.ASCII.GetString(raw) == "record-data",
                    "raw resource read");
                Require(!database.TryReadRaw(1000010, 2, out _), "missing resource");
                Require(System.Linq.Enumerable.Count(database.EnumerateIdentities()) == 1,
                    "resource identity enumeration");
            }

            AOResourceCatalog catalog = AOResourceCatalog.Parse(CreateCatalogRecord());
            Require(catalog.TryGetName(AOResourceTypes.Playfield, 127, out string playfield)
                && playfield == "Andromeda", "resource catalog lookup");
            Require(System.Linq.Enumerable.Count(catalog.Find("andro")) == 1,
                "resource catalog search");

            var converter = new FakeConverter();
            var resolver = new AOInstallAssetResolver(validation,
                Path.Combine(root, "cache"), converter);
            var request = new AOAssetRequest(AOAssetKind.Mesh, 1234, "glb");
            AOResolvedAsset first = await resolver.ResolveAsync(request);
            Require(first.Succeeded && !first.CacheHit && File.Exists(first.Path),
                "first conversion");
            AOResolvedAsset second = await resolver.ResolveAsync(request);
            Require(second.Succeeded && second.CacheHit && second.Path == first.Path,
                "cache hit");
            Require(converter.CallCount == 1, "converter deduplication");
            Require(!File.Exists(first.Path + ".partial"), "atomic temporary cleanup");

            string overrides = Path.Combine(root, "overrides");
            string meshOverrides = Path.Combine(overrides, "mesh");
            Directory.CreateDirectory(meshOverrides);
            File.WriteAllText(Path.Combine(meshOverrides, "1234.glb"), "replacement");
            var composite = new AOCompositeAssetResolver(
                new AODirectoryAssetResolver(overrides), resolver);
            AOResolvedAsset replacement = await composite.ResolveAsync(request);
            Require(replacement.Succeeded && replacement.CacheHit
                && File.ReadAllText(replacement.Path) == "replacement",
                "override source precedence");
            ProbeConfiguredInstall();
            ProbeConfiguredIndoorSurfaceStream();
            Console.WriteLine("PASS AO.Assets installation and cache resolution");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void ProbeConfiguredInstall()
    {
        string path = Environment.GetEnvironmentVariable("PROJECTMAYHEM_TEST_AO_INSTALL");
        if (string.IsNullOrWhiteSpace(path)) return;
        AOInstallValidation validation = AOInstallLocator.Validate(path);
        Require(validation.IsValid, "configured AO installation validation");
        using (var database = new AOResourceDatabase(path))
        {
            Require(database.RecordCount > 1000, "configured AO resource index");
            Require(database.TryReadRaw(1000010, 1, out byte[] info) && info.Length > 12,
                "configured AO metadata record");
            Console.WriteLine("PASS configured AO database probe: "
                + database.RecordCount + " indexed records");
            var identityCounts = database.EnumerateIdentities()
                .GroupBy(identity => identity.Type)
                .ToDictionary(group => group.Key, group => group.Count());
            Console.WriteLine("INFO AO resource counts: tilemaps="
                + (identityCounts.TryGetValue(AOResourceTypes.Tilemap, out int tilemapCount) ? tilemapCount : 0)
                + " surfaces="
                + (identityCounts.TryGetValue(AOResourceTypes.Surface, out int surfaceCount) ? surfaceCount : 0));
            Console.WriteLine("INFO PF 127 dependencies: tilemap126="
                + database.Contains(AOResourceTypes.Tilemap, 126)
                + " surface127x16="
                + database.Contains(AOResourceTypes.Surface, 127 << 16));
            Require(database.TryReadRaw(AOResourceTypes.Tilemap, 126, out byte[] tilemap126),
                "configured PF 127 tilemap record");
            Require(database.TryReadRaw(AOResourceTypes.Surface, 127 << 16, out byte[] surface0),
                "configured PF 127 room-0 surface record");
            Console.WriteLine("INFO PF 127 raw dependencies: tilemapBytes=" + tilemap126.Length
                + " tilemapHeader=" + Convert.ToHexString(tilemap126.Take(48).ToArray())
                + " surface0Bytes=" + surface0.Length
                + " surface0Header=" + Convert.ToHexString(surface0.Take(96).ToArray()));
            AOResourceCatalog catalog = AOResourceCatalog.Load(database);
            ProbePlayfield(database, catalog, 655);
            ProbePlayfield(database, catalog, 6553);
            if (catalog.TryGetName(AOResourceTypes.Playfield, 127, out string playfieldName))
                Console.WriteLine("INFO playfield 127 name: " + playfieldName);
            Require(database.TryReadRaw(AOResourceTypes.Playfield, 127,
                out byte[] playfieldRecord), "configured playfield 127 record");
            Console.WriteLine("INFO playfield 127 record bytes: " + playfieldRecord.Length);
            AOPlayfieldDefinition playfield = AOPlayfieldDefinitionDecoder.Decode(
                playfieldRecord, 127);
            Console.WriteLine("INFO playfield 127: name=" + playfield.Name
                + " version=" + playfield.Version + " tilemap=" + playfield.TilemapId
                + " rooms=" + playfield.RoomCount + " indoor=" + playfield.IsIndoor);
            ExportConfiguredPlayfield(playfield);
        }
    }

    private static void ProbePlayfield(AOResourceDatabase database,
        AOResourceCatalog catalog, int playfieldId)
    {
        bool exists = database.TryReadRaw(AOResourceTypes.Playfield, playfieldId,
            out byte[] raw);
        string catalogName = catalog.TryGetName(AOResourceTypes.Playfield, playfieldId,
            out string value) ? value : "<unnamed>";
        if (!exists)
        {
            Console.WriteLine("INFO playfield " + playfieldId
                + ": missing; catalog=" + catalogName);
            return;
        }

        AOPlayfieldDefinition definition = AOPlayfieldDefinitionDecoder.Decode(raw,
            playfieldId);
        Console.WriteLine("INFO playfield " + playfieldId + ": name="
            + definition.Name + " catalog=" + catalogName + " version="
            + definition.Version + " tilemap=" + definition.TilemapId + " rooms="
            + definition.RoomCount + " indoor=" + definition.IsIndoor);
    }

    private static void ProbeConfiguredIndoorSurfaceStream()
    {
        string path = Environment.GetEnvironmentVariable("PROJECTMAYHEM_TEST_AOIS");
        if (string.IsNullOrWhiteSpace(path)) return;
        AOIndoorSurfaceSet surfaces = AOIndoorSurfaceStreamDecoder.Decode(path, 127);
        int meshCount = surfaces.Rooms.Sum(room => room.Meshes.Count);
        int vertexCount = surfaces.Rooms.Sum(room => room.Meshes.Sum(mesh => mesh.VertexCount));
        int triangleCount = surfaces.Rooms.Sum(room => room.Meshes.Sum(mesh => mesh.TriangleCount));
        Require(surfaces.Rooms.Count == 46, "configured PF 127 AOIS room count");
        Require(surfaces.Tilemap != null && surfaces.Tilemap.Id == 126,
            "configured PF 127 dungeon tilemap");
        Require(meshCount > 0 && vertexCount > 0 && triangleCount > 0,
            "configured PF 127 AOIS geometry");
        Console.WriteLine("PASS direct PF 127 AOIS: rooms=" + surfaces.Rooms.Count
            + " meshes=" + meshCount + " vertices=" + vertexCount
            + " triangles=" + triangleCount);
        string installPath = Environment.GetEnvironmentVariable("PROJECTMAYHEM_TEST_AO_INSTALL");
        if (!string.IsNullOrWhiteSpace(installPath))
        {
            using var database = new AOResourceDatabase(installPath);
            Require(database.TryReadRaw(AOResourceTypes.Playfield, 127, out byte[] raw),
                "configured PF 127 definition for dungeon terrain");
            AOPlayfieldDefinition definition = AOPlayfieldDefinitionDecoder.Decode(raw, 127);
            int terrainTriangles = 0;
            for (int index = 0; index < definition.Rooms.Count; index++)
            {
                AOIndoorSurfaceMesh terrain = AOIndoorDungeonTerrainBuilder.Build(
                    definition.Rooms[index], surfaces.Tilemap);
                terrainTriangles += terrain?.TriangleCount ?? 0;
            }
            Require(terrainTriangles > 0, "configured PF 127 AO dungeon terrain");
            Console.WriteLine("PASS complete PF 127 dungeon terrain triangles="
                + terrainTriangles);
            if (Environment.GetEnvironmentVariable("PROJECTMAYHEM_TEST_SHARPNAV") == "1")
            {
                int completeNavigationTriangles = 0;
                foreach (AOIndoorSurfaceRoom surfaceRoom in surfaces.Rooms)
                {
                    AOIndoorSurfaceMesh terrain = AOIndoorDungeonTerrainBuilder.Build(
                        definition.Rooms[surfaceRoom.Instance], surfaces.Tilemap);
                    AOIndoorNavigationMesh navigation = AOIndoorSharpNavBuilder.Build(
                        surfaceRoom, NavMeshGenerationSettings.CustomDensity(0.3f, 0.5f), terrain);
                    completeNavigationTriangles += navigation.TriangleCount;
                }
                Require(completeNavigationTriangles > 0,
                    "configured complete PF 127 SharpNav geometry");
                Console.WriteLine("PASS complete SharpNav PF 127 triangles="
                    + completeNavigationTriangles);
            }
        }
        string sharpNav = Environment.GetEnvironmentVariable("PROJECTMAYHEM_TEST_SHARPNAV");
        if (sharpNav == "1")
        {
            int navigationRooms = 0;
            int navigationVertices = 0;
            int navigationTriangles = 0;
            foreach (AOIndoorSurfaceRoom room in surfaces.Rooms)
            {
                AOIndoorNavigationMesh navigation = AOIndoorSharpNavBuilder.Build(
                    room, NavMeshGenerationSettings.CustomDensity(0.3f, 0.5f));
                if (navigation.TriangleCount == 0) continue;
                navigationRooms++;
                navigationVertices += navigation.VertexCount;
                navigationTriangles += navigation.TriangleCount;
            }
            Require(navigationRooms > 0 && navigationVertices > 0 && navigationTriangles > 0,
                "configured PF 127 SharpNav geometry");
            Console.WriteLine("PASS SharpNav PF 127: rooms=" + navigationRooms
                + " vertices=" + navigationVertices
                + " triangles=" + navigationTriangles);
        }
    }

    private static void ExportConfiguredPlayfield(AOPlayfieldDefinition playfield)
    {
        string outputPath = Environment.GetEnvironmentVariable("PROJECTMAYHEM_TEST_PF_EXPORT");
        if (string.IsNullOrWhiteSpace(outputPath)) return;

        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var payload = new
        {
            PlayfieldId = playfield.Id,
            playfield.Name,
            playfield.IsIndoor,
            playfield.TilemapId,
            Rooms = playfield.Rooms.Select(room => new
            {
                room.Name,
                room.RotationQuarterTurns,
                TemplatePosition = new { X = room.X, Y = room.Y, Z = room.Z },
                Center = new { X = room.CenterX, Y = 0f, Z = room.CenterZ },
                room.TileX1,
                room.TileY1,
                room.TileX2,
                room.TileY2
            })
        };
        File.WriteAllText(outputPath, System.Text.Json.JsonSerializer.Serialize(payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS exported playfield rooms: " + outputPath);
    }

    private static void CreateSyntheticResourceDatabase(string install)
    {
        string dbRoot = Path.Combine(install, "cd_image", "data", "db");
        string dataPath = Path.Combine(dbRoot, "ResourceDatabase.dat");
        using (var data = new BinaryWriter(File.Open(dataPath, FileMode.Create)))
        {
            data.Write(new byte[10]);
            data.Write(1000010);
            data.Write(1);
            byte[] payload = System.Text.Encoding.ASCII.GetBytes("record-data");
            data.Write(payload.Length);
            data.Write(payload);
        }

        string indexPath = Path.Combine(dbRoot, "ResourceDatabase.idx");
        using (var index = new BinaryWriter(File.Open(indexPath, FileMode.Create)))
        {
            index.Write(new byte[256]);
            index.BaseStream.Position = 12; index.Write((uint)16);
            index.BaseStream.Position = 72; index.Write(128);
            index.BaseStream.Position = 184; index.Write((uint)1024);
            index.BaseStream.Position = 128;
            index.Write(0); index.Write(0); index.Write((short)1);
            index.Write(new byte[18]);
            index.Write((uint)0); index.Write((uint)0);
            WriteBigEndian(index, 1000010); WriteBigEndian(index, 1);
        }
    }

    private static byte[] CreateCatalogRecord()
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(AOResourceTypes.InfoObject);
            writer.Write(1);
            writer.Write(8008);
            writer.Write(1);
            writer.Write(AOResourceTypes.Playfield);
            writer.Write(1);
            writer.Write(127);
            byte[] name = System.Text.Encoding.ASCII.GetBytes("Andromeda\0");
            writer.Write(name.Length);
            writer.Write(name);
            return stream.ToArray();
        }
    }

    private static void WriteBigEndian(BinaryWriter writer, int value)
    {
        writer.Write(new[]
        {
            (byte)(value >> 24), (byte)(value >> 16),
            (byte)(value >> 8), (byte)value
        });
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + message);
    }

    private sealed class FakeConverter : IAOAssetConverter
    {
        public int CallCount { get; private set; }
        public string ConverterVersion => "fake-v1";
        public Task ConvertAsync(string aoInstallationRoot, AOAssetRequest request,
            string destinationPath, CancellationToken cancellationToken)
        {
            CallCount++;
            File.WriteAllText(destinationPath, request.Kind + ":" + request.ResourceId);
            return Task.CompletedTask;
        }
    }
}
