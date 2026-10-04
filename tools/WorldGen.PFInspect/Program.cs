using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AO.Assets.Decoders;
using AO.Assets.ResourceDatabase;
using AO.Assets.Navigation;

if (args.Length < 3 || args.Length > 5)
{
    Console.Error.WriteLine("Usage: WorldGen.PFInspect <AO-install> <PF-id> <report.json> [surface.aois] [collision-output-dir]");
    return 2;
}
int id = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
using var db = new AOResourceDatabase(args[0]);
if (!db.TryReadRaw(AOResourceTypes.Playfield, id, out byte[] raw))
    throw new InvalidDataException("Missing playfield record " + id);
var pf = AOPlayfieldDefinitionDecoder.Decode(raw, id);
var surfaces = args.Length >= 4 ? AOIndoorSurfaceStreamDecoder.Decode(args[3], id) : null;
if (surfaces != null && (surfaces.Rooms.Count != pf.RoomCount || surfaces.Tilemap.Id != pf.TilemapId))
    throw new InvalidDataException("Cached surfaces do not match the playfield room count/tilemap.");
var rooms = pf.Rooms.Select((room, index) =>
{
    var surface = surfaces?.Rooms.SingleOrDefault(r => r.Instance == index);
    var meshes = surface?.Meshes;
    var vertices = meshes?.SelectMany(m => m.Vertices).ToArray() ?? Array.Empty<float>();
    float[] minimum = {float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity};
    float[] maximum = {float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity};
    for (int v = 0; v < vertices.Length; v++)
    { minimum[v % 3] = Math.Min(minimum[v % 3],vertices[v]); maximum[v % 3] = Math.Max(maximum[v % 3],vertices[v]); }
    return new { Index=index, Definition=room, SurfaceRoomIndex=surface?.Instance, SurfaceResourceId=surface == null ? (int?)null : (id << 16) | surface.Instance,
        SurfacePresentInDatabase=surface != null && db.Contains(AOResourceTypes.Surface,(id << 16) | surface.Instance),
        MeshCount=meshes?.Count, VertexCount=meshes?.Sum(m=>m.VertexCount), TriangleCount=meshes?.Sum(m=>m.TriangleCount),
        RawSurfaceBounds=vertices.Length == 0 ? null : new {Minimum=minimum,Maximum=maximum},
        MaterialCandidateIds=meshes?.SelectMany(m=>m.MaterialCandidates).Where(v=>v>0).Distinct().OrderBy(v=>v).ToArray() };
}).ToArray();
var report = new { SchemaVersion=1, Install=Path.GetFullPath(args[0]),DatabaseRecords=db.RecordCount,
    Playfield=new {pf.Id,pf.Name,pf.TilemapId,pf.RoomCount,pf.IsIndoor, RecordSha256=Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant()},
    SurfaceCache=args.Length >= 4 ? new {Path=Path.GetFullPath(args[3]),Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[3]))).ToLowerInvariant()} : null,
    Caveats=new [] {"Cached surfaces are optional; matching IDs/counts do not prove cache freshness.","Material candidates are unresolved raw fields, not confirmed texture identities.","Raw surface bounds have not been converted into generator sockets or placement coordinates."},
    Tilemap=surfaces == null ? null : new {surfaces.Tilemap.Width,surfaces.Tilemap.Height,surfaces.Tilemap.TileSize,surfaces.Tilemap.HeightmapScale},
    Rooms=rooms };
string output=Path.GetFullPath(args[2]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
Console.WriteLine($"PF {id}: {pf.RoomCount} rooms, {rooms.Sum(r=>r.MeshCount ?? 0)} meshes; report {output}");
if (args.Length == 5)
{
    if (surfaces == null) throw new InvalidDataException("Collision export needs surfaces.");
    Directory.CreateDirectory(args[4]);
    string collisionPath = Path.Combine(args[4], $"pf{id}.collision");
    using var collision = new BinaryWriter(File.Create(collisionPath));
    collision.Write(new byte[] {65,79,78,67}); collision.Write(1); collision.Write(id);
    collision.Write(0); // patch triangle count when finished
    int count = 0;
    foreach (var room in surfaces.Rooms)
        foreach (var mesh in room.Meshes) WriteMesh(mesh);
    foreach (var room in pf.Rooms)
    {
        var mesh = AOIndoorDungeonTerrainBuilder.Build(room,surfaces.Tilemap);
        if (mesh != null) WriteMesh(mesh);
    }
    collision.BaseStream.Position = 12; collision.Write(count); collision.Flush();
    string sourceSha=Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
    string surfaceSha=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[3]))).ToLowerInvariant();
    string contentHash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sourceSha+surfaceSha))).ToLowerInvariant();
    File.WriteAllText(Path.Combine(args[4],$"pf{id}.json"),JsonSerializer.Serialize(new {SourcePlayfield=id,SourceSha256=sourceSha,SurfaceSha256=surfaceSha,ContentHash=contentHash,TriangleCount=count,SpawnX=184f,SpawnY=108f,SpawnZ=252f},new JsonSerializerOptions {WriteIndented=true}));
    Console.WriteLine($"Exported {count} collision triangles (surfaces + tile terrain).");
    void WriteMesh(AOIndoorSurfaceMesh mesh)
    {
        foreach (int index in mesh.Triangles)
            for (int axis=0;axis<3;axis++)
            {
                float value=mesh.Vertices[index*3+axis];
                if (!float.IsFinite(value)) throw new InvalidDataException("Nonfinite vertex");
                collision.Write(value);
            }
        count += mesh.TriangleCount;
    }
}
return 0;

