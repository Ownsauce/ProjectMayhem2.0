using AO.Unity.World;using UnityEngine;
using AO.Assets.Navigation;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
var coverage=new NativeSurfaceCoverage(2,.25f);
coverage.Add(new(){new(0,0,0),new(2,0,0),new(0,0,2)},new(){0,1,2});
Check(coverage.Covers(new(.5f,.1f,.5f)),"small collision shell offset covered");
Check(!coverage.Covers(new(.5f,.5f,.5f)),"raised prop retained");
Check(!coverage.Covers(new(1.8f,0,1.8f)),"gap within bounds retained");
Check(!coverage.Covers(new(.5f,8,.5f)),"different floor level retained");
Check(coverage.Covers(new(-.1f,0,.2f)),"near triangle seam covered");
coverage.Add(new(){new(4,0,0),new(4,8,0),new(4,0,2)},new(){0,1,2});
Check(coverage.Covers(new(4.1f,4,.5f)),"vertical wall covered across spatial cells");
Check(coverage.Covers(new(4.2f,4,.5f)),"authored collision wall skin does not hide source texture");
Check(!coverage.Covers(new(4.3f,4,.5f)),"uncovered fixture near wall retained");
Check(!coverage.Covers(new(4.1f,4,.5f),Vector3.up),"horizontal fixture near a wall is not mistaken for a floor");
coverage.Add(new(){new(20,0,0),new(20,0,0),new(20,0,0)},new(){0,1,2});
Check(!coverage.Covers(new(20,0,0)),"degenerate triangle ignored");
byte[] Fixture()
{
    using var data=new MemoryStream();using var writer=new BinaryWriter(data);
    writer.Write("AOVR"u8);writer.Write(1);writer.Write(127);writer.Write(126);writer.Write(1);writer.Write(2);writer.Write(1);
    writer.Write(10);writer.Write(20);writer.Write(130);writer.Write(1);writer.Write(3);
    foreach(var p in new[]{new float[]{0,2,0},new float[]{2,2,0},new float[]{0,2,2}})
    {foreach(float v in p)writer.Write(v);writer.Write(0f);writer.Write(1f);writer.Write(0f);writer.Write(0u);writer.Write(1.5f);writer.Write(-.5f);}
    writer.Write(1);writer.Write(2147489869u);writer.Write(3);writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)2);
    writer.Write("AOVD"u8);writer.Write(1);writer.Write(130);return data.ToArray();
}
var fixture=Fixture();
AO.Assets.Decoders.AOIndoorVisualSet Decode(byte[] bytes,int pf=127)=>AO.Assets.Decoders.AOIndoorVisualStreamDecoder.Decode(new MemoryStream(bytes),pf);
void Reject(byte[] bytes,string label,int pf=127){bool rejected=false;try{Decode(bytes,pf);}catch(InvalidDataException){rejected=true;}Check(rejected,label);}
var decoded=Decode(fixture);var mesh=decoded.Rooms[0].Meshes[2147489869u];
Check(mesh.VertexCount==3&&mesh.Triangles.SequenceEqual(new[]{0,1,2}),"original material triangle group retained");
Check(mesh.Uvs.SequenceEqual(new[]{1.5f,-.5f,1.5f,-.5f,1.5f,-.5f}),"original repeating UVs retained without clamping");
Check(mesh.Positions[1]==2&&decoded.Rooms[0].Index==2,"source coordinates and partial room identity retained");
Reject(fixture.Take(60).ToArray(),"truncated vertex payload rejected");
Reject(fixture.Concat(new byte[]{0}).ToArray(),"trailing payload rejected");
Reject(fixture,"wrong source playfield rejected",128);
var bad=(byte[])fixture.Clone();BitConverter.GetBytes(float.NaN).CopyTo(bad,48);Reject(bad,"nonfinite visual coordinate rejected");
bad=(byte[])fixture.Clone();BitConverter.GetBytes((ushort)65535).CopyTo(bad,48+3*36+4+8);Reject(bad,"invalid material triangle index rejected");
byte[] FloorFixture()
{
    using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
    writer.Write("AOVR"u8); foreach (int value in new[] { 1,127,126,1,0,1,10,20,130,0,18 }) writer.Write(value);
    var positions = new[] {
        new float[] {0,10,0, 0,10,2, 2,10,0}, // Floor.
        new float[] {4,10,0, 4,11,2, 6,10,0}, // Walkable ramp.
        new float[] {8,12,0, 10,12,0, 8,12,2}, // Downward ceiling.
        new float[] {12,10,0, 12,12,0, 12,10,2}, // Wall.
        new float[] {16,10,0, 16,10,0, 16,10,0}, // Degenerate.
        new float[] {20,10,0, 20,20,2, 22,10,0} // Steep face.
    };
    foreach (var triangle in positions) for (int i = 0; i < 9; i += 3)
    {
        writer.Write(triangle[i]); writer.Write(triangle[i+1]); writer.Write(triangle[i+2]);
        writer.Write(0f); writer.Write(1f); writer.Write(0f); writer.Write(0u); writer.Write(0f); writer.Write(0f);
    }
    writer.Write(1); writer.Write(1u); writer.Write(18);
    for (ushort i = 0; i < 18; i++) writer.Write(i);
    writer.Write("AOVD"u8); writer.Write(1); writer.Write(130); return output.ToArray();
}
var floorFixture = AOIndoorVisualFloorBuilder.Build(Decode(FloorFixture()).Rooms[0]);
Check(floorFixture.TriangleCount == 2 && floorFixture.VertexCount == 6,
    "visual support retains floors and ramps and excludes ceilings, walls, steep and degenerate faces");
Check(floorFixture.Vertices.Where((_, i) => i % 3 == 1).SequenceEqual(new float[] {10,10,10,10,11,10}),
    "visual support preserves exact floor/ramp elevations");
float? FloorHeight(AO.Assets.Decoders.AOIndoorSurfaceMesh floor, float x, float z)
    => floor == null ? null : TriangleHeight(floor.Vertices, floor.Triangles, x, z);
float? TriangleHeight(float[] vertices, int[] triangles, float x, float z, float maxY = float.PositiveInfinity)
{
    float? highest = null;
    for (int i = 0; i < triangles.Length; i += 3)
    {
        int a = triangles[i]*3, b = triangles[i+1]*3, c = triangles[i+2]*3;
        float ax = vertices[a], az = vertices[a+2];
        float ux = vertices[b]-ax, uz = vertices[b+2]-az;
        float vx = vertices[c]-ax, vz = vertices[c+2]-az;
        float determinant = ux*vz-uz*vx;
        if (Math.Abs(determinant) < 1e-6f) continue;
        float s = ((x-ax)*vz-(z-az)*vx)/determinant;
        float t = (ux*(z-az)-uz*(x-ax))/determinant;
        if (s < -.0001f || t < -.0001f || s+t > 1.0001f) continue;
        float y = vertices[a+1]+s*(vertices[b+1]-vertices[a+1])+t*(vertices[c+1]-vertices[a+1]);
        if (y > maxY) continue;
        if (!highest.HasValue || y > highest.Value) highest = y;
    }
    return highest;
}
Check(FloorHeight(floorFixture, .5f, .5f) == 10 && FloorHeight(floorFixture, 4.5f, 1) == 10.5f,
    "support queries land on authored floor and interpolated ramp");
var partitionFixture = Decode(FloorFixture()).Rooms[0].Meshes[1u];
List<Vector3> Positions(float[] values)
{
    var output = new List<Vector3>();
    for (int i = 0; i < values.Length; i += 3) output.Add(new(values[i], values[i+1], values[i+2]));
    return output;
}
float[] Flatten(List<Vector3> values) => values.SelectMany(v => new[] {v.x,v.y,v.z}).ToArray();
var partitionFloors = new List<Vector3>(); var partitionFloorIndices = new List<int>();
var partitionWalls = new List<Vector3>(); var partitionWallIndices = new List<int>();
var partitionCeilings = new List<Vector3>(); var partitionCeilingIndices = new List<int>();
NativeIndoorSurfaceTriangles.Append(Positions(partitionFixture.Positions), partitionFixture.Triangles,
    partitionFloors, partitionFloorIndices, partitionWalls, partitionWallIndices, partitionCeilings, partitionCeilingIndices);
Check(partitionFloorIndices.Count == 6 && partitionWallIndices.Count == 6 && partitionCeilingIndices.Count == 3,
    "mixed native mesh preserves floor/ramp, wall and ceiling faces independently");
Check(TriangleHeight(Flatten(partitionFloors), partitionFloorIndices.ToArray(), 4.5f, 1) == 10.5f,
    "mixed native ramp survives face classification at its exact source height");
byte[] ImageRecord(int type, int id, byte[] payload)
{
    using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
    writer.Write(type); writer.Write(id); writer.Write(1); writer.Write(payload); return output.ToArray();
}
var jpeg = new byte[] { 255, 216, 255, 217 };
Check(AO.Assets.Decoders.AOTexturePayloadDecoder.Decode(ImageRecord(1010009, 42, jpeg), 1010009, 42).SequenceEqual(jpeg), "wall JPEG payload read directly without export");
Check(AO.Assets.Decoders.AOTexturePayloadDecoder.Decode(ImageRecord(1010004, 42, jpeg), 1010004, 42).SequenceEqual(jpeg), "general JPEG payload read directly without export");
void RejectImage(byte[] record, int type, int id, string label)
{
    bool rejected = false;
    try { AO.Assets.Decoders.AOTexturePayloadDecoder.Decode(record, type, id); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, label);
}
RejectImage(ImageRecord(1010009, 42, jpeg), 1010009, 43, "wrong texture resource rejected");
RejectImage(ImageRecord(1010009, 42, jpeg), 1010004, 42, "wrong texture type rejected");
RejectImage(ImageRecord(1010009, 42, new byte[] { 0, 0, 0, 0 }), 1010009, 42, "unsupported image encoding rejected");
RejectImage(new byte[4], 1010009, 42, "truncated texture record rejected");
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel(); bool stopped = false;
    try { AO.Assets.Conversion.AOIndoorVisualExtractor.Load(null, 127, "missing.exe", canceled.Token); }
    catch (OperationCanceledException) { stopped = true; }
    Check(stopped, "canceled zone read never launches a helper");
}
byte[] StatelFixture()
{
    using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
    writer.Write(1); writer.Write(8); // One room, followed by its opaque lightmap envelope.
    writer.Write(4); writer.Write(0); writer.Write((ushort)1); writer.Write((ushort)99);
    writer.Write((ushort)1); // Short record has no reusable mesh identity.
    writer.Write(new byte[17]); writer.Write((byte)1); writer.Write(0x7f0000); writer.Write(10); writer.Write(20);
    for (int group = 0; group < 2; group++)
    {
        writer.Write((ushort)1); writer.Write(1f); writer.Write(2f); writer.Write(3f);
        writer.Write(11520); writer.Write(123 + group); writer.Write((byte)90); writer.Write((byte)1);
        writer.Write(1); writer.Write(78404 + group);
    }
    return output.ToArray();
}
var statelFixture = StatelFixture();
var statels = AO.Assets.Decoders.AOIndoorStatelPlacementDecoder.Decode(statelFixture, 1);
Check(statels.Count == 2 && statels[1].MeshId == 124 && statels[0].Y == 2,
    "indoor blocks decode both model lists after lightmaps and short records");
Check(statels[0].TextureOverrides.SequenceEqual(new[] { 1, 78404 }), "indoor texture override identity retained");
void RejectStatels(byte[] bytes, int rooms, string label)
{
    bool rejected = false;
    try { AO.Assets.Decoders.AOIndoorStatelPlacementDecoder.Decode(bytes, rooms); }
    catch (InvalidDataException) { rejected = true; }
    Check(rejected, label);
}
RejectStatels(statelFixture, 2, "indoor placement room count mismatch rejected");
RejectStatels(statelFixture[..^1], 1, "truncated indoor placement override rejected");
bad = (byte[])statelFixture.Clone(); BitConverter.GetBytes(2).CopyTo(bad, 0);
RejectStatels(bad, 1, "unsupported indoor placement version rejected");
bad = (byte[])statelFixture.Clone(); BitConverter.GetBytes(-1).CopyTo(bad, 4);
RejectStatels(bad, 1, "invalid indoor placement offset rejected");
bad = (byte[])statelFixture.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(bad, 54);
RejectStatels(bad, 1, "nonfinite indoor model coordinate rejected");
bool collisionAudit = args.Length > 0 && args[0] == "--native-collision";
if (collisionAudit)
{
    if (args.Length != 3) throw new Exception("--native-collision <PF127.aois> <PF127.visual.aovr>");
    var captured = AO.Assets.Decoders.AOIndoorSurfaceStreamDecoder.Decode(args[1], 127);
    var tileSupport = AOIndoorVisualFloorBuilder.Build(
        AO.Assets.Decoders.AOIndoorVisualStreamDecoder.Decode(args[2], 127).Rooms.Single(r => r.Index == 8));
    var stairMesh = captured.Rooms.Single(r => r.Instance == 8).Meshes[486];
    var stairPositions = Positions(stairMesh.Vertices);
    Vector3 oldAverage = Vector3.zero;
    for (int i = 0; i < Math.Min(24, stairMesh.Triangles.Length); i += 3)
        oldAverage += Vector3.Cross(stairPositions[stairMesh.Triangles[i+1]]-stairPositions[stairMesh.Triangles[i]],
            stairPositions[stairMesh.Triangles[i+2]]-stairPositions[stairMesh.Triangles[i]]).normalized;
    Check(Math.Abs(oldAverage.normalized.y) < .65f, "reported stair mesh was excluded as a wall by whole-mesh classification");
    var floors = new List<Vector3>(); var floorIndices = new List<int>();
    var walls = new List<Vector3>(); var wallIndices = new List<int>();
    var ceilings = new List<Vector3>(); var ceilingIndices = new List<int>();
    NativeIndoorSurfaceTriangles.Append(stairPositions, stairMesh.Triangles, floors, floorIndices, walls, wallIndices, ceilings, ceilingIndices);
    foreach (var point in new[] {(97f,268f,111.1575f),(96f,268f,111.1575f),(98f,268f,111.1575f),(97f,267f,110.6575f),(97f,266f,110.1575f)})
    {
        float? source = TriangleHeight(stairMesh.Vertices, stairMesh.Triangles, point.Item1, point.Item2);
        float? retained = TriangleHeight(Flatten(floors), floorIndices.ToArray(), point.Item1, point.Item2);
        Check(source.HasValue && retained.HasValue && Math.Abs(retained.Value-point.Item3)<.02f
            && Math.Abs(retained.Value-source.Value)<.001f,
            $"reported stair ramp retained at ({point.Item1},{point.Item2}), Y={point.Item3}");
    }
    var retainedPositions = Flatten(floors); var retainedIndices = floorIndices.ToArray();
    bool wholeRamp = true;
    for (float x = 93.5f; x <= 98.5f; x += 1)
    for (float z = 261; z <= 268.5f; z += .5f)
    {
        float? expected = TriangleHeight(stairMesh.Vertices, stairMesh.Triangles, x, z);
        float? actual = TriangleHeight(retainedPositions, retainedIndices, x, z);
        wholeRamp &= expected.HasValue && actual.HasValue && Math.Abs(expected.Value-actual.Value)<.001f;
    }
    Check(wholeRamp, "native support matches the source across the full stair width and run (96 probes)");
    floors.Clear(); floorIndices.Clear(); walls.Clear(); wallIndices.Clear(); ceilings.Clear(); ceilingIndices.Clear();
    foreach (var surface in captured.Rooms.Single(r => r.Instance == 8).Meshes)
        NativeIndoorSurfaceTriangles.Append(Positions(surface.Vertices), surface.Triangles, floors, floorIndices, walls, wallIndices, ceilings, ceilingIndices);
    foreach (var landing in new[] {(260.5f,107.605f),(269.5f,111.605f)})
    {
        var height = TriangleHeight(Flatten(floors), floorIndices.ToArray(), 97, landing.Item1, landing.Item2+1);
        var tileHeight = TriangleHeight(tileSupport.Vertices, tileSupport.Triangles, 97, landing.Item1, landing.Item2+1);
        if (tileHeight.HasValue && (!height.HasValue || tileHeight.Value > height.Value)) height = tileHeight;
        Check(height.HasValue && Math.Abs(height.Value-landing.Item2)<.02f, "combined tile/native stair landing support at Z="+landing.Item1);
    }
    var entranceFloors = Flatten(floors); var entranceIndices = floorIndices.ToArray();
    foreach (var point in new[] {(91f,326f),(90.5f,326f),(91.5f,326f),(91f,325.5f),(91f,326.5f)})
    {
        var landing = TriangleHeight(entranceFloors, entranceIndices, point.Item1, point.Item2, 116f);
        Check(landing.HasValue && Math.Abs(landing.Value-115.605f)<.02f,
            $"upper entrance landing retained under the capsule at X={point.Item1}, Z={point.Item2}");
    }
    for (int turn = 0; turn < 4; turn++)
    {
        (float X, float Z) Rotate(float x, float z) => turn switch {
            1 => (z, -x), 2 => (-x, -z), 3 => (-z, x), _ => (x, z) };
        var moved = new float[entranceFloors.Length];
        for (int i=0; i<moved.Length; i+=3)
        {
            var p = Rotate(entranceFloors[i]-82f, entranceFloors[i+2]-290f);
            moved[i]=150f+p.X; moved[i+1]=entranceFloors[i+1]+3f; moved[i+2]=200f+p.Z;
        }
        foreach (var point in new[] {(91f,326f,115.605f),(97f,268f,111.1575f),(97f,267f,110.6575f)})
        {
            var p = Rotate(point.Item1-82f,point.Item2-290f);
            var height = TriangleHeight(moved,entranceIndices,150f+p.X,200f+p.Z,point.Item3+4f);
            Check(height.HasValue && Math.Abs(height.Value-(point.Item3+3f))<.02f,
                $"reused entrance landing/stairs retain support after quarter turn {turn}");
        }
    }
    int floorCount = 0, wallCount = 0, ceilingCount = 0, total = 0;
    foreach (var room in captured.Rooms) foreach (var surface in room.Meshes)
    {
        floors.Clear(); floorIndices.Clear(); walls.Clear(); wallIndices.Clear(); ceilings.Clear(); ceilingIndices.Clear();
        NativeIndoorSurfaceTriangles.Append(Positions(surface.Vertices), surface.Triangles, floors, floorIndices, walls, wallIndices, ceilings, ceilingIndices);
        floorCount += floorIndices.Count/3; wallCount += wallIndices.Count/3; ceilingCount += ceilingIndices.Count/3;
        total += surface.TriangleCount;
    }
    Check(floorCount > 0 && wallCount > 0 && ceilingCount > 0 && floorCount+wallCount+ceilingCount <= total,
        "all PF127 native surfaces partition without losing horizontal support to wall groups");
    Console.WriteLine($"Native source faces: floors={floorCount}, walls={wallCount}, ceilings={ceilingCount}, degenerate={total-floorCount-wallCount-ceilingCount}");
}
bool statelAudit = args.Length > 0 && args[0] == "--statels";
if (statelAudit)
{
    if (args.Length != 2) throw new Exception("--statels <AO-install>");
    var placements = AO.Assets.Decoders.AOIndoorStatelPlacementDecoder.Decode(
        File.ReadAllBytes(Path.Combine(args[1], "cd_image", "data", "statels", "127.pf")), 46);
    Check(placements.Count == 2247 && placements.Select(p => p.MeshId).Distinct().Count() == 246,
        "all PF127 indoor model placement records decoded");
    Check(placements.Count(p => p.RoomIndex == 2) == 37 && placements.Count(p => p.RoomIndex == 6) == 78,
        "spawn bathroom and nearby dome model placements retained");
    using var database = new AODB.RdbController(args[1]);
    var names = database.Get<AODB.Common.RDBObjects.InfoObject>(1).Types;
    bool Helper(int id) => names.TryGetValue(AODB.Common.RDBObjects.ResourceTypeId.RdbMesh, out var meshes)
        && meshes.TryGetValue(id, out var name) && (name.StartsWith("[OCC]") || name.StartsWith("bsp_"));
    var textures = new HashSet<int>(); var missing = new List<int>(); int triangles = 0, models = 0;
    foreach (int id in placements.Select(p => p.MeshId).Distinct().Where(id => !Helper(id)))
    {
        try
        {
            var meshSource = AbiffMeshSnapshot.FromRdbMesh(database.Get<AODB.Common.RDBObjects.RDBMesh>(id));
            if (meshSource.Length == 0) { missing.Add(id); continue; }
            models++;
            foreach (var placement in placements.Where(p => p.RoomIndex == 2 && p.MeshId == id))
            {
                names[AODB.Common.RDBObjects.ResourceTypeId.RdbMesh].TryGetValue(id, out var name);
                Console.WriteLine($"BATHROOM MODEL {id} {name?.TrimEnd('\0')}: local=({placement.X},{placement.Y},{placement.Z}), sourceYBounds=({meshSource.SelectMany(s => s.Positions).Min(v => v.y)},{meshSource.SelectMany(s => s.Positions).Max(v => v.y)})");
            }
            foreach (var sub in meshSource)
            {
                triangles += sub.Triangles.Length / 3;
                if (sub.Material.DiffuseTextureId > 0) textures.Add(sub.Material.DiffuseTextureId);
                if (sub.Material.EmissionTextureId > 0) textures.Add(sub.Material.EmissionTextureId);
                Check(sub.Triangles.All(index => index >= 0 && index < sub.Positions.Length), "static model triangle indices: " + id);
            }
        }
        catch (Exception error) { Console.WriteLine($"MODEL FAIL {id}: {error}"); missing.Add(id); }
    }
    foreach (var placement in placements.Where(p => !Helper(p.MeshId)))
        foreach (int id in placement.TextureOverrides.Skip(1)) if (id > 0) textures.Add(id);
    foreach (int id in textures)
    {
        var texture = database.Get<AODB.Common.RDBObjects.AOTexture>(id);
        var image = StbImageSharp.ImageResult.FromMemory(texture.JpgData, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Check(image.Width > 0 && image.Height > 0, "original static model texture decodes: " + id);
    }
    Console.WriteLine($"PF127 static models: placements={placements.Count(p => !Helper(p.MeshId))}, resources={models}, trianglesPerUniqueResource={triangles}, textureResources={textures.Count}, unreadable={string.Join(',', missing)}");
    Check(missing.Count == 0, "every rendered PF127 static model resource decodes using the Unity AODB assemblies");
}
bool live = args.Length > 0 && args[0] == "--live";
if (live)
{
    if (args.Length != 3) throw new Exception("--live <AO-install> <runtime-helper>");
    var install = AO.Assets.ResourceDatabase.AOInstallLocator.Validate(args[1]);
    var before = Directory.GetDirectories(Path.GetTempPath(), "pm-indoor-runtime-*").ToHashSet();
    var source = AO.Assets.Conversion.AOIndoorVisualExtractor.Load(install, 127, args[2]);
    Check(source.Geometry.Rooms.Count == 46 && source.Geometry.Rooms.Sum(r => r.Meshes.Values.Sum(m => m.Triangles.Length / 3)) == 113176, "live DB builds all PF127 architecture without a package");
    Check(source.Images.Count == 104, "live DB resolves all original material images");
    foreach (var room in source.Geometry.Rooms)
    {
        float lowest = room.Meshes.Values.SelectMany(m => m.Positions.Where((_, i) => i % 3 == 1)).Min();
        float origin = source.Definition.Rooms[room.Index].Y;
        Check(lowest >= origin - .21f && lowest <= origin + .41f,
            "live room height baseline matches source placement: " + room.Index);
    }
    foreach (var pair in source.Images)
    {
        var image = StbImageSharp.ImageResult.FromMemory(pair.Value, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Check(image.Width > 0 && image.Height > 0, "live original image decodes: " + pair.Key);
    }
    Check(Directory.GetDirectories(Path.GetTempPath(), "pm-indoor-runtime-*").All(before.Contains), "live read removes temporary geometry and plans");
    using var changingZone = new CancellationTokenSource(); changingZone.CancelAfter(100);
    bool abandoned = false;
    try { AO.Assets.Conversion.AOIndoorVisualExtractor.Load(install, 127, args[2], changingZone.Token); }
    catch (OperationCanceledException) { abandoned = true; }
    Check(abandoned, "zone transition cancels a live database read");
    Check(Directory.GetDirectories(Path.GetTempPath(), "pm-indoor-runtime-*").All(before.Contains), "canceled live read cleans temporary work");
}
if(args.Length>0 && !live && !statelAudit && !collisionAudit)
{
    var original=AO.Assets.Decoders.AOIndoorVisualStreamDecoder.Decode(args[0],127);
    Check(original.Rooms.Count==46&&original.Rooms.Sum(r=>r.Meshes.Values.Sum(m=>m.Triangles.Length/3))==113176,"all PF127 source room triangles decoded");
    Check(original.Rooms.SelectMany(r=>r.Meshes.Keys).Distinct().Count()==104&&original.VisualResourceIds.Length==101,"all original materials and visual dependencies retained");
    var entrance = AOIndoorVisualFloorBuilder.Build(original.Rooms.Single(r => r.Index == 8));
    foreach (var point in new[] {(91f,326f),(90f,326f),(92f,326f),(91f,325f),(91f,327f),(89f,324f),(93f,328f)})
    {
        float? height = FloorHeight(entrance, point.Item1, point.Item2);
        Check(height.HasValue && Math.Abs(height.Value-107.60483f) < .02f,
            $"source floor support at reported entrance drop: ({point.Item1},{point.Item2})");
    }
    Console.WriteLine("PF127 visual floor support triangles=" + original.Rooms.Sum(r => AOIndoorVisualFloorBuilder.Build(r)?.TriangleCount ?? 0));
}
if(args.Length>1 && !live && !statelAudit && !collisionAudit)
{
    var visuals=AO.Assets.Decoders.AOIndoorVisualStreamDecoder.Decode(args[0],127);
    var collision=AO.Assets.Decoders.AOIndoorSurfaceStreamDecoder.Decode(args[1],127);
    var all=new NativeSurfaceCoverage(2,.25f);long total=0,covered=0;
    foreach(var room in visuals.Rooms)foreach(var surface in room.Meshes.Values)
    {var positions=new List<Vector3>();for(int i=0;i<surface.Positions.Length;i+=3)positions.Add(new(surface.Positions[i],surface.Positions[i+1],surface.Positions[i+2]));all.Add(positions,surface.Triangles.ToList());}
    foreach(var room in collision.Rooms)foreach(var surface in room.Meshes)
    for(int i=0;i<surface.Triangles.Length;i+=3)
    {
        Vector3 Position(int index){int v=surface.Triangles[index]*3;return new(surface.Vertices[v],surface.Vertices[v+1],surface.Vertices[v+2]);}
        Vector3 a=Position(i),b=Position(i+1),c=Position(i+2);total++;
        Vector3 n=Vector3.Cross(b-a,c-a).normalized;
        if((MathF.Abs(n.y)>.9f&&all.Covers((a+b+c)/3,n))||(all.Covers(a)&&all.Covers(b)&&all.Covers(c)&&all.Covers((a+b+c)/3)))covered++;
    }
    Console.WriteLine($"PF127 source overlap: {covered}/{total} collision surface triangles covered; {total-covered} retained as unresolved visuals.");
    Check(covered>0&&covered<total,"source overlap preserves uncovered geometry");
    if (args.Length > 2 && args[2] == "--diagnose")
    {
        foreach (var room in visuals.Rooms)
        {
            var sourceRoom = collision.Rooms.Single(r => r.Instance == room.Index);
            var tolerances = new[] { .16f, .35f, .65f };
            var matched = new long[tolerances.Length]; var horizontal = new long[tolerances.Length];
            var shell = tolerances.Select(t => new NativeSurfaceCoverage(2, t)).ToArray();
            foreach (var m in room.Meshes.Values)
            {
                var positions = new List<Vector3>();
                for (int i = 0; i < m.Positions.Length; i += 3) positions.Add(new(m.Positions[i], m.Positions[i+1], m.Positions[i+2]));
                foreach (var matcher in shell) matcher.Add(positions, m.Triangles.ToList());
            }
            long count = 0, floors = 0;
            foreach (var m in sourceRoom.Meshes)
                for (int i = 0; i < m.Triangles.Length; i += 3)
                {
                    Vector3 Position(int v) { int j = m.Triangles[v] * 3; return new(m.Vertices[j], m.Vertices[j+1], m.Vertices[j+2]); }
                    var a = Position(i); var b = Position(i+1); var c = Position(i+2);
                    var n = Vector3.Cross(b-a, c-a).normalized; var center = (a+b+c)/3;
                    count++; if (MathF.Abs(n.y) > .9f) floors++;
                    for (int t = 0; t < shell.Length; t++)
                        if (shell[t].Covers(center, n)) { matched[t]++; if (MathF.Abs(n.y) > .9f) horizontal[t]++; }
                }
            Console.WriteLine($"ROOM {room.Index}: source={count}, horizontal={floors}; projected shell matches at .16/.35/.65={string.Join('/',matched)}; horizontal={string.Join('/',horizontal)}");
        }
    }
}
